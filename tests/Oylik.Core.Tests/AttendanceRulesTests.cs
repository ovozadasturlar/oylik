using Oylik.Core.Domain;

namespace Oylik.Core.Tests;

public class AttendanceRulesTests
{
    private static readonly TimeSpan Uz = TimeSpan.FromHours(5);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UZ", Uz, "UZ", "UZ");
    private static readonly Guid Emp = Guid.NewGuid();
    private static readonly DateTimeOffset FarFuture = new(2030, 1, 1, 0, 0, 0, Uz);

    private static DateTimeOffset At(int day, int hour, int minute = 0, int month = 10) => new(2026, month, day, hour, minute, 0, Uz);

    private static AttendanceEvent In(DateTimeOffset at, Guid? emp = null) =>
        new() { EmployeeId = emp ?? Emp, Type = AttendanceEventType.In, OccurredAt = at, DeviceId = Guid.NewGuid() };

    private static AttendanceEvent Out(DateTimeOffset at, Guid? emp = null) =>
        new() { EmployeeId = emp ?? Emp, Type = AttendanceEventType.Out, OccurredAt = at, DeviceId = Guid.NewGuid() };

    private static EmployeeAttendance Analyze(DateTimeOffset? now, params AttendanceEvent[] events) =>
        AttendanceRules.Analyze(Emp, events, Zone, now ?? FarFuture);

    [Fact]
    public void A2_A7_day_shift_0802_to_1732_is_9_5_hours()
    {
        var a = Analyze(null, In(At(12, 8, 2)), Out(At(12, 17, 32)));

        a.Pairs.Should().ContainSingle().Which.Status.Should().Be(PairStatus.Complete);
        var day = a.Day(new DateOnly(2026, 10, 12))!;
        day.Minutes.Should().Be(570);
        day.Hours.Should().Be(9.5m);
        a.Issues.Should().BeEmpty();
    }

    [Fact]
    public void A5_night_shift_belongs_to_the_day_of_In()
    {
        var a = Analyze(null, In(At(12, 22)), Out(At(13, 6)));

        a.Days.Keys.Should().Equal(new DateOnly(2026, 10, 12));
        a.Day(new DateOnly(2026, 10, 12))!.Hours.Should().Be(8m);
        a.Day(new DateOnly(2026, 10, 13)).Should().BeNull();
    }

    [Fact]
    public void A5_work_day_uses_local_date_not_utc()
    {
        // 02:00 mahalliy = oldingi kun 21:00 UTC
        var a = Analyze(null, In(At(12, 2)), Out(At(12, 10)));
        a.Days.Keys.Should().Equal(new DateOnly(2026, 10, 12));
    }

    [Fact]
    public void A1_voided_events_are_ignored_and_order_is_by_time()
    {
        var voidedOut = Out(At(12, 12));
        voidedOut.IsVoided = true;
        // tartibsiz berilgan
        var a = Analyze(null, Out(At(12, 18)), voidedOut, In(At(12, 8)));

        a.Pairs.Should().ContainSingle();
        a.Day(new DateOnly(2026, 10, 12))!.Hours.Should().Be(10m);
    }

    [Fact]
    public void A2_shift_over_16_hours_leaves_In_open_and_Out_orphan()
    {
        var inn = In(At(12, 8));
        var outt = Out(At(13, 0, 1)); // 16 s 1 daq
        var a = Analyze(null, inn, outt);

        a.Pairs.Should().ContainSingle().Which.Status.Should().Be(PairStatus.OpenNoOut);
        a.OrphanOuts.Should().Equal(outt);
        a.Issues.Select(i => i.Kind).Should().Equal(AttendanceIssueKind.OpenNoOut, AttendanceIssueKind.OrphanOut);
        a.Day(new DateOnly(2026, 10, 12))!.HasComplete.Should().BeFalse();
    }

    [Fact]
    public void A2_exactly_16_hours_is_paired()
    {
        var a = Analyze(null, In(At(12, 8)), Out(At(13, 0)));
        a.Pairs.Should().ContainSingle().Which.Status.Should().Be(PairStatus.Complete);
        a.Day(new DateOnly(2026, 10, 12))!.Hours.Should().Be(16m);
    }

    [Fact]
    public void A2_In_without_Out_goes_to_confirmation_after_16_hours()
    {
        var inn = In(At(12, 8));

        Analyze(At(12, 20), inn).Pairs.Single().Status.Should().Be(PairStatus.Inside);
        Analyze(At(12, 20), inn).CurrentInside.Should().NotBeNull();
        Analyze(At(12, 20), inn).Issues.Should().BeEmpty();

        var later = Analyze(At(13, 0, 1), inn);
        later.Pairs.Single().Status.Should().Be(PairStatus.OpenNoOut);
        later.CurrentInside.Should().BeNull();
        later.Issues.Should().ContainSingle().Which.Should().Be(new AttendanceIssue(Emp, AttendanceIssueKind.OpenNoOut, inn));
    }

    [Fact]
    public void A3_Out_without_In_is_orphan()
    {
        var outt = Out(At(12, 17));
        var a = Analyze(null, outt);

        a.Pairs.Should().BeEmpty();
        a.Issues.Should().ContainSingle().Which.Kind.Should().Be(AttendanceIssueKind.OrphanOut);
    }

    [Fact]
    public void A3_second_Out_after_pair_is_orphan()
    {
        var second = Out(At(12, 18));
        var a = Analyze(null, In(At(12, 8)), Out(At(12, 17)), second);

        a.Pairs.Should().ContainSingle().Which.Status.Should().Be(PairStatus.Complete);
        a.OrphanOuts.Should().Equal(second);
    }

    [Fact]
    public void A4_two_Ins_in_a_row_first_is_open_second_starts_new_pair()
    {
        var first = In(At(12, 8));
        var second = In(At(12, 9));
        var a = Analyze(null, first, second, Out(At(12, 17)));

        a.Pairs.Should().HaveCount(2);
        a.Pairs[0].Should().Match<AttendancePair>(p => p.In == first && p.Status == PairStatus.OpenNoOut);
        a.Pairs[1].Should().Match<AttendancePair>(p => p.In == second && p.Status == PairStatus.Complete);
        a.Day(new DateOnly(2026, 10, 12))!.Hours.Should().Be(8m);
        a.Issues.Should().ContainSingle().Which.Event.Should().Be(first);
    }

    [Fact]
    public void A6_multiple_pairs_in_a_day_are_summed()
    {
        var a = Analyze(null,
            In(At(12, 8)), Out(At(12, 12, 30)),
            In(At(12, 13, 15)), Out(At(12, 18)));

        var day = a.Day(new DateOnly(2026, 10, 12))!;
        day.Pairs.Should().HaveCount(2);
        day.Minutes.Should().Be(270 + 285);
        day.Hours.Should().Be(9.25m);
    }

    [Fact]
    public void A7_hours_are_rounded_to_two_places_and_day_equivalent_divides_by_hours_per_day()
    {
        AttendanceRules.Hours(1).Should().Be(0.02m);
        AttendanceRules.Hours(500).Should().Be(8.33m);
        AttendanceRules.DayEquivalent(9.5m, 8).Should().Be(1.19m);
        AttendanceRules.DayEquivalent(171m, 8).Should().Be(21.38m);
    }

    [Fact]
    public void A7_seconds_are_truncated_to_minutes_as_shown_on_screen()
    {
        var a = Analyze(null, In(At(12, 8, 2).AddSeconds(50)), Out(At(12, 17, 32).AddSeconds(10)));
        a.Day(new DateOnly(2026, 10, 12))!.Hours.Should().Be(9.5m);
    }

    [Fact]
    public void A8_month_summary_counts_days_with_complete_pair()
    {
        var a = Analyze(null,
            In(At(30, 22, month: 9)), Out(At(1, 6)),        // sentyabrga tegishli
            In(At(1, 8)), Out(At(1, 17)),                     // 9
            In(At(2, 8)), Out(At(2, 12)),                     // 4
            In(At(3, 8)),                                     // ochiq — hisoblanmaydi
            In(At(31, 22)), Out(At(1, 6, month: 11)));        // 8, oktabrga

        var s = a.Summarize(new MonthKey(2026, 10), 8);
        s.AttendedDays.Should().Be(3);
        s.TotalHours.Should().Be(21m);
        s.DayEquivalent.Should().Be(2.63m);

        a.Summarize(new MonthKey(2026, 9), 8).Should().Be(new AttendanceMonthSummary(1, 8m, 1m));
    }

    [Fact]
    public void Analyze_all_groups_by_employee()
    {
        var other = Guid.NewGuid();
        var all = AttendanceRules.AnalyzeAll([In(At(12, 8)), Out(At(12, 16)), In(At(12, 9), other)], Zone, FarFuture);

        all.Should().HaveCount(2);
        all[Emp].Day(new DateOnly(2026, 10, 12))!.Hours.Should().Be(8m);
        all[other].Issues.Should().ContainSingle();
    }

    [Fact]
    public void A10_same_type_within_two_minutes_is_duplicate()
    {
        var first = In(At(12, 8, 2));
        var existing = new[] { first, Out(At(12, 8, 3)) };

        AttendanceRules.FindDuplicateTap(existing, Emp, AttendanceEventType.In, At(12, 8, 3)).Should().Be(first);
        AttendanceRules.FindDuplicateTap(existing, Emp, AttendanceEventType.In, At(12, 8, 4)).Should().BeNull();
        AttendanceRules.FindDuplicateTap(existing, Guid.NewGuid(), AttendanceEventType.In, At(12, 8, 3)).Should().BeNull();

        first.IsVoided = true;
        AttendanceRules.FindDuplicateTap(existing, Emp, AttendanceEventType.In, At(12, 8, 3)).Should().BeNull();
    }

    [Fact]
    public void A11_manual_event_and_void_are_audited_and_reason_is_required()
    {
        var clock = TestClock.Default();
        var engine = new AttendanceEngine(clock);
        var employee = new Employee { FullName = "Олим ака" };

        var e = engine.AddManual(employee, AttendanceEventType.Out, At(12, 17, 30), " Ketdi bosilmagan ");
        e.DeviceId.Should().BeNull();
        e.IsManual.Should().BeTrue();
        e.PendingPush.Should().BeTrue();
        e.Note.Should().Be("Ketdi bosilmagan");

        var act = () => engine.Void(employee, e, "  ");
        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.ReasonRequired);
        e.IsVoided.Should().BeFalse();

        engine.Void(employee, e, "Xato");
        e.IsVoided.Should().BeTrue();
        e.VoidReason.Should().Be("Xato");
        e.VoidedAt.Should().Be(clock.GetUtcNow());

        engine.Audit.Select(a => a.Field).Should().Equal("Added", "Voided");
        engine.Audit.Should().OnlyContain(a => a.EntityId == e.Id && a.Entity == AttendanceEngine.EntityName);
        engine.Audit[0].NewValue.Should().Be("Олим ака — Ketdi 12.10.2026 17:30");

        var again = () => engine.Void(employee, e, "yana");
        again.Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.AlreadyVoided);
    }

    [Fact]
    public void A12_received_more_than_10_minutes_late_is_flagged()
    {
        var e = In(At(12, 8));
        e.HasClockSkew.Should().BeFalse();
        e.ReceivedAt = At(12, 8, 10);
        e.HasClockSkew.Should().BeFalse();
        e.ReceivedAt = At(12, 8, 11);
        e.HasClockSkew.Should().BeTrue();
    }

    [Fact]
    public void A9_copy_attended_days_to_days_worked_with_audit_comment()
    {
        var engine = new PayrollEngine(new AppSettings(), TestClock.Default());
        var employee = engine.CreateEmployee("Сардор", 6_000_000);
        var period = engine.CreatePeriod(new MonthKey(2026, 10), [employee], []);
        var line = period.Lines.Single();

        engine.SetDaysWorked(period, line, 22, "Davomatdan ko'chirildi");

        line.DaysWorked.Should().Be(22);
        engine.Audit.Last().Comment.Should().Be("Davomatdan ko'chirildi");
    }
}
