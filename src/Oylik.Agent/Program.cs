using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Oylik.Data;
using Oylik.Data.Agent;
using Serilog;

namespace Oylik.Agent;

// Oylik.Agent — fon jarayoni (Bosqich 2): terminal davomati → lokal baza → server.
// Ishga tushirish: Oylik.Agent.exe [--home <papka>] [--no-tray]   (papka berilmasa: OYLIK_HOME, bo'lmasa %LOCALAPPDATA%\Oylik)
// Ikki rejim (docs/agent.md):
//  - Windows service (session 0, UI yo'q) — headless, holat faqat logda va desktopdagi «fon xizmati orqali» yozuvida.
//  - Startup / foydalanuvchi sessiyasi — xuddi shu worker + tizim tray ikonasi (Avalonia, asosiy oynasiz).
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = AgentArgs.HomeOf(args) is { } home ? new AppPaths(home) : AppPaths.Default;
        paths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.Logs, "agent-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();

        try
        {
            // Terminalni faqat bitta jarayon so'raydi: shu baza uchun agent allaqachon ishlayotgan bo'lsa — chiqamiz.
            using var agentLock = AgentLock.TryAcquire(paths);
            if (agentLock is null)
            {
                Log.Warning("Shu baza uchun boshqa agent ishlayapti ({Db}) — chiqildi", paths.Database);
                return 0;
            }

            var tray = AgentArgs.WantsTray(args) && Environment.UserInteractive && !WindowsServiceHelpers.IsWindowsService();
            Log.Information("Oylik.Agent ishga tushdi: {Db} ({Lock}), rejim: {Mode}", paths.Database, agentLock.Name, tray ? "tray" : "headless");

            var feed = new AgentStatusFeed();
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddWindowsService(o => o.ServiceName = AgentArgs.ServiceName);
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);
            builder.Services.AddSingleton(paths);
            builder.Services.AddSingleton(feed);
            builder.Services.AddHostedService<AgentWorker>();
            using var host = builder.Build();

            if (tray)
                RunWithTray(host, paths, feed, args);
            else
                host.Run();

            Log.Information("Oylik.Agent to'xtadi");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Oylik.Agent yiqildi");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>Worker fon oqimlarida ishlaydi; asosiy (STA) oqimda — Avalonia tray. «Chiqish» yoki host to'xtashi ikkalasini yopadi.</summary>
    private static void RunWithTray(IHost host, AppPaths paths, AgentStatusFeed feed, string[] args)
    {
        host.Start();
        try
        {
            AppBuilder.Configure(() => new TrayApp(paths, feed, host.Services.GetRequiredService<IHostApplicationLifetime>()))
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            // Tray ochilmadi (masalan, grafik sessiya yo'q) — agent baribir headless ishlayveradi.
            Log.Error(ex, "Tray ikonasini ochib bo'lmadi — headless rejimda davom etiladi");
            host.WaitForShutdown();
            return;
        }
        host.StopAsync().GetAwaiter().GetResult();
    }
}
