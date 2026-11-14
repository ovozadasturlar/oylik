using System.Net;
using Oylik.Contracts;
using Oylik.Core.Domain;

namespace Oylik.Data.Sync;

/// <summary>
/// TZ 6 "Desktop sinxronizatsiya" mantig'i (UI'siz): xodimlar → server (o'zgargan bo'lsa), desktopda qo'lda qo'shilgan /
/// bekor qilingan voqealar → server, `GET events?sinceSeq` → lokal upsert, qurilmalar ro'yxati.
/// </summary>
/// <param name="runDb">Bazaga yozuvchi amallar shu orqali (bitta DbContext — ketma-ket).</param>
public sealed class ServerSync(PayrollService service, Func<Func<Task>, Task>? runDb = null)
{
    private readonly PayrollService _service = service;
    private readonly Func<Func<Task>, Task> _runDb = runDb ?? (a => a());

    /// <summary>Xodimlar ro'yxati serverga qayta jo'natilishi kerak.</summary>
    public bool EmployeesDirty { get; set; } = true;

    /// <summary>
    /// Bosqich 3: pull'dan yangi/o'zgargan xodimlar — shu zavod terminaliga yozish uchun (best-effort; xatosi sinxronni
    /// to'xtatmasligi kerak — chaqiruvchi o'zi ushlaydi).
    /// </summary>
    public Func<EmployeePullResult, Task>? EmployeesPulled { get; set; }

    /// <summary>Bir marta to'liq sinxronizatsiya. Tarmoq xatolari chaqiruvchiga uzatiladi; jo'natilmaganlar keyingi safar.</summary>
    public async Task RunAsync(ServerClient client)
    {
        // 1. Xodimlar (voqealardan oldin — server noma'lum xodim voqeasini rad etadi). Bosqich 3: faqat serverga yetmagan
        //    lokal o'zgarishlar (SyncHash) — butun ro'yxat emas, aks holda eskirgan zavod boshqasining o'zgarishini bosib
        //    ketadi; o'chirilganlar ham (tombstone → IsActive=false), qabul qilingach tozalanadi.
        EmployeesDirty = false;
        var pendingEmployees = _service.EmployeesPendingPush();
        var tombstones = await _service.GetEmployeeTombstonesAsync();
        if (pendingEmployees.Count > 0 || tombstones.Count > 0)
        {
            try
            {
                await client.PutEmployeesAsync(pendingEmployees.Select(p => p.Employee), _service.MaxShiftMinutesFor, tombstones);
            }
            catch
            {
                EmployeesDirty = true;
                throw;
            }
            await _runDb(async () =>
            {
                await _service.MarkEmployeesPushedAsync(pendingEmployees.Select(p => (p.Employee.Id, p.Hash)).ToList());
                await _service.RemoveEmployeeTombstonesAsync(tombstones.Select(t => t.Id).ToList());
            });
        }

        // 1b. Bosqich 3: boshqa zavod(lar) qo'shgan/o'zgartirgan xodimlar (kursor — server UpdatedAt, shu jumladan;
        //     qayta kelgan teng qiymatlar hech narsa yozmaydi).
        var remote = await client.GetEmployeesAsync(_service.Settings.EmployeesSyncedAt);
        if (remote.Count > 0)
        {
            var pulled = EmployeePullResult.None;
            await _runDb(async () => pulled = await _service.ApplyServerEmployeesAsync(remote.Select(ServerClient.FromServer).ToList()));
            if (pulled.ForTerminal.Any() && EmployeesPulled is { } onPulled)
                await onPulled(pulled);
        }

        // 2. Desktopda qo'lda qo'shilgan / bekor qilingan voqealar.
        var pending = _service.PendingPushEvents.Select(e => (Event: e, e.IsVoided)).ToList();
        if (pending.Count > 0)
        {
            foreach (var chunk in pending.Where(p => p.Event.ServerSeq is null).Select(p => p.Event).Chunk(Api.MaxEventsPageSize))
                await client.PostEventsAsync(chunk);
            foreach (var (e, _) in pending.Where(p => p.IsVoided))
            {
                try
                {
                    await client.VoidEventAsync(e.Id, e.VoidReason ?? "-");
                }
                catch (ServerException ex) when (ex.Status == HttpStatusCode.NotFound)
                {
                    // serverda yo'q (masalan, boshqa serverga ko'chirilgan) — bekor qilish lokal qoladi
                }
            }
            // jo'natish paytida yana o'zgarganlari (masalan, bekor qilingan) keyingi safar
            var done = pending.Where(p => p.Event.IsVoided == p.IsVoided).Select(p => p.Event.Id).ToList();
            await _runDb(() => _service.MarkPushedAsync(done));
        }

        // 3. Kursor bo'yicha tortish.
        while (true)
        {
            var page = await client.GetEventsAsync(_service.Settings.LastEventSeq);
            if (page.Events.Count == 0)
            {
                await _runDb(_service.MarkSyncedAsync);
                break;
            }
            await _runDb(() => _service.ApplyServerEventsAsync(page.Events.Select(ServerClient.FromServer).ToList(), page.LastSeq));
            if (page.Events.Count < Api.MaxEventsPageSize)
                break;
        }

        // 4. Qurilmalar.
        var devices = await client.GetDevicesAsync();
        await _runDb(() => _service.ReplaceDevicesAsync(devices.Select(ServerClient.FromServer)));
    }
}
