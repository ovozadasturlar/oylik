using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Sync;
using Oylik.Mobile.Pages;
using Oylik.Mobile.Services;
using Oylik.Mobile.ViewModels;
using ZXing.Net.Maui.Controls;

namespace Oylik.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Diagnostika — logcat (teg "Oylik"): adb logcat -s Oylik
        Logic.Diag.Sink = msg => Android.Util.Log.Info("Oylik", msg);
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeReader();

        // Maydonlar o'z ramkasida (Border) — Android'ning standart pastki chizig'i kerak emas.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("OylikNoUnderline", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
        });

        var s = builder.Services;
        s.AddSingleton(TimeProvider.System);
        s.AddSingleton(_ => new LocalStore(Path.Combine(FileSystem.AppDataDirectory, "oylik-qorovul.db3")));
        s.AddSingleton<ITokenStore, SecureTokenStore>();
        s.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
        s.AddSingleton<HttpOylikApi>();
#if DEBUG
        // Soxta server ("demo") — faqat Debug'da; Release APK'da qorovul uni tasodifan tanlay olmaydi.
        s.AddSingleton(sp => new FakeOylikApi(sp.GetRequiredService<TimeProvider>()).SeedDemo());
        s.AddSingleton<IOylikApi, ApiRouter>();
        PairingService.AllowDemo = true;
#else
        s.AddSingleton<IOylikApi>(sp => sp.GetRequiredService<HttpOylikApi>());
        PairingService.AllowDemo = false;
#endif
        s.AddSingleton<SyncEngine>();
        s.AddSingleton<AttendanceService>();
        s.AddSingleton<TapHandler>();
        s.AddSingleton<PairingService>();
        s.AddSingleton<SyncScheduler>();
        s.AddSingleton<Navigator>();

        s.AddTransient<PairViewModel>();
        s.AddTransient<PairPage>();
        s.AddTransient<ScanPage>();
        s.AddSingleton<EmployeesViewModel>();
        s.AddSingleton<TodayViewModel>();
        s.AddSingleton<SyncViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddTransient<EmployeesPage>();
        s.AddTransient<TodayPage>();
        s.AddTransient<SyncPage>();
        s.AddTransient<SettingsPage>();
        s.AddTransient<AppShell>();

        return builder.Build();
    }
}
