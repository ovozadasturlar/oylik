using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Oylik.Data;
using Oylik.Data.Sync;
using Oylik.Desktop.Resources;
using Serilog;

namespace Oylik.Desktop.Services;

/// <summary>
/// TZ 6 "Desktop sinxronizatsiya": har 60 s — jo'natilmagan qo'lda tuzatishlar → server, `GET events?sinceSeq` → lokal upsert,
/// qurilmalar ro'yxati. Xodim o'zgarsa 5 s debounce bilan `PUT employees`. Server yo'q bo'lsa jim ishlaydi (dialog yo'q).
/// </summary>
public sealed partial class SyncService : ObservableObject
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan EmployeesDebounce = TimeSpan.FromSeconds(5);

    private readonly PayrollService _service;
    private readonly Func<Func<Task>, Task> _runDb;
    private DispatcherTimer? _timer;
    private DispatcherTimer? _employeesTimer;
    private readonly ServerSync _sync;
    private bool _syncing;
    private bool _syncAgain;

    /// <param name="runDb">Bazaga yozuvchi amallar shu orqali (bitta DbContext — ketma-ket).</param>
    public SyncService(PayrollService service, Func<Func<Task>, Task> runDb)
    {
        _service = service;
        _runDb = runDb;
        _sync = new ServerSync(service, runDb);
        service.EmployeesChanged += (_, _) =>
        {
            _sync.EmployeesDirty = true;
            if (_employeesTimer is null || !IsConfigured)
                return;
            _employeesTimer.Stop();
            _employeesTimer.Start();
        };
    }

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = Strings.ServerNotConfigured;
    [ObservableProperty] private string? _lastError;

    /// <summary>Sinxronizatsiyadan keyin (yangi voqealar bo'lishi mumkin) — ekranlar yangilanadi.</summary>
    public event EventHandler? Synced;

    /// <summary>6.1: lokal yoki global manzil (kamida bittasi) va API kalit.</summary>
    public bool IsConfigured =>
        (_service.Settings.LocalServerUrl is { Length: > 0 } || _service.Settings.ServerUrl is { Length: > 0 }) && ApiKey is not null;

    public string? ApiKey => SecretProtector.Unprotect(_service.Settings.ApiKey);

    /// <summary>Testlar uchun: server bilan xotiradagi HttpClient orqali gaplashish.</summary>
    public HttpClient? Http { get; init; }

    private ServerConnection? _connection;

    /// <summary>Lokal → global tanlovi shu obyektda (lokal qayta tekshiruv vaqti saqlanadi); sozlama o'zgarsa — Reset().</summary>
    public ServerConnection? Connection => _connection ??= IsConfigured
        ? new ServerConnection(_service.Settings.LocalServerUrl, _service.Settings.ServerUrl, ApiKey, TimeProvider.System, Http is null ? null : _ => Http)
        : null;

    /// <summary>Hozir qaysi manzil orqali ulangan (sidebar/Sozlamalar holati).</summary>
    public ServerRoute Route => IsConnected ? _connection?.Route ?? ServerRoute.None : ServerRoute.None;

    /// <summary>Fon taymerlari (UI oqimida): 60 s sinxronizatsiya va xodimlar uchun 5 s debounce.</summary>
    public void Start()
    {
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await SyncNowAsync();
        _employeesTimer = new DispatcherTimer { Interval = EmployeesDebounce };
        _employeesTimer.Tick += async (_, _) =>
        {
            _employeesTimer.Stop();
            await SyncNowAsync();
        };
        UpdateStatus();
        _timer.Start();
        _ = SyncNowAsync();
    }

    public void Stop()
    {
        _timer?.Stop();
        _employeesTimer?.Stop();
    }

    /// <summary>Ulanish o'zgardi: xodimlar to'liq qayta jo'natiladi.</summary>
    public void Reset()
    {
        _connection = null;
        _sync.EmployeesDirty = true;
        IsConnected = false;
        LastError = null;
        UpdateStatus();
    }

    public async Task SyncNowAsync()
    {
        if (_syncing)
        {
            _syncAgain = true;
            return;
        }
        var connection = Connection;
        if (connection is null)
        {
            IsConnected = false;
            UpdateStatus();
            return;
        }

        _syncing = true;
        try
        {
            do
            {
                _syncAgain = false;
                await connection.RunAsync(client => _sync.RunAsync(client));
            }
            while (_syncAgain);
            IsConnected = true;
            LastError = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ServerException or System.Text.Json.JsonException)
        {
            IsConnected = false;
            LastError = ex.Message;
            Log.Warning("Sinxronizatsiya: {Error}", ex.Message);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            LastError = ex.Message;
            Log.Error(ex, "Sinxronizatsiya xatosi");
        }
        finally
        {
            _syncing = false;
            UpdateStatus();
            Synced?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UpdateStatus()
    {
        OnPropertyChanged(nameof(IsConfigured));
        if (!IsConfigured)
        {
            StatusText = Strings.ServerNotConfigured;
            return;
        }
        var last = _service.Settings.LastSyncAt;
        var device = _service.Devices.Where(d => d.IsActive && d.LastSeenAt is not null).MaxBy(d => d.LastSeenAt);
        OnPropertyChanged(nameof(Route));
        StatusText = Strings.ServerStatus(Route, last?.ToLocalTime(), device?.Name, device?.LastSeenAt?.ToLocalTime());
    }
}
