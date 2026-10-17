namespace Oylik.Core.Domain;

/// <summary>4.3 Ish jadvali: shablonlar, biriktirish tarixi, istisnolar, sababli kunlar, hisob usuli. Har biri audit bilan.</summary>
public sealed partial class PayrollEngine
{
    // ---------- H1: shablonlar ----------

    public static void ValidateSchedule(WorkSchedule s)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
            throw new DomainException(DomainError.InvalidSchedule, "Jadval nomi bo'sh.", "Name");
        if (s.DurationMinutes is < WorkSchedule.MinDuration or > WorkSchedule.MaxDuration)
            throw new DomainException(DomainError.InvalidSchedule, "Davomiylik 00:30–24:00 bo'lishi kerak.", "Duration");
        if (s.BreakMinutes < 0 || s.BreakMinutes >= s.DurationMinutes)
            throw new DomainException(DomainError.InvalidSchedule, "Tanaffus smenadan qisqa bo'lishi kerak.", "Break");
        if (s.Kind == ScheduleKind.Weekly && (s.WeekDays & WorkSchedule.EveryDay) == 0)
            throw new DomainException(DomainError.InvalidSchedule, "Kamida bitta hafta kuni tanlang.", "WeekDays");
        if (s.Kind == ScheduleKind.Cycle && (s.CycleWorkDays < 1 || s.CycleOffDays < 0 || s.CycleWorkDays + s.CycleOffDays > 31))
            throw new DomainException(DomainError.InvalidSchedule, "Ish/dam kunlari noto'g'ri.", "Cycle");
    }

    public WorkSchedule CreateSchedule(WorkSchedule schedule)
    {
        schedule.Name = schedule.Name.Trim();
        ValidateSchedule(schedule);
        Log("WorkSchedule", schedule.Id, "Created", null, Describe(schedule));
        return schedule;
    }

    /// <summary>Shablon o'zgarishi — ochiq oylar qayta hisoblanadi, yopiq oylar muzlatilgan (H10).</summary>
    public void UpdateSchedule(WorkSchedule schedule, WorkSchedule changes)
    {
        changes.Name = changes.Name.Trim();
        ValidateSchedule(changes);
        var before = Describe(schedule);
        schedule.Name = changes.Name;
        schedule.Kind = changes.Kind;
        schedule.WeekDays = changes.WeekDays;
        schedule.CycleWorkDays = changes.CycleWorkDays;
        schedule.CycleOffDays = changes.CycleOffDays;
        schedule.StartTime = changes.StartTime;
        schedule.DurationMinutes = changes.DurationMinutes;
        schedule.BreakMinutes = changes.BreakMinutes;
        var after = Describe(schedule);
        if (before != after)
            Log("WorkSchedule", schedule.Id, "Updated", before, after);
    }

    public void EnsureScheduleUnused(WorkSchedule schedule, IEnumerable<EmployeeScheduleAssignment> assignments)
    {
        if (assignments.Any(a => a.ScheduleId == schedule.Id))
            throw new DomainException(DomainError.ScheduleInUse, $"{schedule.Name}: xodimga biriktirilgan.", schedule.Name);
        Log("WorkSchedule", schedule.Id, "Deleted", Describe(schedule), null);
    }

    /// <summary>Audit uchun: `Kunduzgi · Weekly · 08:00 · 09:00 · tanaffus 60`.</summary>
    public static string Describe(WorkSchedule s)
    {
        var duration = $"{s.DurationMinutes / 60:00}:{s.DurationMinutes % 60:00}";
        var pattern = s.Kind == ScheduleKind.Weekly
            ? string.Join(",", Enum.GetValues<DayOfWeek>().Where(s.WorksOn).Select(d => ((int)d).ToString()))
            : $"{s.CycleWorkDays}/{s.CycleOffDays}";
        return $"{s.Name} · {s.Kind} {pattern} · {s.StartTime:HH\\:mm} · {duration} · {s.BreakMinutes}";
    }

    // ---------- H2, H13.5: biriktirish tarixi ----------

    public EmployeeScheduleAssignment AssignSchedule(Employee employee, WorkSchedule? schedule, DateOnly? cycleAnchor, DateOnly effectiveFrom)
    {
        if (schedule is { Kind: ScheduleKind.Cycle } && cycleAnchor is null)
            throw new DomainException(DomainError.CycleAnchorRequired, "Cycle jadval uchun birinchi ish kuni sanasi kerak.");
        var assignment = new EmployeeScheduleAssignment
        {
            EmployeeId = employee.Id,
            ScheduleId = schedule?.Id,
            CycleAnchor = schedule?.Kind == ScheduleKind.Cycle ? cycleAnchor : null,
            EffectiveFrom = effectiveFrom,
            CreatedAt = Now,
        };
        Log("Employee", employee.Id, "Schedule", null,
            schedule is null ? "—" : schedule.Name + (assignment.CycleAnchor is { } a ? $" ({a:dd.MM.yyyy})" : ""),
            $"{effectiveFrom:dd.MM.yyyy} dan");
        employee.UpdatedAt = Now;
        return assignment;
    }

    /// <summary>4.3: hisob usuli — karta va ochiq oylardagi qatorlar (R9 kabi), yopiq oylar o'zgarmaydi.</summary>
    public void SetPayMode(Employee employee, PayMode mode, IEnumerable<PayrollPeriod> periods)
    {
        if (employee.PayMode != mode)
        {
            Log("Employee", employee.Id, "PayMode", employee.PayMode.ToString(), mode.ToString());
            employee.PayMode = mode;
            employee.UpdatedAt = Now;
        }
        foreach (var period in periods.Where(p => !p.IsClosed))
        {
            if (period.FindLine(employee.Id) is { } line && line.PayMode != mode)
            {
                Log("PayrollLine", line.Id, "PayMode", line.PayMode.ToString(), mode.ToString(), period.Key.DisplayName);
                line.PayMode = mode;
            }
        }
    }

    /// <summary>H7, H13.7: null — Sozlamalardagi default.</summary>
    public void SetEmployeeOvertime(Employee employee, OvertimePolicy? policy, decimal? value)
    {
        if (value is < 0)
            throw new DomainException(DomainError.InvalidAmount, "Summa noto'g'ri.");
        if (employee.OvertimePolicy == policy && employee.OvertimeValue == value)
            return;
        Log("Employee", employee.Id, "OvertimePolicy", $"{employee.OvertimePolicy} {employee.OvertimeValue}".Trim(), $"{policy} {value}".Trim());
        employee.OvertimePolicy = policy;
        employee.OvertimeValue = value;
        employee.UpdatedAt = Now;
    }

    // ---------- H2 istisno, H8 sababli kun — izoh majburiy ----------

    private static string RequireNote(string? note)
    {
        var trimmed = note?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new DomainException(DomainError.ReasonRequired, "Izoh majburiy.");
        return trimmed.Length > 200 ? trimmed[..200] : trimmed;
    }

    public ScheduleOverride SetOverride(Employee employee, DateOnly date, bool isOff, TimeOnly? start, int? durationMinutes, int? breakMinutes, string? note)
    {
        var text = RequireNote(note);
        if (!isOff)
        {
            if (start is null || durationMinutes is null or < WorkSchedule.MinDuration or > WorkSchedule.MaxDuration)
                throw new DomainException(DomainError.InvalidSchedule, "Davomiylik 00:30–24:00 bo'lishi kerak.", "Duration");
            if (breakMinutes is < 0 || breakMinutes >= durationMinutes)
                throw new DomainException(DomainError.InvalidSchedule, "Tanaffus smenadan qisqa bo'lishi kerak.", "Break");
        }
        var o = new ScheduleOverride
        {
            EmployeeId = employee.Id,
            Date = date,
            IsOff = isOff,
            StartTime = isOff ? null : start,
            DurationMinutes = isOff ? null : durationMinutes,
            BreakMinutes = isOff ? null : breakMinutes,
            Note = text,
            CreatedAt = Now,
        };
        var value = isOff ? "Dam" : $"{start:HH\\:mm} +{durationMinutes / 60:00}:{durationMinutes % 60:00}";
        Log("ScheduleOverride", employee.Id, date.ToString("dd.MM.yyyy"), null, value, text);
        return o;
    }

    public void RemoveOverride(Employee employee, ScheduleOverride o, string? note) =>
        Log("ScheduleOverride", employee.Id, o.Date.ToString("dd.MM.yyyy"), o.IsOff ? "Dam" : $"{o.StartTime:HH\\:mm}", null, RequireNote(note));

    public ExcusedDay SetExcused(Employee employee, DateOnly date, ExcusedKind kind, string? note)
    {
        var text = RequireNote(note);
        Log("ExcusedDay", employee.Id, date.ToString("dd.MM.yyyy"), null, kind.ToString(), text);
        return new ExcusedDay { EmployeeId = employee.Id, Date = date, Kind = kind, Note = text, CreatedAt = Now };
    }

    public void RemoveExcused(Employee employee, ExcusedDay day, string? note) =>
        Log("ExcusedDay", employee.Id, day.Date.ToString("dd.MM.yyyy"), day.Kind.ToString(), null, RequireNote(note));

    // ---------- H9/H10: natijani qatorga yozish ----------

    /// <summary>Ochiq oy qatoriga hisob natijasi (jonli). Yopiq oy — muzlatilgan, o'zgartirilmaydi (H10).</summary>
    public static void ApplySchedule(PayrollPeriod period, PayrollLine line, ScheduleMonthResult result)
    {
        if (period.IsClosed)
            return;
        line.PlannedMinutes = result.PlannedMinutes;
        line.WorkedMinutes = result.WorkedMinutes;
        line.LateMinutes = result.LateMinutes;
        line.EarlyLeaveMinutes = result.EarlyLeaveMinutes;
        line.OvertimeMinutes = result.OvertimeMinutes;
        line.BasePay = result.BasePay;
        line.OvertimePay = result.OvertimePay;
        line.GraceMinutes = result.Policy.GraceMinutes;
        line.OvertimePolicy = result.Policy.Overtime;
        line.OvertimeValue = result.Policy.OvertimeValue;
    }

    /// <summary>Xodim uchun amaldagi siyosat: Sozlamalar default + xodim (H13.7).</summary>
    public static SchedulePolicy PolicyFor(AppSettings settings, Employee employee) => new(
        Math.Clamp(settings.LateGraceMinutes, 0, ScheduleCalculator.MaxGraceMinutes),
        employee.OvertimePolicy ?? settings.OvertimePolicy,
        employee.OvertimePolicy is null ? settings.OvertimeValue : employee.OvertimeValue ?? settings.OvertimeValue);
}
