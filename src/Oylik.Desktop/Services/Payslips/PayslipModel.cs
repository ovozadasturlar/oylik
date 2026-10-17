using Oylik.Core.Domain;
using Oylik.Data;

namespace Oylik.Desktop.Services.Payslips;

/// <summary>5.11: hisob varaqasining bitta kun qatori.</summary>
public sealed record PayslipDay(
    DateOnly Date,
    string? Plan,
    DateTimeOffset? In,
    DateTimeOffset? Out,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    int OvertimeMinutes,
    long Pay,
    string? Note,
    bool IsCorrected,
    ExcusedKind? Excused);

public sealed record PayslipAdvance(DateOnly Date, long Amount, AdvanceKind Kind, string? Note);

/// <summary>5.11: bitta xodimning oy bo'yicha hisob varaqasi — PDF shu ma'lumotdan chiziladi (testlanadi).</summary>
public sealed record PayslipModel
{
    public required string Company { get; init; }
    public required string? Branch { get; init; }
    public required string EmployeeName { get; init; }
    public required int TabelNo { get; init; }
    public required MonthKey Month { get; init; }
    public required bool IsFinal { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required PayMode Mode { get; init; }
    public required long MonthlySalary { get; init; }

    // Kunbay
    public long DailyRate { get; init; }
    public decimal DaysWorked { get; init; }
    public int DaysInMonth { get; init; }

    // Jadval bo'yicha
    public string? ScheduleName { get; init; }
    public string? ScheduleSummary { get; init; }
    public int PlannedDays { get; init; }
    public int PlannedMinutes { get; init; }
    public int WorkedMinutes { get; init; }
    public int LateMinutes { get; init; }
    public int EarlyLeaveMinutes { get; init; }
    public int OvertimeMinutes { get; init; }
    public decimal HourlyRate { get; init; }
    public int GraceMinutes { get; init; }
    public OvertimePolicy OvertimePolicy { get; init; }
    public decimal OvertimeValue { get; init; }
    public long BasePay { get; init; }
    public long OvertimePay { get; init; }

    public required IReadOnlyList<PayslipDay> Days { get; init; }
    public required IReadOnlyList<PayslipAdvance> Advances { get; init; }
    public required long Total { get; init; }
    public required long AdvanceTotal { get; init; }
    public long Remainder => Total - AdvanceTotal;
    public long CarryOverDebt => Advances.Where(a => a.Kind == AdvanceKind.CarryOverDebt).Sum(a => a.Amount);

    /// <summary>H13.10: `OY-2026-10-0042`.</summary>
    public string DocumentNumber => $"OY-{Month.Year:0000}-{Month.Month:00}-{TabelNo:0000}";
}

/// <summary>Umumiy vedomost qatori (imzo ustuni bilan).</summary>
public sealed record PayrollSheetRow(int No, int TabelNo, string Name, PayMode Mode, string Measure, long Total, long Advance, long Remainder);

public sealed record PayrollSheetModel(string Company, string? Branch, MonthKey Month, bool IsFinal, DateTimeOffset GeneratedAt, IReadOnlyList<PayrollSheetRow> Rows)
{
    public long Total => Rows.Sum(r => r.Total);
    public long Advance => Rows.Sum(r => r.Advance);
    public long Remainder => Rows.Sum(r => r.Remainder);
    public string DocumentNumber => $"OY-{Month.Year:0000}-{Month.Month:00}-V";
}

/// <summary>Servisdan hisob varaqasi ma'lumotini yig'adi.</summary>
public static class PayslipBuilder
{
    public static PayslipModel Build(PayrollService service, PayrollPeriod period, PayrollLine line, Func<DateOnly, DateOnly, string?>? scheduleNameOn = null)
    {
        var employee = line.Employee ?? service.Employees.First(e => e.Id == line.EmployeeId);
        var zone = service.LocalZone;
        var analysis = service.AnalyzeAttendance();
        var attendance = analysis.GetValueOrDefault(employee.Id);
        var days = new List<PayslipDay>();
        var first = period.Key.FirstDay;
        var last = first.AddMonths(1).AddDays(-1);

        DateTimeOffset Local(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, zone);
        (bool Corrected, string? Note) Corrections(DateOnly date)
        {
            var events = service.EventsOf(employee.Id, date, attendance);
            var notes = events.Where(e => e.IsManual || e.IsVoided)
                .Select(e => e.IsVoided ? $"× {Local(e.OccurredAt):HH:mm} — {e.VoidReason}" : $"{Local(e.OccurredAt):HH:mm} — {e.Note}")
                .Distinct()
                .ToList();
            return (notes.Count > 0, notes.Count > 0 ? string.Join("; ", notes) : null);
        }

        var model = new PayslipModel
        {
            Company = service.Settings.CompanyName,
            Branch = service.FindBranch(line.BranchId)?.Name,
            EmployeeName = employee.FullName,
            TabelNo = employee.TabelNo,
            Month = period.Key,
            IsFinal = period.IsClosed,
            GeneratedAt = service.Clock.GetLocalNow(),
            Mode = line.PayMode,
            MonthlySalary = line.MonthlySalary,
            DailyRate = line.DailyRate,
            DaysWorked = line.DaysWorked,
            DaysInMonth = service.Settings.DaysInMonth,
            Days = days,
            Advances = line.Advances.OrderBy(a => a.Date).Select(a => new PayslipAdvance(a.Date, a.Amount, a.Kind, a.Note)).ToList(),
            Total = line.Total,
            AdvanceTotal = line.AdvanceTotal,
        };

        if (!line.IsSchedule)
        {
            // Kunbay: davomat bo'lsa — kunlar bo'yicha (A5–A7)
            for (var d = first; d <= last; d = d.AddDays(1))
            {
                if (attendance?.Day(d) is not { } day || day.Pairs.Count == 0)
                    continue;
                var (corrected, note) = Corrections(d);
                days.Add(new PayslipDay(d, null, day.Pairs[0].In.OccurredAt, day.Pairs.LastOrDefault(p => p.Out is not null)?.Out?.OccurredAt,
                    day.Minutes, 0, 0, 0, 0, note, corrected, null));
            }
            return model;
        }

        // Jadval bo'yicha: kunlik tafsilot — joriy ma'lumotdan (yopiq oyda muzlatilgan siyosat bilan); jami — qatordagi (H10)
        var result = service.CalculateSchedule(period, line, analysis);
        // 5.11: kunlik summalar Σ = BasePay aniq (largest remainder)
        var pays = ScheduleCalculator.DistributeBasePay(line.MonthlySalary, line.PlannedMinutes,
            result.Days.Select(d => d.WorkedMinutes).ToList(), line.BasePay);
        var index = 0;
        foreach (var d in result.Days)
        {
            var (corrected, note) = Corrections(d.Date);
            if (d.Excused is { } x)
                note = string.IsNullOrEmpty(note) ? x.Note : $"{x.Note}; {note}";
            var plan = d.Shift is { } w ? $"{Local(w.Start):HH:mm}–{Local(w.End):HH:mm}" : null;
            var pay = pays[index++];
            days.Add(new PayslipDay(d.Date, plan, d.FirstIn, d.LastOut, d.WorkedMinutes, d.LateMinutes, d.EarlyLeaveMinutes, d.OvertimeMinutes,
                pay, note, corrected, d.Excused?.Kind));
        }
        return model with
        {
            ScheduleName = scheduleNameOn?.Invoke(first, last),
            ScheduleSummary = null,
            PlannedDays = result.Days.Count(d => d.Shift is not null),
            PlannedMinutes = line.PlannedMinutes,
            WorkedMinutes = line.WorkedMinutes,
            LateMinutes = line.LateMinutes,
            EarlyLeaveMinutes = line.EarlyLeaveMinutes,
            OvertimeMinutes = line.OvertimeMinutes,
            HourlyRate = line.HourlyRate,
            GraceMinutes = line.GraceMinutes,
            OvertimePolicy = line.OvertimePolicy,
            OvertimeValue = line.OvertimeValue,
            BasePay = line.BasePay,
            OvertimePay = line.OvertimePay,
        };
    }

    public static PayrollSheetModel Sheet(PayrollService service, PayrollPeriod period, IEnumerable<PayrollLine> lines, string? branch, Func<PayrollLine, string> measure)
    {
        var rows = lines.Select((l, i) =>
        {
            var e = l.Employee ?? service.Employees.First(x => x.Id == l.EmployeeId);
            return new PayrollSheetRow(i + 1, e.TabelNo, e.FullName, l.PayMode, measure(l), l.Total, l.AdvanceTotal, l.Remainder);
        }).ToList();
        return new PayrollSheetModel(service.Settings.CompanyName, branch, period.Key, period.IsClosed, service.Clock.GetLocalNow(), rows);
    }
}
