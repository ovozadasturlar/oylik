using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oylik.Contracts;

namespace Oylik.Server;

public static class Secrets
{
    /// <summary>32 bayt tasodifiy, base64url — API kalit / qurilma tokeni.</summary>
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Kalitlar yuqori entropiyali, shuning uchun SHA-256 yetarli (sekin hash shart emas).</summary>
    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static string NewPairingCode() =>
        RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, Api.PairingCodeLength)).ToString(new string('0', Api.PairingCodeLength));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

[Flags]
public enum CallerKind
{
    Desktop = 1,
    Device = 2,
    Any = Desktop | Device,
}

/// <summary>Autentifikatsiya qilingan chaqiruvchi: desktop (X-Api-Key) yoki telefon (Bearer deviceToken).</summary>
public sealed record Caller(Guid InstallationId, Guid? DeviceId)
{
    public bool IsDevice => DeviceId is not null;

    public static Caller Get(HttpContext ctx) => (Caller)ctx.Items[typeof(Caller)]!;
}

/// <summary>Endpoint filtri: ruxsat etilgan chaqiruvchi turini tekshiradi, aks holda 401.</summary>
public sealed class AuthFilter(CallerKind allowed) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<ServerDb>();
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();

        Caller? caller = null;
        if (allowed.HasFlag(CallerKind.Desktop) && http.Request.Headers[Api.ApiKeyHeader].ToString() is { Length: > 0 } key)
        {
            var hash = Secrets.Hash(key);
            // Bosqich 3: kalitlar alohida jadvalda (o'rnatmaga 1:ko'p — har mashina o'z kaliti).
            var apiKey = await db.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.KeyHash == hash);
            if (apiKey is not null)
                caller = new Caller(apiKey.InstallationId, null);
        }
        else if (allowed.HasFlag(CallerKind.Device)
                 && http.Request.Headers.Authorization.ToString() is { } auth
                 && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var hash = Secrets.Hash(auth["Bearer ".Length..].Trim());
            var device = await db.Devices.FirstOrDefaultAsync(d => d.TokenHash == hash && d.IsActive);
            if (device is not null)
            {
                device.LastSeenAt = clock.GetUtcNow();
                await db.SaveChangesAsync();
                caller = new Caller(device.InstallationId, device.Id);
            }
        }

        if (caller is null)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Kalit yoki token noto'g'ri.");

        http.Items[typeof(Caller)] = caller;
        return await next(context);
    }
}

public static class AuthExtensions
{
    public static TBuilder RequireCaller<TBuilder>(this TBuilder builder, CallerKind kind) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new AuthFilter(kind));
}
