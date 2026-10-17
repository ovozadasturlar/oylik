using System.Globalization;

namespace Oylik.Core.Domain;

public enum AmountField
{
    MonthlySalary,
    DailyRate,
    Advance,
}

/// <summary>R11: qisqa kiritish.</summary>
public static class ShortInput
{
    public const decimal SalaryThreshold = 1_000m;
    public const decimal SalaryMultiplier = 1_000_000m;
    public const decimal SmallThreshold = 10_000m;
    public const decimal SmallMultiplier = 1_000m;

    public static long Expand(decimal value, AmountField field, bool enabled)
    {
        if (enabled)
        {
            if (field == AmountField.MonthlySalary && value < SalaryThreshold)
                value *= SalaryMultiplier;
            else if (field != AmountField.MonthlySalary && value < SmallThreshold)
                value *= SmallMultiplier;
        }
        return PayrollMath.RoundToSum(value);
    }

    /// <summary>"6" → 6 000 000, "6,5" → 6 500 000, "200" → 200 000, "6 000 000" → 6 000 000.</summary>
    public static bool TryParse(string? text, AmountField field, bool enabled, out long result)
    {
        result = 0;
        if (!NumberText.TryParseDecimal(text, out var value) || value < 0)
            return false;
        result = Expand(value, field, enabled);
        return true;
    }
}

public static class NumberText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c) && c != ' ' && c != ' ').ToArray())
            .Replace(',', '.')
            .Replace('−', '-');
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant, out value);
    }

    public static bool TryParseDays(string? text, out decimal days)
    {
        if (TryParseDecimal(text, out days) && PayrollMath.IsValidDaysWorked(days))
            return true;
        days = 0;
        return false;
    }

    /// <summary>6000000 → "6 000 000" (bo'sh joy — ming ajratgich); manfiy → "−1 200 000".</summary>
    public static string Money(long value)
    {
        var digits = Math.Abs(value).ToString("#,0", Invariant).Replace(',', ' ');
        return value < 0 ? "−" + digits : digits;
    }

    /// <summary>26 → "26", 26.5 → "26,5".</summary>
    public static string Days(decimal days) => days.ToString("0.##", Invariant).Replace('.', ',');
}
