using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Services;

namespace Oylik.Mobile;

public partial class App : Application
{
    private readonly LocalStore _store;
    private readonly Navigator _nav;
    private readonly SyncScheduler _scheduler;

    public App(LocalStore store, Navigator nav, SyncScheduler scheduler)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        _store = store;
        _nav = nav;
        _scheduler = scheduler;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Lokal SQLite — tez; sqlite-net async'i thread pool'da ishlaydi, shuning uchun bloklash xavfsiz.
        var settings = Task.Run(_store.GetSettingsAsync).GetAwaiter().GetResult();
        DeviceDisplay.Current.KeepScreenOn = settings.KeepScreenOn;
        Logic.Diag.Log($"start: v{AppInfo.Current.BuildString} bog'langan={settings.IsPaired} uzilgan={settings.Revoked} qurilma={settings.DeviceId}");

        var window = new Window(settings.IsPaired ? _nav.CreateMain() : _nav.CreatePair(settings.Revoked ? Logic.Strings.DeviceRevoked : null));
        window.Resumed += (_, _) => _scheduler.Start(kick: true);
        window.Stopped += (_, _) => _scheduler.Stop();
        window.Created += (_, _) => _scheduler.Start(kick: true);
        return window;
    }
}
