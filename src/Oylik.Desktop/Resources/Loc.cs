using System.ComponentModel;
using System.Reflection;
using Avalonia.Data;

namespace Oylik.Desktop.Resources;

/// <summary>
/// XAML uchun jonli matnlar: `Loc.Instance["NavPayroll"]` = <see cref="Strings"/>.NavPayroll joriy tilda.
/// Til almashganda indexer o'zgargani e'lon qilinadi — barcha bog'langan matnlar darhol yangilanadi.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private static readonly Dictionary<string, Func<string>> Getters = typeof(Strings)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType == typeof(string))
        .Select(p => (p.Name, Get: (Func<string>)p.GetMethod!.CreateDelegate(typeof(Func<string>))))
        .Concat(typeof(Strings).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (f.Name, Get: (Func<string>)(() => (string)f.GetValue(null)!))))
        .ToDictionary(x => x.Name, x => x.Get);

    private Loc() => Strings.LanguageChanged += () =>
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public static bool Has(string key) => Getters.ContainsKey(key);

    /// <summary>Noma'lum kalit — `#kalit#` (xato ko'zga tashlanadi; test barcha XAML kalitlarini tekshiradi).</summary>
    public string this[string key] => Getters.TryGetValue(key, out var get) ? get() : $"#{key}#";
}

/// <summary>`Text="{r:T NavPayroll}"` — <see cref="Loc"/> ga OneWay bog'lanish.</summary>
public sealed class TExtension(string key)
{
    public string Key { get; } = key;

    public Binding ProvideValue(IServiceProvider serviceProvider) => new($"[{Key}]")
    {
        Source = Loc.Instance,
        Mode = BindingMode.OneWay,
    };
}
