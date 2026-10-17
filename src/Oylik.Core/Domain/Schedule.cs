namespace Oylik.Core.Domain;

/// <summary>4.3: hisob usuli — kunbay (4.1, Kun qo'lda) yoki jadval bo'yicha (soatbay, H1–H13).</summary>
public enum PayMode
{
    Daily,
    Schedule,
}

/// <summary>H1: Weekly — hafta kunlari; Cycle — WorkDays ish / OffDays dam, takrorlanadi.</summary>
public enum ScheduleKind
{
    Weekly,
    Cycle,
}

/// <summary>H7: ortiqcha ish siyosati.</summary>
public enum OvertimePolicy
{
    /// <summary>To'lanmaydi, faqat ko'rsatiladi.</summary>
    None,
    /// <summary>k × HourlyRate.</summary>
    Multiplier,
    /// <summary>So'm/soat.</summary>
    FixedPerHour,
}

/// <summary>H8: sababli kun.</summary>
public enum ExcusedKind
{
    /// <summary>Ta'til, kasallik — reja daqiqalari ishlangan deb hisoblanadi.</summary>
    Paid,
    /// <summary>Ishlangan 0, kechikish yozilmaydi.</summary>
    Unpaid,
}

/// <summary>H1: jadval shabloni.</summary>
public sealed class WorkSchedule
{
    public const int MinDuration = 30;
    public const int MaxDuration = 24 * 60;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ScheduleKind Kind { get; set; }

    /// <summary>Weekly: hafta kunlari bitlari (1 &lt;&lt; (int)DayOfWeek).</summary>
    public int WeekDays { get; set; }

    /// <summary>Cycle: ish kunlari soni (1/1 — 1).</summary>
    public int CycleWorkDays { get; set; } = 1;

    /// <summary>Cycle: dam kunlari soni.</summary>
    public int CycleOffDays { get; set; } = 1;

    /// <summary>Mahalliy boshlanish vaqti.</summary>
    public TimeOnly StartTime { get; set; } = new(8, 0);

    /// <summary>Smena davomiyligi, daqiqa (30–1440).</summary>
    public int DurationMinutes { get; set; } = 9 * 60;

    /// <summary>To'lanmaydigan tanaffus, daqiqa.</summary>
    public int BreakMinutes { get; set; }

    public int SortOrder { get; set; }

    // Migratsiyadagi 3 ta namuna (5.10). Nomi o'zgartirilmaguncha UI'da tarjima qilinadi.
    public static readonly Guid SampleDaytimeId = new("5c1e0b7a-3d2f-4e8a-9b61-0a7d2c4e1f01");
    public static readonly Guid SampleTwelveHoursId = new("5c1e0b7a-3d2f-4e8a-9b61-0a7d2c4e1f02");
    public static readonly Guid SampleDailyId = new("5c1e0b7a-3d2f-4e8a-9b61-0a7d2c4e1f03");

    public static int Bit(DayOfWeek day) => 1 << (int)day;
    public bool WorksOn(DayOfWeek day) => (WeekDays & Bit(day)) != 0;

    /// <summary>Du–Sh (yakshanba dam).</summary>
    public static int MondayToSaturday => Bit(DayOfWeek.Monday) | Bit(DayOfWeek.Tuesday) | Bit(DayOfWeek.Wednesday)
                                          | Bit(DayOfWeek.Thursday) | Bit(DayOfWeek.Friday) | Bit(DayOfWeek.Saturday);

    public static int EveryDay => MondayToSaturday | Bit(DayOfWeek.Sunday);
}

/// <summary>H13.5: jadval biriktirish tarixi — har kun o'sha sanada amalda bo'lgan jadval bilan hisoblanadi.</summary>
public sealed class EmployeeScheduleAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }

    /// <summary>null — shu sanadan jadval yo'q (reja yo'q).</summary>
    public Guid? ScheduleId { get; set; }

    /// <summary>Cycle uchun: birinchi ish kuni sanasi.</summary>
    public DateOnly? CycleAnchor { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>H2: kunlik istisno — Dam yoki boshqa vaqt (dam kunini ish kuniga aylantira oladi, H13.3).</summary>
public sealed class ScheduleOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsOff { get; set; }
    public TimeOnly? StartTime { get; set; }
    public int? DurationMinutes { get; set; }

    /// <summary>Bo'sh — shablonniki (H13.3).</summary>
    public int? BreakMinutes { get; set; }

    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>H8: sababli kun.</summary>
public sealed class ExcusedDay
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public ExcusedKind Kind { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>H3: bitta kunning smena oynasi [Start, End); Date — boshlanish sanasi.</summary>
public sealed record ShiftWindow(DateOnly Date, DateTimeOffset Start, DateTimeOffset End, int BreakMinutes, bool IsOverride)
{
    public int DurationMinutes => (int)((End - Start).Ticks / TimeSpan.TicksPerMinute);

    /// <summary>H3: Duration − BreakMinutes.</summary>
    public int PlannedMinutes => Math.Max(0, DurationMinutes - BreakMinutes);
}

/// <summary>Bitta xodimning jadval ma'lumotlari (biriktirish tarixi, istisnolar, sababli kunlar).</summary>
public sealed class ScheduleContext
{
    private readonly IReadOnlyList<EmployeeScheduleAssignment> _assignments;

    public ScheduleContext(
        IEnumerable<EmployeeScheduleAssignment> assignments,
        IReadOnlyDictionary<Guid, WorkSchedule> schedules,
        IEnumerable<ScheduleOverride> overrides,
        IEnumerable<ExcusedDay> excused,
        TimeZoneInfo zone)
    {
        _assignments = assignments.OrderBy(a => a.EffectiveFrom).ThenBy(a => a.CreatedAt).ToList();
        Schedules = schedules;
        Overrides = overrides.GroupBy(o => o.Date).ToDictionary(g => g.Key, g => g.OrderBy(o => o.CreatedAt).Last());
        Excused = excused.GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).Last());
        Zone = zone;
    }

    public IReadOnlyDictionary<Guid, WorkSchedule> Schedules { get; }
    public IReadOnlyDictionary<DateOnly, ScheduleOverride> Overrides { get; }
    public IReadOnlyDictionary<DateOnly, ExcusedDay> Excused { get; }
    public TimeZoneInfo Zone { get; }

    /// <summary>H13.5: shu sanada amalda bo'lgan biriktirish (EffectiveFrom ≤ date bo'lgan oxirgisi).</summary>
    public EmployeeScheduleAssignment? AssignmentOn(DateOnly date)
    {
        EmployeeScheduleAssignment? found = null;
        foreach (var a in _assignments)
        {
            if (a.EffectiveFrom > date)
                break;
            found = a;
        }
        return found;
    }

    public WorkSchedule? ScheduleOn(DateOnly date) =>
        AssignmentOn(date) is { ScheduleId: { } id } && Schedules.TryGetValue(id, out var s) ? s : null;

    /// <summary>H3: shu sana smenasi (yoki null — dam/reja yo'q). Istisno (H2) shablondan ustun.</summary>
    public ShiftWindow? ShiftFor(DateOnly date)
    {
        var schedule = ScheduleOn(date);
        if (Overrides.TryGetValue(date, out var o))
        {
            if (o.IsOff)
                return null;
            var start = o.StartTime ?? schedule?.StartTime;
            var duration = o.DurationMinutes ?? schedule?.DurationMinutes;
            if (start is null || duration is null)
                return null;
            return Window(date, start.Value, duration.Value, o.BreakMinutes ?? schedule?.BreakMinutes ?? 0, isOverride: true);
        }
        if (schedule is null || !IsWorkDay(schedule, AssignmentOn(date)!, date))
            return null;
        return Window(date, schedule.StartTime, schedule.DurationMinutes, schedule.BreakMinutes, isOverride: false);
    }

    private static bool IsWorkDay(WorkSchedule schedule, EmployeeScheduleAssignment assignment, DateOnly date)
    {
        if (schedule.Kind == ScheduleKind.Weekly)
            return schedule.WorksOn(date.DayOfWeek);
        var anchor = assignment.CycleAnchor ?? assignment.EffectiveFrom;
        var length = Math.Max(1, schedule.CycleWorkDays + schedule.CycleOffDays);
        var position = ((date.DayNumber - anchor.DayNumber) % length + length) % length;
        return position < schedule.CycleWorkDays;
    }

    private ShiftWindow Window(DateOnly date, TimeOnly start, int durationMinutes, int breakMinutes, bool isOverride)
    {
        var local = date.ToDateTime(start);
        var begin = new DateTimeOffset(local, Zone.GetUtcOffset(local));
        var duration = Math.Clamp(durationMinutes, WorkSchedule.MinDuration, WorkSchedule.MaxDuration);
        return new ShiftWindow(date, begin, begin.AddMinutes(duration), Math.Clamp(breakMinutes, 0, duration), isOverride);
    }
}
