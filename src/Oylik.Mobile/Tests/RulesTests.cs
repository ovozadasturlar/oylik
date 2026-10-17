using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;
using Oylik.Mobile.Logic.Sync;
using static Oylik.Mobile.Tests.TestClock;

namespace Oylik.Mobile.Tests;

public class PresenceTests
{
    private static readonly Guid A = Guid.NewGuid();

    [Fact]
    public void No_events_is_dash()
    {
        PresenceRules.GetPresence([], At(2026, 10, 9, 10, 0), Tashkent).Kind.Should().Be(PresenceKind.None);
    }

    [Fact]
    public void Last_in_within_16h_is_inside_and_button_is_out()
    {
        var p = PresenceRules.GetPresence([Ev.In(A, At(2026, 10, 9, 8, 2))], At(2026, 10, 9, 10, 0), Tashkent);
        p.Kind.Should().Be(PresenceKind.Inside);
        Format.Time(p.At!.Value, Tashkent).Should().Be("08:02");
        p.NextAction.Should().Be(AttendanceType.Out);
    }

    [Fact]
    public void In_older_than_16h_is_not_inside()
    {
        var p = PresenceRules.GetPresence([Ev.In(A, At(2026, 10, 8, 8, 0))], At(2026, 10, 9, 0, 1), Tashkent);
        p.Kind.Should().Be(PresenceKind.None);
        p.NextAction.Should().Be(AttendanceType.In);
    }

    [Fact]
    public void Out_today_is_left()
    {
        var p = PresenceRules.GetPresence(
            [Ev.In(A, At(2026, 10, 9, 8, 0)), Ev.Out(A, At(2026, 10, 9, 17, 30))], At(2026, 10, 9, 18, 0), Tashkent);
        p.Kind.Should().Be(PresenceKind.Left);
        Format.Time(p.At!.Value, Tashkent).Should().Be("17:30");
    }

    [Fact]
    public void Out_yesterday_is_dash()
    {
        var p = PresenceRules.GetPresence([Ev.Out(A, At(2026, 10, 8, 17, 30))], At(2026, 10, 9, 7, 0), Tashkent);
        p.Kind.Should().Be(PresenceKind.None);
    }

    [Fact]
    public void Voided_events_are_ignored()
    {
        var p = PresenceRules.GetPresence(
            [Ev.In(A, At(2026, 10, 9, 8, 0)), Ev.Out(A, At(2026, 10, 9, 9, 0), voided: true)], At(2026, 10, 9, 10, 0), Tashkent);
        p.Kind.Should().Be(PresenceKind.Inside);
    }

    [Fact]
    public void Night_shift_still_inside_after_midnight()
    {
        var p = PresenceRules.GetPresence([Ev.In(A, At(2026, 10, 8, 22, 0))], At(2026, 10, 9, 5, 0), Tashkent);
        p.Kind.Should().Be(PresenceKind.Inside);
    }
}

public class DuplicateTapTests
{
    private static readonly Guid A = Guid.NewGuid();

    [Fact]
    public void A10_same_type_within_2_minutes_is_duplicate()
    {
        var first = Ev.In(A, At(2026, 10, 9, 8, 2));
        PresenceRules.FindDuplicate([first], AttendanceType.In, At(2026, 10, 9, 8, 3)).Should().BeSameAs(first);
    }

    [Fact]
    public void A10_after_2_minutes_is_allowed()
    {
        var first = Ev.In(A, At(2026, 10, 9, 8, 2));
        PresenceRules.FindDuplicate([first], AttendanceType.In, At(2026, 10, 9, 8, 4)).Should().BeNull();
    }

    [Fact]
    public void A10_other_type_is_allowed()
    {
        PresenceRules.FindDuplicate([Ev.In(A, At(2026, 10, 9, 8, 2))], AttendanceType.Out, At(2026, 10, 9, 8, 3)).Should().BeNull();
    }

    [Fact]
    public void A10_voided_event_does_not_block()
    {
        PresenceRules.FindDuplicate([Ev.In(A, At(2026, 10, 9, 8, 2), voided: true)], AttendanceType.In, At(2026, 10, 9, 8, 3))
            .Should().BeNull();
    }
}

public class DayReportTests
{
    private static readonly LocalEmployee Olim = new() { Id = Guid.NewGuid(), FullName = "Олим ака", IsActive = true };
    private static readonly LocalEmployee Sardor = new() { Id = Guid.NewGuid(), FullName = "Сардор", IsActive = true };
    private static readonly DateOnly Oct9 = new(2026, 10, 9);

    [Fact]
    public void Completed_pair_08_02_to_17_32_is_9_5_hours()
    {
        var r = DayReportBuilder.Build([Olim],
            [Ev.In(Olim.Id, At(2026, 10, 9, 8, 2)), Ev.Out(Olim.Id, At(2026, 10, 9, 17, 32))],
            Oct9, At(2026, 10, 9, 20, 0), Tashkent);
        var row = r.Rows.Single();
        row.Hours.Should().Be(9.5);
        Format.Hours(row.Worked).Should().Be("9:30");
        row.Inside.Should().BeFalse();
        r.LeftCount.Should().Be(1);
    }

    [Fact]
    public void Night_shift_22_to_06_is_8_hours_on_first_day()
    {
        var events = new[] { Ev.In(Olim.Id, At(2026, 10, 8, 22, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 6, 0)) };
        var now = At(2026, 10, 9, 12, 0);

        DayReportBuilder.Build([Olim], events, new DateOnly(2026, 10, 8), now, Tashkent).Rows.Single().Hours.Should().Be(8);
        var next = DayReportBuilder.Build([Olim], events, Oct9, now, Tashkent).Rows.Single();
        next.Absent.Should().BeTrue();
        next.Hours.Should().Be(0);
    }

    [Fact]
    public void Open_in_today_counts_live_hours_and_inside()
    {
        var r = DayReportBuilder.Build([Olim, Sardor], [Ev.In(Olim.Id, At(2026, 10, 9, 8, 0))], Oct9, At(2026, 10, 9, 10, 30), Tashkent);
        var row = r.Rows.Single(x => x.EmployeeId == Olim.Id);
        row.Inside.Should().BeTrue();
        row.Hours.Should().Be(2.5);
        r.InsideCount.Should().Be(1);
        r.AbsentCount.Should().Be(1);
    }

    [Fact]
    public void Several_pairs_are_summed_and_voided_ignored()
    {
        var r = DayReportBuilder.Build([Olim],
        [
            Ev.In(Olim.Id, At(2026, 10, 9, 8, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 12, 0)),
            Ev.In(Olim.Id, At(2026, 10, 9, 13, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 17, 0)),
            Ev.Out(Olim.Id, At(2026, 10, 9, 18, 0), voided: true),
        ], Oct9, At(2026, 10, 9, 20, 0), Tashkent);
        var row = r.Rows.Single();
        row.Hours.Should().Be(8);
        Format.Time(row.FirstIn!.Value, Tashkent).Should().Be("08:00");
        Format.Time(row.LastOut!.Value, Tashkent).Should().Be("17:00");
    }

    [Fact]
    public void A4_double_in_first_stays_open_second_pairs()
    {
        var r = DayReportBuilder.Build([Olim],
            [Ev.In(Olim.Id, At(2026, 10, 9, 8, 0)), Ev.In(Olim.Id, At(2026, 10, 9, 9, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 17, 0))],
            Oct9, At(2026, 10, 9, 20, 0), Tashkent);
        var row = r.Rows.Single();
        row.Hours.Should().Be(8);
        Format.Time(row.FirstIn!.Value, Tashkent).Should().Be("08:00");
    }

    [Fact]
    public void Pair_longer_than_16h_is_not_counted()
    {
        var r = DayReportBuilder.Build([Olim],
            [Ev.In(Olim.Id, At(2026, 10, 9, 6, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 23, 0))],
            Oct9, At(2026, 10, 9, 23, 30), Tashkent);
        var row = r.Rows.Single();
        row.Hours.Should().Be(0);
        row.Inside.Should().BeFalse();
        row.Absent.Should().BeFalse();
    }
}

public class HoursFormatTests
{
    private static readonly LocalEmployee Olim = new() { Id = Guid.NewGuid(), FullName = "Олим ака", IsActive = true };
    private static readonly DateOnly Oct9 = new(2026, 10, 9);

    [Theory]
    [InlineData(0, 0, 0, "0:00")]
    [InlineData(0, 5, 0, "0:05")]
    [InlineData(0, 30, 0, "0:30")]
    [InlineData(8, 0, 0, "8:00")]
    [InlineData(9, 30, 0, "9:30")]
    [InlineData(4, 29, 50, "4:29")] // pastga, butun daqiqagacha
    [InlineData(0, 0, 59, "0:00")]
    [InlineData(15, 59, 59, "15:59")]
    public void Hours_are_shown_as_h_mm(int h, int m, int s, string expected)
    {
        Format.Hours(new TimeSpan(h, m, s)).Should().Be(expected);
    }

    [Fact]
    public void Five_minutes_pair_is_0_05_not_0_1()
    {
        var row = DayReportBuilder.Build([Olim],
            [Ev.In(Olim.Id, At(2026, 10, 9, 8, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 8, 5))],
            Oct9, At(2026, 10, 9, 20, 0), Tashkent).Rows.Single();
        Format.Hours(row.Worked).Should().Be("0:05");
    }

    [Fact]
    public void A6_several_pairs_sum_is_h_mm()
    {
        var row = DayReportBuilder.Build([Olim],
        [
            Ev.In(Olim.Id, At(2026, 10, 9, 8, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 8, 30)),
            Ev.In(Olim.Id, At(2026, 10, 9, 10, 30)), Ev.Out(Olim.Id, At(2026, 10, 9, 14, 30)),
        ], Oct9, At(2026, 10, 9, 20, 0), Tashkent).Rows.Single();
        Format.Hours(row.Worked).Should().Be("4:30");
    }

    [Fact]
    public void A6_sum_is_floored_once_after_adding_seconds()
    {
        // 2:14:40 + 2:14:40 = 4:29:20 → "4:29" (har juftlik alohida yaxlitlanmaydi)
        var row = DayReportBuilder.Build([Olim],
        [
            Ev.In(Olim.Id, At(2026, 10, 9, 8, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 8, 0).AddSeconds(2 * 3600 + 14 * 60 + 40)),
            Ev.In(Olim.Id, At(2026, 10, 9, 13, 0)), Ev.Out(Olim.Id, At(2026, 10, 9, 13, 0).AddSeconds(2 * 3600 + 14 * 60 + 40)),
        ], Oct9, At(2026, 10, 9, 20, 0), Tashkent).Rows.Single();
        Format.Hours(row.Worked).Should().Be("4:29");
    }

    [Fact]
    public void Live_inside_hours_are_h_mm_and_floored()
    {
        var row = DayReportBuilder.Build([Olim], [Ev.In(Olim.Id, At(2026, 10, 9, 8, 0))],
            Oct9, At(2026, 10, 9, 12, 29).AddSeconds(50), Tashkent).Rows.Single();
        row.Inside.Should().BeTrue();
        Format.Hours(row.Worked).Should().Be("4:29");
    }
}

public class BackoffTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(4, 15)]
    [InlineData(10, 15)]
    public void Backoff_1_2_5_15_minutes(int failures, int minutes)
    {
        Backoff.Delay(failures).Should().Be(TimeSpan.FromMinutes(minutes));
    }
}
