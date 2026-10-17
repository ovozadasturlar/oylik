using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Sync;
using Oylik.Mobile.Pages;

namespace Oylik.Mobile.Services;

/// <summary>Token — Android Keystore bilan shifrlangan SecureStorage'da.</summary>
public sealed class SecureTokenStore : ITokenStore
{
    private const string Key = "oylik.deviceToken";

    public Task<string?> GetAsync() => SecureStorage.Default.GetAsync(Key);

    public Task SetAsync(string token) => SecureStorage.Default.SetAsync(Key, token);

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}

/// <summary>Bog'lash ekrani ↔ asosiy (tablar) almashinuvi.</summary>
public sealed class Navigator(IServiceProvider services)
{
    public Page CreateMain() => services.GetRequiredService<AppShell>();

    public Page CreatePair(string? notice)
    {
        var page = services.GetRequiredService<PairPage>();
        page.SetNotice(notice);
        return new NavigationPage(page)
        {
            BarBackgroundColor = Color.FromArgb("#FFFFFF"),
            BarTextColor = Color.FromArgb("#1C1F23"),
        };
    }

    private static Window? Window => Application.Current?.Windows.FirstOrDefault();

    public void ShowMain() => MainThread.BeginInvokeOnMainThread(() =>
    {
        try
        {
            if (Window is { } w) w.Page = CreateMain();
            else Logic.Diag.Log("nav: oyna yo'q — asosiy ekran ochilmadi");
        }
        catch (Exception ex) { Logic.Diag.Log($"nav: asosiy ekran xatosi {ex}"); }
    });

    public void ShowPair(string? notice) => MainThread.BeginInvokeOnMainThread(() =>
    {
        Logic.Diag.Log($"nav: bog'lash ekrani ({notice ?? "-"})");
        if (Window is not { } w) return;
        if (w.Page is not NavigationPage) w.Page = CreatePair(notice);
        else if (notice is not null && w.Page is NavigationPage { RootPage: PairPage p }) p.SetNotice(notice); // allaqachon shu ekranda — sabab ko'rinsin
    });
}

/// <summary>
/// TZ 7: ilova ochiq bo'lganda har 60 s (backoff'ni hisobga olib) va resume'da sinxronlash.
/// 401 kelsa — Bog'lash ekraniga qaytaradi.
/// </summary>
public sealed class SyncScheduler
{
    private readonly SyncEngine _engine;
    private readonly Navigator _nav;
    private IDispatcherTimer? _timer;

    public SyncScheduler(SyncEngine engine, Navigator nav)
    {
        _engine = engine;
        _nav = nav;
        _engine.Completed += r =>
        {
            if (r.Outcome is SyncOutcome.Ok or SyncOutcome.Failed or SyncOutcome.Revoked) Diag.Log($"sync: {r.Outcome} {r.Error}");
            if (r.Outcome == SyncOutcome.Revoked) _nav.ShowPair(Strings.DeviceRevoked);
        };
        // Tarmoq qaytishi bilan — backoff'ni kutmasdan darhol jo'natamiz (TZ 9: "2 daqiqa ichida").
        // Wi-Fi'ga ulandi — lokal server 5 daqiqani kutmasdan qayta tekshiriladi.
        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (!IsUsable(e.NetworkAccess)) return;
            if (e.ConnectionProfiles.Contains(ConnectionProfile.WiFi)) _engine.RecheckLocal();
            Run(force: true);
        };
    }

    /// <summary>Internetsiz Wi-Fi (NetworkAccess.Local) ham — lokal server shu tarmoqda bo'lishi mumkin.</summary>
    private static bool IsUsable(NetworkAccess a) => a is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet or NetworkAccess.Local;

    private static bool Online => IsUsable(Connectivity.Current.NetworkAccess);

    public void Start(bool kick)
    {
        if (_timer is null)
        {
            _timer = Application.Current!.Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(20); // tez-tez tekshiradi; haqiqiy oraliq — NextAttemptAt (60 s / backoff)
            _timer.Tick += (_, _) => Run(force: false);
        }
        if (!_timer.IsRunning) _timer.Start();
        if (kick) Run(force: false);
    }

    public void Stop() => _timer?.Stop();

    /// <summary>Yangi voqea yoki [Hozir sinxronlash] — darhol (backoff'siz).</summary>
    public void Kick() => Run(force: true);

    private void Run(bool force) => _ = Task.Run(async () =>
    {
        // Tarmoq umuman yo'q — urinmaymiz: backoff bekorga 15 daqiqagacha o'smasin. Voqealar Pending'da kutadi.
        if (!Online) return;
        try { await _engine.SyncAsync(force); }
        catch (Exception ex) { Diag.Log($"sync: kutilmagan xato {ex}"); }
    });
}

public static class Haptics
{
    public static void Tap()
    {
        try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(70)); }
        catch { /* vibratsiya yo'q qurilma */ }
    }
}
