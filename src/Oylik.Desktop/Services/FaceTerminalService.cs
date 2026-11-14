using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Data.Terminal;
using Oylik.Desktop.Resources;
using Serilog;

namespace Oylik.Desktop.Services;

/// <summary>
/// 2.3: Yuz terminalidan (Hikvision) davomatni o'qiydi. Har <see cref="Interval"/>da yangi yuz voqealarini oladi,
/// Keldi/Ketdi qilib lokal bazaga yozadi. Mantiq UI'siz <see cref="TerminalPoller"/>da (Oylik.Data) — bu yerda faqat
/// taymer, UI holati va yuzli xodimlar ro'yxati. Server yo'q/parol xato bo'lsa jim ishlaydi (dialogsiz), status qatorida ko'rinadi.
/// Bosqich 2: fon agenti ishlayotgan bo'lsa (<c>AgentLock</c>) terminal davomatini agent o'qiydi — desktop so'ramaydi,
/// faqat ko'rsatadi (holat — agent yangilagan qurilma yozuvidan).
/// </summary>
public sealed partial class FaceTerminalService : ObservableObject
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    /// <summary>Agent rejimida yuzli xodimlar ro'yxati shuncha vaqtda bir o'qiladi (faqat o'qish; voqealarni agent oladi).</summary>
    public static readonly TimeSpan AgentFaceRefresh = TimeSpan.FromMinutes(5);

    /// <summary>Agent rejimida terminal "ulangan" deb ko'rsatiladi, agar agent shu vaqt ichida unga ulangan bo'lsa.</summary>
    public static readonly TimeSpan AgentFreshness = TimeSpan.FromMinutes(1);

    private readonly PayrollService _service;
    private readonly TerminalPoller _poller;
    private readonly Func<bool> _agentRunning;
    private DispatcherTimer? _timer;
    private bool _busy;

    /// <param name="agentRunning">Shu bazaning fon agenti ishlayaptimi (null — agent yo'q deb hisoblanadi).</param>
    public FaceTerminalService(PayrollService service, Func<Func<Task>, Task> runDb, Func<bool>? agentRunning = null)
    {
        _service = service;
        _agentRunning = agentRunning ?? (() => false);
        _poller = new TerminalPoller(service, runDb, log: message => Log.Information("{Message}", message), deviceName: DeviceName);
    }

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _lastError;

    /// <summary>Terminal davomatini fon agenti o'qiyapti (desktop faqat ko'rsatadi).</summary>
    [ObservableProperty] private bool _isAgentMode;

    /// <summary>Yangi davomat voqealari yozilganda — ekranlar yangilanadi.</summary>
    public event EventHandler? Changed;

    /// <summary>Terminalda yuzi bor xodimlar (TabelNo) — qatorda yuz bor/yo'qligini ko'rsatish uchun.</summary>
    public IReadOnlySet<int> FacedTabels { get; private set; } = new HashSet<int>();
    private DateTimeOffset _lastFaceRefresh = DateTimeOffset.MinValue;

    /// <summary>Yuz muvaffaqiyatli qo'shilganda — darhol belgilanadi (ro'yxat yangilanishini kutmay).</summary>
    private void MarkFaced(int tabelNo)
    {
        if (tabelNo <= 0 || FacedTabels.Contains(tabelNo))
            return;
        FacedTabels = new HashSet<int>(FacedTabels) { tabelNo };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsEnabled => _poller.IsEnabled;

    public void Start()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
        UpdateStatus();
        _ = PollAsync();
    }

    public void Stop() => _timer?.Stop();

    /// <summary>
    /// Foydalanuvchi amallari (xodimni yozish, yuz qo'shish) uchun ulanish. Agent ishlayotganda tarmoq skanini qilmaydi —
    /// IP o'zgarsa agent topadi va bazaga yozadi (desktop uni bazadan oladi).
    /// </summary>
    private Task<(HikTerminalClient Client, TerminalInfo Info)?> AcquireAsync() =>
        _poller.AcquireAsync(allowDiscovery: !_agentRunning());

    /// <summary>Yuzli xodimlar ro'yxatini o'qiydi; o'zgargan bo'lsa true.</summary>
    private async Task<bool> RefreshFacesAsync(HikTerminalClient client, DateTimeOffset now)
    {
        _lastFaceRefresh = now;
        var faced = await client.ListFacedTabelsAsync();
        if (faced.SetEquals(FacedTabels))
            return false;
        FacedTabels = faced;
        return true;
    }

    public async Task PollAsync()
    {
        if (_busy || !IsEnabled)
            return;
        _busy = true;
        try
        {
            if (_agentRunning())
            {
                await AgentModeTickAsync();
                return;
            }
            IsAgentMode = false;

            // Yuzli xodimlar ro'yxatini vaqti-vaqti bilan yangilaymiz (qatorda yuz bor/yo'qligi ko'rsatkichi uchun).
            var facesChanged = false;
            var result = await _poller.PollAsync(async client =>
            {
                var now = _service.Clock.GetUtcNow();
                if (now - _lastFaceRefresh >= TimeSpan.FromSeconds(30))
                    facesChanged = await RefreshFacesAsync(client, now);
            });
            if (!result.Connected)
            {
                IsConnected = false;
                UpdateStatus();
                return;
            }

            IsConnected = true;
            LastError = null;
            UpdateStatus();
            if (result.Added > 0 || facesChanged)
                Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (TerminalException ex)
        {
            IsConnected = false;
            LastError = ex.Message;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            LastError = ex.Message;
            Log.Warning(ex, "Terminal pollda xato");
            UpdateStatus();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Agent rejimi: voqealarni o'qimaydi (agent o'qiydi). Holat — agent oxirgi marta terminalga ulangan vaqt (qurilma
    /// yozuvi, bazadan yangilanadi). Yuzli xodimlar ro'yxati kamdan-kam o'qiladi (faqat o'qish, skansiz).
    /// </summary>
    private async Task AgentModeTickAsync()
    {
        IsAgentMode = true;
        var now = _service.Clock.GetUtcNow();
        IsConnected = _service.TerminalDevice?.LastSeenAt is { } seen && now - seen <= AgentFreshness;
        LastError = null;
        UpdateStatus();
        if (now - _lastFaceRefresh < AgentFaceRefresh)
            return;
        _lastFaceRefresh = now;
        try
        {
            var acq = await AcquireAsync();
            if (acq is not { } a)
                return;
            using var client = a.Client;
            if (await RefreshFacesAsync(client, now))
                Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is TerminalException or HttpRequestException or TaskCanceledException)
        {
            Log.Information("Agent rejimi: yuzlar ro'yxatini o'qib bo'lmadi: {Error}", ex.Message);
        }
    }

    // ─── Desktop → Terminal (xodimlar) ───────────────────────────────────────
    // Desktop asosiy manba: xodim shu yerda qo'shiladi/tahrirlanadi, terminalga yoziladi.

    /// <summary>Xodimni terminalга yozadi/yangilaydi (ID=TabelNo, ism). Best-effort: terminal o'chiq/xato bo'lsa jim o'tadi.</summary>
    public async Task PushEmployeeAsync(Employee emp)
    {
        if (!IsEnabled || emp.TabelNo <= 0)
            return;
        try
        {
            var acq = await AcquireAsync();
            if (acq is not { } a)
                return;
            using var client = a.Client;
            await client.UpsertUserAsync(emp.TabelNo.ToString(), emp.FullName, emp.IsActive);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Xodimni terminalга yozishда xato (TabelNo {Tabel})", emp.TabelNo);
        }
    }

    /// <summary>
    /// Bosqich 3: serverdan (boshqa zavoddan) kelgan xodimlarni shu terminalga yozadi (nofaol — bloklanadi). Best-effort.
    /// </summary>
    public async Task PushPulledEmployeesAsync(IEnumerable<Employee> employees)
    {
        try
        {
            if (await _poller.PushEmployeesAsync(employees, allowDiscovery: !_agentRunning()) > 0)
                Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Serverdan kelgan xodimlarni terminalga yozishda xato");
        }
    }

    /// <summary>Xodimni terminaldan o'chiradi (yuzi bilan). Best-effort.</summary>
    public async Task RemoveEmployeeAsync(int tabelNo)
    {
        if (!IsEnabled || tabelNo <= 0)
            return;
        try
        {
            var acq = await AcquireAsync();
            if (acq is not { } a)
                return;
            using var client = a.Client;
            await client.DeleteUserAsync(tabelNo.ToString());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Xodimni terminaldan o'chirishда xato (TabelNo {Tabel})", tabelNo);
        }
    }

    /// <summary>Hamma (berilgan) xodimni terminalга yozadi. (muvaffaqiyatli, xato) sonini qaytaradi.</summary>
    public async Task<(int Ok, int Fail)> SyncAllAsync(IReadOnlyList<Employee> employees)
    {
        if (!IsEnabled)
            return (0, 0);
        var acq = await AcquireAsync();
        if (acq is not { } a)
            return (0, 0);
        using var client = a.Client;
        int ok = 0, fail = 0;
        foreach (var emp in employees)
        {
            if (emp.TabelNo <= 0)
                continue;
            try
            {
                await client.UpsertUserAsync(emp.TabelNo.ToString(), emp.FullName, emp.IsActive);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                Log.Warning(ex, "Sinxronlashда xato (TabelNo {Tabel})", emp.TabelNo);
            }
        }
        return (ok, fail);
    }

    /// <summary>Xodimга yuz (foto) yuklaydi: avval UserInfo bo'lishini kafolatlaydi (ID+ism), keyin yuzni yozadi. Xato bo'lsa — otadi (UI xabar ko'rsatadi).</summary>
    public async Task UploadFaceAsync(Employee emp, byte[] jpeg)
    {
        var acq = await AcquireAsync() ?? throw new TerminalException(Strings.TerminalNotConnected);
        using var client = acq.Client;
        await client.UpsertUserAsync(emp.TabelNo.ToString(), emp.FullName, emp.IsActive);
        await client.UploadFaceAsync(emp.TabelNo.ToString(), jpeg);
        MarkFaced(emp.TabelNo);
    }

    /// <summary>
    /// Terminal kamerasi orqali yuz oladi va xodimga biriktiradi (masofadan). Xato bo'lsa — otadi.
    /// Qo'lda rejimda (Kelish/Ketish) terminal tanigan yuzga "Holatni tanlang" so'rab capture'ni to'sadi —
    /// shuning uchun capture vaqtida rejim vaqtincha "holatsiz"ga o'tkaziladi va oxirida albatta tiklanadi.
    /// </summary>
    public async Task CaptureFaceAsync(Employee emp)
    {
        var acq = await AcquireAsync() ?? throw new TerminalException(Strings.TerminalNotConnected);
        using var client = acq.Client;
        var restoreManual = _service.Settings.TerminalRequireConfirm;
        if (restoreManual)
            await client.SetAttendanceModeAsync(false);
        var captured = false;
        try
        {
            await client.UpsertUserAsync(emp.TabelNo.ToString(), emp.FullName, emp.IsActive);
            var jpeg = await client.CaptureFaceAsync();
            await client.UploadFaceAsync(emp.TabelNo.ToString(), jpeg);
            MarkFaced(emp.TabelNo);
            captured = true;
        }
        finally
        {
            if (restoreManual)
            {
                try
                {
                    await client.SetAttendanceModeAsync(true);
                }
                catch (Exception ex) when (!captured)
                {
                    // Asl (capture) xatosi muhimroq — uni yashirmaymiz; tiklash xatosi faqat logga.
                    Log.Warning(ex, "Yuz olishdan keyin davomat rejimini tiklab bo'lmadi");
                }
            }
        }
    }

    /// <summary>Qurilma nomi UI tilida (desktop terminalni o'zi so'raganda).</summary>
    private static string DeviceName(TerminalInfo info) =>
        string.IsNullOrWhiteSpace(info.Model) ? Strings.FaceTerminal : $"{Strings.FaceTerminal} ({info.Model})";

    private void UpdateStatus()
    {
        if (!IsEnabled)
            StatusText = Strings.TerminalDisabled;
        else if (IsAgentMode)
            StatusText = Strings.TerminalViaAgent(_service.TerminalDevice?.LastSeenAt?.ToLocalTime());
        else if (IsConnected)
            StatusText = Strings.TerminalConnected;
        else
            StatusText = LastError ?? Strings.TerminalNotConnected;
    }

    /// <summary>Sozlamalardagi [Tekshirish] uchun: ulanib, qurilma ma'lumotini qaytaradi (xato bo'lsa — otadi).</summary>
    public static async Task<TerminalInfo> TestAsync(string url, string password)
    {
        using var client = new HikTerminalClient(url, "admin", password);
        return await client.GetInfoAsync();
    }
}
