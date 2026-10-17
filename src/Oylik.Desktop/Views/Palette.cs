using Avalonia;
using Avalonia.Media;

namespace Oylik.Desktop.Views;

/// <summary>
/// Kod ichida ishlatiladigan mavzu ranglari (AppStyles.axaml dagi ThemeDictionaries kalitlari).
/// Har kalit uchun bitta brush; mavzu almashganda rangi yangilanadi — bog'langan joylar o'zi qayta chiziladi.
/// </summary>
public static class Palette
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public static IBrush Get(string key)
    {
        if (!Cache.TryGetValue(key, out var brush))
            Cache[key] = brush = new SolidColorBrush(Lookup(key));
        return brush;
    }

    public static void Refresh()
    {
        foreach (var (key, brush) in Cache)
            brush.Color = Lookup(key);
    }

    private static Color Lookup(string key) =>
        Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : Colors.Gray;
}
