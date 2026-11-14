using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Tests;

/// <summary>Bosqich 3: Sozlamalar → Server — ko'p-zavod ulanish formasi (tekshiruvlar; to'liq oqim Server.Tests'da).</summary>
public sealed class JoinSettingsTests : IAsyncLifetime
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "oylik-join-tests-" + Guid.NewGuid().ToString("N"));
    private PayrollService _service = null!;
    private MainWindowViewModel _main = null!;

    public async Task InitializeAsync()
    {
        var paths = new AppPaths(_home);
        paths.EnsureCreated();
        _service = new PayrollService(paths, TimeProvider.System);
        _main = new MainWindowViewModel(_service, paths) { NoSync = true };
        await _main.InitializeAsync();
        _main.Settings.Load();
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
    public async Task Join_form_validates_before_contacting_the_server()
    {
        var s = _main.Settings;
        s.IsServerConfigured.Should().BeFalse("ulanmagan — «mavjud o'rnatmaga ulanish» formasi ko'rinadi");

        await s.JoinInstallationCommand.ExecuteAsync(null);
        s.ServerMessage.Should().Be(Strings.ServerUrlRequired);

        s.GlobalServerUrl = "http://127.0.0.1:9";
        s.JoinInstallationId = "bu-id-emas";
        await s.JoinInstallationCommand.ExecuteAsync(null);
        s.ServerMessage.Should().Be(Strings.JoinInstallationIdInvalid);

        s.JoinInstallationId = Guid.NewGuid().ToString();
        await s.JoinInstallationCommand.ExecuteAsync(null);
        s.ServerMessage.Should().Be(Strings.JoinCodeRequired);
        _service.Settings.TabelBase.Should().Be(0, "hech narsa saqlanmadi");
    }

    [AvaloniaFact]
    public async Task Join_code_needs_a_configured_server()
    {
        await _main.Settings.CreateJoinCodeCommand.ExecuteAsync(null);
        _main.Settings.ServerMessage.Should().Be(Strings.ServerNotConfigured);
        _main.Settings.JoinCodeText.Should().BeNull();
    }
}
