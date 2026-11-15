using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Oylik.Contracts;

namespace Oylik.Server;

// TZ 6 "Ma'lumotlar". Kalit/token faqat SHA-256 hash ko'rinishida saqlanadi.

public sealed class Installation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Bosqich 3: muvaffaqiyatli join'lar sanagichi — MONOTON (kamaymaydi, kalit o'chirilsa ham qayta ishlatilmaydi).
    /// Har ulangan mashina sayt raqami = shu qiymat; tabel diapazoni <see cref="ApiKey.TabelBaseOf"/>.
    /// </summary>
    public int JoinCount { get; set; }
}

/// <summary>
/// Bosqich 3: desktop kaliti (X-Api-Key). O'rnatmaga 1:ko'p — asosiy mashina (ro'yxatdan o'tganda, sayt 0) va har join
/// qilgan mashina o'z kalitini oladi (bitta kalit ulashilmaydi).
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public string KeyHash { get; set; } = "";

    /// <summary>Mashina nomi (join so'rovidagi; asosiy — o'rnatma nomi).</summary>
    public string Name { get; set; } = "";

    /// <summary>0 — asosiy mashina; 1, 2, ... — join tartibi.</summary>
    public int SiteIndex { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Sayt tabel diapazoni boshi: 0 → 1 (asosiy, lokal default), n → n·1 000 000 + 1.</summary>
    public static int TabelBaseOf(int siteIndex) => siteIndex * 1_000_000 + 1;
}

/// <summary>Bosqich 3: ikkinchi mashina uchun ulanish kodi — telefon <see cref="PairingCode"/> dan ALOHIDA jadval.</summary>
public sealed class JoinCode
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public Guid InstallationId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PairingCode
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public Guid InstallationId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class EmployeeSnapshot
{
    public Guid InstallationId { get; set; }
    public Guid EmployeeId { get; set; }
    public string FullName { get; set; } = "";
    public string? Note { get; set; }
    public bool IsActive { get; set; }

    /// <summary>H14: juftlik chegarasi (daqiqa) — telefon «Ichkarida» uchun; null — Api.DefaultMaxShift.</summary>
    public int? MaxShiftMinutes { get; set; }

    /// <summary>Bosqich 3: doimiy tabel raqami — ko'p-zavodda har zavod terminali xodimni shu raqam bilan taniydi. null — berilmagan.</summary>
    public string? TabelNo { get; set; }

    /// <summary>Server vaqti — o'zgarish serverga yetgan payt (telefon `updatedSince` kursori uchun).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AttendanceEvent
{
    /// <summary>
    /// Sinxronizatsiya kursori: voqea qo'shilganda va bekor qilinganda yangi (o'suvchi) qiymat oladi —
    /// shunda `sinceSeq` bilan tortgan desktop bekor qilishni ham ko'radi.
    /// </summary>
    public long Seq { get; set; }
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public Guid EmployeeId { get; set; }
    public AttendanceType Type { get; set; }

    /// <summary>Telefon vaqti, offset bilan saqlanadi (ISO 8601 matn).</summary>
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    public ServerEventDto ToDto() => new(Seq, Id, EmployeeId, Type, OccurredAt, DeviceId, ReceivedAt, IsVoided, VoidedAt, VoidReason);
}

/// <summary>Seq hisoblagichi (bitta qator).</summary>
public sealed class SeqCounter
{
    public int Id { get; set; } = 1;
    public long Value { get; set; }
}

/// <summary>Server bazasi provayderi (config <c>Oylik:Database</c>).</summary>
public enum DatabaseProvider
{
    /// <summary>Prod (default): migratsiyalar shu provayder uchun.</summary>
    Postgres,

    /// <summary>Testlar / lokal sinov: sxema EnsureCreated bilan, migratsiyasiz.</summary>
    Sqlite,
}

public sealed class ServerDb(DbContextOptions<ServerDb> options) : DbContext(options)
{
    /// <summary><c>postgres</c> (yoki bo'sh) | <c>sqlite</c>; boshqa qiymat — xato (jim default emas).</summary>
    public static DatabaseProvider ParseProvider(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
        "sqlite" => DatabaseProvider.Sqlite,
        _ => throw new InvalidOperationException($"Oylik:Database noto'g'ri: '{value}' (postgres | sqlite)"),
    };

    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PairingCode> PairingCodes => Set<PairingCode>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<JoinCode> JoinCodes => Set<JoinCode>();
    public DbSet<EmployeeSnapshot> Employees => Set<EmployeeSnapshot>();
    public DbSet<AttendanceEvent> Events => Set<AttendanceEvent>();
    public DbSet<SeqCounter> Counters => Set<SeqCounter>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Solishtiriladigan vaqtlar UTC ticks; OccurredAt alohida — offset saqlanishi kerak.
        builder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<UtcTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Installation>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200);
        });

        b.Entity<ApiKey>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.InstallationId);
        });

        b.Entity<JoinCode>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Api.PairingCodeLength).IsRequired();
            e.HasIndex(x => new { x.InstallationId, x.Code });
        });

        b.Entity<Device>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.InstallationId);
        });

        b.Entity<PairingCode>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Api.PairingCodeLength).IsRequired();
            e.HasIndex(x => new { x.InstallationId, x.Code });
        });

        b.Entity<EmployeeSnapshot>(e =>
        {
            e.HasKey(x => new { x.InstallationId, x.EmployeeId });
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Note).HasMaxLength(100);
            e.Property(x => x.TabelNo).HasMaxLength(16);
            e.HasIndex(x => new { x.InstallationId, x.UpdatedAt });
        });

        b.Entity<AttendanceEvent>(e =>
        {
            // Seq bekor qilishda o'zgaradi, shuning uchun kalit — Id; Seq unique indeks.
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Seq).IsUnique();
            e.HasIndex(x => new { x.InstallationId, x.Seq });
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(8);
            e.Property(x => x.OccurredAt).HasConversion(
                v => v.ToString("O"),
                v => DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
                .HasMaxLength(40);
            e.Property(x => x.VoidReason).HasMaxLength(200);
        });

        b.Entity<SeqCounter>(e => e.Property(x => x.Id).ValueGeneratedNever());
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.UtcTicks,
        v => new DateTimeOffset(v, TimeSpan.Zero));
}

/// <summary>
/// `dotnet ef` uchun — migratsiyalar PostgreSQL uchun. Ulanish satri env <c>OYLIK_DESIGN_PG</c> dan (faqat
/// <c>database update</c> kabi bazaga ulanadigan buyruqlarga kerak; <c>migrations add</c> ulanmaydi).
/// </summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ServerDb>
{
    public const string ConnectionVariable = "OYLIK_DESIGN_PG";

    public ServerDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ServerDb>()
            .UseNpgsql(Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } cs
                ? cs
                : "Host=localhost;Port=5432;Database=oylik;Username=oylik")
            .Options);
}
