namespace Oylik.Core.Domain;

public static class PayrollMath
{
    public const decimal MaxDaysWorked = 31m;
    public const decimal DaysStep = 0.5m;

    /// <summary>R1: DailyRate = round(MonthlySalary ÷ DaysInMonth).</summary>
    public static long DailyRate(long monthlySalary, int daysInMonth)
    {
        if (daysInMonth <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysInMonth));
        return RoundToSum((decimal)monthlySalary / daysInMonth);
    }

    /// <summary>R2: Total = round(DaysWorked × DailyRate).</summary>
    public static long Total(decimal daysWorked, long dailyRate) => RoundToSum(daysWorked * dailyRate);

    public static long RoundToSum(decimal value) => (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    public static bool IsValidDaysWorked(decimal days) =>
        days >= 0 && days <= MaxDaysWorked && days % DaysStep == 0;
}
