using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>Bazadan yangilash natijasi: boshqa jarayon (agent ↔ desktop) yozgan o'zgarishlar soni.</summary>
public sealed record StoreChanges(int Events, int Employees, int Devices, bool Settings)
{
    public bool Any => Events > 0 || Employees > 0 || Devices > 0 || Settings;
}

/// <summary>
/// Bosqich 2: agent va desktop bitta SQLite bazada ishlaydi; har biri ma'lumotni xotirada ushlaydi. Shu metod boshqa
/// jarayon yozgan o'zgarishlarni xotiradagi nusxaga oladi (yangi/o'zgargan voqealar, xodimlar, qurilmalar, sozlamalar).
/// Faqat "Unchanged" yozuvlar yangilanadi — saqlanmagan lokal o'zgarish bo'lsa (bo'lmasligi kerak: har amal darhol
/// saqlanadi) unga tegilmaydi.
/// </summary>
public sealed partial class PayrollService
{
    /// <summary>Soat farqi (server vaqti) va yozish/o'qish oralig'i uchun zaxira.</summary>
    private static readonly TimeSpan StoreMargin = TimeSpan.FromMinutes(10);

    private DateTimeOffset _storeCheckedAt;

    public async Task<StoreChanges> RefreshFromStoreAsync(CancellationToken ct = default)
    {
        var checkedAt = _clock.GetUtcNow();
        var since = _storeCheckedAt - StoreMargin;

        // 1. Sozlamalar (bitta qator): kursorlar, terminal manzili/paroli, server kaliti.
        var settingsChanged = false;
        var settingsEntry = _db.Entry(Settings);
        if (settingsEntry.State == EntityState.Unchanged)
        {
            var before = settingsEntry.CurrentValues.Clone();
            await settingsEntry.ReloadAsync(ct);
            settingsChanged = settingsEntry.Properties.Any(p => !Equals(before[p.Metadata], p.CurrentValue));
        }

        // 2. Voqealar: yangi qabul qilingan / bekor qilingan / jo'natilishi kutilayotganlar (ikki tomondagi).
        var localPending = _db.AttendanceEvents.Local.Where(e => e.PendingPush).Select(e => e.Id).ToList();
        var rows = await _db.AttendanceEvents.AsNoTracking()
            .Where(e => e.PendingPush || e.ReceivedAt >= since || (e.VoidedAt != null && e.VoidedAt >= since) || localPending.Contains(e.Id))
            .ToListAsync(ct);
        var events = Merge(_db.AttendanceEvents.Local, rows, e => e.Id);

        // 3. Xodimlar va qurilmalar — kichik jadvallar, to'liq solishtiriladi.
        var employees = Merge(_db.Employees.Local, await _db.Employees.AsNoTracking().ToListAsync(ct), e => e.Id, removeMissing: true);
        var devices = Merge(_db.Devices.Local, await _db.Devices.AsNoTracking().ToListAsync(ct), d => d.Id, removeMissing: true);

        _storeCheckedAt = checkedAt;
        if (employees > 0 && !AttendanceOnly)
            await EnsureActiveEmployeesInOpenPeriodsAsync(); // Bosqich 3: agent serverdan tortgan yangi xodim — desktop ochiq oyiga
        if (events > 0)
        {
            RecalculateOpenSchedules(); // H10 (desktop): boshqa jarayondan kelgan voqealar — Schedule qatorlari jonli
            if (_db.ChangeTracker.HasChanges())
                await _db.SaveChangesAsync(ct);
        }
        return new StoreChanges(events, employees, devices, settingsChanged);
    }

    /// <summary>Bazadagi qatorlarni xotiradagi kuzatilayotgan obyektlarga qo'shadi/yangilaydi. O'zgargan yozuvlar sonini qaytaradi.</summary>
    private int Merge<T>(LocalView<T> local, IReadOnlyList<T> rows, Func<T, Guid> key, bool removeMissing = false) where T : class
    {
        var changed = 0;
        var byId = local.ToDictionary(key);
        foreach (var row in rows)
        {
            if (!byId.TryGetValue(key(row), out var tracked))
            {
                _db.Attach(row); // boshqa jarayon qo'shgan — Unchanged holatda
                changed++;
                continue;
            }
            var entry = _db.Entry(tracked);
            if (entry.State != EntityState.Unchanged)
                continue;
            var differs = entry.Properties.Any(p => !Equals(p.CurrentValue, _db.Entry(row).Property(p.Metadata.Name).CurrentValue));
            if (!differs)
                continue;
            entry.CurrentValues.SetValues(row);
            entry.State = EntityState.Unchanged; // bazadagi holat — "o'zgartirilgan" deb belgilanmaydi
            changed++;
        }
        if (removeMissing)
        {
            var present = rows.Select(key).ToHashSet();
            foreach (var gone in local.Where(x => !present.Contains(key(x)) && _db.Entry(x).State == EntityState.Unchanged).ToList())
            {
                _db.Entry(gone).State = EntityState.Detached;
                changed++;
            }
        }
        return changed;
    }
}
