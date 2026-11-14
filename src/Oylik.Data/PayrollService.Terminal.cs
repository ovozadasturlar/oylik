using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oylik.Core.Domain;
using Oylik.Data.Terminal;

namespace Oylik.Data;

public sealed partial class PayrollService
{
    /// <summary>
    /// Yuz terminalidan bir kelishda kamera bir necha marta taniydi; shu oraliqdagi takror tanishlar bitta holat deb
    /// hisoblanadi (yangi Keldi/Ketdi yozilmaydi). A10 (2 daqiqa) bilan bir xil.
    /// </summary>
    public static readonly TimeSpan TerminalSamePresenceWindow = AttendanceRules.DuplicateTapWindow;

    /// <summary>Terminal voqealarini yozadigan qurilma (SerialNo'dan barqaror Id).</summary>
    public async Task<Device> EnsureTerminalDeviceAsync(string serialNumber, string name)
    {
        var id = DeterministicGuid($"hik-terminal:{serialNumber}");
        var device = _db.Devices.Local.FirstOrDefault(d => d.Id == id)
            ?? await _db.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (device is null)
        {
            device = new Device { Id = id, Name = name, PairedAt = _clock.GetUtcNow(), IsActive = true };
            _db.Devices.Add(device);
        }
        device.LastSeenAt = _clock.GetUtcNow();
        if (!string.IsNullOrEmpty(name))
            device.Name = name;
        await _db.SaveChangesAsync();
        return device;
    }

    /// <summary>
    /// Terminaldan o'qilgan yuz voqealarini (SerialNo tartibida) davomatga yozadi: <c>employeeNo = Employee.TabelNo</c>.
    /// Keldi/Ketdi: voqeada <c>attendanceStatus</c> bo'lsa (qo'lda rejim — xodim tanlagan) o'sha yoziladi, aks holda
    /// toggle (avto rejim — 1-tanish Keldi, keyingisi Ketdi). Takror (shu oynadagi) tanishlar o'tkazib yuboriladi.
    /// Idempotent: Voqea Id SerialNo'dan. Kursor (TerminalLastSerial) suriladi.
    /// </summary>
    /// <param name="log">Ogohlantirishlar (takroriy tabel) — ism/Id'siz.</param>
    /// <returns>Yangi yozilgan voqealar soni.</returns>
    public async Task<int> ApplyTerminalEventsAsync(IReadOnlyList<TerminalEvent> events, Guid deviceId, Action<string>? log = null)
    {
        if (events.Count == 0)
            return 0;

        var byTabel = new Dictionary<int, Employee>();
        foreach (var group in _db.Employees.Local.Where(e => e.TabelNo > 0).GroupBy(e => e.TabelNo))
        {
            // Bosqich 3 himoyasi: takroriy tabel (ko'p-zavod to'qnashuvi) terminal pollni to'xtatmasin — kimga tegishli
            // noaniq, shuning uchun bu raqamning voqealari o'tkazib yuboriladi.
            if (group.Count() == 1)
                byTabel[group.Key] = group.First();
            else
                log?.Invoke($"Takroriy tabel {group.Key} o'tkazib yuborildi");
        }
        var now = _clock.GetUtcNow();
        var engine = new AttendanceEngine(_clock);
        int added = 0;
        long maxSerial = Settings.TerminalLastSerial;

        foreach (var ev in events.OrderBy(e => e.SerialNo))
        {
            if (ev.SerialNo > maxSerial)
                maxSerial = ev.SerialNo;
            if (ev.SerialNo <= Settings.TerminalLastSerial)
                continue;
            if (!int.TryParse(ev.EmployeeNo, out var tabel) || !byTabel.TryGetValue(tabel, out var employee))
                continue; // terminalda bor, Oylikda yo'q xodim — o'tkazib yuboriladi (kursor baribab suriladi)

            var id = DeterministicGuid($"hik-event:{deviceId}:{ev.SerialNo}");
            if (_db.AttendanceEvents.Local.Any(e => e.Id == id))
                continue;

            // Shu xodimning oxirgi (bekor qilinmagan) voqeasi — vaqt bo'yicha.
            var last = _db.AttendanceEvents.Local
                .Where(e => e.EmployeeId == employee.Id && !e.IsVoided && e.OccurredAt <= ev.Time)
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Type)
                .FirstOrDefault();

            // Qaysi holat: qo'lda rejimda terminal "attendanceStatus" beradi (xodim tanlagan) — o'shani yozamiz;
            // avto rejimda maydon bo'lmaydi — toggle (1-tanish Keldi, keyingisi Ketdi).
            var explicitType = ev.AttendanceStatus switch
            {
                "checkIn" => (AttendanceEventType?)AttendanceEventType.In,
                "checkOut" => AttendanceEventType.Out,
                _ => null,
            };
            var type = explicitType
                ?? (last is null || last.Type == AttendanceEventType.Out
                    ? AttendanceEventType.In
                    : AttendanceEventType.Out);

            // Bir kelishdagi takror tanish — o'tkazib yuboriladi (yangi holat emas). Qo'lda rejimda faqat bir xil
            // statusning takrori birlashtiriladi; xodim ataylab boshqa tugmani bossa (Kelish→Ketish) — yoziladi.
            if (last is not null && ev.Time - last.OccurredAt < TerminalSamePresenceWindow
                && (explicitType is null || last.Type == type))
                continue;

            var e = new AttendanceEvent
            {
                Id = id,
                EmployeeId = employee.Id,
                Type = type,
                OccurredAt = ev.Time,
                DeviceId = deviceId,
                ReceivedAt = now,
                PendingPush = true,
            };
            _db.AttendanceEvents.Add(e);
            engine.LogTerminal(e, employee, ev.Name);
            added++;
        }

        if (maxSerial > Settings.TerminalLastSerial)
            Settings.TerminalLastSerial = maxSerial;
        if (added > 0)
            RecalculateOpenSchedules(); // H10: Schedule qatorlari jonli
        await SaveAsync(engine.Audit);
        return added;
    }

    /// <summary>
    /// Yuz terminali sozlamalari (manzil, shifrlangan parol, yoqilgan, tasdiqlash rejimi). <paramref name="startSerial"/>
    /// berilsa — kursor shunga o'rnatiladi (yoqishda eski test voqealarini import qilmaslik uchun joriy oxirgi SerialNo).
    /// </summary>
    public Task SaveTerminalSettingsAsync(string? url, string? protectedPassword, bool enabled, bool requireConfirm, long? startSerial = null, string? serial = null) => RunAsync(e =>
    {
        if (Settings.TerminalUrl != url)
            e.Log("AppSettings", Settings.InstallationId, "TerminalUrl", Settings.TerminalUrl, url);
        if (Settings.TerminalEnabled != enabled)
            e.Log("AppSettings", Settings.InstallationId, "TerminalEnabled", Settings.TerminalEnabled.ToString(), enabled.ToString());
        Settings.TerminalUrl = url;
        Settings.TerminalPassword = protectedPassword;
        Settings.TerminalEnabled = enabled;
        Settings.TerminalRequireConfirm = requireConfirm;
        if (startSerial is { } s)
            Settings.TerminalLastSerial = s;
        if (!string.IsNullOrWhiteSpace(serial))
            Settings.TerminalSerial = serial;
    });

    /// <summary>Faqat terminal seriya raqamini saqlaydi (IP o'zgarganda qidirish uchun kalit).</summary>
    public Task SaveTerminalSerialAsync(string serial) => RunAsync(_ => Settings.TerminalSerial = serial);

    /// <summary>Faqat terminal manzilini (IP) yangilaydi — tarmoqdan qayta topilганда.</summary>
    public Task SaveTerminalUrlAsync(string url) => RunAsync(e =>
    {
        if (Settings.TerminalUrl != url)
            e.Log("AppSettings", Settings.InstallationId, "TerminalUrl", Settings.TerminalUrl, url);
        Settings.TerminalUrl = url;
    });

    /// <summary>Barqaror GUID (matndan) — idempotentlik uchun.</summary>
    internal static Guid DeterministicGuid(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
