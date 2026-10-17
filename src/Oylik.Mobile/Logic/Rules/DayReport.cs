using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;

namespace Oylik.Mobile.Logic.Rules;

/// <summary>"Bugun" ekranining bir qatori.</summary>
public sealed record DayRow(
    Guid EmployeeId,
    string Name,
    DateTimeOffset? FirstIn,
    DateTimeOffset? LastOut,
    TimeSpan Worked,
    bool Inside,
    bool Absent)
{
    /// <summary>Soat (o'nli) — faqat testlar/taqqoslash uchun; ko'rinish <see cref="Format.Hours"/> orqali "s:dd".</summary>
    public double Hours => Math.Round(Worked.TotalHours, 2);
}

public sealed record DayReport(DateOnly Date, IReadOnlyList<DayRow> Rows)
{
    public int InsideCount => Rows.Count(r => r.Inside);
    public int LeftCount => Rows.Count(r => !r.Inside && !r.Absent);
    public int AbsentCount => Rows.Count(r => r.Absent);
}

/// <summary>
/// Telefondagi kunlik hisob (faqat ko'rsatish uchun; rasmiy hisob desktopda, A1–A8).
/// A1: bekor qilinganlar hisobga olinmaydi. A2/H14: In → keyingi birinchi Out, ≤ xodim chegarasi (16 soat; sutkalik — 28). A4: ketma-ket In — birinchisi ochiq qoladi.
/// A5: kun = In'ning mahalliy sanasi (tungi smena oldingi kunga). A6: juftliklar yig'iladi.
/// Ichkarida (bugun): oxirgi ochiq In 16 soat ichida — soat hozirgacha jonli hisoblanadi.
/// </summary>
public static class DayReportBuilder
{
    public static DayReport Build(
        IEnumerable<LocalEmployee> employees,
        IEnumerable<LocalEvent> events,
        DateOnly date,
        DateTimeOffset now,
        TimeZoneInfo tz)
    {
        var byEmployee = events.Where(e => !e.IsVoided).GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = new List<DayRow>();

        foreach (var emp in employees)
        {
            byEmployee.TryGetValue(emp.Id, out var evs);
            rows.Add(BuildRow(emp, evs ?? [], date, now, tz));
        }

        rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return new DayReport(date, rows);
    }

    private static DayRow BuildRow(LocalEmployee emp, List<LocalEvent> evs, DateOnly date, DateTimeOffset now, TimeZoneInfo tz)
    {
        evs.Sort((a, b) => a.OccurredAtUtcTicks.CompareTo(b.OccurredAtUtcTicks));

        DateTimeOffset? firstIn = null, lastOut = null;
        var worked = TimeSpan.Zero;
        bool inside = false;
        DateTimeOffset? open = null;

        void NoteIn(DateTimeOffset at)
        {
            if (PresenceRules.LocalDate(at, tz) == date && (firstIn is null || at < firstIn)) firstIn = at;
        }

        foreach (var e in evs)
        {
            if (e.Type == AttendanceType.In)
            {
                // A4: oldingi ochiq In juftsiz qoladi (OpenNoOut) — daqiqa qo'shilmaydi.
                open = e.OccurredAt;
                NoteIn(open.Value);
                continue;
            }

            // Out
            if (open is { } start && e.OccurredAt - start <= emp.MaxShift)
            {
                if (PresenceRules.LocalDate(start, tz) == date)
                {
                    worked += e.OccurredAt - start;
                    if (lastOut is null || e.OccurredAt > lastOut) lastOut = e.OccurredAt;
                }
                open = null;
            }
            // aks holda — yetim Out (A3), telefonda e'tiborsiz.
        }

        if (open is { } o && PresenceRules.LocalDate(o, tz) == date && now - o <= emp.MaxShift && now >= o)
        {
            inside = true;
            worked += now - o;
        }

        return new DayRow(emp.Id, emp.FullName, firstIn, lastOut, worked, inside, firstIn is null);
    }
}
