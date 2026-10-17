using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;
using Oylik.Mobile.Logic.Sync;
using Oylik.Mobile.Services;

namespace Oylik.Mobile.ViewModels;

// ---------------- Bugun ----------------

public sealed record TodayRowView(
    string Name, string InText, Color InColor, string OutText, Color OutColor, string HoursText, Color HoursColor, bool HoursBold);

public sealed partial class TodayViewModel : ObservableObject
{
    private static readonly Color Ink = Color.FromArgb("#1C1F23");
    private static readonly Color Dim = Color.FromArgb("#6B7280");
    private static readonly Color Green = Color.FromArgb("#145C2B");
    private static readonly Color Red = Color.FromArgb("#8F1D22");

    private readonly AttendanceService _attendance;
    private List<DateOnly> _dates = [];

    public TodayViewModel(AttendanceService attendance, SyncEngine engine)
    {
        _attendance = attendance;
        engine.Completed += _ => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
    }

    /// <summary>Bugun va oldingi 30 kun (jami 31).</summary>
    [ObservableProperty] public partial List<string> DateLabels { get; set; } = [];
    [ObservableProperty] public partial int SelectedDateIndex { get; set; } = -1;
    [ObservableProperty] public partial List<TodayRowView> Rows { get; set; } = [];
    [ObservableProperty] public partial int InsideCount { get; set; }
    [ObservableProperty] public partial int LeftCount { get; set; }
    [ObservableProperty] public partial int AbsentCount { get; set; }
    [ObservableProperty] public partial string LiveNote { get; set; } = "";

    partial void OnSelectedDateIndexChanged(int value) => _ = RefreshAsync();

    public async Task RefreshAsync()
    {
        var now = _attendance.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        if (_dates.Count == 0 || _dates[0] != today)
        {
            var keep = SelectedDateIndex > 0 && SelectedDateIndex < _dates.Count ? _dates[SelectedDateIndex] : today;
            _dates = Enumerable.Range(0, 31).Select(i => today.AddDays(-i)).ToList();
            DateLabels = _dates.Select(Format.Date).ToList();
            var idx = Math.Max(0, _dates.IndexOf(keep));
            if (SelectedDateIndex != idx) { SelectedDateIndex = idx; return; } // OnSelectedDateIndexChanged qayta chaqiradi
        }
        if (SelectedDateIndex < 0) return;

        var report = await _attendance.GetDayAsync(_dates[SelectedDateIndex]);
        var tz = _attendance.TimeZone;
        Rows = report.Rows.Select(r => new TodayRowView(
            r.Name,
            r.FirstIn is { } i ? Format.Time(i, tz) : Strings.Dash, r.FirstIn is null ? Dim : Ink,
            r.LastOut is { } o && !r.Inside ? Format.Time(o, tz) : Strings.Dash, r.LastOut is null || r.Inside ? Dim : Ink,
            r.Absent ? Strings.Absent : Format.Hours(r.Worked) + (r.Inside ? " •" : ""),
            r.Absent ? Red : r.Inside ? Green : Ink,
            r.Inside)).ToList();
        InsideCount = report.InsideCount;
        LeftCount = report.LeftCount;
        AbsentCount = report.AbsentCount;
        LiveNote = report.InsideCount > 0 ? $"{Strings.LiveNote} ({Format.Time(now, tz)} holatiga)" : "";
    }
}

// ---------------- Sinxronizatsiya ----------------

public sealed partial class SyncViewModel : ObservableObject
{
    private readonly LocalStore _store;
    private readonly SyncEngine _engine;
    private readonly AttendanceService _attendance;

    public SyncViewModel(LocalStore store, SyncEngine engine, AttendanceService attendance)
    {
        _store = store;
        _engine = engine;
        _attendance = attendance;
        engine.Completed += _ => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
    }

    [ObservableProperty] public partial int PendingCount { get; set; }
    [ObservableProperty] public partial string LastOk { get; set; } = "";
    [ObservableProperty] public partial string ServerText { get; set; } = "";
    [ObservableProperty] public partial string ServerState { get; set; } = "";
    [ObservableProperty] public partial Color ServerStateColor { get; set; } = Colors.Gray;
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string NextTry { get; set; } = "";
    [ObservableProperty] public partial bool ClockWrong { get; set; }
    [ObservableProperty] public partial int FailedCount { get; set; }
    [ObservableProperty] public partial bool HasFailed { get; set; }
    [ObservableProperty] public partial string FailedError { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    public partial bool IsBusy { get; set; }

    public async Task RefreshAsync()
    {
        var s = await _store.GetSettingsAsync();
        var tz = _attendance.TimeZone;
        PendingCount = await _store.CountUnsentAsync(s.InstallationId);
        LastOk = s.LastSyncAt is { } t ? Format.DateTime(t, tz) : Strings.SyncNever;
        ServerText = ServerUrls(s);
        var ok = s.LastError is null && s.ActiveServerUrl is not null;
        ServerState = ServerRoutes.Describe(s);
        ServerStateColor = ok ? Color.FromArgb("#145C2B") : Color.FromArgb("#8F1D22");
        Error = s.LastError;
        HasError = s.LastError is not null;
        NextTry = s.LastError is not null && s.NextAttemptAt is { } n ? $"{Strings.SyncNextTry}: {Format.Time(n, tz)}" : "";
        ClockWrong = s.ClockWrong;
        var failed = await _store.GetFailedAsync(s.InstallationId);
        FailedCount = failed.Count;
        HasFailed = failed.Count > 0;
        FailedError = failed.FirstOrDefault()?.LastError ?? "";
        IsBusy = _engine.IsRunning;
    }

    /// <summary>Sozlangan manzil(lar): lokal birinchi qatorda.</summary>
    public static string ServerUrls(DeviceSettings s) =>
        FakeOylikApi.IsDemo(s.ServerUrl) ? Strings.Demo
        : s.HasLocalServer ? $"{s.LocalServerUrl}\n{s.ServerUrl}"
        : s.ServerUrl ?? "";

    private bool CanSync() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        try { await Task.Run(() => _engine.SyncAsync(force: true)); }
        finally { IsBusy = false; }
        await RefreshAsync();
    }
}

// ---------------- Sozlamalar ----------------

public sealed partial class SettingsViewModel(LocalStore store, PairingService pairing, Navigator nav, SyncScheduler scheduler) : ObservableObject
{
    private bool _loading;

    // Manzillarni o'zgartirish (qayta bog'lashsiz)
    [ObservableProperty] public partial bool IsEditingAddresses { get; set; }
    [ObservableProperty] public partial string EditServer { get; set; } = "";
    [ObservableProperty] public partial string EditLocalServer { get; set; } = "";
    [ObservableProperty] public partial string? AddressError { get; set; }
    [ObservableProperty] public partial bool HasAddressError { get; set; }
    [ObservableProperty] public partial string? AddressNotice { get; set; }
    [ObservableProperty] public partial bool HasAddressNotice { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAddressesCommand))]
    public partial bool IsCheckingAddresses { get; set; }

    partial void OnAddressErrorChanged(string? value) => HasAddressError = !string.IsNullOrEmpty(value);
    partial void OnAddressNoticeChanged(string? value) => HasAddressNotice = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task EditAddressesAsync()
    {
        var s = await store.GetSettingsAsync();
        EditServer = s.ServerUrl ?? "";
        EditLocalServer = s.LocalServerUrl ?? "";
        AddressError = null;
        AddressNotice = null;
        IsEditingAddresses = true;
    }

    [RelayCommand]
    private void CancelAddresses()
    {
        IsEditingAddresses = false;
        AddressError = null;
    }

    private bool CanSaveAddresses() => !IsCheckingAddresses;

    [RelayCommand(CanExecute = nameof(CanSaveAddresses))]
    private async Task SaveAddressesAsync()
    {
        IsCheckingAddresses = true;
        AddressError = null;
        try
        {
            var err = await Task.Run(() => pairing.UpdateAddressesAsync(EditServer, EditLocalServer));
            if (err is not null)
            {
                AddressError = err;
                return;
            }
            IsEditingAddresses = false;
            AddressNotice = Strings.AddrSaved;
            scheduler.Kick();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Diag.Log($"manzillar: kutilmagan xato {ex}");
            AddressError = $"{Strings.ErrServer}: {ex.Message}";
        }
        finally
        {
            IsCheckingAddresses = false;
        }
    }

    [ObservableProperty] public partial string DeviceName { get; set; } = "";
    [ObservableProperty] public partial string ServerText { get; set; } = "";
    [ObservableProperty] public partial bool KeepScreenOn { get; set; }
    public string Version => $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

    public async Task RefreshAsync()
    {
        var s = await store.GetSettingsAsync();
        _loading = true;
        DeviceName = s.DeviceName ?? "";
        ServerText = $"{ServerRoutes.Describe(s)}\n{SyncViewModel.ServerUrls(s)}";
        KeepScreenOn = s.KeepScreenOn;
        _loading = false;
    }

    partial void OnKeepScreenOnChanged(bool value)
    {
        if (_loading) return;
        DeviceDisplay.Current.KeepScreenOn = value;
        _ = SaveKeepScreenOnAsync(value);
    }

    private async Task SaveKeepScreenOnAsync(bool value)
    {
        var s = await store.GetSettingsAsync();
        s.KeepScreenOn = value;
        await store.SaveSettingsAsync(s);
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;
        var s = await store.GetSettingsAsync();
        var pending = await store.CountUnsentAsync(s.InstallationId);
        var text = Strings.DisconnectAsk + (pending > 0 ? $"\n\n{Strings.SyncPending}: {pending}. {Strings.DisconnectPendingWarn}" : "");
        if (!await page.DisplayAlertAsync(Strings.DisconnectTitle, text, Strings.Yes, Strings.No)) return;

        await pairing.UnpairAsync();
        nav.ShowPair(null);
    }
}

// ---------------- Bog'lash ----------------

public sealed partial class PairViewModel(PairingService pairing, Navigator nav, SyncScheduler scheduler) : ObservableObject
{
    [ObservableProperty] public partial string Server { get; set; } = "";
    [ObservableProperty] public partial string LocalServer { get; set; } = "";
    [ObservableProperty] public partial string Code { get; set; } = "";
    [ObservableProperty] public partial string DeviceName { get; set; } = Strings.DefaultDeviceName;
    [ObservableProperty] public partial string? Error { get; set; }
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string? Notice { get; set; }
    [ObservableProperty] public partial bool HasNotice { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    public partial bool IsBusy { get; set; }

    partial void OnErrorChanged(string? value) => HasError = !string.IsNullOrEmpty(value);
    partial void OnNoticeChanged(string? value) => HasNotice = !string.IsNullOrEmpty(value);

    private bool CanPair() => !IsBusy;

    /// <summary>Qo'lda: server (+ ixtiyoriy lokal) + kod; o'rnatmani server kod bo'yicha topadi (InstallationId = null).</summary>
    [RelayCommand(CanExecute = nameof(CanPair))]
    private Task PairAsync() => DoPairAsync(Server, null, Code, LocalServer);

    /// <summary>QR'dan: InstallationId bilan; "localServer" bo'lsa — avval lokal, keyin asosiy manzil.</summary>
    public Task PairFromQrAsync(Oylik.Contracts.PairingQrPayload p)
    {
        Server = p.Server;
        LocalServer = p.LocalServer ?? "";
        Code = p.Code;
        return DoPairAsync(p.Server, p.InstallationId, p.Code, p.LocalServer);
    }

    private async Task DoPairAsync(string server, Guid? installationId, string code, string? localServer)
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = null;
        try
        {
            // syncNow: false — token saqlangach darhol asosiy ekran; xodimlar fonda yuklanadi (sinxron xatosi bog'lashni bekor qilmaydi).
            var err = await Task.Run(() => pairing.PairAsync(server, installationId, code, DeviceName,
                localServerInput: localServer, syncNow: false));
            if (err is null)
            {
                Diag.Log("pair: asosiy ekranga o'tish");
                nav.ShowMain();
                scheduler.Kick();
            }
            else Error = err;
        }
        catch (Exception ex)
        {
            Diag.Log($"pair: kutilmagan xato {ex}");
            Error = $"{Strings.ErrServer}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
