namespace Oylik.Core.Domain;

public sealed record PeriodSummary(int EmployeeCount, decimal DaysWorked, long Total, long AdvanceTotal, long Remainder)
{
    public static PeriodSummary Of(IEnumerable<PayrollLine> lines)
    {
        int count = 0;
        decimal days = 0;
        long total = 0, advances = 0;
        foreach (var line in lines)
        {
            count++;
            days += line.DaysWorked;
            total += line.Total;
            advances += line.AdvanceTotal;
        }
        return new(count, days, total, advances, total - advances);
    }
}
