using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>Bosqich 3: serverdan kelgan xodim (pul ma'lumotisiz). <see cref="TabelNo"/> null — server bilmaydi (eski mijoz).</summary>
public sealed record RemoteEmployee(Guid Id, string FullName, string? Note, bool IsActive, int? TabelNo, DateTimeOffset UpdatedAt);

/// <summary>Pull natijasi: terminalga yozilishi kerak bo'lgan (yangi yoki o'zgargan) xodimlar.</summary>
public sealed record EmployeePullResult(IReadOnlyList<Employee> Created, IReadOnlyList<Employee> Changed)
{
    public static readonly EmployeePullResult None = new([], []);

    /// <summary>Terminalga yoziladiganlar (yangi + ism/tabel/faollik o'zgargan).</summary>
    public IEnumerable<Employee> ForTerminal => Created.Concat(Changed);
}

/// <summary>Bosqich 3: ko'p-zavod xodim sinxroni — tombstone'lar, pull (server → lokal roster), join sozlamalari.</summary>
public sealed partial class PayrollService
{
    /// <summary>Serverga boradigan maydonlar xeshi (pul ma'lumotisiz).</summary>
    public string EmployeeSyncHash(Employee e)
    {
        var text = $"{e.FullName}\u001f{e.Note}\u001f{e.IsActive}\u001f{e.TabelNo}\u001f{MaxShiftMinutesFor(e)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    /// <summary>
    /// Serverga yetmagan lokal o'zgarishlar (yangi, tahrirlangan, smena chegarasi o'zgargan). Butun ro'yxat emas — eskirgan
    /// zavod boshqa zavodning yangi o'zgarishini (masalan nofaol qilishni) bosib ketmasin.
    /// </summary>
    public IReadOnlyList<(Employee Employee, string Hash)> EmployeesPendingPush() =>
        _db.Employees.Local.Select(e => (Employee: e, Hash: EmployeeSyncHash(e))).Where(x => x.Employee.SyncHash != x.Hash).ToList();

    /// <summary>Server qabul qildi: push paytidagi xesh yoziladi (shu orada yana o'zgargan bo'lsa — kutishda qoladi).</summary>
    public async Task MarkEmployeesPushedAsync(IReadOnlyList<(Guid Id, string Hash)> pushed)
    {
        var changed = false;
        foreach (var (id, hash) in pushed)
        {
            var e = _db.Employees.Local.FirstOrDefault(x => x.Id == id);
            if (e is null || EmployeeSyncHash(e) != hash || e.SyncHash == hash)
                continue;
            e.SyncHash = hash;
            changed = true;
        }
        if (changed)
            await _db.SaveChangesAsync(); // EmployeesChanged chaqirilmaydi — bu sinxron holati, xodim o'zgarishi emas
    }
    /// <summary>Serverga hali IsActive=false bo'lib yetmagan o'chirilgan xodimlar (bazadan — agent ham ko'radi).</summary>
    public async Task<IReadOnlyList<EmployeeTombstone>> GetEmployeeTombstonesAsync(CancellationToken ct = default) =>
        await _db.EmployeeTombstones.AsNoTracking().OrderBy(t => t.DeletedAt).ToListAsync(ct);

    /// <summary>Server qabul qilgan tombstone'larni tozalaydi.</summary>
    public async Task RemoveEmployeeTombstonesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return;
        await _db.EmployeeTombstones.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(ct);
        foreach (var local in _db.EmployeeTombstones.Local.Where(t => ids.Contains(t.Id)).ToList())
            _db.Entry(local).State = EntityState.Detached;
    }

    /// <summary>
    /// Serverdan tortilgan xodimlarni lokal rosterga qo'llaydi (Id bo'yicha) va kursorni suradi. Qoidalar:
    /// <list type="bullet">
    /// <item>faol xodim — yangi bo'lsa yaratiladi (Oklad 0 — pul serverda yo'q; buxgalter kiritadi), bor bo'lsa ism/izoh/tabel/faollik yangilanadi;</item>
    /// <item>nofaol xodim — lokal bo'lsa nofaol qilinadi; lokal YO'Q bo'lsa o'tkazib yuboriladi (o'chirilgan xodim "tirilmasin");</item>
    /// <item>lokal o'chirilgan (tombstone hali jo'natilmagan) — o'tkazib yuboriladi;</item>
    /// <item>lokal o'zgarishi serverga hali yetmagan xodim (<see cref="Employee.SyncHash"/> farq qiladi) — o'tkazib yuboriladi
    /// (lokal g'olib; keyingi push serverga olib boradi).</item>
    /// </list>
    /// Qo'llangan xodim "kelishilgan" deb belgilanadi (SyncHash) — qayta push qilinmaydi; qiymatlar teng bo'lsa hech narsa
    /// yozilmaydi — o'z push'imiz qaytib kelganda halqa bo'lmaydi.
    /// </summary>
    public async Task<EmployeePullResult> ApplyServerEmployeesAsync(IReadOnlyList<RemoteEmployee> remote)
    {
        if (remote.Count == 0)
            return EmployeePullResult.None;
        var tombstoned = (await _db.EmployeeTombstones.AsNoTracking().Select(t => t.Id).ToListAsync()).ToHashSet();
        var cursor = remote.Max(r => r.UpdatedAt);
        var created = new List<Employee>();
        var changed = new List<Employee>();

        await RunAsync(e =>
        {
            foreach (var r in remote)
            {
                var name = r.FullName?.Trim();
                if (string.IsNullOrEmpty(name) || tombstoned.Contains(r.Id))
                    continue;
                var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
                var local = _db.Employees.Local.FirstOrDefault(x => x.Id == r.Id);
                if (local is null)
                {
                    if (!r.IsActive)
                        continue; // o'chirilgan/nofaol — yaratilmaydi
                    var employee = e.CreateEmployee(name, 0, null, note);
                    employee.Id = r.Id;
                    employee.TabelNo = r.TabelNo ?? NextTabelNo();
                    e.Log("Employee", employee.Id, "Pulled", null, name, "serverdan (boshqa zavod)");
                    _db.Employees.Add(employee);
                    employee.SyncHash = EmployeeSyncHash(employee);
                    created.Add(employee);
                    continue;
                }
                if (local.SyncHash != EmployeeSyncHash(local))
                    continue; // lokal o'zgarish hali serverga yetmagan — lokal g'olib, keyingi push'da serverga boradi

                var dirty = false;
                var oldName = local.FullName;
                e.RenameEmployee(local, name); // normallashtirilgan solishtiruv — farq faqat bo'shliqda bo'lsa o'zgarish yo'q
                if (local.FullName != oldName)
                    dirty = true;
                if (local.Note != note)
                {
                    e.Log("Employee", local.Id, "Note", local.Note, note, "serverdan");
                    local.Note = note;
                    dirty = true;
                }
                if (r.TabelNo is { } tabel && local.TabelNo != tabel)
                {
                    e.Log("Employee", local.Id, "TabelNo", local.TabelNo.ToString(), tabel.ToString(), "serverdan");
                    local.TabelNo = tabel;
                    dirty = true;
                }
                if (local.IsActive != r.IsActive)
                {
                    e.SetEmployeeActive(local, r.IsActive);
                    dirty = true;
                }
                if (dirty)
                {
                    local.UpdatedAt = _clock.GetUtcNow();
                    local.SyncHash = EmployeeSyncHash(local); // server bilan kelishilgan — qayta push qilinmaydi
                    changed.Add(local);
                }
            }
            if (Settings.EmployeesSyncedAt is not { } prev || cursor > prev)
                Settings.EmployeesSyncedAt = cursor;
        });

        if (!AttendanceOnly && (created.Count > 0 || changed.Any(x => x.IsActive)))
            await EnsureActiveEmployeesInOpenPeriodsAsync(); // yangi/qayta faol xodim ochiq oyda (agent esa Oylik qatorlariga tegmaydi)
        return new EmployeePullResult(created, changed);
    }

    /// <summary>
    /// Bosqich 3: shu mashina mavjud o'rnatmaga ulandi (join). O'rnatma Id, kalit (shifrlangan), tabel diapazoni saqlanadi;
    /// kursorlar boshidan — yangi o'rnatma oqimi.
    /// </summary>
    public Task SaveJoinedInstallationAsync(string? localUrl, string? globalUrl, Guid installationId, string protectedApiKey, int tabelBase) =>
        RunAsync(e =>
        {
            LogAddresses(e, localUrl, globalUrl);
            if (Settings.InstallationId != installationId)
                e.Log("AppSettings", installationId, "InstallationId", Settings.InstallationId.ToString(), installationId.ToString(), "mavjud o'rnatmaga ulandi");
            if (Settings.TabelBase != tabelBase)
                e.Log("AppSettings", installationId, "TabelBase", Settings.TabelBase.ToString(), tabelBase.ToString());
            Settings.LocalServerUrl = localUrl;
            Settings.ServerUrl = globalUrl;
            Settings.InstallationId = installationId;
            Settings.ApiKey = protectedApiKey;
            Settings.TabelBase = tabelBase;
            Settings.LastEventSeq = 0;
            Settings.LastSyncAt = null;
            Settings.EmployeesSyncedAt = null;
        });
}
