using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oylik.Contracts;

// TZ 6-bo'lim. Server pul ma'lumotini SAQLAMAYDI va UZATMAYDI — bu yerda summa maydonlari bo'lmasligi kerak.

public enum AttendanceType
{
    In = 0,   // Keldi
    Out = 1,  // Ketdi
}

// ---------- O'rnatma (desktop) ----------

/// <summary>
/// POST /api/installations. <paramref name="InstallationId"/> — desktopning o'z "Kompyuter ID"si (AppSettings.InstallationId);
/// server shu Id bilan ro'yxatga oladi (null bo'lsa — yangi Id beradi). Bu Id serverda allaqachon bo'lsa → 409 Conflict.
/// </summary>
public sealed record RegisterInstallationRequest(string Name, string RegistrationKey, Guid? InstallationId = null);

public sealed record RegisterInstallationResponse(Guid InstallationId, string ApiKey);

/// <summary>POST /api/installations/{id}/pairing-codes → 6 xonali kod, 10 daqiqa.</summary>
public sealed record PairingCodeResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// Desktop ko'rsatadigan QR ichidagi JSON: {"server":"https://…","installationId":"…","code":"482913","localServer":"http://192.168.1.19:5000"}.
/// <paramref name="Server"/> — har joydan ishlaydigan manzil (global URL; faqat LAN sozlangan bo'lsa — LAN manzili),
/// eski ilovalar faqat shuni o'qiydi. <paramref name="LocalServer"/> — ikkala manzil sozlanganda LAN manzili: avval shunga
/// urinish, ulanmasa <paramref name="Server"/> ga o'tish. Ikkalasi bir xil serverga (bitta baza) qaraydi.
/// </summary>
public sealed record PairingQrPayload(string Server, Guid InstallationId, string Code, string? LocalServer = null);

// ---------- Telefon ----------

/// <summary>
/// POST /api/devices/pair. QR'dan bog'lanishda <paramref name="InstallationId"/> beriladi; qo'lda (TZ: "URL + kod") — null,
/// server o'rnatmani faol (muddati o'tmagan, ishlatilmagan) kod bo'yicha o'zi topadi. Endpoint urinishlar soni bo'yicha cheklanadi.
/// </summary>
public sealed record PairDeviceRequest(Guid? InstallationId, string Code, string DeviceName);

/// <summary>InstallationId — qaysi o'rnatmaga ulangani (qo'lda bog'lashda telefon uni faqat shu javobdan biladi).</summary>
public sealed record PairDeviceResponse(Guid DeviceId, string DeviceToken, Guid InstallationId);

/// <summary>GET /api/devices (desktop, X-Api-Key) — bog'langan qurilmalar ro'yxati (TZ 5.6, status qatori).</summary>
public sealed record DeviceDto(Guid Id, string Name, DateTimeOffset PairedAt, DateTimeOffset? LastSeenAt, bool IsActive);

// ---------- Xodimlar ----------

/// <summary>
/// PUT /api/employees (desktop, to'liq ro'yxat, upsert) · GET /api/employees?updatedSince= (telefon).
/// updatedSince — ISO 8601 "O" format, URL-encoded; javob — o'ralmagan massiv EmployeeDto[].
/// </summary>
/// <remarks>
/// <paramref name="MaxShiftMinutes"/> — juftlik (Keldi→Ketdi) chegarasi shu xodim uchun (H14): jadval bo'yicha xodimda eng uzun
/// smena + 4 soat (sutkalik → 28 soat); null — <see cref="Api.DefaultMaxShift"/> (16 soat). Telefon "Ichkarida" holatini shunga qarab
/// aniqlaydi. Pul ma'lumoti emas.
/// </remarks>
public sealed record EmployeeDto(Guid Id, string FullName, string? Note, bool IsActive, DateTimeOffset UpdatedAt, int? MaxShiftMinutes = null);

// ---------- Davomat ----------

/// <summary>Voqea. <see cref="Id"/> telefonda yaratiladi — idempotentlik kaliti.</summary>
public sealed record AttendanceEventDto(
    Guid Id,
    Guid EmployeeId,
    AttendanceType Type,
    DateTimeOffset OccurredAt,
    Guid? DeviceId);

/// <summary>
/// POST /api/attendance/events (batch) javobi. Telefon (Bearer) va desktop (X-Api-Key, DeviceId = null — A11 qo'lda kiritilgan) chaqira oladi.
/// </summary>
public sealed record PostEventsResponse(IReadOnlyList<Guid> Accepted, IReadOnlyList<Guid> Duplicate);

/// <summary>POST /api/attendance/events/{id}/void</summary>
public sealed record VoidEventRequest(string Reason);

/// <summary>
/// GET /api/attendance/events?sinceSeq=&amp;limit= — serverdagi voqea. <see cref="Seq"/> — o'zgarish kursori: voqea yaratilganda
/// VA bekor qilinganda yangi (kattaroq) Seq oladi, shuning uchun bekor qilish ham sinceSeq orqali boshqa qurilmalarga yetadi.
/// </summary>
public sealed record ServerEventDto(
    long Seq,
    Guid Id,
    Guid EmployeeId,
    AttendanceType Type,
    DateTimeOffset OccurredAt,
    Guid? DeviceId,
    DateTimeOffset ReceivedAt,
    bool IsVoided,
    DateTimeOffset? VoidedAt,
    string? VoidReason);

public sealed record EventsPageResponse(IReadOnlyList<ServerEventDto> Events, long LastSeq);

public sealed record HealthResponse(string Status, DateTimeOffset ServerTime, string Version);

// ---------- Umumiy ----------

public static class Api
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const int PairingCodeLength = 6;
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(10);
    public const int MaxEventsPageSize = 500;

    /// <summary>A10: bir xodim uchun bir xil turdagi voqea shu oraliqda takrorlansa — qabul qilinmaydi (telefonda).</summary>
    public static readonly TimeSpan DuplicateTapWindow = TimeSpan.FromMinutes(2);

    /// <summary>A12: ReceivedAt − OccurredAt shundan oshsa — ogohlantirish (rad etilmaydi).</summary>
    public static readonly TimeSpan ClockSkewWarning = TimeSpan.FromMinutes(10);

    /// <summary>A2: juftlik chegarasi (jadvalsiz xodim). Jadval bo'yicha xodimda — EmployeeDto.MaxShiftMinutes (H14).</summary>
    public static readonly TimeSpan DefaultMaxShift = TimeSpan.FromHours(16);

    /// <summary>H14: jadval bo'yicha xodimda juftlik chegarasi = eng uzun smena + shu zaxira.</summary>
    public static readonly TimeSpan ShiftPairingSlack = TimeSpan.FromHours(4);

    /// <summary>Ikki manzil: LAN manzili shu vaqt ichida javob bermasa — global manzilga o'tiladi.</summary>
    public static readonly TimeSpan LocalServerProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Global manzilda ishlayotganda LAN manzili qayta shu oraliqda tekshiriladi (Wi-Fi'ga qaytilganda tezroq yo'lga o'tish).</summary>
    public static readonly TimeSpan LocalServerRecheckInterval = TimeSpan.FromMinutes(5);

    public static class Routes
    {
        public const string Installations = "/api/installations";
        public static string PairingCodes(Guid installationId) => $"/api/installations/{installationId}/pairing-codes";
        /// <summary>POST (telefon). Xatolar: 400 — kod noto'g'ri, 409 — kod allaqachon ishlatilgan, 410 — muddati o'tgan, 429 — urinishlar limiti.</summary>
        public const string PairDevice = "/api/devices/pair";
        public const string Devices = "/api/devices";
        /// <summary>POST (desktop, X-Api-Key) — telefonni uzish: token bekor qilinadi.</summary>
        public static string DeactivateDevice(Guid id) => $"/api/devices/{id}/deactivate";
        public const string Employees = "/api/employees";
        public const string Events = "/api/attendance/events";
        public static string VoidEvent(Guid id) => $"/api/attendance/events/{id}/void";
        public const string Health = "/api/health";
    }

    /// <summary>Barcha tomonlar bir xil JSON sozlamalaridan foydalanadi (camelCase, enum — matn).</summary>
    public static JsonSerializerOptions Json { get; } = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
