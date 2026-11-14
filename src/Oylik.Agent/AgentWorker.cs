using Microsoft.Extensions.Hosting;
using Oylik.Data;
using Oylik.Data.Agent;
using Serilog;

namespace Oylik.Agent;

/// <summary>Host ichidagi fon xizmati: <see cref="AgentRunner"/> siklini service/konsol to'xtaguncha aylantiradi.</summary>
public sealed class AgentWorker(AppPaths paths, AgentStatusFeed feed) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var runner = new AgentRunner(paths, log: message => Log.Information("{Message}", message));
        runner.StatusChanged += feed.Publish;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runner.InitializeAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Baza vaqtincha band/yo'q (masalan, desktop migratsiya qilyapti) — biroz kutib qayta urinamiz.
                Log.Error(ex, "Bazani ochib bo'lmadi: {Db}", paths.Database);
                feed.Publish(feed.Current with { TerminalError = ex.Message }); // tray: «Ishga tushmoqda…» + xato matni
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
        await runner.RunAsync(stoppingToken);
    }
}

/// <summary>Worker → tray: oxirgi holat (o'zgarmas surat) va o'zgarish hodisasi (worker oqimida chaqiriladi).</summary>
public sealed class AgentStatusFeed
{
    private AgentStatus _current = AgentStatus.Initial;

    public AgentStatus Current => Volatile.Read(ref _current);

    public event Action<AgentStatus>? Changed;

    public void Publish(AgentStatus status)
    {
        Volatile.Write(ref _current, status);
        Changed?.Invoke(status);
    }
}

/// <summary>Buyruq qatori: <c>--home &lt;papka&gt;</c>, <c>--no-tray</c>.</summary>
public static class AgentArgs
{
    public const string ServiceName = "OylikAgent";

    /// <summary>Tray ikonasi kerakmi (interaktiv sessiyada ham <c>--no-tray</c> bilan o'chiriladi).</summary>
    public static bool WantsTray(string[] args) => !args.Contains("--no-tray");

    public static string? HomeOf(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--home")
                return args[i + 1];
        return null;
    }
}
