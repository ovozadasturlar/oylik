using System.Text;
using Oylik.Data.Sync;
using Oylik.Data.Terminal;

namespace Oylik.Data.Agent;

/// <summary>Agent sikli sozlamalari.</summary>
public sealed record AgentOptions
{
    /// <summary>Terminal poll va bazadan yangilash oralig'i.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Server sinxronizatsiyasi oralig'i (desktopdagi kabi).</summary>
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Shuncha vaqtda bir xotiradagi nusxa to'liq qayta yuklanadi (jadval/biriktirishlar va h.k. o'zgarishlari).</summary>
    public TimeSpan ReloadInterval { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>Bitta tick natijasi (log va testlar uchun).</summary>
public sealed record AgentTickResult(TerminalPollResult Terminal, bool Synced, string? TerminalError, string? SyncError, StoreChanges Store);

/// <summary>
/// Bosqich 2: fon agentining UI'siz sikli (<c>Oylik.Agent</c> faqat host). Har tick: boshqa jarayon (desktop) yozganini
/// bazadan olish → terminal poll (<see cref="TerminalPoller"/>) → har <see cref="AgentOptions.SyncInterval"/>da server
/// sinxronizatsiyasi (<see cref="ServerSync"/>). Davomatdan boshqa narsaga (Oylik qatorlari) tegmaydi — <see cref="PayrollService.AttendanceOnly"/>.
/// Xatolar tick'ni yiqitmaydi — keyingi tick'da qayta uriniladi.
/// </summary>
public sealed class AgentRunner(
    AppPaths paths,
    TimeProvider? clock = null,
    AgentOptions? options = null,
    Func<PayrollService, TerminalPoller>? pollerFactory = null,
    Func<string, HttpClient?>? serverHttp = null,
    Action<string>? log = null) : IDisposable
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly AgentOptions _options = options ?? new AgentOptions();
    private readonly Action<string> _log = log ?? (_ => { });

    private PayrollService? _service;
    private TerminalPoller? _poller;
    private ServerSync? _sync;
    private ServerConnection? _connection;
    private string? _connectionKey;
    private string? _employeesSignature;
    private DateTimeOffset _loadedAt;
    private DateTimeOffset _nextSync = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastPollAt;
    private string? _syncError;
    private AgentStatus _status = AgentStatus.Initial;

    public AgentOptions Options => _options;

    /// <summary>Oxirgi tick holati (o'zgarmas; istalgan oqimdan o'qiladi).</summary>
    public AgentStatus Status => Volatile.Read(ref _status);

    /// <summary>Har tick'dan keyin (agent oqimida) — tray shu orqali yangilanadi.</summary>
    public event Action<AgentStatus>? StatusChanged;

    /// <summary>Joriy xotiradagi servis (testlar uchun).</summary>
    public PayrollService? Service => _service;

    /// <summary>Bazani ochadi (migratsiya, WAL) — agent rejimida.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        paths.EnsureCreated();
        _service?.Dispose();
        var service = new PayrollService(paths, _clock, attendanceOnly: true);
        await service.InitializeAsync(dailyBackup: false);
        _service = service;
        _poller = pollerFactory?.Invoke(service) ?? new TerminalPoller(service, clock: _clock, log: _log);
        _sync = new ServerSync(service)
        {
            // Bosqich 3: boshqa zavoddan kelgan xodim shu zavod terminaliga ham yoziladi (best-effort).
            EmployeesPulled = async pulled =>
            {
                try
                {
                    await _poller!.PushEmployeesAsync(pulled.ForTerminal);
                }
                catch (Exception ex)
                {
                    _log($"Serverdan kelgan xodimlarni terminalga yozib bo'lmadi: {ex.Message}");
                }
            },
        };
        _employeesSignature = null; // yangi ServerSync — xodimlar baribir bir marta jo'natiladi
        _loadedAt = _clock.GetUtcNow();
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Bitta tick. Tarmoq/terminal xatolari natijada qaytadi (otilmaydi).</summary>
    public async Task<AgentTickResult> TickAsync(CancellationToken ct = default)
    {
        if (_service is null || _clock.GetUtcNow() - _loadedAt >= _options.ReloadInterval)
            await InitializeAsync(ct);
        var service = _service!;

        var store = await service.RefreshFromStoreAsync(ct);

        TerminalPollResult terminal = TerminalPollResult.Offline;
        string? terminalError = null;
        try
        {
            terminal = await _poller!.PollAsync(ct: ct);
        }
        catch (Exception ex) when (ex is TerminalException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            terminalError = ex.Message;
        }

        var synced = false;
        string? syncError = null;
        if (_clock.GetUtcNow() >= _nextSync)
        {
            _nextSync = _clock.GetUtcNow() + _options.SyncInterval;
            (synced, syncError) = await SyncAsync(service, ct);
            _syncError = syncError;
        }
        if (terminal.Connected)
            _lastPollAt = _clock.GetUtcNow();
        PublishStatus(service, terminal.Connected, terminalError);
        return new AgentTickResult(terminal, synced, terminalError, syncError, store);
    }

    private void PublishStatus(PayrollService service, bool terminalConnected, string? terminalError)
    {
        var s = service.Settings;
        var zone = service.LocalZone;
        var now = _clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var status = new AgentStatus
        {
            Language = s.Language,
            TerminalEnabled = s.TerminalEnabled,
            TerminalConnected = terminalConnected,
            LastPollAt = _lastPollAt,
            TerminalError = terminalError,
            TodayEvents = service.AttendanceEvents.Count(e =>
                !e.IsVoided && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.OccurredAt, zone).DateTime) == today),
            ServerConfigured = (s.LocalServerUrl is { Length: > 0 } || s.ServerUrl is { Length: > 0 }) && s.ApiKey is { Length: > 0 },
            LastSyncAt = s.LastSyncAt,
            SyncError = _syncError,
            UpdatedAt = now,
        };
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
    }

    private async Task<(bool Synced, string? Error)> SyncAsync(PayrollService service, CancellationToken ct)
    {
        var s = service.Settings;
        var apiKey = SecretProtector.Unprotect(s.ApiKey);
        if ((s.LocalServerUrl is not { Length: > 0 } && s.ServerUrl is not { Length: > 0 }) || apiKey is null)
            return (false, null); // server sozlanmagan — faqat lokal yig'iladi

        // Manzil/kalit o'zgarsa — yangi ulanish va xodimlar to'liq qayta jo'natiladi.
        var key = $"{s.LocalServerUrl}|{s.ServerUrl}|{s.ApiKey}";
        if (_connection is null || _connectionKey != key)
        {
            _connection = new ServerConnection(s.LocalServerUrl, s.ServerUrl, apiKey, _clock, serverHttp);
            _connectionKey = key;
            _employeesSignature = null;
        }

        // Xodimlar faqat o'zgarganda (desktop qo'shgan/tahrirlagan) — imzo bo'yicha.
        var signature = EmployeesSignature(service);
        if (signature != _employeesSignature)
            _sync!.EmployeesDirty = true;
        try
        {
            await _connection.RunAsync(client => _sync!.RunAsync(client), ct);
            _employeesSignature = signature;
            return (true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ServerException or System.Text.Json.JsonException
                                   && !ct.IsCancellationRequested)
        {
            return (false, ex.Message);
        }
    }

    private static string EmployeesSignature(PayrollService service)
    {
        var sb = new StringBuilder();
        foreach (var e in service.Employees.OrderBy(e => e.Id))
            sb.Append(e.Id).Append('|').Append(e.FullName).Append('|').Append(e.Note).Append('|').Append(e.IsActive)
              .Append('|').Append(e.TabelNo).Append('|').Append(service.MaxShiftMinutesFor(e)).Append(';');
        return sb.ToString();
    }

    /// <summary>To'xtatilguncha sikl: tick → pauza. Kutilmagan xato log qilinadi va servis qayta ochiladi.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = await TickAsync(ct);
                if (r.TerminalError is not null)
                    _log($"Terminal: {r.TerminalError}");
                if (r.SyncError is not null)
                    _log($"Server: {r.SyncError}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"Agent tick xatosi: {ex}");
                _service?.Dispose();
                _service = null; // keyingi tick bazani qayta ochadi
            }
            try
            {
                await Task.Delay(_options.PollInterval, _clock, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose() => _service?.Dispose();
}
