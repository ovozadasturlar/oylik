using System.Diagnostics;
using Oylik.Contracts;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Sync;
using static Oylik.Mobile.Tests.TestClock;

namespace Oylik.Mobile.Tests;

/// <summary>Ikki manzil: lokal (Wi-Fi) + global (domen). Avval lokal (2 s), ulanmasa global; globalda — har 5 daqiqada lokal qayta.</summary>
public sealed class ServerRoutesTests : IAsyncLifetime
{
    private const string Local = "http://192.168.1.19:5000";
    private const string Global = "https://oylik.example.uz";

    private readonly TempDb _db = new();
    private readonly TestClock _clock = new(At(2026, 10, 13, 8, 0));
    private readonly MemoryTokenStore _tokens = new();
    private FakeOylikApi _server = null!;
    private SyncEngine _sync = null!;
    private AttendanceService _att = null!;
    private PairingService _pairing = null!;

    private LocalStore Store => _db.Store;

    public Task InitializeAsync()
    {
        _server = new FakeOylikApi(_clock).SeedDemo();
        _sync = new SyncEngine(Store, _server, _tokens, _clock) { LocalProbeTimeout = TimeSpan.FromMilliseconds(300) };
        _att = new AttendanceService(Store, _clock);
        _pairing = new PairingService(Store, _server, _tokens, _sync);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>Desktop yasaydigan QR (ikkala manzil bilan).</summary>
    private static string Qr(string server, string? local) =>
        System.Text.Json.JsonSerializer.Serialize(new PairingQrPayload(server, FakeOylikApi.DemoInstallationId, "482913", local), Api.Json);

    private async Task PairByQrAsync(string server, string? local)
    {
        var p = PairingService.ParseQr(Qr(server, local))!;
        (await _pairing.PairAsync(p.Server, p.InstallationId, p.Code, "Qorovul-1", localServerInput: p.LocalServer)).Should().BeNull();
        _server.Log.Clear();
    }

    private async Task<Guid> RecordAsync()
    {
        var emp = (await Store.GetEmployeesAsync()).OrderBy(e => e.FullName).First().Id;
        _clock.Advance(TimeSpan.FromMinutes(3)); // A10 to'sig'idan o'tish
        var r = await _att.RecordAsync(emp, (await _att.GetPresenceAsync([emp]))[emp].NextAction);
        return r.Recorded!.Id;
    }

    private IEnumerable<string> Calls(string server) => _server.Log.Where(l => l.StartsWith(server + " ", StringComparison.Ordinal));

    // ---------- QR ----------

    [Fact]
    public void Qr_with_local_server_is_parsed()
    {
        var json = $$"""{"server":"{{Global}}","installationId":"{{FakeOylikApi.DemoInstallationId}}","code":"482913","localServer":"{{Local}}"}""";
        var p = PairingService.ParseQr(json)!;
        p.Server.Should().Be(Global);
        p.LocalServer.Should().Be(Local);
    }

    [Fact]
    public void Old_qr_without_local_server_still_works()
    {
        var json = $$"""{"server":"{{Local}}","installationId":"{{FakeOylikApi.DemoInstallationId}}","code":"482913"}""";
        var p = PairingService.ParseQr(json)!;
        p.Server.Should().Be(Local);
        p.LocalServer.Should().BeNull();
    }

    [Fact]
    public void Bad_local_server_in_qr_is_ignored_not_rejected()
    {
        var p = PairingService.ParseQr(Qr(Global, "bu manzil emas"))!;
        p.Should().NotBeNull();
        p.LocalServer.Should().BeNull();
    }

    // ---------- Bog'lash ----------

    [Fact]
    public async Task Pairing_goes_to_local_first_and_saves_both_urls()
    {
        var p = PairingService.ParseQr(Qr(Global, Local))!;
        (await _pairing.PairAsync(p.Server, p.InstallationId, p.Code, "Qorovul-1", localServerInput: p.LocalServer)).Should().BeNull();

        _server.Log.Should().Contain($"{Local} pair").And.NotContain($"{Global} pair");
        var s = await Store.GetSettingsAsync();
        s.ServerUrl.Should().Be(Global);
        s.LocalServerUrl.Should().Be(Local);
        s.ActiveServerUrl.Should().Be(Local);
        ServerRoutes.Describe(s).Should().Be("lokal (192.168.1.19)");
    }

    [Fact]
    public async Task Pairing_falls_back_to_global_when_local_unreachable()
    {
        _server.Unreachable.Add(Local);
        await PairByQrAsync(Global, Local);

        var s = await Store.GetSettingsAsync();
        s.IsPaired.Should().BeTrue();
        s.LocalServerUrl.Should().Be(Local, "lokal manzil saqlanadi — keyin qaytishi mumkin");
        s.ActiveServerUrl.Should().Be(Global);
        ServerRoutes.Describe(s).Should().Be(Strings.ServerInternet);
        (await Store.GetEmployeesAsync()).Should().HaveCount(12);
    }

    [Fact]
    public async Task Manual_pairing_uses_single_url()
    {
        (await _pairing.PairAsync(Global, null, "482913", "Qorovul-1")).Should().BeNull();
        var s = await Store.GetSettingsAsync();
        s.LocalServerUrl.Should().BeNull();
        s.HasLocalServer.Should().BeFalse();
        _server.Log.Should().OnlyContain(l => l.StartsWith(Global + " "));
    }

    // ---------- Sinxronizatsiya ----------

    [Fact]
    public async Task Local_down_falls_back_to_global_without_backoff()
    {
        await PairByQrAsync(Global, Local);
        var id = await RecordAsync();
        _server.Unreachable.Add(Local);

        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);

        (await Store.FindEventAsync(id))!.SyncState.Should().Be(SyncState.Sent);
        Calls(Local).Should().Equal($"{Local} health");
        Calls(Global).Should().Contain($"{Global} events");
        var s = await Store.GetSettingsAsync();
        s.ConsecutiveFailures.Should().Be(0);
        s.LastError.Should().BeNull();
        s.ActiveServerUrl.Should().Be(Global);
        s.NextAttemptAt.Should().Be(_clock.GetUtcNow() + Backoff.Interval);
        ServerRoutes.Describe(s).Should().Be("internet orqali");
    }

    [Fact]
    public async Task On_global_local_is_rechecked_only_every_5_minutes_and_comes_back()
    {
        await PairByQrAsync(Global, Local);
        _server.Unreachable.Add(Local);
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);

        // 1 daqiqadan keyin — lokal tekshirilmaydi (har so'rovda 2 s yo'qotmaymiz).
        _server.Log.Clear();
        _clock.Advance(TimeSpan.FromMinutes(1));
        (await _sync.SyncAsync(force: false)).Outcome.Should().Be(SyncOutcome.Ok);
        Calls(Local).Should().BeEmpty();

        // Lokal qaytdi, lekin 5 daqiqa o'tmagan — hali globalda.
        _server.Unreachable.Remove(Local);
        _clock.Advance(TimeSpan.FromMinutes(2));
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await Store.GetSettingsAsync()).ActiveServerUrl.Should().Be(Global);
        Calls(Local).Should().BeEmpty();

        // 5 daqiqa o'tdi — lokal qayta tekshiriladi va unga o'tiladi.
        _clock.Advance(TimeSpan.FromMinutes(3));
        var id = await RecordAsync();
        _server.Log.Clear();
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        Calls(Local).Should().Contain($"{Local} events");
        Calls(Global).Should().BeEmpty();
        (await Store.FindEventAsync(id))!.SyncState.Should().Be(SyncState.Sent);
        var s = await Store.GetSettingsAsync();
        s.ActiveServerUrl.Should().Be(Local);
        ServerRoutes.Describe(s).Should().Be("lokal (192.168.1.19)");
    }

    [Fact]
    public async Task Wifi_connected_rechecks_local_immediately()
    {
        await PairByQrAsync(Global, Local);
        _server.Unreachable.Add(Local);
        await _sync.SyncAsync(force: true);
        _server.Unreachable.Remove(Local);

        _clock.Advance(TimeSpan.FromSeconds(30));
        _sync.RecheckLocal(); // ConnectivityChanged + ConnectionProfile.WiFi
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await Store.GetSettingsAsync()).ActiveServerUrl.Should().Be(Local);
    }

    [Fact]
    public async Task On_local_it_stays_local_without_touching_global()
    {
        await PairByQrAsync(Global, Local);
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            (await _sync.SyncAsync(force: false)).Outcome.Should().Be(SyncOutcome.Ok);
        }
        Calls(Global).Should().BeEmpty();
        Calls(Local).Count(l => l.EndsWith(" health")).Should().Be(3);
    }

    [Fact]
    public async Task Only_global_configured_never_probes_anything_else()
    {
        await PairByQrAsync(Global, null);
        var id = await RecordAsync();
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await Store.FindEventAsync(id))!.SyncState.Should().Be(SyncState.Sent);
        _server.Log.Should().OnlyContain(l => l.StartsWith(Global + " "));
        ServerRoutes.Describe(await Store.GetSettingsAsync()).Should().Be(Strings.ServerInternet);
    }

    [Fact]
    public async Task Only_local_configured_works_and_is_shown_as_local()
    {
        await PairByQrAsync(Local, null);
        var id = await RecordAsync();
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await Store.FindEventAsync(id))!.SyncState.Should().Be(SyncState.Sent);
        var s = await Store.GetSettingsAsync();
        s.HasLocalServer.Should().BeFalse();
        ServerRoutes.Describe(s).Should().Be("lokal (192.168.1.19)");
    }

    [Fact]
    public async Task Both_down_grows_backoff_keeps_events_and_recovers()
    {
        await PairByQrAsync(Global, Local);
        var id = await RecordAsync();
        _server.Unreachable.Add(Local);
        _server.Unreachable.Add(Global);

        var r = await _sync.SyncAsync(force: true);
        r.Outcome.Should().Be(SyncOutcome.Failed);
        r.Error.Should().Be(Strings.ErrNoNetwork);
        var s = await Store.GetSettingsAsync();
        s.ConsecutiveFailures.Should().Be(1);
        s.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromMinutes(1));
        s.ActiveServerUrl.Should().BeNull();
        ServerRoutes.Describe(s).Should().Be(Strings.ServerNotConnected);
        Calls(Local).Should().NotBeEmpty();
        Calls(Global).Should().NotBeEmpty();

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await _sync.SyncAsync(force: false)).Outcome.Should().Be(SyncOutcome.Failed);
        s = await Store.GetSettingsAsync();
        s.ConsecutiveFailures.Should().Be(2);
        s.NextAttemptAt.Should().Be(_clock.GetUtcNow() + TimeSpan.FromMinutes(2));
        (await Store.FindEventAsync(id))!.SyncState.Should().Be(SyncState.Pending);

        // Faqat global qaytdi — o'tadi, backoff nolga tushadi.
        _server.Unreachable.Remove(Global);
        _clock.Advance(TimeSpan.FromMinutes(2));
        (await _sync.SyncAsync(force: false)).Outcome.Should().Be(SyncOutcome.Ok);
        s = await Store.GetSettingsAsync();
        s.ConsecutiveFailures.Should().Be(0);
        s.ActiveServerUrl.Should().Be(Global);
        (await Store.FindEventAsync(id))!.SyncState.Should().Be(SyncState.Sent);
    }

    [Fact]
    public async Task Hanging_local_times_out_quickly_and_global_is_used()
    {
        await PairByQrAsync(Global, Local);
        _server.Hanging.Add(Local);
        _sync.RecheckLocal();

        var sw = Stopwatch.StartNew();
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        (await Store.GetSettingsAsync()).ActiveServerUrl.Should().Be(Global);
    }

    [Fact]
    public void Default_probe_timeout_is_from_contracts()
    {
        new SyncEngine(Store, _server, _tokens, _clock).LocalProbeTimeout.Should().Be(Api.LocalServerProbeTimeout);
        Api.LocalServerProbeTimeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("http://192.168.1.19:5000", true)]
    [InlineData("http://10.0.0.5", true)]
    [InlineData("http://172.20.1.1:5000", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://server.local:5000", true)]
    [InlineData("https://oylik.example.uz", false)]
    [InlineData("http://172.32.0.1", false)]
    [InlineData("http://8.8.8.8", false)]
    public void Local_address_detection(string url, bool expected) => ServerRoutes.IsLocalAddress(url).Should().Be(expected);
}

public sealed class SettingsMigrationTests
{
    [Fact]
    public async Task Old_database_without_new_columns_opens_and_keeps_data()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oylik-mobile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "old.db3");
        using (var old = new SQLite.SQLiteConnection(path))
        {
            old.Execute("""CREATE TABLE "Settings" ("Id" integer primary key not null, "ServerUrl" varchar, "DeviceName" varchar, "LastSeq" bigint)""");
            old.Execute("""INSERT INTO "Settings" ("Id","ServerUrl","DeviceName","LastSeq") VALUES (1,'http://192.168.1.19:5000','Qorovul-1',42)""");
        }

        var store = new LocalStore(path);
        try
        {
            var s = await store.GetSettingsAsync();
            s.ServerUrl.Should().Be("http://192.168.1.19:5000");
            s.LastSeq.Should().Be(42);
            s.LocalServerUrl.Should().BeNull();
            s.ActiveServerUrl.Should().BeNull();

            s.LocalServerUrl = "http://192.168.1.19:5000";
            s.ServerUrl = "https://oylik.example.uz";
            await store.SaveSettingsAsync(s);
            (await store.GetSettingsAsync()).LocalServerUrl.Should().Be("http://192.168.1.19:5000");
        }
        finally
        {
            await store.CloseAsync();
            try { Directory.Delete(dir, true); } catch { /* band */ }
        }
    }
}
