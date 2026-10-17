namespace Oylik.Core.Domain;

public readonly record struct MonthKey(int Year, int Month) : IComparable<MonthKey>
{
    private static readonly string[] Names =
    [
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
        "Iyul", "Avgust", "Sentyabr", "Oktabr", "Noyabr", "Dekabr",
    ];

    public static string MonthName(int month) => Names[month - 1];

    public string Name => MonthName(Month);
    public string DisplayName => $"{Name} {Year}";

    public MonthKey Next() => Month == 12 ? new(Year + 1, 1) : new(Year, Month + 1);
    public MonthKey Previous() => Month == 1 ? new(Year - 1, 12) : new(Year, Month - 1);
    public DateOnly FirstDay => new(Year, Month, 1);

    public int CompareTo(MonthKey other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public static bool operator <(MonthKey a, MonthKey b) => a.CompareTo(b) < 0;
    public static bool operator >(MonthKey a, MonthKey b) => a.CompareTo(b) > 0;
    public static bool operator <=(MonthKey a, MonthKey b) => a.CompareTo(b) <= 0;
    public static bool operator >=(MonthKey a, MonthKey b) => a.CompareTo(b) >= 0;
}
