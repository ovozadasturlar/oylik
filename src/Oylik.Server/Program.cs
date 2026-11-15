using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Oylik.Contracts;
using Oylik.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Oylik"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WriteGate>();
builder.Services.AddProblemDetails();

// Baza: prod — PostgreSQL (default), testlar — SQLite (Oylik:Database=sqlite). Ulanish satri: ConnectionStrings:Oylik.
var provider = ServerDb.ParseProvider(builder.Configuration["Oylik:Database"]);
var connection = builder.Configuration.GetConnectionString("Oylik");
if (provider == DatabaseProvider.Sqlite && string.IsNullOrWhiteSpace(connection))
    connection = "Data Source=data/oylik-server.db";
else if (string.IsNullOrWhiteSpace(connection))
    throw new InvalidOperationException(
        "ConnectionStrings:Oylik berilmagan (env ConnectionStrings__Oylik) — PostgreSQL ulanish satri kerak (docs/server-postgres.md).");
builder.Services.AddDbContext<ServerDb>(o =>
{
    if (provider == DatabaseProvider.Sqlite)
        o.UseSqlite(connection);
    else
        o.UseNpgsql(connection);
});

builder.Services.ConfigureHttpJsonOptions(o =>
{
    var api = Api.Json;
    o.SerializerOptions.PropertyNamingPolicy = api.PropertyNamingPolicy;
    o.SerializerOptions.PropertyNameCaseInsensitive = api.PropertyNameCaseInsensitive;
    foreach (var c in api.Converters)
        o.SerializerOptions.Converters.Add(c);
});

// Bog'lash kodini taxmin qilishdan himoya: IP bo'yicha daqiqasiga N urinish → 429.
var pairPermits = builder.Configuration.GetValue("Oylik:PairPermitsPerMinute", 5);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(Endpoints.PairRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = pairPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Bosqich 3: ulanish kodi ham xuddi shunday cheklanadi (alohida hisob — telefon bog'lash urinishlari bilan aralashmaydi).
    o.AddPolicy(Endpoints.JoinRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = pairPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Caddy / Cloudflare orqasida haqiqiy IP.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDb>();
    if (provider == DatabaseProvider.Sqlite)
    {
        // Testlar / lokal sinov: migratsiyalar PostgreSQL uchun — SQLite'da sxema modeldan yaratiladi.
        var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection).DataSource;
        if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { } dir)
            Directory.CreateDirectory(dir);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
    else
    {
        // Toza bazada EF Migrate tarix jadvalini yaratishdan oldin uni o'qiydi va zararsiz "Failed executing DbCommand"
        // xatosini loglaydi (mijozni chalg'itadi) — jadvalni oldindan yaratamiz (CREATE TABLE IF NOT EXISTS).
        // Baza o'zi yo'q bo'lsa (test) — MigrateAsync uni yaratadi; bu holatda tekshiruv kerak emas.
        if (await db.GetService<IRelationalDatabaseCreator>().ExistsAsync())
            await db.GetService<IHistoryRepository>().CreateIfNotExistsAsync();
        await db.Database.MigrateAsync();
    }
    app.Logger.LogInformation("Baza: {Provider}", provider);
}

if (string.IsNullOrEmpty(app.Configuration["Oylik:RegistrationKey"]))
    app.Logger.LogWarning("Oylik:RegistrationKey berilmagan — yangi o'rnatmalar ro'yxatdan o'ta olmaydi.");

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
Endpoints.Map(app);

app.Run();

public partial class Program;
