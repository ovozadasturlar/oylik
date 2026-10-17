namespace Oylik.Core.Domain;

/// <summary>Hisob siyosati (Sozlamalar default + xodim, H5/H7). Yopilganda qatorga muzlatiladi (H10).</summary>
public sealed record SchedulePolicy(int GraceMinutes, OvertimePolicy Overtime, decimal OvertimeValue)
{
    public static SchedulePolicy Default { get; } = new(5, OvertimePolicy.None, 1m);
}

/// <summary>Bitta reja kuni yoki rejasiz kunda ishlangan vaqt natijasi.</summary>
public sealed record ScheduleDayResult(
    DateOnly Date,
    ShiftWindow? Shift,
    ExcusedDay? Excused,
    int PlannedMinutes,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int OvertimeMinutes,
    DateTimeOffset? FirstIn,
    DateTimeOffset? LastOut);

/// <summary>Oy natijasi (H3–H9). Total = BasePay + OvertimePay.</summary>
public sealed record ScheduleMonthResult(
    MonthKey Month,
    IReadOnlyList<ScheduleDayResult> Days,
    int PlannedMinutes,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int OvertimeMinutes,
    decimal MinuteRate,
    long BasePay,
    long OvertimePay,
    SchedulePolicy Policy)
{
    public long Total => BasePay + OvertimePay;

    /// <summary>H6: ko'rsatish uchun.</summary>
    public decimal HourlyRate => MinuteRate * 60;

    /// <summary>H13.4: reja yo'q — ogohlantirish.</summary>
    public bool HasNoPlan => PlannedMinutes == 0;

    public ScheduleDayResult? Day(DateOnly date) => Days.FirstOrDefault(d => d.Date == date);
}

/// <summary>H12: xodim-kun holati.</summary>
public enum ShiftState
{
    /// <summary>Reja yo'q (dam kuni) — bo'sh.</summary>
    Off,
    /// <summary>Smena boshlanmagan — kulrang.</summary>
    NotStarted,
    /// <summary>Smena boshlandi, kelmagan — sariq (`Kechikmoqda · 12 daq`).</summary>
    Late,
    /// <summary>Start + AbsentAfter o'tdi yoki smena tugadi — qizil (`Kelmadi`).</summary>
    Absent,
    /// <summary>Keldi — yashil (kech kelgan bo'lsa LateMinutes > 0).</summary>
    Present,
    /// <summary>Kun tugadi: Worked ÷ Planned ≥ Green — yashil.</summary>
    DoneGreen,
    /// <summary>≥ Yellow — sariq.</summary>
    DoneYellow,
    /// <summary>Kam — qizil.</summary>
    DoneRed,
    /// <summary>H8 sababli kun.</summary>
    Excused,
}

/// <summary>H12 holati. <paramref name="WorkedMinutes"/> — faqat kun oxiri (Done*) holatlarida to'ldiriladi.</summary>
public sealed record ShiftStatus(ShiftState State, int Minutes, ShiftWindow? Shift, ExcusedDay? Excused = null, int WorkedMinutes = 0)
{
    /// <summary>Smena tugadi, umuman kelmadi (ishlangan 0, sababli emas).</summary>
    public bool IsNoShow => State is ShiftState.DoneGreen or ShiftState.DoneYellow or ShiftState.DoneRed && WorkedMinutes == 0;
}

/// <summary>Rang chegaralari va «Kelmadi» vaqti (Sozlamalar, H12).</summary>
public sealed record StatusThresholds(int AbsentAfterMinutes, int GreenPercent, int YellowPercent)
{
    public static StatusThresholds Default { get; } = new(60, 95, 75);
}

/// <summary>4.3 H3–H13: jadval bo'yicha soatbay hisob. Saqlashni bilmaydi; vaqt — parametr.</summary>
public static class ScheduleCalculator
{
    public const int MaxGraceMinutes = 180;

    private readonly record struct Interval(long From, long To)
    {
        public long Length => Math.Max(0, To - From);
    }

    private static long Minute(DateTimeOffset t) => t.UtcTicks / TimeSpan.TicksPerMinute;

    private static DateTimeOffset FromMinute(long minute) => new(minute * TimeSpan.TicksPerMinute, TimeSpan.Zero);

    private sealed record Pair(long In, long Out);

    private static List<Pair> CompletePairs(IEnumerable<AttendancePair> pairs) =>
        pairs.Where(p => p.Status == PairStatus.Complete && p.Out is not null)
            .Select(p => new Pair(Minute(p.In.OccurredAt), Minute(p.Out!.OccurredAt)))
            .Where(p => p.Out > p.In)
            .OrderBy(p => p.In)
            .ToList();

    /// <summary>H3–H9: oy hisobi. <paramref name="pairs"/> — xodimning juftliklari (A2), oy chegarasidan tashqarisi ham bo'lishi mumkin.</summary>
    public static ScheduleMonthResult Calculate(MonthKey month, long monthlySalary, IEnumerable<AttendancePair> pairs, ScheduleContext ctx, SchedulePolicy policy)
    {
        var grace = Math.Clamp(policy.GraceMinutes, 0, MaxGraceMinutes);
        var list = CompletePairs(pairs);
        var first = month.FirstDay;
        var last = first.AddMonths(1).AddDays(-1);

        // Qo'shni kunlar ham — tungi smena va oy chegarasidagi kesishmalarni to'g'ri ajratish uchun
        var windows = new List<ShiftWindow>();
        for (var d = first.AddDays(-1); d <= last.AddDays(1); d = d.AddDays(1))
            if (ctx.ShiftFor(d) is { } w)
                windows.Add(w);

        var overtime = new Dictionary<DateOnly, int>();
        foreach (var pair in list)
            AddOvertime(pair, windows, ctx.Zone, overtime);

        var days = new List<ScheduleDayResult>();
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var window = windows.FirstOrDefault(w => w.Date == d);
            ctx.Excused.TryGetValue(d, out var excused);
            var ot = excused is null ? overtime.GetValueOrDefault(d) : 0; // H8, H13.6: sababli kunda ortiqcha yozilmaydi
            if (window is null)
            {
                if (ot > 0 || excused is not null)
                    days.Add(new ScheduleDayResult(d, null, excused, 0, 0, 0, 0, ot, null, null));
                continue;
            }
            days.Add(Day(window, excused, list, grace, ot));
        }

        var planned = days.Sum(d => d.PlannedMinutes);
        var worked = days.Sum(d => d.WorkedMinutes);
        var overtimeTotal = days.Sum(d => d.OvertimeMinutes);
        var minuteRate = planned > 0 ? (decimal)monthlySalary / planned : 0m;

        // H6: MinuteRate oraliqda yaxlitlanmaydi — reja to'liq ishlansa BasePay = Oklad aniq
        var basePay = planned > 0 ? PayrollMath.RoundToSum((decimal)monthlySalary * Math.Min(worked, planned) / planned) : 0;
        var overtimePay = policy.Overtime switch
        {
            OvertimePolicy.Multiplier when planned > 0 =>
                PayrollMath.RoundToSum(overtimeTotal * policy.OvertimeValue * monthlySalary / planned),
            OvertimePolicy.FixedPerHour => PayrollMath.RoundToSum(overtimeTotal * policy.OvertimeValue / 60m),
            _ => 0, // None; Multiplier + reja yo'q (H13.4)
        };

        return new ScheduleMonthResult(month, days, planned, worked, days.Sum(d => d.LateMinutes), days.Sum(d => d.EarlyLeaveMinutes),
            overtimeTotal, minuteRate, basePay, overtimePay, policy with { GraceMinutes = grace });
    }

    /// <summary>H4, H5, H8, H13.1: bitta smena.</summary>
    private static ScheduleDayResult Day(ShiftWindow w, ExcusedDay? excused, List<Pair> pairs, int grace, int overtime)
    {
        var planned = w.PlannedMinutes;
        if (excused is { Kind: ExcusedKind.Paid })
            return new ScheduleDayResult(w.Date, w, excused, planned, planned, 0, 0, 0, null, null);
        if (excused is { Kind: ExcusedKind.Unpaid })
            return new ScheduleDayResult(w.Date, w, excused, planned, 0, 0, 0, 0, null, null);

        long start = Minute(w.Start), end = Minute(w.End);
        var touching = pairs.Where(p => p.In < end && p.Out > start).ToList();
        if (touching.Count == 0)
            return new ScheduleDayResult(w.Date, w, null, planned, 0, 0, 0, overtime, null, null);

        var firstIn = touching[0].In;
        var lastOut = touching[^1].Out;
        var late = 0;
        long raw = 0;
        for (var i = 0; i < touching.Count; i++)
        {
            var p = touching[i];
            var from = Math.Max(p.In, start);
            if (i == 0)
            {
                // H5: In ≤ Start + Grace → vaqtida, Start'dan sanaladi; aks holda to'liq kechikish, In'dan
                if (p.In <= start + grace)
                    from = start;
                else
                    late = (int)(p.In - start);
            }
            raw += new Interval(from, Math.Min(p.Out, end)).Length;
        }
        var early = lastOut < end ? (int)(end - lastOut) : 0; // H5: erta ketish — grace yo'q, faqat oxirgi Out (H13.1)
        var worked = (int)(raw - Math.Min(w.BreakMinutes, raw)); // H4: tanaffus ishlangan vaqtdan, BreakMinutes gacha
        return new ScheduleDayResult(w.Date, w, null, planned, Math.Min(worked, planned), late, early, overtime,
            FromMinute(firstIn), FromMinute(lastOut));
    }

    /// <summary>
    /// H7, H13.2: juftlikning hech qaysi smenaga kirmagan qismi — smenadan keyin → ortiqcha (o'sha smena kuniga);
    /// smenadan oldin → sanalmaydi; hech qaysi smenaga tegmasa: In kunida reja yo'q → to'liq ortiqcha,
    /// reja bor bo'lsa — smena tugagandan keyin → ortiqcha, oldin → sanalmaydi.
    /// </summary>
    private static void AddOvertime(Pair pair, List<ShiftWindow> windows, TimeZoneInfo zone, Dictionary<DateOnly, int> overtime)
    {
        var inside = windows
            .Select(w => (Window: w, From: Math.Max(pair.In, Minute(w.Start)), To: Math.Min(pair.Out, Minute(w.End))))
            .Where(x => x.To > x.From)
            .OrderBy(x => x.From)
            .ToList();

        // smenalar bilan qoplanmagan bo'laklar
        var cursor = pair.In;
        var outside = new List<Interval>();
        foreach (var x in inside)
        {
            if (x.From > cursor)
                outside.Add(new Interval(cursor, x.From));
            cursor = Math.Max(cursor, x.To);
        }
        if (pair.Out > cursor)
            outside.Add(new Interval(cursor, pair.Out));

        foreach (var segment in outside)
        {
            DateOnly? day;
            if (inside.Count > 0)
            {
                // juftlik ichida oldin smena bo'lgan bo'lsa — smenadan keyin; aks holda smenadan oldin (sanalmaydi)
                var before = inside.LastOrDefault(x => x.To <= segment.From);
                day = before.Window?.Date;
            }
            else
            {
                var inDay = AttendanceRules.LocalDate(FromMinute(pair.In), zone);
                var planned = windows.FirstOrDefault(w => w.Date == inDay);
                day = planned is null || segment.From >= Minute(planned.End) ? inDay : null;
            }
            if (day is { } d)
                overtime[d] = overtime.GetValueOrDefault(d) + (int)segment.Length;
        }
    }

    /// <summary>
    /// 5.11: asosiy haqni kunlarga taqsimlash («largest remainder»): har kun floor(Oklad × w_i ÷ P), qolgan so'mlar
    /// kasr qismi kattalariga 1 so'mdan (teng bo'lsa — oldingi kunga). Σ = <paramref name="basePay"/> aniq.
    /// Kunlik daqiqalar yig'indisi qatordagi (muzlatilgan) qiymatdan farq qilsa — basePay to'g'ridan-to'g'ri w_i ulushida taqsimlanadi.
    /// </summary>
    public static long[] DistributeBasePay(long monthlySalary, int plannedMinutes, IReadOnlyList<int> workedPerDay, long basePay)
    {
        var n = workedPerDay.Count;
        var result = new long[n];
        if (n == 0 || basePay <= 0)
            return result;
        var weightTotal = workedPerDay.Sum(w => (long)Math.Max(0, w));
        if (weightTotal == 0)
            return result;

        long[] Allocate(long amount, long divisor)
        {
            var shares = new long[n];
            var remainders = new long[n];
            for (var i = 0; i < n; i++)
            {
                var numerator = amount * Math.Max(0, workedPerDay[i]);
                shares[i] = numerator / divisor;
                remainders[i] = numerator % divisor;
            }
            return shares.Concat(remainders).ToArray();
        }

        // TZ: floor(Oklad × w_i ÷ P); qoldiq BasePay − Σ floor — 0..n oralig'ida bo'lishi kerak
        var useSalary = plannedMinutes > 0;
        var parts = useSalary ? Allocate(monthlySalary, plannedMinutes) : Allocate(basePay, weightTotal);
        var floorSum = 0L;
        for (var i = 0; i < n; i++)
            floorSum += parts[i];
        var left = basePay - floorSum;
        if (useSalary && (left < 0 || left > n))
        {
            parts = Allocate(basePay, weightTotal);
            floorSum = 0;
            for (var i = 0; i < n; i++)
                floorSum += parts[i];
            left = basePay - floorSum;
        }
        for (var i = 0; i < n; i++)
            result[i] = parts[i];
        foreach (var i in Enumerable.Range(0, n).Where(i => workedPerDay[i] > 0).OrderByDescending(i => parts[n + i]).ThenBy(i => i).Take((int)left))
            result[i]++;
        return result;
    }

    /// <summary>H12: shu kunning jonli holati (<paramref name="now"/> — TimeProvider'dan).</summary>
    public static ShiftStatus Status(DateOnly date, ScheduleContext ctx, IEnumerable<AttendancePair> pairs, DateTimeOffset now,
        int graceMinutes, StatusThresholds thresholds)
    {
        var shift = ctx.ShiftFor(date);
        if (ctx.Excused.TryGetValue(date, out var excused))
            return new ShiftStatus(ShiftState.Excused, 0, shift, excused);
        if (shift is null)
            return new ShiftStatus(ShiftState.Off, 0, null);
        if (now < shift.Start)
            return new ShiftStatus(ShiftState.NotStarted, 0, shift);

        long start = Minute(shift.Start), end = Minute(shift.End), current = Minute(now);
        if (current >= end)
        {
            var day = Day(shift, null, CompletePairs(pairs), graceMinutes, 0);
            var percent = day.PlannedMinutes > 0 ? 100m * day.WorkedMinutes / day.PlannedMinutes : 100m;
            var state = percent >= thresholds.GreenPercent ? ShiftState.DoneGreen
                : percent >= thresholds.YellowPercent ? ShiftState.DoneYellow
                : ShiftState.DoneRed;
            return new ShiftStatus(state, day.LateMinutes, shift, null, day.WorkedMinutes);
        }

        // keldi: smena oynasiga tegadigan juftlik (ichkaridagi ochiq In ham — Out o'rniga hozir)
        var firstIn = pairs
            .Where(p => p.Status is PairStatus.Complete or PairStatus.Inside)
            .Select(p => (In: Minute(p.In.OccurredAt), Out: p.Out is null ? current : Minute(p.Out.OccurredAt)))
            .Where(p => p.In < end && p.Out > start)
            .Select(p => (long?)p.In)
            .Min();
        if (firstIn is { } arrived)
            return new ShiftStatus(ShiftState.Present, arrived > start + graceMinutes ? (int)(arrived - start) : 0, shift);

        var waiting = (int)(current - start);
        return waiting >= thresholds.AbsentAfterMinutes
            ? new ShiftStatus(ShiftState.Absent, waiting, shift)
            : new ShiftStatus(ShiftState.Late, waiting, shift);
    }
}
