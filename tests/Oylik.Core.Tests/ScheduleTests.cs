using Oylik.Core.Domain;

namespace Oylik.Core.Tests;

/// <summary>4.3 H1–H13: ish jadvali va soatbay hisob.</summary>
public sealed class ScheduleTests
{
    private static readonly TimeSpan Uz = TimeSpan.FromHours(5);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UZ", Uz, "UZ", "UZ");
    private static readonly MonthKey Oct = new(2026, 10); // 1-oktabr — payshanba, 31 kun
    private static readonly Guid Emp = Guid.NewGuid();

    private static WorkSchedule Daytime(int breakMinutes = 60) => new()
    {
        Name = "Kunduzgi", Kind = ScheduleKind.Weekly, WeekDays = WorkSchedule.MondayToSaturday,
        StartTime = new(8, 0), DurationMinutes = 9 * 60, BreakMinutes = breakMinutes,
    };

    private static WorkSchedule Daily24() => new()
    {
        Name = "Sutkalik", Kind = ScheduleKind.Cycle, CycleWorkDays = 1, CycleOffDays = 1,
        StartTime = new(8, 0), DurationMinutes = 24 * 60,
    };

    private static WorkSchedule Night() => new()
    {
        Name = "Tungi", Kind = ScheduleKind.Weekly, WeekDays = WorkSchedule.EveryDay,
        StartTime = new(20, 0), DurationMinutes = 12 * 60,
    };

    private static ScheduleContext Ctx(WorkSchedule schedule, DateOnly? anchor = null,
        IEnumerable<ScheduleOverride>? overrides = null, IEnumerable<ExcusedDay>? excused = null,
        IEnumerable<(WorkSchedule Schedule, DateOnly From, DateOnly? Anchor)>? more = null)
    {
        var all = new List<(WorkSchedule Schedule, DateOnly From, DateOnly? Anchor)> { (schedule, new DateOnly(2026, 1, 1), anchor) };
        if (more is not null)
            all.AddRange(more);
        return new ScheduleContext(
            all.Select(a => new EmployeeScheduleAssignment { EmployeeId = Emp, ScheduleId = a.Schedule.Id, EffectiveFrom = a.From, CycleAnchor = a.Anchor }),
            all.Select(a => a.Schedule).DistinctBy(s => s.Id).ToDictionary(s => s.Id),
            overrides ?? [], excused ?? [], Zone);
    }

    private static DateTimeOffset At(int day, int hour, int minute = 0, int second = 0) => new(2026, 10, day, hour, minute, second, Uz);

    private static readonly List<AttendanceEvent> NoEvents = [];

    /// <summary>Sutkalik smenalar uchun juftlik chegarasi 28 soat (A2/H1 — ScheduleMaxPair).</summary>
    private static IReadOnlyList<AttendancePair> Pairs(params (DateTimeOffset In, DateTimeOffset Out)[] pairs)
    {
        var events = pairs.SelectMany(p => new[]
        {
            new AttendanceEvent { EmployeeId = Emp, Type = AttendanceEventType.In, OccurredAt = p.In },
            new AttendanceEvent { EmployeeId = Emp, Type = AttendanceEventType.Out, OccurredAt = p.Out },
        });
        return AttendanceRules.Analyze(Emp, events, Zone, At(31, 23, 59).AddDays(2), _ => TimeSpan.FromHours(28)).Pairs;
    }

    private static ScheduleMonthResult Calc(ScheduleContext ctx, IReadOnlyList<AttendancePair> pairs, long salary = 6_000_000,
        int grace = 5, OvertimePolicy policy = OvertimePolicy.None, decimal value = 1m) =>
        ScheduleCalculator.Calculate(Oct, salary, pairs, ctx, new SchedulePolicy(grace, policy, value));

    // ---------- H1, H3: reja ----------

    [Fact]
    public void Weekly_plan_mon_to_sat_with_break()
    {
        var r = Calc(Ctx(Daytime()), []);
        // Oktabr 2026: 4 yakshanba → 27 ish kuni × (9 s − 60 daq) = 27 × 480
        r.Days.Count(d => d.Shift is not null).Should().Be(27);
        r.PlannedMinutes.Should().Be(27 * 480);
        r.Days.Should().NotContain(d => d.Date.DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public void Cycle_1_1_daily_24h_in_31_day_month()
    {
        var r = Calc(Ctx(Daily24(), anchor: new DateOnly(2026, 9, 29)), []);
        // 29-sentyabr ish kuni → 1, 3, 5, … 31-oktabr: 16 sutka × 1440
        r.Days.Where(d => d.Shift is not null).Select(d => d.Date.Day).Should().Equal(Enumerable.Range(0, 16).Select(i => 1 + 2 * i));
        r.PlannedMinutes.Should().Be(16 * 1440);
        r.Days[0].Shift!.End.Should().Be(At(2, 8));
    }

    [Theory]
    [InlineData(6_000_000)]
    [InlineData(7_123_457)]
    [InlineData(1_000_001)]
    [InlineData(333_333)]
    public void Full_plan_worked_pays_exact_salary(long salary)
    {
        var ctx = Ctx(Daily24(), anchor: new DateOnly(2026, 10, 1));
        var pairs = Enumerable.Range(0, 16).Select(i => (At(1 + 2 * i, 7, 55), At(1 + 2 * i, 8).AddDays(1))).ToArray();
        var r = Calc(ctx, Pairs(pairs), salary);
        r.WorkedMinutes.Should().Be(r.PlannedMinutes);
        r.BasePay.Should().Be(salary);
        r.LateMinutes.Should().Be(0);
        r.OvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public void Property_full_plan_always_equals_salary_and_pay_is_monotonic()
    {
        var rnd = new Random(42);
        for (var run = 0; run < 300; run++)
        {
            var salary = rnd.NextInt64(100_000, 50_000_000);
            var planned = rnd.Next(30, 31 * 1440);
            decimal rate = (decimal)salary / planned;
            PayrollMath.RoundToSum((decimal)salary * planned / planned).Should().Be(salary);
            var worked = rnd.Next(0, planned);
            var pay = PayrollMath.RoundToSum((decimal)salary * worked / planned);
            pay.Should().BeLessThanOrEqualTo(salary);
            Math.Abs(pay - rate * worked).Should().BeLessThan(1m);
        }

        // Haqiqiy kalkulyator: tasodifiy Weekly jadval, to'liq ishlangan oy
        for (var run = 0; run < 40; run++)
        {
            var schedule = new WorkSchedule
            {
                Name = "R", Kind = ScheduleKind.Weekly, WeekDays = rnd.Next(1, 128),
                StartTime = new(rnd.Next(0, 24), rnd.Next(0, 4) * 15), DurationMinutes = rnd.Next(2, 47) * 30, // ≤ 23 s: ketma-ket kunlar ustma-ust tushmasin
            };
            schedule.BreakMinutes = rnd.Next(0, schedule.DurationMinutes / 30) * 15;
            var ctx = Ctx(schedule);
            var pairs = Enumerable.Range(1, 31).Select(d => ctx.ShiftFor(new DateOnly(2026, 10, d))).OfType<ShiftWindow>()
                .Select(w => (w.Start, w.End)).ToArray();
            var salary = rnd.NextInt64(500_000, 20_000_000);
            var r = Calc(ctx, Pairs(pairs), salary);
            r.BasePay.Should().Be(salary, Describe(schedule));
        }
    }

    private static string Describe(WorkSchedule s) => PayrollEngine.Describe(s);

    // ---------- H4, H5: smena ichida, grace ----------

    [Fact]
    public void Grace_boundary_exactly_on_time_and_one_minute_more_is_full_late()
    {
        var ctx = Ctx(Daytime(breakMinutes: 0));
        var onTime = Calc(ctx, Pairs((At(5, 8, 5, 59), At(5, 17))));
        onTime.Day(new DateOnly(2026, 10, 5))!.Should().Match<ScheduleDayResult>(d => d.LateMinutes == 0 && d.WorkedMinutes == 540);

        var late = Calc(ctx, Pairs((At(5, 8, 6), At(5, 17))));
        late.Day(new DateOnly(2026, 10, 5))!.Should().Match<ScheduleDayResult>(d => d.LateMinutes == 6 && d.WorkedMinutes == 534);
    }

    [Fact]
    public void Arriving_before_shift_is_not_counted_and_staying_after_is_overtime()
    {
        var ctx = Ctx(Daytime(breakMinutes: 0));
        var r = Calc(ctx, Pairs((At(5, 6, 30), At(5, 18, 30))));
        var day = r.Day(new DateOnly(2026, 10, 5))!;
        day.WorkedMinutes.Should().Be(540);
        day.OvertimeMinutes.Should().Be(90, "smenadan oldingi 90 daqiqa sanalmaydi, keyingi 90 — ortiqcha");
    }

    [Fact]
    public void Early_leave_and_break_up_to_worked_time()
    {
        var ctx = Ctx(Daytime(breakMinutes: 60));
        var r = Calc(ctx, Pairs((At(5, 8), At(5, 10))));
        var day = r.Day(new DateOnly(2026, 10, 5))!;
        day.EarlyLeaveMinutes.Should().Be(7 * 60);
        day.WorkedMinutes.Should().Be(60, "120 daqiqa − 60 tanaffus");

        var short30 = Calc(ctx, Pairs((At(5, 8), At(5, 8, 30)))).Day(new DateOnly(2026, 10, 5))!;
        short30.WorkedMinutes.Should().Be(0, "tanaffus ishlangan vaqtgacha ayiriladi");
    }

    [Fact]
    public void Two_pairs_in_one_shift_late_only_first_in_early_only_last_out()
    {
        var ctx = Ctx(Daytime(breakMinutes: 60));
        var r = Calc(ctx, Pairs((At(5, 8, 20), At(5, 12)), (At(5, 13), At(5, 16, 30))));
        var day = r.Day(new DateOnly(2026, 10, 5))!;
        day.LateMinutes.Should().Be(20);
        day.EarlyLeaveMinutes.Should().Be(30);
        day.WorkedMinutes.Should().Be(220 + 210 - 60);
        day.FirstIn.Should().Be(At(5, 8, 20));
        day.LastOut.Should().Be(At(5, 16, 30));
    }

    [Fact]
    public void Seconds_are_truncated_to_minutes()
    {
        var ctx = Ctx(Daytime(breakMinutes: 0));
        var day = Calc(ctx, Pairs((At(5, 8, 0, 40), At(5, 16, 59, 59)))).Day(new DateOnly(2026, 10, 5))!;
        day.WorkedMinutes.Should().Be(539);
        day.EarlyLeaveMinutes.Should().Be(1);
    }

    // ---------- tungi smena, H13.2 ----------

    [Fact]
    public void Night_shift_20_to_08_counts_for_start_day_and_late_after_midnight_belongs_to_it()
    {
        var ctx = Ctx(Night());
        var r = Calc(ctx, Pairs((At(5, 19, 55), At(6, 8, 10)), (At(7, 0, 30), At(7, 8)), (At(7, 19, 58), At(8, 8))));
        var d5 = r.Day(new DateOnly(2026, 10, 5))!;
        d5.WorkedMinutes.Should().Be(720);
        d5.OvertimeMinutes.Should().Be(10);
        d5.LateMinutes.Should().Be(0);

        var d6 = r.Day(new DateOnly(2026, 10, 6))!;
        d6.LateMinutes.Should().Be(270, "00:30 da kelgan — 6-kungi 20:00 smenasiga");
        d6.WorkedMinutes.Should().Be(450);
        r.Day(new DateOnly(2026, 10, 7))!.WorkedMinutes.Should().Be(720, "7-kun smenasi 20:00 dan 08:00 gacha");
    }

    [Fact]
    public void Work_on_day_off_is_overtime_and_pre_shift_visit_is_not_counted()
    {
        var ctx = Ctx(Daytime(breakMinutes: 0));
        var r = Calc(ctx, Pairs((At(4, 10), At(4, 14)), (At(5, 5), At(5, 7))), policy: OvertimePolicy.FixedPerHour, value: 30_000);
        r.Day(new DateOnly(2026, 10, 4))!.OvertimeMinutes.Should().Be(240, "yakshanba — reja yo'q");
        r.Day(new DateOnly(2026, 10, 5))!.OvertimeMinutes.Should().Be(0, "smenadan oldin, smenaga tegmagan");
        r.Day(new DateOnly(2026, 10, 5))!.WorkedMinutes.Should().Be(0);
        r.OvertimePay.Should().Be(120_000);
    }

    [Fact]
    public void Separate_visit_after_shift_end_same_day_is_overtime()
    {
        var ctx = Ctx(Daytime(breakMinutes: 0));
        var r = Calc(ctx, Pairs((At(5, 8), At(5, 17)), (At(5, 18), At(5, 20))));
        r.Day(new DateOnly(2026, 10, 5))!.OvertimeMinutes.Should().Be(120);
    }

    // ---------- H7: 3 siyosat ----------

    [Theory]
    [InlineData(OvertimePolicy.None, 1, 0)]
    [InlineData(OvertimePolicy.Multiplier, 1.5, 0)]
    [InlineData(OvertimePolicy.FixedPerHour, 40_000, 60_000)]
    public void Overtime_policies(OvertimePolicy policy, decimal value, long fixedExpected)
    {
        var ctx = Ctx(Daytime(breakMinutes: 0));
        var r = Calc(ctx, Pairs((At(5, 8), At(5, 18, 30))), salary: 7_290_000, policy: policy, value: value);
        r.OvertimeMinutes.Should().Be(90);
        var expected = policy switch
        {
            OvertimePolicy.Multiplier => PayrollMath.RoundToSum(90 * 1.5m * 7_290_000m / r.PlannedMinutes),
            _ => fixedExpected,
        };
        r.OvertimePay.Should().Be(expected);
        r.Total.Should().Be(r.BasePay + r.OvertimePay);
    }

    // ---------- H8, H13.6: sababli kun ----------

    [Fact]
    public void Paid_excused_replaces_worked_unpaid_counts_zero()
    {
        var excused = new[]
        {
            new ExcusedDay { EmployeeId = Emp, Date = new(2026, 10, 5), Kind = ExcusedKind.Paid, Note = "Kasallik" },
            new ExcusedDay { EmployeeId = Emp, Date = new(2026, 10, 6), Kind = ExcusedKind.Unpaid, Note = "Ruxsat" },
        };
        var ctx = Ctx(Daytime(breakMinutes: 0), excused: excused);
        var r = Calc(ctx, Pairs((At(5, 9), At(5, 19)), (At(6, 10), At(6, 12))));
        r.Day(new DateOnly(2026, 10, 5))!.Should().Match<ScheduleDayResult>(d =>
            d.WorkedMinutes == 540 && d.LateMinutes == 0 && d.OvertimeMinutes == 0 && d.Excused != null);
        r.Day(new DateOnly(2026, 10, 6))!.Should().Match<ScheduleDayResult>(d =>
            d.WorkedMinutes == 0 && d.LateMinutes == 0 && d.EarlyLeaveMinutes == 0 && d.PlannedMinutes == 540);
    }

    // ---------- H2, H13.3, H13.5: istisno va tarix ----------

    [Fact]
    public void Override_off_and_custom_time_including_duty_on_day_off()
    {
        var overrides = new[]
        {
            new ScheduleOverride { EmployeeId = Emp, Date = new(2026, 10, 5), IsOff = true, Note = "Bayram" },
            new ScheduleOverride { EmployeeId = Emp, Date = new(2026, 10, 4), StartTime = new(10, 0), DurationMinutes = 240, Note = "Navbatchilik" },
            new ScheduleOverride { EmployeeId = Emp, Date = new(2026, 10, 6), StartTime = new(9, 0), DurationMinutes = 600, BreakMinutes = 30, Note = "Almashuv" },
        };
        var ctx = Ctx(Daytime(), overrides: overrides);
        ctx.ShiftFor(new DateOnly(2026, 10, 5)).Should().BeNull();
        ctx.ShiftFor(new DateOnly(2026, 10, 4))!.PlannedMinutes.Should().Be(240 - 60, "tanaffus bo'sh — shablonniki");
        ctx.ShiftFor(new DateOnly(2026, 10, 6))!.Should().Match<ShiftWindow>(w => w.Start == At(6, 9) && w.PlannedMinutes == 570);
        Calc(ctx, []).PlannedMinutes.Should().Be(27 * 480 - 480 + 180 - 480 + 570);
    }

    [Fact]
    public void Schedule_history_each_day_uses_schedule_effective_that_day()
    {
        var ctx = Ctx(Daytime(), more: [(Daily24(), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16))]);
        ctx.ShiftFor(new DateOnly(2026, 10, 15))!.PlannedMinutes.Should().Be(480);
        ctx.ShiftFor(new DateOnly(2026, 10, 16))!.PlannedMinutes.Should().Be(1440);
        ctx.ShiftFor(new DateOnly(2026, 10, 17)).Should().BeNull();
        // 1–15: 13 ish kuni (4 va 11 yakshanba); 16–31: 16,18,…,30 → 8 sutka
        Calc(ctx, []).PlannedMinutes.Should().Be(13 * 480 + 8 * 1440);
    }

    [Fact]
    public void Zero_plan_gives_zero_base_pay_and_no_multiplier_overtime_but_fixed_works()
    {
        var off = Enumerable.Range(1, 31).Select(d => new ScheduleOverride { EmployeeId = Emp, Date = new(2026, 10, d), IsOff = true, Note = "x" });
        var ctx = Ctx(Daytime(), overrides: off);
        var pairs = Pairs((At(5, 8), At(5, 10)));
        var multi = Calc(ctx, pairs, policy: OvertimePolicy.Multiplier, value: 2);
        multi.HasNoPlan.Should().BeTrue();
        multi.BasePay.Should().Be(0);
        multi.OvertimeMinutes.Should().Be(120);
        multi.OvertimePay.Should().Be(0);
        Calc(ctx, pairs, policy: OvertimePolicy.FixedPerHour, value: 30_000).OvertimePay.Should().Be(60_000);
    }

    // ---------- 31 kunlik oy, 1/1 sutkalik: kechikish + ortiqcha ----------

    [Fact]
    public void Daily_24h_month_with_late_and_overtime()
    {
        var ctx = Ctx(Daily24(), anchor: new DateOnly(2026, 10, 1));
        var pairs = new List<(DateTimeOffset, DateTimeOffset)>();
        for (var i = 0; i < 16; i++)
            pairs.Add((At(1 + 2 * i, 7, 50), At(1 + 2 * i, 8).AddDays(1)));
        pairs[2] = (At(5, 8, 30), At(6, 8, 45));     // 30 daq kech, 45 daq ortiqcha
        var r = Calc(ctx, Pairs(pairs.ToArray()), salary: 6_400_000, policy: OvertimePolicy.Multiplier, value: 1m);
        r.PlannedMinutes.Should().Be(16 * 1440);
        r.LateMinutes.Should().Be(30);
        r.WorkedMinutes.Should().Be(16 * 1440 - 30);
        r.OvertimeMinutes.Should().Be(45);
        r.BasePay.Should().Be(PayrollMath.RoundToSum(6_400_000m * (16 * 1440 - 30) / (16 * 1440)));
        r.OvertimePay.Should().Be(PayrollMath.RoundToSum(45m * 6_400_000 / (16 * 1440)));
        r.HourlyRate.Should().Be(6_400_000m / (16 * 1440) * 60);
    }

    [Fact]
    public void Daily_24h_pair_needs_schedule_limit_not_16h()
    {
        var ctx = Ctx(Daily24(), anchor: new DateOnly(2026, 10, 1));
        var events = new[]
        {
            new AttendanceEvent { EmployeeId = Emp, Type = AttendanceEventType.In, OccurredAt = At(1, 7, 55) },
            new AttendanceEvent { EmployeeId = Emp, Type = AttendanceEventType.Out, OccurredAt = At(2, 8, 5) },
        };
        AttendanceRules.Analyze(Emp, events, Zone, At(3, 0)).Pairs.Single().Status.Should().Be(PairStatus.OpenNoOut, "Daily — A2 16 soat");
        var schedule = AttendanceRules.Analyze(Emp, events, Zone, At(3, 0), AttendanceRules.ScheduleMaxPair(ctx));
        schedule.Pairs.Single().Status.Should().Be(PairStatus.Complete, "sutkalik smena 24 s + 4 s");
        schedule.OrphanOuts.Should().BeEmpty();
        AttendanceRules.ScheduleMaxPair(ctx)(new DateOnly(2026, 10, 2)).Should().Be(AttendanceRules.MaxShift, "dam kuni — 16 soat");
        AttendanceRules.ScheduleMaxPair(Ctx(Daytime()))(new DateOnly(2026, 10, 5)).Should().Be(TimeSpan.FromHours(13), "H14: 9 s + 4 s");
    }

    [Fact]
    public void Max_shift_minutes_for_phone_is_longest_current_or_future_shift_plus_slack()
    {
        var day = Daytime();
        var full = Daily24();
        var schedules = new[] { day, full }.ToDictionary(s => s.Id);
        EmployeeScheduleAssignment A(WorkSchedule? s, DateOnly from) => new() { EmployeeId = Emp, ScheduleId = s?.Id, EffectiveFrom = from };

        AttendanceRules.MaxShiftMinutesFor([], schedules, [], new DateOnly(2026, 10, 12)).Should().BeNull();
        AttendanceRules.MaxShiftMinutesFor([A(day, new(2026, 1, 1))], schedules, [], new DateOnly(2026, 10, 12)).Should().Be(13 * 60);
        AttendanceRules.MaxShiftMinutesFor([A(full, new(2026, 1, 1)), A(day, new(2026, 9, 1))], schedules, [], new DateOnly(2026, 10, 12))
            .Should().Be(13 * 60, "eski sutkalik jadval endi amalda emas");
        AttendanceRules.MaxShiftMinutesFor([A(day, new(2026, 1, 1)), A(full, new(2026, 11, 1))], schedules, [], new DateOnly(2026, 10, 12))
            .Should().Be(28 * 60, "kelgusi jadval");
        AttendanceRules.MaxShiftMinutesFor([A(day, new(2026, 1, 1))], schedules,
            [new ScheduleOverride { Date = new(2026, 10, 20), StartTime = new(8, 0), DurationMinutes = 1200, Note = "x" }], new DateOnly(2026, 10, 12))
            .Should().Be(24 * 60);
        AttendanceRules.MaxShiftMinutesFor([A(null, new(2026, 1, 1))], schedules, [], new DateOnly(2026, 10, 12)).Should().BeNull();
    }

    [Fact]
    public void Pairing_constants_come_from_contracts()
    {
        AttendanceRules.MaxShift.Should().Be(Contracts.Api.DefaultMaxShift);
        Contracts.Api.ShiftPairingSlack.Should().Be(TimeSpan.FromHours(4));
    }

    // ---------- H12: holat ----------

    [Fact]
    public void Live_status_colors()
    {
        var ctx = Ctx(Daytime(breakMinutes: 60));
        var day = new DateOnly(2026, 10, 5);
        var t = StatusThresholds.Default;
        ShiftStatus S(DateTimeOffset now, IReadOnlyList<AttendancePair> p) => ScheduleCalculator.Status(day, ctx, p, now, 5, t);

        S(At(5, 7, 59), []).State.Should().Be(ShiftState.NotStarted);
        S(At(5, 8, 12), []).Should().Match<ShiftStatus>(s => s.State == ShiftState.Late && s.Minutes == 12);
        S(At(5, 9, 0), []).State.Should().Be(ShiftState.Absent, "Start + 60");

        var inside = AttendanceRules.Analyze(Emp, [new AttendanceEvent { EmployeeId = Emp, Type = AttendanceEventType.In, OccurredAt = At(5, 8, 25) }], Zone, At(5, 9)).Pairs;
        S(At(5, 9), inside).Should().Match<ShiftStatus>(s => s.State == ShiftState.Present && s.Minutes == 25);

        S(At(5, 17), Pairs((At(5, 8), At(5, 17)))).State.Should().Be(ShiftState.DoneGreen);
        S(At(5, 17, 30), Pairs((At(5, 8), At(5, 15)))).State.Should().Be(ShiftState.DoneYellow, "360 ÷ 480 = 75%");
        S(At(5, 18), Pairs((At(5, 8), At(5, 12)))).State.Should().Be(ShiftState.DoneRed);
        S(At(5, 18), []).State.Should().Be(ShiftState.DoneRed, "kelmadi, kun tugadi");
        S(At(5, 18), []).IsNoShow.Should().BeTrue("ishlangan 0 — ism katagi «Kelmadi»");
        S(At(5, 18), Pairs((At(5, 8), At(5, 12)))).Should().Match<ShiftStatus>(s => s.WorkedMinutes > 0 && !s.IsNoShow);
        S(At(5, 12), []).IsNoShow.Should().BeFalse("faqat kun oxirida");
        ScheduleCalculator.Status(new DateOnly(2026, 10, 4), ctx, [], At(4, 12), 5, t).State.Should().Be(ShiftState.Off);
    }

    // ---------- Engine: hisob usuli (R9 kabi), validatsiya, H10 ----------

    private static PayrollEngine Engine() => new(new AppSettings(), TestClock.Default());

    [Fact]
    public void Pay_mode_changes_card_and_open_line_only()
    {
        var engine = Engine();
        var employee = engine.CreateEmployee("Олим", 6_000_000);
        var sep = engine.CreatePeriod(new MonthKey(2026, 9), [employee], []);
        sep.Status = PeriodStatus.Closed;
        var oct = engine.CreatePeriod(Oct, [employee], [sep]);

        engine.SetPayMode(employee, PayMode.Schedule, [sep, oct]);

        employee.PayMode.Should().Be(PayMode.Schedule);
        oct.FindLine(employee.Id)!.PayMode.Should().Be(PayMode.Schedule);
        sep.FindLine(employee.Id)!.PayMode.Should().Be(PayMode.Daily);
        engine.CreatePeriod(new MonthKey(2026, 11), [employee], [sep, oct]).FindLine(employee.Id)!.PayMode.Should().Be(PayMode.Schedule);
        engine.Audit.Should().Contain(a => a.Field == "PayMode" && a.NewValue == "Schedule");
    }

    [Fact]
    public void Daily_line_total_is_unchanged_and_schedule_line_total_is_base_plus_overtime()
    {
        var line = new PayrollLine { DailyRate = 200_000, DaysWorked = 26, BasePay = 999, OvertimePay = 1 };
        line.Total.Should().Be(5_200_000);
        line.PayMode = PayMode.Schedule;
        line.Total.Should().Be(1000);
    }

    [Fact]
    public void Closed_month_is_frozen_h10()
    {
        var period = new PayrollPeriod { Year = 2026, Month = 10, Status = PeriodStatus.Closed };
        var line = new PayrollLine { PayMode = PayMode.Schedule, BasePay = 5_000_000, PlannedMinutes = 100, GraceMinutes = 5 };
        var other = Calc(Ctx(Daytime()), []) with { BasePay = 1 };
        PayrollEngine.ApplySchedule(period, line, other);
        line.BasePay.Should().Be(5_000_000);
        line.PlannedMinutes.Should().Be(100);

        period.Status = PeriodStatus.Open;
        PayrollEngine.ApplySchedule(period, line, other);
        line.BasePay.Should().Be(1);
        line.PlannedMinutes.Should().Be(other.PlannedMinutes);
    }

    [Fact]
    public void Validation_and_required_notes()
    {
        var engine = Engine();
        var employee = engine.CreateEmployee("Олим", 6_000_000);
        var bad = Daytime();
        bad.DurationMinutes = 20;
        engine.Invoking(e => e.CreateSchedule(bad)).Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.InvalidSchedule);
        var noDays = Daytime();
        noDays.WeekDays = 0;
        engine.Invoking(e => e.CreateSchedule(noDays)).Should().Throw<DomainException>();
        engine.Invoking(e => e.AssignSchedule(employee, Daily24(), null, new DateOnly(2026, 10, 1)))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.CycleAnchorRequired);
        engine.Invoking(e => e.SetOverride(employee, new DateOnly(2026, 10, 5), true, null, null, null, " "))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.ReasonRequired);
        engine.Invoking(e => e.SetExcused(employee, new DateOnly(2026, 10, 5), ExcusedKind.Paid, null))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.ReasonRequired);

        var schedule = engine.CreateSchedule(Daytime());
        var assignment = engine.AssignSchedule(employee, schedule, null, new DateOnly(2026, 10, 1));
        engine.Invoking(e => e.EnsureScheduleUnused(schedule, [assignment]))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.ScheduleInUse);
    }

    [Fact]
    public void Employee_overtime_overrides_settings_default()
    {
        var settings = new AppSettings { LateGraceMinutes = 10, OvertimePolicy = OvertimePolicy.Multiplier, OvertimeValue = 1.5m };
        var employee = new Employee();
        PayrollEngine.PolicyFor(settings, employee).Should().Be(new SchedulePolicy(10, OvertimePolicy.Multiplier, 1.5m));
        employee.OvertimePolicy = OvertimePolicy.FixedPerHour;
        employee.OvertimeValue = 25_000;
        PayrollEngine.PolicyFor(settings, employee).Should().Be(new SchedulePolicy(10, OvertimePolicy.FixedPerHour, 25_000));
    }

    [Fact]
    public void Daily_pay_distribution_sums_exactly_to_base_pay_in_200_random_cases()
    {
        var random = new Random(20261013);
        for (var c = 0; c < 200; c++)
        {
            var salary = random.NextInt64(1, 50_000_000);
            var n = random.Next(1, 32);
            var worked = Enumerable.Range(0, n).Select(_ => random.Next(4) == 0 ? 0 : random.Next(1, 1500)).ToList();
            var planned = random.Next(1, 40_000);
            var basePay = PayrollMath.RoundToSum((decimal)salary * Math.Min(worked.Sum(), planned) / planned);

            var pays = ScheduleCalculator.DistributeBasePay(salary, planned, worked, basePay);

            pays.Sum().Should().Be(basePay, $"holat {c}");
            pays.All(p => p >= 0).Should().BeTrue();
            pays.Where((_, i) => worked[i] == 0).All(p => p == 0).Should().BeTrue();
            if (worked.Sum() <= planned)
                pays.Select((p, i) => p - salary * worked[i] / planned).Should().OnlyContain(d => d == 0 || d == 1, "floor + 0/1 so'm");
        }
    }
}
