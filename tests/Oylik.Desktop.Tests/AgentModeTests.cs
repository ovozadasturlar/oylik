using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Tests;

/// <summary>Bosqich 2: fon agenti ishlayotganda desktop terminal/serverga o'zi ulanmaydi, agent yozganini bazadan oladi.</summary>
public sealed class AgentModeTests : IAsyncLifetime
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "oylik-agentmode-tests-" + Guid.NewGuid().ToString("N"));
    private AppPaths _paths = null!;
    private PayrollService _service = null!;
    private MainWindowViewModel _main = null!;
    private bool _agentRunning;

    public async Task InitializeAsync()
    {
        _paths = new AppPaths(_home);
        _paths.EnsureCreated();
        _service = new PayrollService(_paths, TimeProvider.System);
        _main = new MainWindowViewModel(_service, _paths) { NoSync = true, AgentProbe = _ => _agentRunning };
        await _main.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        _main.Dispose();
        _service.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_home, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task Desktop_picks_up_events_written_by_the_agent_only_while_agent_runs()
    {
        var olim = await _service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);

        using (var agent = new PayrollService(_paths, TimeProvider.System, attendanceOnly: true))
        {
            await agent.InitializeAsync(dailyBackup: false);
            await agent.AddManualEventAsync(agent.Employees.Single(), AttendanceEventType.In, DateTimeOffset.UtcNow.AddHours(-1), null);
        }

        await _main.RefreshFromAgentAsync();
        _service.AttendanceEvents.Should().BeEmpty("agent ishlamayapti — desktop yagona yozuvchi, bazani qayta o'qimaydi");

        _agentRunning = true;
        await _main.RefreshFromAgentAsync();
        _service.AttendanceEvents.Should().ContainSingle(e => e.EmployeeId == olim.Id);
    }

    [AvaloniaFact]
    public async Task Server_sync_is_left_to_the_agent()
    {
        await _service.SaveServerConnectionAsync("http://127.0.0.1:9", null, "test-key");
        _main.Sync.Reset();
        _agentRunning = true;

        await _main.Sync.SyncNowAsync(); // tarmoqqa chiqmaydi (port 9 — javob yo'q bo'lardi)
        _main.Sync.IsAgentMode.Should().BeTrue();
        _main.Sync.LastError.Should().BeNull();
        _main.Sync.StatusText.Should().Be(Strings.ServerViaAgent(null));
    }
}
