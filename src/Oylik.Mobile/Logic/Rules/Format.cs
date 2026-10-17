using System.Globalization;

namespace Oylik.Mobile.Logic.Rules;

/// <summary>Ko'rinish: vaqt "08:02", soat "9:30", sana "09.10.2026".</summary>
public static class Format
{
    public static string Time(DateTimeOffset t, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTime(t, tz).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Ishlangan vaqt "s:dd" (5 daq → "0:05", 9,5 s → "9:30"). Butun daqiqagacha pastga: 4:29:50 → "4:29".</summary>
    public static string Hours(TimeSpan worked)
    {
        var minutes = Math.Max(0, (long)Math.Floor(worked.TotalMinutes));
        return $"{minutes / 60}:{minutes % 60:00}";
    }

    public static string Date(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string DateTime(DateTimeOffset t, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTime(t, tz).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
