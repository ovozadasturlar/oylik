using Microsoft.EntityFrameworkCore;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>4.3 Ish jadvali: shablonlar, biriktirish tarixi, istisnolar, sababli kunlar va Schedule qatorlarini jonli hisoblash (H10).</summary>
public sealed partial class PayrollService
{
    public IReadOnlyList<WorkSchedule> Schedules => _db.Schedules.Local.OrderBy(s => s.SortOrder).ThenBy(s => s.Name, NameComparer.Instance).ToList();

    public WorkSchedule? FindSchedule(Guid? id) => id is { } x ? _db.Schedules.Local.FirstOrDefault(s => s.Id == x) : null;

    private async Task LoadSchedulesAsync()
    {
        await _db.Schedules.LoadAsync();
        await _db.ScheduleAssignments.LoadAsync();
        await _db.ScheduleOverrides.LoadAsync();
        await _db.ExcusedDays.LoadAsync();
    }

    public IReadOnlyList<EmployeeScheduleAssignment> AssignmentsOf(Guid employeeId) =>
        _db.ScheduleAssignments.Local.Where(a => a.EmployeeId == employeeId).OrderBy(a => a.EffectiveFrom).ThenBy(a => a.CreatedAt).ToList();

    public IReadOnlyList<ScheduleOverride> OverridesOf(Guid employeeId) =>
        _db.ScheduleOverrides.Local.Where(o => o.EmployeeId == employeeId).ToList();

    public IReadOnlyList<ExcusedDay> ExcusedOf(Guid employeeId) =>
        _db.ExcusedDays.Local.Where(x => x.EmployeeId == employeeId).ToList();

    public ScheduleContext ScheduleContextFor(Guid employeeId) => new(
        AssignmentsOf(employeeId),
        _db.Schedules.Local.ToDictionary(s => s.Id),
        OverridesOf(employeeId),
        ExcusedOf(employeeId),
        LocalZone);

    /// <summary>Amaldagi jadval (bugun) — Xodimlar jadvali uchun.</summary>
    public EmployeeScheduleAssignment? CurrentAssignment(Guid employeeId) =>
        ScheduleContextFor(employeeId).AssignmentOn(Today);

    public DateOnly Today => AttendanceRules.LocalDate(_clock.GetUtcNow(), LocalZone);

    public SchedulePolicy PolicyFor(Employee employee) => PayrollEngine.PolicyFor(Settings, employee);

    public StatusThresholds Thresholds => new(Settings.AbsentAfterMinutes, Settings.GreenPercent, Settings.YellowPercent);

    /// <summary>H12: Schedule xodimining bugungi jonli holati (ism katagini bo'yash uchun); Daily — null.</summary>
    public ShiftStatus? TodayStatus(Employee employee, IReadOnlyDictionary<Guid, EmployeeAttendance>? analysis = null)
    {
        if (employee.PayMode != PayMode.Schedule)
            return null;
        analysis ??= AnalyzeAttendance();
        return ScheduleCalculator.Status(Today, ScheduleContextFor(employee.Id), analysis.GetValueOrDefault(employee.Id)?.Pairs ?? [],
            _clock.GetUtcNow(), PolicyFor(employee).GraceMinutes, Thresholds);
    }

    /// <summary>H14: juftlik chegarasi — Schedule xodimida smenaga qarab, qolganlarda 16 soat.</summary>
    private Func<Guid, Func<DateOnly, TimeSpan>?> MaxPairResolver()
    {
        var scheduled = _db.Employees.Local.Where(e => e.PayMode == PayMode.Schedule).Select(e => e.Id).ToHashSet();
        return id => scheduled.Contains(id) ? AttendanceRules.ScheduleMaxPair(ScheduleContextFor(id)) : null;
    }

    /// <summary>H14 / EmployeeDto.MaxShiftMinutes: telefon «Ichkarida» chegarasi (Daily — null).</summary>
    public int? MaxShiftMinutesFor(Employee employee) => employee.PayMode != PayMode.Schedule
        ? null
        : AttendanceRules.MaxShiftMinutesFor(AssignmentsOf(employee.Id), _db.Schedules.Local.ToDictionary(s => s.Id), OverridesOf(employee.Id), Today.AddDays(-1));

    /// <summary>Schedule qatori uchun oy hisobi — ochiq oyda joriy siyosat bilan (yopiq oy qatorida muzlatilgan qiymatlar).</summary>
    public ScheduleMonthResult CalculateSchedule(PayrollPeriod period, PayrollLine line, IReadOnlyDictionary<Guid, EmployeeAttendance>? analysis = null)
    {
        var employee = EmployeeOf(line);
        analysis ??= AnalyzeAttendance();
        var pairs = analysis.TryGetValue(employee.Id, out var a) ? a.Pairs : [];
        var policy = period.IsClosed
            ? new SchedulePolicy(line.GraceMinutes, line.OvertimePolicy, line.OvertimeValue)
            : PolicyFor(employee);
        return ScheduleCalculator.Calculate(period.Key, line.MonthlySalary, pairs, ScheduleContextFor(employee.Id), policy);
    }

    /// <summary>H10: ochiq oylardagi Schedule qatorlari jonli qayta hisoblanadi; yopiq oylar — hech qachon.</summary>
    private void RecalculateOpenSchedules()
    {
        if (AttendanceOnly)
            return; // agent: Oylik qatorlari desktopniki
        var lines = _db.Periods.Local.Where(p => !p.IsClosed)
            .SelectMany(p => p.Lines.Where(l => l.PayMode == PayMode.Schedule).Select(l => (Period: p, Line: l)))
            .ToList();
        if (lines.Count == 0)
            return;
        var analysis = AnalyzeAttendance();
        foreach (var (period, line) in lines)
            PayrollEngine.ApplySchedule(period, line, CalculateSchedule(period, line, analysis));
    }

    /// <summary>H11: yopishdan oldin — oydagi tasdiqlanmagan voqealar (OpenNoOut/OrphanOut) soni.</summary>
    public int UnconfirmedEventsCount(PayrollPeriod period)
    {
        var (from, to) = MonthWindow(period.Key);
        var ids = period.Lines.Select(l => l.EmployeeId).ToHashSet();
        return AnalyzeAttendance().Values.Where(a => ids.Contains(a.EmployeeId)).SelectMany(a => a.Issues)
            .Count(i => i.Event.OccurredAt >= from.AddDays(1) && i.Event.OccurredAt < to.AddDays(-1));
    }

    // ---------- H1: shablonlar ----------

    public Task<WorkSchedule> CreateScheduleAsync(WorkSchedule schedule) => RunAsync(e =>
    {
        schedule.SortOrder = _db.Schedules.Local.Select(s => s.SortOrder).DefaultIfEmpty(0).Max() + 1;
        e.CreateSchedule(schedule);
        _db.Schedules.Add(schedule);
        return schedule;
    });

    /// <summary>Shablon o'zgarsa — biriktirilgan xodimlar «o'zgargan» (telefonga MaxShiftMinutes qayta boradi, H14).</summary>
    public Task UpdateScheduleAsync(WorkSchedule schedule, WorkSchedule changes) => RunAsync(e =>
    {
        e.UpdateSchedule(schedule, changes);
        TouchEmployees(_db.ScheduleAssignments.Local.Where(a => a.ScheduleId == schedule.Id).Select(a => a.EmployeeId));
    });

    public bool IsScheduleUsed(WorkSchedule schedule) => _db.ScheduleAssignments.Local.Any(a => a.ScheduleId == schedule.Id);

    public Task DeleteScheduleAsync(WorkSchedule schedule) => RunAsync(e =>
    {
        e.EnsureScheduleUnused(schedule, _db.ScheduleAssignments.Local);
        _db.Schedules.Remove(schedule);
    });

    private void TouchEmployees(IEnumerable<Guid> ids)
    {
        var now = _clock.GetUtcNow();
        foreach (var id in ids.Distinct())
            if (_db.Employees.Local.FirstOrDefault(x => x.Id == id) is { } employee)
                employee.UpdatedAt = now;
    }

    // ---------- H2, H13.5: biriktirish, hisob usuli, siyosat ----------

    public Task<EmployeeScheduleAssignment> AssignScheduleAsync(Employee employee, WorkSchedule? schedule, DateOnly? cycleAnchor, DateOnly effectiveFrom) =>
        RunAsync(e =>
        {
            // shu sanadan boshlangan oldingi yozuv bo'lsa — almashtiriladi (tarixda bir sanada bitta)
            foreach (var same in _db.ScheduleAssignments.Local.Where(a => a.EmployeeId == employee.Id && a.EffectiveFrom == effectiveFrom).ToList())
                _db.ScheduleAssignments.Remove(same);
            var assignment = e.AssignSchedule(employee, schedule, cycleAnchor, effectiveFrom);
            _db.ScheduleAssignments.Add(assignment);
            return assignment;
        });

    public Task RemoveAssignmentAsync(Employee employee, EmployeeScheduleAssignment assignment) => RunAsync(e =>
    {
        e.Log("Employee", employee.Id, "Schedule", FindSchedule(assignment.ScheduleId)?.Name ?? "—", null, $"{assignment.EffectiveFrom:dd.MM.yyyy} dan — o'chirildi");
        employee.UpdatedAt = _clock.GetUtcNow();
        _db.ScheduleAssignments.Remove(assignment);
    });

    public Task SetPayModeAsync(Employee employee, PayMode mode) =>
        RunAsync(e => e.SetPayMode(employee, mode, _db.Periods.Local));

    public Task SetEmployeeOvertimeAsync(Employee employee, OvertimePolicy? policy, decimal? value) =>
        RunAsync(e => e.SetEmployeeOvertime(employee, policy, value));

    // ---------- H2 istisno, H8 sababli kun ----------

    public Task<ScheduleOverride> SetOverrideAsync(Employee employee, DateOnly date, bool isOff, TimeOnly? start, int? duration, int? breakMinutes, string? note) =>
        RunAsync(e =>
        {
            var o = e.SetOverride(employee, date, isOff, start, duration, breakMinutes, note);
            _db.ScheduleOverrides.RemoveRange(_db.ScheduleOverrides.Local.Where(x => x.EmployeeId == employee.Id && x.Date == date).ToList());
            _db.ScheduleOverrides.Add(o);
            employee.UpdatedAt = _clock.GetUtcNow();
            return o;
        });

    public Task RemoveOverrideAsync(Employee employee, ScheduleOverride o, string? note) => RunAsync(e =>
    {
        e.RemoveOverride(employee, o, note);
        _db.ScheduleOverrides.Remove(o);
        employee.UpdatedAt = _clock.GetUtcNow();
    });

    public Task<ExcusedDay> SetExcusedAsync(Employee employee, DateOnly date, ExcusedKind kind, string? note) => RunAsync(e =>
    {
        var day = e.SetExcused(employee, date, kind, note);
        _db.ExcusedDays.RemoveRange(_db.ExcusedDays.Local.Where(x => x.EmployeeId == employee.Id && x.Date == date).ToList());
        _db.ExcusedDays.Add(day);
        return day;
    });

    public Task RemoveExcusedAsync(Employee employee, ExcusedDay day, string? note) => RunAsync(e =>
    {
        e.RemoveExcused(employee, day, note);
        _db.ExcusedDays.Remove(day);
    });
}
