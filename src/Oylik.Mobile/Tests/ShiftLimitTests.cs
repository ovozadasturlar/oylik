using System.Text.Json;
using Oylik.Contracts;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;
using Oylik.Mobile.Logic.Sync;
using static Oylik.Mobile.Tests.TestClock;

namespace Oylik.Mobile.Tests;

/// <summary>H14: sutkalik smena — "Ichkarida" xodimning juftlik chegarasi (EmployeeDto.MaxShiftMinutes) bilan; null — 16 soat.</summary>
public sealed class ShiftLimitTests : IAsyncLifetime
{
    private const int Sutkalik = 28 * 60; // 24:00 smena + 4 soat (Api.ShiftPairingSlack)

    private readonly TempDb _db = new();
    private readonly TestClock _clock = new(At(2026, 10, 13, 8, 0));
    private readonly MemoryTokenStore _tokens = new();
    private FakeOylikApi _server = null!;
    private SyncEngine _sync = null!;
    private AttendanceService _att = null!;
    private readonly Guid _guard = Guid.NewGuid();   // sutkalik qorovul
    private readonly Guid _worker = Guid.NewGuid();  // oddiy (jadvalsiz)

    private LocalStore Store => _db.Store;

    public async Task InitializeAsync()
    {
        _server = new FakeOylikApi(_clock);
        var at = _clock.GetUtcNow().AddDays(-1);
        _server.SetEmployee(new EmployeeDto(_guard, "Бахтиёр (сутка)", null, true, at, Sutkalik));
        _server.SetEmployee(new EmployeeDto(_worker, "Сардор", null, true, at));
        _sync = new SyncEngine(Store, _server, _tokens, _clock);
        _att = new AttendanceService(Store, _clock);
        var pairing = new PairingService(Store, _server, _tokens, _sync);
        (await pairing.PairAsync("demo", null, "482913", "Qorovul-1")).Should().BeNull();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<Presence> PresenceOf(Guid id) => (await _att.GetPresenceAsync([id]))[id];

    [Fact]
    public async Task Limit_is_stored_from_server()
    {
        var emps = (await Store.GetEmployeesAsync()).ToDictionary(e => e.Id);
        emps[_guard].MaxShiftMinutes.Should().Be(Sutkalik);
        emps[_guard].MaxShift.Should().Be(TimeSpan.FromHours(28));
        emps[_worker].MaxShiftMinutes.Should().BeNull();
        emps[_worker].MaxShift.Should().Be(Api.DefaultMaxShift);
    }

    [Fact]
    public async Task Sutkalik_stays_inside_after_20_hours_and_button_is_ketdi()
    {
        (await _att.RecordAsync(_guard, AttendanceType.In)).IsRecorded.Should().BeTrue();
        (await _att.RecordAsync(_worker, AttendanceType.In)).IsRecorded.Should().BeTrue();

        _clock.Advance(TimeSpan.FromHours(20)); // ertasi kuni 04:00

        var guard = await PresenceOf(_guard);
        guard.Kind.Should().Be(PresenceKind.Inside);
        guard.NextAction.Should().Be(AttendanceType.Out);
        var row = new EmployeeRow(_guard, "Бахтиёр (сутка)");
        row.Apply(guard, Tashkent);
        row.ButtonText.Should().Be(Strings.Out);
        row.StatusText.Should().Be($"{Strings.Inside} · 08:00");

        (await PresenceOf(_worker)).Kind.Should().Be(PresenceKind.None, "jadvalsiz xodim — 16 soat, avvalgidek");
    }

    [Fact]
    public async Task Sutkalik_ketdi_next_morning_pairs_24_hours()
    {
        await _att.RecordAsync(_guard, AttendanceType.In);
        _clock.Advance(TimeSpan.FromHours(24));
        (await _att.RecordAsync(_guard, AttendanceType.Out)).IsRecorded.Should().BeTrue();

        var day = await _att.GetDayAsync(new DateOnly(2026, 10, 13));
        var row = day.Rows.Single(r => r.EmployeeId == _guard);
        row.Worked.Should().Be(TimeSpan.FromHours(24));
        Format.Hours(row.Worked).Should().Be("24:00");
        (await PresenceOf(_guard)).Kind.Should().Be(PresenceKind.Left);
    }

    [Fact]
    public async Task Sutkalik_live_hours_on_today_screen_after_20_hours()
    {
        await _att.RecordAsync(_guard, AttendanceType.In);
        _clock.Advance(TimeSpan.FromHours(20).Add(TimeSpan.FromMinutes(15)));
        var row = (await _att.GetDayAsync(new DateOnly(2026, 10, 13))).Rows.Single(r => r.EmployeeId == _guard);
        row.Inside.Should().BeTrue();
        Format.Hours(row.Worked).Should().Be("20:15");
    }

    [Fact]
    public async Task Sutkalik_beyond_28_hours_is_not_inside()
    {
        await _att.RecordAsync(_guard, AttendanceType.In);
        _clock.Advance(TimeSpan.FromHours(28).Add(TimeSpan.FromMinutes(1)));
        (await PresenceOf(_guard)).Kind.Should().Be(PresenceKind.None);
    }

    [Fact]
    public async Task Changed_limit_on_server_is_picked_up_by_sync()
    {
        _clock.Advance(TimeSpan.FromMinutes(1));
        _server.SetEmployee(new EmployeeDto(_worker, "Сардор", null, true, _clock.GetUtcNow(), Sutkalik));
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await Store.GetEmployeesAsync()).Single(e => e.Id == _worker).MaxShiftMinutes.Should().Be(Sutkalik);
    }

    [Fact]
    public async Task Old_server_without_field_works_with_16_hours()
    {
        // Eski server javobi — "maxShiftMinutes" maydoni yo'q.
        var json = $$"""[{"id":"{{_worker}}","fullName":"Сардор","note":null,"isActive":true,"updatedAt":"2026-10-13T03:00:00+00:00"}]""";
        var list = JsonSerializer.Deserialize<List<EmployeeDto>>(json, Api.Json)!;
        list.Single().MaxShiftMinutes.Should().BeNull();
        await Store.UpsertEmployeesAsync(list);
        (await Store.GetEmployeesAsync()).Single(e => e.Id == _worker).MaxShift.Should().Be(TimeSpan.FromHours(16));
    }

    [Fact]
    public void Rules_default_is_16_hours_and_respects_given_limit()
    {
        var a = Guid.NewGuid();
        var events = new[] { Ev.In(a, At(2026, 10, 13, 8, 0)) };
        PresenceRules.GetPresence(events, At(2026, 10, 14, 0, 0), Tashkent).Kind.Should().Be(PresenceKind.Inside);   // 16:00
        PresenceRules.GetPresence(events, At(2026, 10, 14, 0, 1), Tashkent).Kind.Should().Be(PresenceKind.None);     // 16:01
        PresenceRules.GetPresence(events, At(2026, 10, 14, 4, 0), Tashkent, TimeSpan.FromHours(28)).Kind.Should().Be(PresenceKind.Inside);
        PresenceRules.GetPresence(events, At(2026, 10, 14, 12, 1), Tashkent, TimeSpan.FromHours(28)).Kind.Should().Be(PresenceKind.None);
    }

    [Fact]
    public async Task Old_employee_table_without_column_is_migrated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "oylik-mobile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "old.db3");
        var id = Guid.NewGuid();
        using (var old = new SQLite.SQLiteConnection(path))
        {
            old.Execute("""CREATE TABLE "Employee" ("Id" varchar(36) primary key not null, "FullName" varchar, "Note" varchar, "IsActive" integer, "UpdatedAtUtcTicks" bigint)""");
            old.Execute($"""INSERT INTO "Employee" VALUES ('{id}','Сардор',NULL,1,0)""");
        }
        var store = new LocalStore(path);
        try
        {
            var e = (await store.GetEmployeesAsync()).Single();
            e.MaxShiftMinutes.Should().BeNull();
            e.MaxShift.Should().Be(Api.DefaultMaxShift);
        }
        finally
        {
            await store.CloseAsync();
            try { Directory.Delete(dir, true); } catch { /* band */ }
        }
    }
}
