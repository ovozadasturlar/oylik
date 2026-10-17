using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Oylik.Mobile.Logic.Data;

namespace Oylik.Mobile.Logic.Rules;

/// <summary>
/// Xodimlar ro'yxatining bitta qatori. Obyekt identifikatori xodim uchun DOIMIY — ro'yxat yangilanganda
/// shu obyekt joyida yangilanadi, almashtirilmaydi (tugma qaysi xodimga tegishli ekani o'zgarmasin).
/// </summary>
public sealed class EmployeeRow(Guid id, string name) : INotifyPropertyChanged
{
    public Guid Id { get; } = id;

    public string Name { get => name; internal set => Set(ref name, value); }

    private Presence _presence = Presence.None;
    public Presence Presence { get => _presence; private set => Set(ref _presence, value); }

    private string _statusText = Strings.Dash;
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _isInside;
    public bool IsInside { get => _isInside; private set => Set(ref _isInside, value); }

    private string _buttonText = Strings.In;
    public string ButtonText { get => _buttonText; private set => Set(ref _buttonText, value); }

    /// <summary>Oxirgi bosish vaqti (UTC) — barmoq ikki marta tegib ketishidan himoya.</summary>
    public DateTime LastTapUtc { get; set; }

    public void Apply(Presence p, TimeZoneInfo tz)
    {
        Presence = p;
        IsInside = p.Kind == PresenceKind.Inside;
        StatusText = p.Kind switch
        {
            PresenceKind.Inside => $"{Strings.Inside} · {Format.Time(p.At!.Value, tz)}",
            PresenceKind.Left => $"{Strings.Left} · {Format.Time(p.At!.Value, tz)}",
            _ => Strings.Dash,
        };
        ButtonText = IsInside ? Strings.Out : Strings.In;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? prop = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}

/// <summary>
/// Ko'rinadigan ro'yxat (<see cref="Items"/>). Hech qachon Clear/Reset qilinmaydi: mavjud qatorlar joyida yangilanadi,
/// faqat yangilari qo'shiladi, yo'qolganlari olib tashlanadi; tartib faqat ism (yoki filtr) o'zgarganda o'zgaradi.
/// </summary>
public sealed class EmployeeList
{
    private readonly Dictionary<Guid, EmployeeRow> _all = [];
    private readonly StringComparer _cmp = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
    private string _filter = "";

    public ObservableCollection<EmployeeRow> Items { get; } = [];

    public int TotalCount => _all.Count;

    public EmployeeRow? Find(Guid id) => _all.GetValueOrDefault(id);

    public void Update(IEnumerable<LocalEmployee> employees, IReadOnlyDictionary<Guid, Presence> presence, TimeZoneInfo tz)
    {
        var seen = new HashSet<Guid>();
        foreach (var e in employees)
        {
            seen.Add(e.Id);
            if (!_all.TryGetValue(e.Id, out var row)) _all[e.Id] = row = new EmployeeRow(e.Id, e.FullName);
            else row.Name = e.FullName;
            row.Apply(presence.GetValueOrDefault(e.Id, Presence.None), tz);
        }
        foreach (var gone in _all.Keys.Where(k => !seen.Contains(k)).ToList()) _all.Remove(gone);
        Rebuild();
    }

    public void SetFilter(string? text)
    {
        _filter = text?.Trim() ?? "";
        Rebuild();
    }

    private void Rebuild()
    {
        var desired = _all.Values
            .Where(r => _filter.Length == 0 || r.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(r => r.Name, _cmp)
            .ThenBy(r => r.Id)
            .ToList();
        Sync(Items, desired);
    }

    /// <summary>Minimal o'zgarishlar bilan <paramref name="target"/> ni <paramref name="desired"/> ga keltiradi (Clear yo'q).</summary>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        var keep = new HashSet<T>(desired, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!keep.Contains(target[i])) target.RemoveAt(i);

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i])) continue;
            var at = -1;
            for (var j = i + 1; j < target.Count; j++)
                if (ReferenceEquals(target[j], desired[i])) { at = j; break; }
            if (at >= 0) target.Move(at, i);
            else target.Insert(i, desired[i]);
        }
    }
}
