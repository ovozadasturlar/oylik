using Oylik.Contracts;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;
using Oylik.Mobile.Logic.Sync;
using static Oylik.Mobile.Tests.TestClock;

namespace Oylik.Mobile.Tests;

public sealed class SyncTests : IAsyncLifetime
{
    private readonly TempDb _db = new();
    private readonly TestClock _clock = new(At(2026, 10, 9, 8, 0));
    private readonly MemoryTokenStore _tokens = new();
    private FakeOylikApi _server = null!;
    private SyncEngine _sync = null!;
    private AttendanceService _att = null!;
    private PairingService _pairing = null!;

    private LocalStore Store => _db.Store;

    public async Task InitializeAsync()
    {
        _server = new FakeOylikApi(_clock).SeedDemo();
        _sync = new SyncEngine(Store, _server, _tokens, _clock);
        _att = new AttendanceService(Store, _clock);
        _pairing = new PairingService(Store, _server, _tokens, _sync);
        (await _pairing.PairAsync("demo", null, "482913", "Qorovul-1")).Should().BeNull();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<Guid> FirstEmployee() => (await Store.GetEmployeesAsync()).OrderBy(e => e.FullName).First().Id;

    [Fact]
    public async Task Pairing_loads_employees_and_stores_token()
    {
        (await Store.GetEmployeesAsync()).Should().HaveCount(12);
        (await _tokens.GetAsync()).Should().NotBeNullOrEmpty();
        var s = await Store.GetSettingsAsync();
        s.IsPaired.Should().BeTrue();
        s.InstallationId.Should().Be(FakeOylikApi.DemoInstallationId);
        s.LastSyncAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Tap_is_saved_locally_as_pending_then_sent()
    {
        var emp = await FirstEmployee();
        var r = await _att.RecordAsync(emp, AttendanceType.In);
        r.IsRecorded.Should().BeTrue();
        (await Store.FindEventAsync(r.Recorded!.Id))!.SyncState.Should().Be(SyncState.Pending);

        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await Store.FindEventAsync(r.Recorded.Id))!.SyncState.Should().Be(SyncState.Sent);
        _server.Events.Should().ContainSingle(e => e.Id == r.Recorded.Id);
    }

    [Fact]
    public async Task A10_second_tap_within_2_minutes_is_rejected()
    {
        var emp = await FirstEmployee();
        await _att.RecordAsync(emp, AttendanceType.In);
        _clock.Advance(TimeSpan.FromSeconds(30));
        var second = await _att.RecordAsync(emp, AttendanceType.In);
        second.IsRecorded.Should().BeFalse();
        Format.Time(second.Duplicate!.OccurredAt, Tashkent).Should().Be("08:00");
    }

    [Fact]
    public async Task Offline_10_taps_are_kept_and_sent_once_without_duplicates()
    {
        var emps = (await Store.GetEmployeesAsync()).Take(5).Select(e => e.Id).ToList();
        _server.Offline = true;
        foreach (var e in emps)
        {
            await _att.RecordAsync(e, AttendanceType.In);
            (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Failed);
        }
        _clock.Advance(TimeSpan.FromHours(8));
        foreach (var e in emps)
            await _att.RecordAsync(e, AttendanceType.Out);

        var s = await Store.GetSettingsAsync();
        (await Store.CountUnsentAsync(s.InstallationId)).Should().Be(10);
        s.LastError.Should().Be(Strings.ErrNoNetwork);

        _server.Offline = false;
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);

        _server.Events.Should().HaveCount(10);
        _server.Events.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        (await Store.CountUnsentAsync(s.InstallationId)).Should().Be(0);
    }

    [Fact]
    public async Task Failures_back_off_1_2_5_15_and_timer_respects_it()
    {
        _server.Offline = true;
        var expected = new[] { 1, 2, 5, 15, 15 };
        foreach (var m in expected)
        {
            (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Failed);
            var s = await Store.GetSettingsAsync();
            (s.NextAttemptAt!.Value - _clock.GetUtcNow()).Should().Be(TimeSpan.FromMinutes(m));
        }

        // Taymer (force=false) vaqti kelmaguncha so'rov yubormaydi.
        var calls = _server.Calls;
        (await _sync.SyncAsync(force: false)).Outcome.Should().Be(SyncOutcome.Skipped);
        _server.Calls.Should().Be(calls);

        _server.Offline = false;
        _clock.Advance(TimeSpan.FromMinutes(15));
        (await _sync.SyncAsync(force: false)).Outcome.Should().Be(SyncOutcome.Ok);
        (await Store.GetSettingsAsync()).ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task Undo_voids_event_on_server_even_if_not_sent_yet()
    {
        var emp = await FirstEmployee();
        var r = await _att.RecordAsync(emp, AttendanceType.In);
        (await _att.UndoAsync(r.Recorded!.Id)).Should().BeTrue();

        // Bekor qilingach yana bosish mumkin (A10 bloklamaydi).
        (await _att.RecordAsync(emp, AttendanceType.In)).IsRecorded.Should().BeTrue();

        await _sync.SyncAsync(force: true);
        _server.Events.Single(e => e.Id == r.Recorded.Id).IsVoided.Should().BeTrue();
        (await Store.FindEventAsync(r.Recorded.Id))!.VoidPending.Should().BeFalse();
    }

    [Fact]
    public async Task Events_from_other_devices_are_pulled_and_change_presence()
    {
        var emp = await FirstEmployee();
        _server.AddServerEvent(emp, AttendanceType.In, At(2026, 10, 9, 7, 45), Guid.NewGuid());
        await _sync.SyncAsync(force: true);

        var p = (await _att.GetPresenceAsync([emp]))[emp];
        p.Kind.Should().Be(PresenceKind.Inside);
        Format.Time(p.At!.Value, Tashkent).Should().Be("07:45");
    }

    [Fact]
    public async Task Server_401_marks_revoked_and_keeps_pending_events()
    {
        var emp = await FirstEmployee();
        await _att.RecordAsync(emp, AttendanceType.In);
        _server.RevokeAll();

        var res = await _sync.SyncAsync(force: true);
        res.Outcome.Should().Be(SyncOutcome.Revoked);
        res.Error.Should().Be(Strings.DeviceRevoked);

        var s = await Store.GetSettingsAsync();
        s.IsPaired.Should().BeFalse();
        (await _tokens.GetAsync()).Should().BeNull();
        (await Store.CountUnsentAsync(s.InstallationId)).Should().Be(1);

        // Qayta bog'langach — jo'natiladi.
        (await _pairing.PairAsync("demo", null, "111111", "Qorovul-1")).Should().BeNull();
        (await Store.CountUnsentAsync(s.InstallationId)).Should().Be(0);
        _server.Events.Should().ContainSingle(e => e.EmployeeId == emp);
    }

    [Fact]
    public async Task Clock_more_than_10_minutes_off_is_flagged()
    {
        _server.ServerClockShift = TimeSpan.FromMinutes(11);
        await _sync.SyncAsync(force: true);
        (await Store.GetSettingsAsync()).ClockWrong.Should().BeTrue();

        _server.ServerClockShift = TimeSpan.FromMinutes(3);
        await _sync.SyncAsync(force: true);
        (await Store.GetSettingsAsync()).ClockWrong.Should().BeFalse();
    }

    [Fact]
    public async Task Event_keeps_local_offset()
    {
        var emp = await FirstEmployee();
        var r = await _att.RecordAsync(emp, AttendanceType.In);
        (await Store.FindEventAsync(r.Recorded!.Id))!.OccurredAt.Offset.Should().Be(TimeSpan.FromHours(5));
    }

    [Fact]
    public async Task Batch_400_isolates_bad_event_marks_it_failed_and_sends_the_rest()
    {
        var emp = await FirstEmployee();
        var good = await _att.RecordAsync(emp, AttendanceType.In);
        var stranger = Guid.NewGuid(); // serverda yo'q xodim
        var bad = await _att.RecordAsync(stranger, AttendanceType.In);

        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);

        (await Store.FindEventAsync(good.Recorded!.Id))!.SyncState.Should().Be(SyncState.Sent);
        var failed = (await Store.FindEventAsync(bad.Recorded!.Id))!;
        failed.SyncState.Should().Be(SyncState.Failed);
        failed.LastError.Should().StartWith(Strings.ErrRejected);

        // Abadiy qayta urinilmaydi.
        var attempts = failed.Attempts;
        await _sync.SyncAsync(force: true);
        (await Store.FindEventAsync(bad.Recorded.Id))!.Attempts.Should().Be(attempts);
        var inst = (await Store.GetSettingsAsync()).InstallationId;
        (await Store.CountUnsentAsync(inst)).Should().Be(0);
        (await Store.GetFailedAsync(inst)).Should().ContainSingle();
    }

    [Fact]
    public async Task Pair_429_shows_retry_later_message()
    {
        (await _pairing.PairAsync("demo", null, "429429", "Qorovul-1")).Should().Be(Strings.ErrTooManyAttempts);
        (await _pairing.PairAsync("demo", null, "000000", "Qorovul-1")).Should().Be(Strings.ErrCodeRejected);
        (await _pairing.PairAsync("demo", null, "12a", "Qorovul-1")).Should().Be(Strings.ErrBadCode);
    }

    [Fact]
    public async Task Unpair_keeps_unsent_events()
    {
        var emp = await FirstEmployee();
        await _att.RecordAsync(emp, AttendanceType.In);
        var inst = (await Store.GetSettingsAsync()).InstallationId;
        await _pairing.UnpairAsync();

        (await Store.GetSettingsAsync()).IsPaired.Should().BeFalse();
        (await Store.GetEmployeesAsync()).Should().BeEmpty();
        (await Store.CountUnsentAsync(inst)).Should().Be(1);
    }
}

public class PairingParseTests
{
    [Fact]
    public void Qr_payload_from_desktop_is_parsed()
    {
        var id = Guid.NewGuid();
        var p = PairingService.ParseQr($$"""{"server":"https://oylik.example.uz","installationId":"{{id}}","code":"482913"}""");
        p.Should().Be(new PairingQrPayload("https://oylik.example.uz", id, "482913"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("""{"server":"https://x","installationId":"00000000-0000-0000-0000-000000000000","code":"482913"}""")]
    [InlineData("""{"server":"https://x","installationId":"6f1c2b9e-1d1a-4a4b-9a0e-0b8c7f3e2d11","code":"48291"}""")]
    public void Bad_qr_is_rejected(string text) => PairingService.ParseQr(text).Should().BeNull();

    [Theory]
    [InlineData("192.168.1.10:5000", "http://192.168.1.10:5000")]
    [InlineData("http://10.0.2.2:5000/", "http://10.0.2.2:5000")]
    [InlineData("https://oylik.uz", "https://oylik.uz")]
    [InlineData("DEMO", "demo")]
    [InlineData("ftp://x", null)]
    [InlineData("  ", null)]
    [InlineData("salom", null)]
    [InlineData("localhost:5000", "http://localhost:5000")]
    public void Server_url_is_normalized(string input, string? expected) =>
        PairingService.NormalizeServer(input).Should().Be(expected);
}
