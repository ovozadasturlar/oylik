namespace Oylik.Core.Domain;

public enum PairStatus
{
    /// <summary>In + Out, ≤ 16 soat.</summary>
    Complete,

    /// <summary>Ochiq In, hali 16 soat o'tmagan — xodim ichkarida.</summary>
    Inside,

    /// <summary>A2/A4: Out'siz In (16 soatdan oshgan yoki keyin yana In kelgan) — tasdiqlash kerak.</summary>
    OpenNoOut,
}

public sealed record AttendancePair(DateOnly WorkDay, AttendanceEvent In, AttendanceEvent? Out, PairStatus Status)
{
    /// <summary>Daqiqalar (vaqtlar daqiqagacha qirqiladi — ekranda ko'rinadigan HH:mm bilan bir xil). Faqat Complete.</summary>
    public int Minutes => Out is null ? 0 : AttendanceRules.MinutesBetween(In.OccurredAt, Out.OccurredAt);
}

public sealed record AttendanceDay(DateOnly Date, IReadOnlyList<AttendancePair> Pairs)
{
    /// <summary>A6: bir kundagi barcha tugallangan juftliklar daqiqalari yig'indisi.</summary>
    public int Minutes => Pairs.Where(p => p.Status == PairStatus.Complete).Sum(p => p.Minutes);

    /// <summary>A7</summary>
    public decimal Hours => AttendanceRules.Hours(Minutes);

    public bool HasComplete => Pairs.Any(p => p.Status == PairStatus.Complete);
    public bool HasOpen => Pairs.Any(p => p.Status != PairStatus.Complete);
}

public enum AttendanceIssueKind
{
    OpenNoOut,
    OrphanOut,
}

public sealed record AttendanceIssue(Guid EmployeeId, AttendanceIssueKind Kind, AttendanceEvent Event);

public sealed record AttendanceMonthSummary(int AttendedDays, decimal TotalHours, decimal DayEquivalent)
{
    public static AttendanceMonthSummary Empty { get; } = new(0, 0, 0);
}

/// <summary>Bitta xodimning juftlangan davomati.</summary>
public sealed class EmployeeAttendance
{
    public required Guid EmployeeId { get; init; }
    public required IReadOnlyList<AttendancePair> Pairs { get; init; }

    /// <summary>A3: oldidan 16 soat ichida ochiq In bo'lmagan Out'lar.</summary>
    public required IReadOnlyList<AttendanceEvent> OrphanOuts { get; init; }

    public required IReadOnlyDictionary<DateOnly, AttendanceDay> Days { get; init; }

    /// <summary>Oxirgi juftlik ochiq va 16 soat o'tmagan — ichkarida.</summary>
    public AttendancePair? CurrentInside => Pairs.Count > 0 && Pairs[^1].Status == PairStatus.Inside ? Pairs[^1] : null;

    public AttendanceDay? Day(DateOnly date) => Days.GetValueOrDefault(date);

    public IEnumerable<AttendanceIssue> Issues =>
        Pairs.Where(p => p.Status == PairStatus.OpenNoOut).Select(p => new AttendanceIssue(EmployeeId, AttendanceIssueKind.OpenNoOut, p.In))
            .Concat(OrphanOuts.Select(o => new AttendanceIssue(EmployeeId, AttendanceIssueKind.OrphanOut, o)))
            .OrderBy(i => i.Event.OccurredAt);

    /// <summary>A8: oy xulosasi.</summary>
    public AttendanceMonthSummary Summarize(MonthKey month, int hoursPerDay)
    {
        var days = Days.Values.Where(d => d.Date.Year == month.Year && d.Date.Month == month.Month).ToList();
        var attended = days.Count(d => d.HasComplete);
        var totalHours = days.Sum(d => d.Hours);
        return new AttendanceMonthSummary(attended, totalHours, AttendanceRules.DayEquivalent(totalHours, hoursPerDay));
    }
}

/// <summary>4.2 davomat qoidalari (A1–A8, A10, A12). Saqlashni bilmaydi.</summary>
public static class AttendanceRules
{
    /// <summary>A2: juftlik davomiyligi chegarasi.</summary>
    public static readonly TimeSpan MaxShift = Contracts.Api.DefaultMaxShift;
    public static int MaxShiftHours => (int)MaxShift.TotalHours;

    /// <summary>A10: takror bosish oynasi.</summary>
    public static readonly TimeSpan DuplicateTapWindow = TimeSpan.FromMinutes(2);

    /// <summary>A12</summary>
    public static readonly TimeSpan ClockSkewWarning = TimeSpan.FromMinutes(10);

    public static int MinutesBetween(DateTimeOffset from, DateTimeOffset to) =>
        (int)((TruncateToMinute(to) - TruncateToMinute(from)).Ticks / TimeSpan.TicksPerMinute);

    private static DateTimeOffset TruncateToMinute(DateTimeOffset t) =>
        new(t.UtcTicks - t.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);

    /// <summary>A7: Hours = Minutes ÷ 60, 2 xona.</summary>
    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    /// <summary>A7: DayEquivalent = Hours ÷ HoursPerDay, 2 xona.</summary>
    public static decimal DayEquivalent(decimal hours, int hoursPerDay)
    {
        if (hoursPerDay <= 0)
            throw new ArgumentOutOfRangeException(nameof(hoursPerDay));
        return Math.Round(hours / hoursPerDay, 2, MidpointRounding.AwayFromZero);
    }

    public static DateOnly LocalDate(DateTimeOffset t, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, zone).DateTime);

    /// <summary>A1: vaqt bo'yicha; bir xil vaqtda In oldin.</summary>
    public static IEnumerable<AttendanceEvent> Ordered(IEnumerable<AttendanceEvent> events) =>
        events.Where(e => !e.IsVoided).OrderBy(e => e.OccurredAt.UtcTicks).ThenBy(e => e.Type).ThenBy(e => e.Id);

    /// <summary>A1–A6: bitta xodim voqealarini juftlash.</summary>
    /// <param name="zone">WorkDay sanasi uchun mahalliy vaqt zonasi (A5).</param>
    /// <param name="now">Ochiq In 16 soatdan oshganmi — shu vaqtga nisbatan.</param>
    /// <param name="maxPair">
    /// Juftlik chegarasi In'ning mahalliy sanasi bo'yicha (null — <see cref="MaxShift"/>). Schedule xodimi uchun
    /// sutkalik smenada 16 soat yetmaydi — chegara smena davomiyligi + Api.ShiftPairingSlack (H14).
    /// </param>
    public static EmployeeAttendance Analyze(Guid employeeId, IEnumerable<AttendanceEvent> events, TimeZoneInfo zone, DateTimeOffset now,
        Func<DateOnly, TimeSpan>? maxPair = null)
    {
        TimeSpan Limit(AttendanceEvent i) => maxPair?.Invoke(LocalDate(i.OccurredAt, zone)) ?? MaxShift;
        var pairs = new List<AttendancePair>();
        var orphans = new List<AttendanceEvent>();
        AttendanceEvent? open = null;

        void CloseOpenAsNoOut()
        {
            pairs.Add(new AttendancePair(LocalDate(open!.OccurredAt, zone), open, null, PairStatus.OpenNoOut));
            open = null;
        }

        foreach (var e in Ordered(events.Where(x => x.EmployeeId == employeeId)))
        {
            if (e.Type == AttendanceEventType.In)
            {
                if (open is not null)
                    CloseOpenAsNoOut(); // A4
                open = e;
                continue;
            }

            if (open is not null && e.OccurredAt - open.OccurredAt <= Limit(open))
            {
                pairs.Add(new AttendancePair(LocalDate(open.OccurredAt, zone), open, e, PairStatus.Complete)); // A2, A5
                open = null;
                continue;
            }

            if (open is not null)
                CloseOpenAsNoOut(); // A2: 16 soatdan oshdi
            orphans.Add(e); // A3
        }

        if (open is not null)
        {
            var status = now - open.OccurredAt > Limit(open) ? PairStatus.OpenNoOut : PairStatus.Inside;
            pairs.Add(new AttendancePair(LocalDate(open.OccurredAt, zone), open, null, status));
        }

        var days = pairs.GroupBy(p => p.WorkDay)
            .ToDictionary(g => g.Key, g => new AttendanceDay(g.Key, g.ToList()));

        return new EmployeeAttendance { EmployeeId = employeeId, Pairs = pairs, OrphanOuts = orphans, Days = days };
    }

    public static IReadOnlyDictionary<Guid, EmployeeAttendance> AnalyzeAll(IEnumerable<AttendanceEvent> events, TimeZoneInfo zone, DateTimeOffset now,
        Func<Guid, Func<DateOnly, TimeSpan>?>? maxPair = null) =>
        events.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => Analyze(g.Key, g, zone, now, maxPair?.Invoke(g.Key)));

    /// <summary>H14: Schedule xodimi — juftlik chegarasi o'sha kun smenasi + Api.ShiftPairingSlack; reja yo'q kun — 16 soat (A2).</summary>
    public static Func<DateOnly, TimeSpan> ScheduleMaxPair(ScheduleContext ctx) => date =>
        ctx.ShiftFor(date) is { } shift ? TimeSpan.FromMinutes(shift.DurationMinutes) + Contracts.Api.ShiftPairingSlack : MaxShift;

    /// <summary>
    /// H14 / EmployeeDto.MaxShiftMinutes: telefon uchun — xodimning <paramref name="from"/> dan boshlab amaldagi jadvallari va
    /// istisnolaridagi eng uzun smena + slack; jadval yo'q — null (telefon 16 soat oladi).
    /// </summary>
    public static int? MaxShiftMinutesFor(IEnumerable<EmployeeScheduleAssignment> assignments, IReadOnlyDictionary<Guid, WorkSchedule> schedules,
        IEnumerable<ScheduleOverride> overrides, DateOnly from)
    {
        var ordered = assignments.OrderBy(a => a.EffectiveFrom).ThenBy(a => a.CreatedAt).ToList();
        var current = ordered.LastOrDefault(a => a.EffectiveFrom <= from);
        var durations = ordered.Where(a => a == current || a.EffectiveFrom > from)
            .Select(a => a.ScheduleId is { } id && schedules.TryGetValue(id, out var s) ? s.DurationMinutes : 0)
            .Concat(overrides.Where(o => o.Date >= from && !o.IsOff).Select(o => o.DurationMinutes ?? 0))
            .DefaultIfEmpty(0)
            .Max();
        return durations > 0 ? durations + (int)Contracts.Api.ShiftPairingSlack.TotalMinutes : null;
    }

    /// <summary>A10: shu xodimning shu turdagi voqeasi 2 daqiqa ichida bo'lgan bo'lsa — o'sha voqea (yangi qabul qilinmaydi).</summary>
    public static AttendanceEvent? FindDuplicateTap(IEnumerable<AttendanceEvent> existing, Guid employeeId, AttendanceEventType type, DateTimeOffset at) =>
        existing.Where(e => !e.IsVoided && e.EmployeeId == employeeId && e.Type == type && (at - e.OccurredAt).Duration() < DuplicateTapWindow)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefault();
}

/// <summary>A11: desktopda qo'lda tuzatish. Audit yozuvlarini <see cref="Audit"/> ga yig'adi.</summary>
public sealed class AttendanceEngine(TimeProvider clock)
{
    public const string EntityName = "AttendanceEvent";

    public List<AuditEntry> Audit { get; } = [];

    public AttendanceEvent AddManual(Employee employee, AttendanceEventType type, DateTimeOffset occurredAt, string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var e = new AttendanceEvent
        {
            EmployeeId = employee.Id,
            Type = type,
            OccurredAt = occurredAt,
            DeviceId = null,
            Note = trimmed,
            PendingPush = true,
        };
        Log(e.Id, "Added", null, Describe(employee, e), trimmed);
        return e;
    }

    public void Void(Employee? employee, AttendanceEvent e, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(DomainError.ReasonRequired, "Bekor qilish sababi majburiy.");
        if (e.IsVoided)
            throw new DomainException(DomainError.AlreadyVoided, "Voqea allaqachon bekor qilingan.");
        e.IsVoided = true;
        e.VoidedAt = clock.GetUtcNow();
        e.VoidReason = reason.Trim();
        e.PendingPush = true;
        Log(e.Id, "Voided", Describe(employee, e), null, e.VoidReason);
    }

    private string Describe(Employee? employee, AttendanceEvent e)
    {
        var local = TimeZoneInfo.ConvertTime(e.OccurredAt, clock.LocalTimeZone);
        var kind = e.Type == AttendanceEventType.In ? "Keldi" : "Ketdi";
        return $"{employee?.FullName} — {kind} {local:dd.MM.yyyy HH:mm}".TrimStart(' ', '—');
    }

    private void Log(Guid id, string field, string? oldValue, string? newValue, string? comment) =>
        Audit.Add(new AuditEntry
        {
            At = clock.GetUtcNow(),
            Entity = EntityName,
            EntityId = id,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Comment = comment,
        });
}
