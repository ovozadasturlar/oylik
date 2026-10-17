using Microsoft.EntityFrameworkCore;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>2-bosqich: davomat voqealari, qurilmalar, server sinxronizatsiyasi uchun lokal holat.</summary>
public sealed partial class PayrollService
{
    /// <summary>Ishga tushganda shuncha kunlik voqealar yuklanadi; eskilari oy tanlanganda.</summary>
    private const int PreloadDays = 400;

    private readonly HashSet<MonthKey> _loadedMonths = [];
    private DateTimeOffset _preloadedFrom;

    public IReadOnlyList<Device> Devices => _db.Devices.Local.OrderBy(d => d.Name, NameComparer.Instance).ToList();

    /// <summary>Xotiradagi barcha voqealar (bekor qilinganlar ham).</summary>
    public IEnumerable<AttendanceEvent> AttendanceEvents => _db.AttendanceEvents.Local;

    public TimeZoneInfo LocalZone => _clock.LocalTimeZone;

    private async Task LoadAttendanceAsync()
    {
        _preloadedFrom = _clock.GetUtcNow().AddDays(-PreloadDays);
        var from = _preloadedFrom;
        await _db.AttendanceEvents.Where(e => e.OccurredAt >= from || e.PendingPush).LoadAsync();
        await _db.Devices.LoadAsync();
    }

    /// <summary>Oy (± 1 kun chegara, tungi smena uchun) voqealari xotirada bo'lishini ta'minlaydi.</summary>
    public async Task EnsureEventsLoadedAsync(MonthKey month)
    {
        var (from, to) = MonthWindow(month);
        if (from >= _preloadedFrom || !_loadedMonths.Add(month))
            return;
        await _db.AttendanceEvents.Where(e => e.OccurredAt >= from && e.OccurredAt < to).LoadAsync();
    }

    private (DateTimeOffset From, DateTimeOffset To) MonthWindow(MonthKey month)
    {
        var start = month.FirstDay.ToDateTime(TimeOnly.MinValue);
        var offset = LocalZone.GetUtcOffset(start);
        var from = new DateTimeOffset(start, offset).AddDays(-1);
        return (from, from.AddDays(DateTime.DaysInMonth(month.Year, month.Month) + 2));
    }

    /// <summary>A1–A8: xotiradagi voqealar bo'yicha har xodim davomati (hozirgi vaqtga nisbatan).</summary>
    public IReadOnlyDictionary<Guid, EmployeeAttendance> AnalyzeAttendance() =>
        AttendanceRules.AnalyzeAll(_db.AttendanceEvents.Local, LocalZone, _clock.GetUtcNow(), MaxPairResolver());

    /// <summary>A8: oy xulosalari xodim bo'yicha (voqeasi bo'lmagan xodim lug'atda yo'q).</summary>
    public async Task<IReadOnlyDictionary<Guid, AttendanceMonthSummary>> GetMonthSummariesAsync(MonthKey month)
    {
        await EnsureEventsLoadedAsync(month);
        return AnalyzeAttendance().ToDictionary(kv => kv.Key, kv => kv.Value.Summarize(month, Settings.HoursPerDay));
    }

    /// <summary>Kun tafsiloti: shu kunga tegishli juftliklar voqealari (tungi smena Out'i ham) va shu sanadagi barcha voqealar (bekor qilinganlar ham).</summary>
    public IReadOnlyList<AttendanceEvent> EventsOf(Guid employeeId, DateOnly workDay, EmployeeAttendance? analysis)
    {
        var result = _db.AttendanceEvents.Local
            .Where(e => e.EmployeeId == employeeId && AttendanceRules.LocalDate(e.OccurredAt, LocalZone) == workDay)
            .ToHashSet();
        if (analysis?.Day(workDay) is { } day)
        {
            foreach (var p in day.Pairs)
            {
                result.Add(p.In);
                if (p.Out is not null)
                    result.Add(p.Out);
            }
        }
        return result.OrderBy(e => e.OccurredAt).ThenBy(e => e.Type).ToList();
    }

    // ---------- A11: qo'lda tuzatish ----------

    public async Task<AttendanceEvent> AddManualEventAsync(Employee employee, AttendanceEventType type, DateTimeOffset occurredAt, string? note)
    {
        var engine = new AttendanceEngine(_clock);
        var e = engine.AddManual(employee, type, occurredAt, note);
        _db.AttendanceEvents.Add(e);
        await SaveAsync(engine.Audit);
        return e;
    }

    public async Task VoidEventAsync(AttendanceEvent e, string reason)
    {
        var engine = new AttendanceEngine(_clock);
        try
        {
            engine.Void(_db.Employees.Local.FirstOrDefault(x => x.Id == e.EmployeeId), e, reason);
        }
        catch
        {
            DiscardChanges();
            throw;
        }
        await SaveAsync(engine.Audit);
    }

    // ---------- A9: [Ko'chirish] ----------

    public const string CopiedFromAttendanceComment = "Davomatdan ko'chirildi";

    /// <summary>A9: AttendedDays → DaysWorked (faqat ochiq oyda, audit bilan). Avtomatik chaqirilmaydi.</summary>
    public Task CopyAttendanceToDaysAsync(PayrollPeriod period, PayrollLine line, int attendedDays) =>
        RunAsync(e => e.SetDaysWorked(period, line, attendedDays, CopiedFromAttendanceComment));

    // ---------- Server sinxronizatsiyasi ----------

    /// <summary>Serverga jo'natilishi kerak: desktopda qo'shilgan yoki bekor qilingan voqealar.</summary>
    public IReadOnlyList<AttendanceEvent> PendingPushEvents =>
        _db.AttendanceEvents.Local.Where(e => e.PendingPush).OrderBy(e => e.OccurredAt).ToList();

    public async Task MarkPushedAsync(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        foreach (var e in _db.AttendanceEvents.Local.Where(e => set.Contains(e.Id)))
            e.PendingPush = false;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// GET events?sinceSeq natijasini lokal bazaga upsert qiladi. Bekor qilish faqat bir tomonga:
    /// serverda bekor bo'lsa lokalda ham; lokal bekor qilish (hali jo'natilmagan) saqlanib qoladi.
    /// </summary>
    /// <returns>Yangi qo'shilgan voqealar soni.</returns>
    public async Task<int> ApplyServerEventsAsync(IEnumerable<AttendanceEvent> incoming, long lastSeq)
    {
        int added = 0;
        foreach (var s in incoming)
        {
            var local = _db.AttendanceEvents.Local.FirstOrDefault(e => e.Id == s.Id)
                ?? await _db.AttendanceEvents.FirstOrDefaultAsync(e => e.Id == s.Id);
            if (local is null)
            {
                s.PendingPush = false;
                _db.AttendanceEvents.Add(s);
                added++;
                continue;
            }

            local.ServerSeq = s.ServerSeq;
            local.ReceivedAt = s.ReceivedAt;
            if (s.IsVoided && !local.IsVoided)
            {
                local.IsVoided = true;
                local.VoidedAt = s.VoidedAt;
                local.VoidReason = s.VoidReason;
            }
        }

        if (lastSeq > Settings.LastEventSeq)
            Settings.LastEventSeq = lastSeq;
        Settings.LastSyncAt = _clock.GetUtcNow();
        if (added > 0 || incoming.Any(s => s.IsVoided))
            RecalculateOpenSchedules(); // H10: telefondan kelgan voqealar — Schedule qatorlari jonli
        await _db.SaveChangesAsync();
        return added;
    }

    public async Task MarkSyncedAsync()
    {
        Settings.LastSyncAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync();
    }

    /// <summary>Serverdagi qurilmalar ro'yxati bilan lokal nusxani almashtiradi.</summary>
    public async Task ReplaceDevicesAsync(IEnumerable<Device> devices)
    {
        var incoming = devices.ToDictionary(d => d.Id);
        foreach (var local in _db.Devices.Local.ToList())
        {
            if (!incoming.ContainsKey(local.Id))
                _db.Devices.Remove(local);
        }
        foreach (var d in incoming.Values)
        {
            var local = _db.Devices.Local.FirstOrDefault(x => x.Id == d.Id);
            if (local is null)
            {
                _db.Devices.Add(d);
                continue;
            }
            local.Name = d.Name;
            local.PairedAt = d.PairedAt;
            local.LastSeenAt = d.LastSeenAt;
            local.IsActive = d.IsActive;
        }
        await _db.SaveChangesAsync();
    }

    public Device? FindDevice(Guid? id) => id is { } x ? _db.Devices.Local.FirstOrDefault(d => d.Id == x) : null;

    /// <summary>Server ulanishi (ro'yxatdan o'tish natijasi) saqlanadi; kursor boshidan.</summary>
    /// <summary>Yangi ulanish (ro'yxatdan o'tish yoki uzish): manzillar, API kalit; kursor boshidan.</summary>
    public Task SaveServerConnectionAsync(string? localUrl, string? globalUrl, string? apiKey) => RunAsync(e =>
    {
        LogAddresses(e, localUrl, globalUrl);
        Settings.LocalServerUrl = localUrl;
        Settings.ServerUrl = globalUrl;
        Settings.ApiKey = apiKey;
        Settings.LastEventSeq = 0;
        Settings.LastSyncAt = null;
    });

    /// <summary>Faqat manzillar o'zgardi (server o'sha — qayta ro'yxatdan o'tish shart emas, kursor saqlanadi).</summary>
    public Task SaveServerAddressesAsync(string? localUrl, string? globalUrl) => RunAsync(e =>
    {
        LogAddresses(e, localUrl, globalUrl);
        Settings.LocalServerUrl = localUrl;
        Settings.ServerUrl = globalUrl;
    });

    private void LogAddresses(PayrollEngine e, string? localUrl, string? globalUrl)
    {
        if (Settings.LocalServerUrl != localUrl)
            e.Log("AppSettings", Settings.InstallationId, "LocalServerUrl", Settings.LocalServerUrl, localUrl);
        if (Settings.ServerUrl != globalUrl)
            e.Log("AppSettings", Settings.InstallationId, "ServerUrl", Settings.ServerUrl, globalUrl);
    }
}
