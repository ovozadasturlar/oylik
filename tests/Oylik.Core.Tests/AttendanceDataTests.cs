using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;

namespace Oylik.Core.Tests;

public sealed class AttendanceDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = TestClock.Default();
    private static readonly TimeSpan Uz = TimeSpan.FromHours(5);
    private AppPaths Paths => new(_root);

    public AttendanceDataTests() => Paths.EnsureCreated();

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

    private static AttendanceEvent ServerEvent(Guid emp, AttendanceEventType type, DateTimeOffset at, long seq) => new()
    {
        EmployeeId = emp,
        Type = type,
        OccurredAt = at,
        DeviceId = Guid.NewGuid(),
        ReceivedAt = at.AddSeconds(5),
        ServerSeq = seq,
    };

    [Fact]
    public void Shared_constants_match_contracts()
    {
        AttendanceRules.DuplicateTapWindow.Should().Be(Contracts.Api.DuplicateTapWindow);
        AttendanceRules.ClockSkewWarning.Should().Be(Contracts.Api.ClockSkewWarning);
        ((int)AttendanceEventType.In).Should().Be((int)Contracts.AttendanceType.In);
        ((int)AttendanceEventType.Out).Should().Be((int)Contracts.AttendanceType.Out);
    }

    [Fact]
    public async Task Server_events_are_upserted_idempotently_and_survive_restart()
    {
        Guid emp;
        var inn = ServerEvent(Guid.Empty, AttendanceEventType.In, new DateTimeOffset(2026, 10, 9, 8, 2, 0, Uz), 1);
        using (var s = await OpenAsync())
        {
            emp = (await s.CreateEmployeeAsync("Олим ака", 6_000_000, null, null)).Id;
            inn.EmployeeId = emp;
            var outt = ServerEvent(emp, AttendanceEventType.Out, new DateTimeOffset(2026, 10, 9, 17, 32, 0, Uz), 2);

            (await s.ApplyServerEventsAsync([inn, outt], 2)).Should().Be(2);
            // takror — qo'shilmaydi
            var again = ServerEvent(emp, AttendanceEventType.In, inn.OccurredAt, 1);
            again.Id = inn.Id;
            (await s.ApplyServerEventsAsync([again], 2)).Should().Be(0);

            s.Settings.LastEventSeq.Should().Be(2);
            s.Settings.LastSyncAt.Should().NotBeNull();
        }

        using (var s = await OpenAsync())
        {
            s.AttendanceEvents.Should().HaveCount(2);
            s.Settings.LastEventSeq.Should().Be(2);
            var summary = await s.GetMonthSummariesAsync(new MonthKey(2026, 10));
            summary[emp].Should().Be(new AttendanceMonthSummary(1, 9.5m, 1.19m));
        }
    }

    [Fact]
    public async Task Server_void_is_applied_locally()
    {
        using var s = await OpenAsync();
        var emp = (await s.CreateEmployeeAsync("Сардор", 6_000_000, null, null)).Id;
        var e = ServerEvent(emp, AttendanceEventType.In, new DateTimeOffset(2026, 10, 9, 8, 0, 0, Uz), 5);
        await s.ApplyServerEventsAsync([e], 5);

        var voided = ServerEvent(emp, AttendanceEventType.In, e.OccurredAt, 5);
        voided.Id = e.Id;
        voided.IsVoided = true;
        voided.VoidReason = "Xato bosildi";
        voided.VoidedAt = e.OccurredAt.AddMinutes(1);
        await s.ApplyServerEventsAsync([voided], 6);

        s.AttendanceEvents.Single().IsVoided.Should().BeTrue();
        s.AttendanceEvents.Single().VoidReason.Should().Be("Xato bosildi");
        s.Settings.LastEventSeq.Should().Be(6);
    }

    [Fact]
    public async Task A11_manual_event_and_void_are_pending_push_and_audited()
    {
        using var s = await OpenAsync();
        var emp = await s.CreateEmployeeAsync("Сардор", 6_000_000, null, null);
        var e = await s.AddManualEventAsync(emp, AttendanceEventType.In, new DateTimeOffset(2026, 10, 9, 8, 0, 0, Uz), "Telefon o'chiq edi");

        s.PendingPushEvents.Should().Equal(e);
        await s.MarkPushedAsync([e.Id]);
        s.PendingPushEvents.Should().BeEmpty();

        var act = () => s.VoidEventAsync(e, "");
        await act.Should().ThrowAsync<DomainException>();
        e.IsVoided.Should().BeFalse();

        await s.VoidEventAsync(e, "Noto'g'ri xodim");
        s.PendingPushEvents.Should().Equal(e);

        // server eski (bekor qilinmagan) holatni qaytarsa ham lokal bekor qilish saqlanadi
        var stale = ServerEvent(emp.Id, AttendanceEventType.In, e.OccurredAt, 7);
        stale.Id = e.Id;
        await s.ApplyServerEventsAsync([stale], 7);
        e.IsVoided.Should().BeTrue();
        e.ServerSeq.Should().Be(7);

        var audit = await s.GetAuditAsync(e.Id);
        audit.Select(a => a.Field).Should().BeEquivalentTo(["Added", "Voided"]);
    }

    [Fact]
    public async Task A9_copy_attended_days_writes_days_worked_with_audit_only_in_open_month()
    {
        using var s = await OpenAsync();
        var emp = await s.CreateEmployeeAsync("Сардор", 6_000_000, null, null);
        var period = await s.CreatePeriodAsync(new MonthKey(2026, 10));
        var line = period.Lines.Single();

        await s.CopyAttendanceToDaysAsync(period, line, 22);
        line.DaysWorked.Should().Be(22);
        (await s.GetAuditAsync(line.Id)).First().Comment.Should().Be(PayrollService.CopiedFromAttendanceComment);

        await s.ClosePeriodAsync(period);
        var act = () => s.CopyAttendanceToDaysAsync(period, line, 20);
        await act.Should().ThrowAsync<DomainException>().Where(x => x.Error == DomainError.PeriodClosed);
    }

    [Fact]
    public async Task Employee_changes_raise_event_for_server_push()
    {
        using var s = await OpenAsync();
        int raised = 0;
        s.EmployeesChanged += (_, _) => raised++;

        var emp = await s.CreateEmployeeAsync("Сардор", 6_000_000, null, null);
        raised.Should().Be(1);
        await s.RenameAsync(emp, "Сардор ака");
        raised.Should().Be(2);
        await s.SaveSettingsAsync(x => x.CompanyName = "X");
        raised.Should().Be(2);
    }

    [Fact]
    public async Task Devices_are_replaced_from_server()
    {
        using var s = await OpenAsync();
        var a = new Device { Id = Guid.NewGuid(), Name = "Qorovul-1", PairedAt = _clock.GetUtcNow() };
        var b = new Device { Id = Guid.NewGuid(), Name = "Qorovul-2", PairedAt = _clock.GetUtcNow() };
        await s.ReplaceDevicesAsync([a, b]);
        await s.ReplaceDevicesAsync([new Device { Id = b.Id, Name = "Darvoza", PairedAt = b.PairedAt, IsActive = false }]);

        s.Devices.Should().ContainSingle().Which.Should().Match<Device>(d => d.Name == "Darvoza" && !d.IsActive);
        s.FindDevice(b.Id)!.Name.Should().Be("Darvoza");
    }
}
