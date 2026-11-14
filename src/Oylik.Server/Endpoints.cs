using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oylik.Contracts;

namespace Oylik.Server;

public sealed class ServerOptions
{
    /// <summary>Yangi o'rnatma ro'yxatdan o'tishi uchun kalit. Bo'sh bo'lsa ro'yxatdan o'tish yopiq.</summary>
    public string RegistrationKey { get; set; } = "";
}

/// <summary>Seq qiymatlarini ketma-ket berish uchun yozuvlarni navbatga qo'yadi (SQLite bitta yozuvchi).</summary>
public sealed class WriteGate
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
}

public static class Endpoints
{
    public const int MaxNameLength = 100;
    public const int MaxTabelNoLength = 16;
    public const int MaxReasonLength = 200;
    public const string PairRateLimitPolicy = "pair";
    public const string JoinRateLimitPolicy = "join";

    public static void Map(WebApplication app)
    {
        app.MapGet(Api.Routes.Health, (TimeProvider clock) =>
            new HealthResponse("ok", clock.GetUtcNow(), typeof(Endpoints).Assembly.GetName().Version?.ToString(3) ?? "0"));

        app.MapPost(Api.Routes.Installations, RegisterInstallation);
        app.MapPost("/api/installations/{id:guid}/pairing-codes", CreatePairingCode).RequireCaller(CallerKind.Desktop);
        app.MapPost(Api.Routes.PairDevice, PairDevice).RequireRateLimiting(PairRateLimitPolicy);
        app.MapPost("/api/installations/{id:guid}/join-codes", CreateJoinCode).RequireCaller(CallerKind.Desktop);
        app.MapPost(Api.Routes.JoinInstallation, JoinInstallation).RequireRateLimiting(JoinRateLimitPolicy);
        app.MapGet(Api.Routes.Devices, ListDevices).RequireCaller(CallerKind.Desktop);
        app.MapPost("/api/devices/{id:guid}/deactivate", DeactivateDevice).RequireCaller(CallerKind.Desktop);

        app.MapPut(Api.Routes.Employees, PutEmployees).RequireCaller(CallerKind.Desktop);
        app.MapGet(Api.Routes.Employees, GetEmployees).RequireCaller(CallerKind.Any);

        app.MapPost(Api.Routes.Events, PostEvents).RequireCaller(CallerKind.Any);
        app.MapPost("/api/attendance/events/{id:guid}/void", VoidEvent).RequireCaller(CallerKind.Any);
        app.MapGet(Api.Routes.Events, GetEvents).RequireCaller(CallerKind.Any);
    }

    private static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);

    // ---------- O'rnatma ----------

    private static async Task<IResult> RegisterInstallation(
        RegisterInstallationRequest req, ServerDb db, IOptions<ServerOptions> options, TimeProvider clock)
    {
        var expected = options.Value.RegistrationKey;
        if (string.IsNullOrEmpty(expected) || !Secrets.FixedTimeEquals(req.RegistrationKey ?? "", expected))
            return Problem(StatusCodes.Status403Forbidden, "Ro'yxatdan o'tish kaliti noto'g'ri.");
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 200)
            return Problem(StatusCodes.Status400BadRequest, "Nom noto'g'ri.");

        var id = req.InstallationId is { } given && given != Guid.Empty ? given : Guid.NewGuid();
        if (await db.Installations.AnyAsync(i => i.Id == id))
            return Problem(StatusCodes.Status409Conflict, "Bu o'rnatma serverda allaqachon ro'yxatdan o'tgan.");

        var apiKey = Secrets.NewToken();
        var now = clock.GetUtcNow();
        db.Installations.Add(new Installation { Id = id, Name = req.Name.Trim(), CreatedAt = now });
        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            InstallationId = id,
            KeyHash = Secrets.Hash(apiKey),
            Name = req.Name.Trim(),
            SiteIndex = 0, // asosiy mashina — tabel 1..
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return Results.Ok(new RegisterInstallationResponse(id, apiKey));
    }

    // ---------- Ko'p-zavod: ulanish (Bosqich 3) ----------

    /// <summary>Ikkinchi mashina uchun ulanish kodi: 6 xona, 10 daqiqa, bir martalik. Telefon kodlaridan alohida jadval.</summary>
    private static async Task<IResult> CreateJoinCode(Guid id, HttpContext http, ServerDb db, TimeProvider clock)
    {
        var caller = Caller.Get(http);
        if (caller.InstallationId != id)
            return Problem(StatusCodes.Status403Forbidden, "Boshqa o'rnatma.");

        var now = clock.GetUtcNow();
        string code;
        do code = Secrets.NewPairingCode();
        while (await db.JoinCodes.AnyAsync(c => c.Code == code && c.InstallationId == id && c.UsedAt == null && c.ExpiresAt > now));

        var expiresAt = now + Api.PairingCodeLifetime;
        db.JoinCodes.Add(new JoinCode { Code = code, InstallationId = id, ExpiresAt = expiresAt });
        await db.SaveChangesAsync();
        return Results.Ok(new JoinCodeResponse(code, expiresAt));
    }

    /// <summary>
    /// Mavjud o'rnatmaga ulanish (kalitsiz, kod + InstallationId). Muvaffaqiyatda: kod ishlatilgan, JoinCount+1 (monoton),
    /// YANGI alohida ApiKey va shu mashinaning tabel diapazoni. Xatolar PairDevice kabi: 400/409/410 (+429 — limiter).
    /// </summary>
    private static async Task<IResult> JoinInstallation(JoinInstallationRequest req, ServerDb db, TimeProvider clock)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 200)
            return Problem(StatusCodes.Status400BadRequest, "Nom noto'g'ri.");

        var now = clock.GetUtcNow();
        var code = (req.Code ?? "").Trim();
        var candidates = await db.JoinCodes.Where(c => c.Code == code && c.InstallationId == req.InstallationId).ToListAsync();
        var match = candidates.FirstOrDefault(c => c.UsedAt == null && c.ExpiresAt > now);
        if (match is null)
        {
            return candidates.MaxBy(c => c.ExpiresAt) switch
            {
                null => Problem(StatusCodes.Status400BadRequest, "Kod noto'g'ri."),
                { UsedAt: not null } => Problem(StatusCodes.Status409Conflict, "Kod allaqachon ishlatilgan."),
                _ => Problem(StatusCodes.Status410Gone, "Kod muddati o'tgan."),
            };
        }
        var installation = await db.Installations.FirstOrDefaultAsync(i => i.Id == req.InstallationId);
        if (installation is null)
            return Problem(StatusCodes.Status400BadRequest, "Kod noto'g'ri.");

        match.UsedAt = now;
        installation.JoinCount++; // monoton: diapazon hech qachon qayta berilmaydi
        var siteIndex = installation.JoinCount;
        var apiKey = Secrets.NewToken();
        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            InstallationId = installation.Id,
            KeyHash = Secrets.Hash(apiKey),
            Name = name,
            SiteIndex = siteIndex,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return Results.Ok(new JoinInstallationResponse(installation.Id, apiKey, ApiKey.TabelBaseOf(siteIndex)));
    }

    private static async Task<IResult> CreatePairingCode(Guid id, HttpContext http, ServerDb db, TimeProvider clock)
    {
        var caller = Caller.Get(http);
        if (caller.InstallationId != id)
            return Problem(StatusCodes.Status403Forbidden, "Boshqa o'rnatma.");

        var now = clock.GetUtcNow();
        string code;
        do code = Secrets.NewPairingCode();
        while (await db.PairingCodes.AnyAsync(c => c.Code == code && c.UsedAt == null && c.ExpiresAt > now)); // barcha o'rnatmalar bo'yicha unikal

        var expiresAt = now + Api.PairingCodeLifetime;
        db.PairingCodes.Add(new PairingCode { Code = code, InstallationId = id, ExpiresAt = expiresAt });
        await db.SaveChangesAsync();
        return Results.Ok(new PairingCodeResponse(code, expiresAt));
    }

    /// <summary>Bog'lash. InstallationId null bo'lsa (qo'lda kiritish) — o'rnatma faol kod bo'yicha topiladi. Kod bir martalik.</summary>
    private static async Task<IResult> PairDevice(PairDeviceRequest req, ServerDb db, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceName) || req.DeviceName.Trim().Length > MaxNameLength)
            return Problem(StatusCodes.Status400BadRequest, "Qurilma nomi noto'g'ri.");

        var now = clock.GetUtcNow();
        var code = (req.Code ?? "").Trim();
        var query = db.PairingCodes.Where(c => c.Code == code);
        if (req.InstallationId is { } installationId)
            query = query.Where(c => c.InstallationId == installationId);
        var candidates = await query.ToListAsync();

        // Faol kod (faol kodlar global yagona). Bo'lmasa — oxirgisi bo'yicha sabab: ishlatilgan → 409, muddati o'tgan → 410;
        // topilmadi (yoki boshqa o'rnatmaniki) → 400. Telefon bularni farqlab ko'rsatadi.
        var match = candidates.FirstOrDefault(c => c.UsedAt == null && c.ExpiresAt > now);
        if (match is null)
        {
            return candidates.MaxBy(c => c.ExpiresAt) switch
            {
                null => Problem(StatusCodes.Status400BadRequest, "Kod noto'g'ri."),
                { UsedAt: not null } => Problem(StatusCodes.Status409Conflict, "Kod allaqachon ishlatilgan."),
                _ => Problem(StatusCodes.Status410Gone, "Kod muddati o'tgan."),
            };
        }

        match.UsedAt = now;
        var token = Secrets.NewToken();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            InstallationId = match.InstallationId,
            Name = req.DeviceName.Trim(),
            TokenHash = Secrets.Hash(token),
            PairedAt = now,
            LastSeenAt = now,
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return Results.Ok(new PairDeviceResponse(device.Id, token, device.InstallationId));
    }

    private static async Task<IResult> ListDevices(HttpContext http, ServerDb db)
    {
        var caller = Caller.Get(http);
        var devices = await db.Devices.AsNoTracking()
            .Where(d => d.InstallationId == caller.InstallationId)
            .ToListAsync();
        return Results.Ok(devices
            .OrderBy(d => d.PairedAt)
            .Select(d => new DeviceDto(d.Id, d.Name, d.PairedAt, d.LastSeenAt, d.IsActive))
            .ToList());
    }

    private static async Task<IResult> DeactivateDevice(Guid id, HttpContext http, ServerDb db)
    {
        var caller = Caller.Get(http);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.InstallationId == caller.InstallationId);
        if (device is null)
            return Problem(StatusCodes.Status404NotFound, "Qurilma topilmadi.");
        device.IsActive = false;
        await db.SaveChangesAsync();
        return Results.Ok(new DeviceDto(device.Id, device.Name, device.PairedAt, device.LastSeenAt, device.IsActive));
    }

    // ---------- Xodimlar ----------

    /// <summary>To'liq ro'yxat: upsert; ro'yxatda yo'q xodimlar nofaol qilinadi (telefonda ko'rinmasin).</summary>
    /// <summary>H14: juftlik chegarasi yuqori chegarasi (sutkalik 28 s dan ortig'i bilan).</summary>
    private const int MaxShiftMinutesLimit = 48 * 60;

    private static async Task<IResult> PutEmployees(List<EmployeeDto> items, HttpContext http, ServerDb db, TimeProvider clock)
    {
        var caller = Caller.Get(http);
        if (items.Any(e => e.Id == Guid.Empty || string.IsNullOrWhiteSpace(e.FullName) || e.FullName.Trim().Length > MaxNameLength))
            return Problem(StatusCodes.Status400BadRequest, "Xodim ma'lumoti noto'g'ri.");
        if (items.Select(e => e.Id).Distinct().Count() != items.Count)
            return Problem(StatusCodes.Status400BadRequest, "Takroriy xodim Id.");
        if (items.Any(e => e.MaxShiftMinutes is < 1 or > MaxShiftMinutesLimit))
            return Problem(StatusCodes.Status400BadRequest, "MaxShiftMinutes noto'g'ri.");

        var now = clock.GetUtcNow();
        var existing = await db.Employees.Where(e => e.InstallationId == caller.InstallationId).ToDictionaryAsync(e => e.EmployeeId);
        int changed = 0;
        foreach (var dto in items)
        {
            var name = dto.FullName.Trim();
            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            if (note?.Length > MaxNameLength) note = note[..MaxNameLength];
            var tabel = NormalizeTabelNo(dto.TabelNo);

            if (!existing.Remove(dto.Id, out var snap))
            {
                db.Employees.Add(new EmployeeSnapshot
                {
                    InstallationId = caller.InstallationId,
                    EmployeeId = dto.Id,
                    FullName = name,
                    Note = note,
                    IsActive = dto.IsActive,
                    MaxShiftMinutes = dto.MaxShiftMinutes,
                    TabelNo = tabel,
                    UpdatedAt = now,
                });
                changed++;
                continue;
            }
            tabel ??= snap.TabelNo; // eski desktop TabelNo yubormaydi — mavjudini o'chirmaymiz
            if (snap.FullName == name && snap.Note == note && snap.IsActive == dto.IsActive && snap.MaxShiftMinutes == dto.MaxShiftMinutes
                && snap.TabelNo == tabel)
                continue;
            snap.FullName = name;
            snap.Note = note;
            snap.IsActive = dto.IsActive;
            snap.MaxShiftMinutes = dto.MaxShiftMinutes;
            snap.TabelNo = tabel;
            snap.UpdatedAt = now;
            changed++;
        }
        // Bosqich 3: upsert-only. Ro'yxatda yo'q xodim nofaol QILINMAYDI — bir o'rnatmaga bir nechta zavod (desktop) yozadi,
        // har biri faqat o'zi bilganini jo'natadi. Nofaol qilish faqat IsActive=false ni push qilish orqali.
        await db.SaveChangesAsync();
        return Results.Ok(new { changed });
    }

    /// <summary>`updatedSince` — shu vaqtdan (shu jumladan) keyin o'zgarganlar; berilmasa hammasi.</summary>
    private static async Task<IResult> GetEmployees(DateTimeOffset? updatedSince, HttpContext http, ServerDb db)
    {
        var caller = Caller.Get(http);
        var query = db.Employees.AsNoTracking().Where(e => e.InstallationId == caller.InstallationId);
        if (updatedSince is { } since)
        {
            var ticks = since.UtcTicks;
            query = query.Where(e => e.UpdatedAt >= new DateTimeOffset(ticks, TimeSpan.Zero));
        }
        var list = await query.ToListAsync();
        return Results.Ok(list
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .Select(e => new EmployeeDto(e.EmployeeId, e.FullName, e.Note, e.IsActive, e.UpdatedAt, e.MaxShiftMinutes, e.TabelNo))
            .ToList());
    }

    /// <summary>Tabel raqami: bo'sh — null; uzunligi cheklanadi.</summary>
    private static string? NormalizeTabelNo(string? value)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        return t.Length > MaxTabelNoLength ? t[..MaxTabelNoLength] : t;
    }

    // ---------- Davomat ----------

    private static async Task<long> NextSeqAsync(ServerDb db)
    {
        // batch ichida hali saqlanmagan hisoblagich ham hisobga olinadi
        var counter = db.Counters.Local.FirstOrDefault() ?? await db.Counters.FirstOrDefaultAsync();
        if (counter is null)
        {
            counter = new SeqCounter();
            db.Counters.Add(counter);
        }
        return ++counter.Value;
    }

    /// <summary>Batch, Id bo'yicha idempotent. Telefon: DeviceId tokendan olinadi. Desktop: DeviceId = null.</summary>
    private static async Task<IResult> PostEvents(List<AttendanceEventDto> items, HttpContext http, ServerDb db, TimeProvider clock, WriteGate gate)
    {
        var caller = Caller.Get(http);
        if (items.Count > Api.MaxEventsPageSize)
            return Problem(StatusCodes.Status400BadRequest, $"Bir so'rovda ko'pi bilan {Api.MaxEventsPageSize} ta voqea.");
        if (items.Any(e => e.Id == Guid.Empty || e.OccurredAt == default || !Enum.IsDefined(e.Type)))
            return Problem(StatusCodes.Status400BadRequest, "Voqea ma'lumoti noto'g'ri.");

        var employeeIds = items.Select(e => e.EmployeeId).Distinct().ToList();
        var known = await db.Employees
            .Where(e => e.InstallationId == caller.InstallationId && employeeIds.Contains(e.EmployeeId))
            .Select(e => e.EmployeeId)
            .ToListAsync();
        if (known.Count != employeeIds.Count)
            return Problem(StatusCodes.Status400BadRequest, "Noma'lum xodim (bu o'rnatmaga tegishli emas).");

        await gate.Lock.WaitAsync();
        try
        {
            var ids = items.Select(e => e.Id).ToList();
            var existing = (await db.Events.Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync()).ToHashSet();

            var now = clock.GetUtcNow();
            var accepted = new List<Guid>();
            var duplicate = new List<Guid>();
            foreach (var dto in items)
            {
                if (!existing.Add(dto.Id))
                {
                    duplicate.Add(dto.Id);
                    continue;
                }
                db.Events.Add(new AttendanceEvent
                {
                    Seq = await NextSeqAsync(db),
                    Id = dto.Id,
                    InstallationId = caller.InstallationId,
                    EmployeeId = dto.EmployeeId,
                    Type = dto.Type,
                    OccurredAt = dto.OccurredAt,
                    DeviceId = caller.DeviceId,
                    ReceivedAt = now,
                });
                accepted.Add(dto.Id);
            }
            await db.SaveChangesAsync();
            return Results.Ok(new PostEventsResponse(accepted, duplicate));
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    /// <summary>Bekor qilish: voqea o'chirilmaydi; yangi Seq oladi — boshqalar kursor orqali ko'radi. Takror chaqirish zararsiz.</summary>
    private static async Task<IResult> VoidEvent(Guid id, VoidEventRequest req, HttpContext http, ServerDb db, TimeProvider clock, WriteGate gate)
    {
        var caller = Caller.Get(http);
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length > MaxReasonLength)
            return Problem(StatusCodes.Status400BadRequest, "Sabab majburiy.");

        await gate.Lock.WaitAsync();
        try
        {
            var e = await db.Events.FirstOrDefaultAsync(x => x.Id == id && x.InstallationId == caller.InstallationId);
            if (e is null)
                return Problem(StatusCodes.Status404NotFound, "Voqea topilmadi.");
            if (!e.IsVoided)
            {
                e.IsVoided = true;
                e.VoidedAt = clock.GetUtcNow();
                e.VoidReason = req.Reason.Trim();
                e.Seq = await NextSeqAsync(db);
                await db.SaveChangesAsync();
            }
            return Results.Ok(e.ToDto());
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    private static async Task<IResult> GetEvents(long? sinceSeq, int? limit, HttpContext http, ServerDb db)
    {
        var caller = Caller.Get(http);
        var since = Math.Max(0, sinceSeq ?? 0);
        var take = Math.Clamp(limit ?? Api.MaxEventsPageSize, 1, Api.MaxEventsPageSize);
        var events = await db.Events.AsNoTracking()
            .Where(e => e.InstallationId == caller.InstallationId && e.Seq > since)
            .OrderBy(e => e.Seq)
            .Take(take)
            .ToListAsync();
        var last = events.Count > 0 ? events[^1].Seq : since;
        return Results.Ok(new EventsPageResponse(events.Select(e => e.ToDto()).ToList(), last));
    }
}
