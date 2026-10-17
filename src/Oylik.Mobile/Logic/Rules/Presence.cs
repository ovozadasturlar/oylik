using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;

namespace Oylik.Mobile.Logic.Rules;

public enum PresenceKind
{
    None,
    Inside,
    Left,
}

/// <summary>Xodimlar ro'yxatidagi holat: "Ichkarida · 08:02" / "Ketdi · 17:30" / "—".</summary>
public readonly record struct Presence(PresenceKind Kind, DateTimeOffset? At)
{
    public static readonly Presence None = new(PresenceKind.None, null);

    /// <summary>Bosiladigan tugma: ichkarida bo'lsa KETDI, aks holda KELDI.</summary>
    public AttendanceType NextAction => Kind == PresenceKind.Inside ? AttendanceType.Out : AttendanceType.In;
}

public static class PresenceRules
{
    /// <summary>
    /// TZ 7 / H14: "Ichkarida" — oxirgi bekor qilinmagan voqea <c>In</c> va u xodimning juftlik chegarasi ichida
    /// (<paramref name="maxShift"/>: <see cref="LocalEmployee.MaxShift"/>; berilmasa — <see cref="Api.DefaultMaxShift"/>, 16 soat).
    /// "Ketdi" — oxirgi voqea <c>Out</c> va u bugun (mahalliy sana). Aks holda "—".
    /// </summary>
    public static Presence GetPresence(IEnumerable<LocalEvent> employeeEvents, DateTimeOffset now, TimeZoneInfo tz, TimeSpan? maxShift = null)
    {
        var limit = maxShift ?? Api.DefaultMaxShift;
        LocalEvent? last = null;
        foreach (var e in employeeEvents)
        {
            if (e.IsVoided) continue;
            if (last is null || e.OccurredAtUtcTicks > last.OccurredAtUtcTicks) last = e;
        }
        if (last is null) return Presence.None;

        var at = last.OccurredAt;
        if (last.Type == AttendanceType.In)
            return now - at <= limit ? new Presence(PresenceKind.Inside, at) : Presence.None;

        return LocalDate(at, tz) == LocalDate(now, tz) ? new Presence(PresenceKind.Left, at) : Presence.None;
    }

    /// <summary>
    /// A10: shu xodim uchun shu turdagi bekor qilinmagan voqea 2 daqiqa ichida bor bo'lsa — o'sha voqea (yangisi yozilmaydi).
    /// </summary>
    public static LocalEvent? FindDuplicate(IEnumerable<LocalEvent> employeeEvents, AttendanceType type, DateTimeOffset now)
    {
        LocalEvent? hit = null;
        foreach (var e in employeeEvents)
        {
            if (e.IsVoided || e.Type != type) continue;
            if ((now - e.OccurredAt).Duration() >= Api.DuplicateTapWindow) continue;
            if (hit is null || e.OccurredAtUtcTicks > hit.OccurredAtUtcTicks) hit = e;
        }
        return hit;
    }

    public static DateOnly LocalDate(DateTimeOffset t, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, tz).DateTime);

    public static DateTimeOffset StartOfLocalDay(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }
}
