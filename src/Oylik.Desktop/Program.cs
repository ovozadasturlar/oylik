using Avalonia;
using Oylik.Data;
using Serilog;

namespace Oylik.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppPaths.Default.EnsureCreated();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.Default.Logs, "oylik-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");

        try
        {
            if (args is ["--terminal-probe", var url, var pass, ..])
                return TerminalProbe.Run(url, pass).GetAwaiter().GetResult();
            if (args is ["--face-probe", var furl, var fpass, var emp, var name, var mode, ..])
                return TerminalProbe.Face(furl, fpass, emp, name, mode, args.Length > 6 ? args[6] : null).GetAwaiter().GetResult();
            if (args is ["--discover-probe", var dserial, var dpass, ..])
                return TerminalProbe.Discover(dserial, dpass).GetAwaiter().GetResult();
            Log.Information("Oylik ishga tushdi");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
