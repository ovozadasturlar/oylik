using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Data.Terminal;

namespace Oylik.Core.Tests;

public sealed class TerminalSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-term-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = TestClock.Default();
    private static readonly TimeSpan Uz = TimeSpan.FromHours(5);
    private AppPaths Paths => new(_root);

    public TerminalSyncTests() => Paths.EnsureCreated();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<PayrollService> OpenAsync()
    {
        var service = new PayrollService(Paths, _clock);
        await service.InitializeAsync(dailyBackup: false);
        return service;
    }

    private static TerminalEvent Ev(long serial, string empNo, int h, int m, int s) =>
        new(serial, empNo, "OLIM AKA", new DateTimeOffset(2026, 11, 12, h, m, s, Uz), null);

    /// <summary>Qo'lda rejim: xodim tanlagan status bilan (checkIn/checkOut).</summary>
    private static TerminalEvent EvS(long serial, string status, int h, int m, int s) =>
        new(serial, "1", "OLIM AKA", new DateTimeOffset(2026, 11, 12, h, m, s, Uz), null, status);

    private static readonly Guid Dev = PayrollService.DeterministicGuid("hik-terminal:TESTSN");

    [Fact]
    public async Task Repeated_scans_in_window_collapse_to_one_and_then_toggle()
    {
        using var s = await OpenAsync();
        var emp = await s.CreateEmployeeAsync("OLIM AKA", 6_000_000, null, null);
        emp.TabelNo.Should().Be(1);

        // Haqiqiy terminal ketma-ketligi: 11:05:23/27/38, 11:06:23 (bitta kelish) → Keldi;
        // 11:12:57/13:00 (7 daqiqadan keyin) → Ketdi; 11:37:21/24 → Keldi.
        var events = new[]
        {
            Ev(32, "1", 11, 5, 23), Ev(34, "1", 11, 5, 27), Ev(36, "1", 11, 5, 38), Ev(39, "1", 11, 6, 23),
            Ev(42, "1", 11, 12, 57), Ev(44, "1", 11, 13, 0),
            Ev(47, "1", 11, 37, 21), Ev(49, "1", 11, 37, 24),
        };

        var added = await s.ApplyTerminalEventsAsync(events, Dev);
        added.Should().Be(3);

        var recorded = s.AttendanceEvents.Where(e => !e.IsVoided).OrderBy(e => e.OccurredAt).ToList();
        recorded.Select(e => e.Type).Should().Equal(AttendanceEventType.In, AttendanceEventType.Out, AttendanceEventType.In);
        recorded[0].OccurredAt.Hour.Should().Be(11);
        recorded[0].OccurredAt.Minute.Should().Be(5);
        recorded[1].OccurredAt.Minute.Should().Be(12);
        recorded[2].OccurredAt.Minute.Should().Be(37);
        recorded.Should().OnlyContain(e => e.DeviceId == Dev && e.PendingPush);
        s.Settings.TerminalLastSerial.Should().Be(49);
    }

    [Fact]
    public async Task Duplicate_tabel_numbers_are_skipped_without_stopping_the_poll()
    {
        using var s = await OpenAsync();
        var first = await s.CreateEmployeeAsync("OLIM AKA", 6_000_000, null, null);   // TabelNo 1
        var second = await s.CreateEmployeeAsync("SANJAR", 5_000_000, null, null);    // TabelNo 2
        var third = await s.CreateEmployeeAsync("SARDOR", 5_000_000, null, null);     // TabelNo 3
        third.TabelNo = first.TabelNo; // Bosqich 3: ko'p-zavod to'qnashuvi (diapazonsiz pull)

        var log = new List<string>();
        var added = await s.ApplyTerminalEventsAsync([Ev(10, "1", 8, 0, 0), Ev(11, "2", 8, 1, 0)], Dev, log.Add);

        added.Should().Be(1, "takroriy tabel 1 noaniq — o'tkazib yuboriladi, tabel 2 yoziladi");
        s.AttendanceEvents.Should().ContainSingle().Which.EmployeeId.Should().Be(second.Id);
        s.Settings.TerminalLastSerial.Should().Be(11);
        log.Should().ContainSingle().Which.Should().Be("Takroriy tabel 1 o'tkazib yuborildi");
    }

    [Fact]
    public async Task Manual_mode_records_selected_status_not_toggle()
    {
        using var s = await OpenAsync();
        await s.CreateEmployeeAsync("OLIM AKA", 6_000_000, null, null); // TabelNo 1

        // Haqiqiy qo'lda-rejim ketma-ketligi: Kelish 12:13, Ketish 12:19, keyin ataylab Kelish 14:22:26 →
        // Ketish 14:22:33 (7 soniyadan keyin, boshqa tugma) — hammasi yoziladi, toggle emas.
        var events = new[]
        {
            EvS(58, "checkIn", 12, 13, 39), EvS(61, "checkOut", 12, 19, 46),
            EvS(64, "checkIn", 14, 22, 26), EvS(67, "checkOut", 14, 22, 33),
        };

        var added = await s.ApplyTerminalEventsAsync(events, Dev);
        added.Should().Be(4);

        var recorded = s.AttendanceEvents.Where(e => !e.IsVoided).OrderBy(e => e.OccurredAt).ToList();
        recorded.Select(e => e.Type).Should().Equal(
            AttendanceEventType.In, AttendanceEventType.Out, AttendanceEventType.In, AttendanceEventType.Out);
    }

    [Fact]
    public async Task Manual_mode_same_status_repeat_in_window_collapses()
    {
        using var s = await OpenAsync();
        await s.CreateEmployeeAsync("OLIM AKA", 6_000_000, null, null); // TabelNo 1

        // Xodim "Kelish" ni bosib ketma-ket ikki marta tanildi (30 soniya ichida) — bitta Keldi bo'ladi.
        var added = await s.ApplyTerminalEventsAsync(
            [EvS(70, "checkIn", 9, 0, 0), EvS(71, "checkIn", 9, 0, 30)], Dev);

        added.Should().Be(1);
        s.AttendanceEvents.Single(e => !e.IsVoided).Type.Should().Be(AttendanceEventType.In);
    }

    [Fact]
    public async Task Reapplying_same_events_is_idempotent()
    {
        using var s = await OpenAsync();
        await s.CreateEmployeeAsync("OLIM AKA", 6_000_000, null, null);
        var events = new[] { Ev(32, "1", 11, 5, 23), Ev(42, "1", 11, 12, 57) };

        (await s.ApplyTerminalEventsAsync(events, Dev)).Should().Be(2);
        (await s.ApplyTerminalEventsAsync(events, Dev)).Should().Be(0); // kursor o'tgan
        s.AttendanceEvents.Count(e => !e.IsVoided).Should().Be(2);
    }

    [Fact]
    public async Task Unknown_employee_is_skipped_but_cursor_advances()
    {
        using var s = await OpenAsync();
        await s.CreateEmployeeAsync("OLIM AKA", 6_000_000, null, null); // TabelNo 1
        var events = new[] { Ev(50, "7", 9, 0, 0), Ev(51, "1", 9, 1, 0) };

        var added = await s.ApplyTerminalEventsAsync(events, Dev);
        added.Should().Be(1); // faqat TabelNo 1
        s.Settings.TerminalLastSerial.Should().Be(51);
        s.AttendanceEvents.Single(e => !e.IsVoided).Type.Should().Be(AttendanceEventType.In);
    }

    [Fact]
    public async Task Events_survive_restart()
    {
        using (var s = await OpenAsync())
        {
            await s.CreateEmployeeAsync("OLIM AKA", 6_000_000, null, null);
            await s.ApplyTerminalEventsAsync([Ev(32, "1", 8, 0, 0), Ev(60, "1", 17, 0, 0)], Dev);
        }
        using (var s = await OpenAsync())
        {
            s.AttendanceEvents.Count(e => !e.IsVoided).Should().Be(2);
            s.Settings.TerminalLastSerial.Should().Be(60);
        }
    }
}
