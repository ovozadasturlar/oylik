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
    public const int MaxReasonLength = 200;
    public const string PairRateLimitPolicy = "pair";

    public static void Map(WebApplication app)
    {
        app.MapGet(Api.Routes.Health, (TimeProvider clock) =>
            new HealthResponse("ok", clock.GetUtcNow(), typeof(Endpoints).Assembly.GetName().Version?.ToString(3) ?? "0"));

        app.MapPost(Api.Routes.Installations, RegisterInstallation);
        app.MapPost("/api/installations/{id:guid}/pairing-codes", CreatePairingCode).RequireCaller(CallerKind.Desktop);
        app.MapPost(Api.Routes.PairDevice, PairDevice).RequireRateLimiting(PairRateLimitPolicy);
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
        db.Installations.Add(new Installation
        {
            Id = id,
            Name = req.Name.Trim(),
            ApiKeyHash = Secrets.Hash(apiKey),
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return Results.Ok(new RegisterInstallationResponse(id, apiKey));
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
                    UpdatedAt = now,
                });
                changed++;
                continue;
            }
            if (snap.FullName == name && snap.Note == note && snap.IsActive == dto.IsActive && snap.MaxShiftMinutes == dto.MaxShiftMinutes)
                continue;
            snap.FullName = name;
            snap.Note = note;
            snap.IsActive = dto.IsActive;
            snap.MaxShiftMinutes = dto.MaxShiftMinutes;
            snap.UpdatedAt = now;
            changed++;
        }
        foreach (var missing in existing.Values.Where(s => s.IsActive))
        {
            missing.IsActive = false;
            missing.UpdatedAt = now;
            changed++;
        }
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
            .Select(e => new EmployeeDto(e.EmployeeId, e.FullName, e.Note, e.IsActive, e.UpdatedAt, e.MaxShiftMinutes))
            .ToList());
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
