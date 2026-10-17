using Oylik.Contracts;
using SQLite;

namespace Oylik.Mobile.Logic.Data;

// Lokal baza (TZ 7). Vaqtlar UTC tick + offset (daqiqa) bo'lib saqlanadi — sqlite-net DateTimeOffset offsetini yo'qotadi.

public enum SyncState
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

[Table("Employee")]
public sealed class LocalEmployee
{
    [PrimaryKey] public Guid Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Note { get; set; }
    public bool IsActive { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    /// <summary>H14: juftlik chegarasi (daqiqa) — sutkalik smenada 28 soat; null — <see cref="Api.DefaultMaxShift"/>. Yangi ustun.</summary>
    public int? MaxShiftMinutes { get; set; }

    /// <summary>"Ichkarida" va juftlik chegarasi shu xodim uchun.</summary>
    [Ignore]
    public TimeSpan MaxShift => MaxShiftMinutes is > 0 and var m ? TimeSpan.FromMinutes(m) : Api.DefaultMaxShift;

    [Ignore]
    public DateTimeOffset UpdatedAt
    {
        get => new(UpdatedAtUtcTicks, TimeSpan.Zero);
        set => UpdatedAtUtcTicks = value.UtcTicks;
    }
}

[Table("Event")]
public sealed class LocalEvent
{
    [PrimaryKey] public Guid Id { get; set; }
    [Indexed] public Guid EmployeeId { get; set; }
    /// <summary>Qaysi o'rnatmaga tegishli — boshqa korxonaga qayta bog'lansa eski voqealar u yerga jo'natilmaydi.</summary>
    [Indexed] public Guid InstallationId { get; set; }
    public AttendanceType Type { get; set; }
    [Indexed] public long OccurredAtUtcTicks { get; set; }
    public int OffsetMinutes { get; set; }
    public Guid? DeviceId { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    /// <summary>Bekor qilish serverga hali yetkazilmagan.</summary>
    public bool VoidPending { get; set; }
    [Indexed] public SyncState SyncState { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public long? ServerSeq { get; set; }

    [Ignore]
    public DateTimeOffset OccurredAt
    {
        get => new DateTimeOffset(OccurredAtUtcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(OffsetMinutes));
        set
        {
            OccurredAtUtcTicks = value.UtcTicks;
            OffsetMinutes = (int)value.Offset.TotalMinutes;
        }
    }

    public AttendanceEventDto ToDto() => new(Id, EmployeeId, Type, OccurredAt, DeviceId);
}

/// <summary>Bitta qator (Id = 1). Token bu yerda EMAS — SecureStorage'da.</summary>
[Table("Settings")]
public sealed class DeviceSettings
{
    [PrimaryKey] public int Id { get; set; } = 1;
    /// <summary>Asosiy (global) manzil; faqat lokal sozlangan bo'lsa — lokal.</summary>
    public string? ServerUrl { get; set; }
    /// <summary>Lokal (Wi-Fi) manzil — ikkala manzil sozlanganda; avval shunga ulaniladi (QR "localServer"). Yangi ustun.</summary>
    public string? LocalServerUrl { get; set; }
    /// <summary>Oxirgi muvaffaqiyatli ulangan manzil (lokal yoki global); null — ulanmagan.</summary>
    public string? ActiveServerUrl { get; set; }
    public Guid InstallationId { get; set; }
    public Guid DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public long LastSeq { get; set; }
    public long? LastSyncAtUtcTicks { get; set; }
    /// <summary>GET employees?updatedSince= uchun — olingan eng katta UpdatedAt.</summary>
    public long? EmployeesSinceUtcTicks { get; set; }
    public bool KeepScreenOn { get; set; } = true;
    public int ConsecutiveFailures { get; set; }
    public long? NextAttemptAtUtcTicks { get; set; }
    public string? LastError { get; set; }
    /// <summary>Server soati − telefon soati (soniya); /api/health dan.</summary>
    public double? ClockOffsetSeconds { get; set; }
    /// <summary>Server 401 qaytardi — qurilma desktopdan uzilgan.</summary>
    public bool Revoked { get; set; }

    [Ignore] public bool IsPaired => DeviceId != Guid.Empty && !string.IsNullOrEmpty(ServerUrl) && !Revoked;
    /// <summary>Lokal manzil alohida sozlangan (ikkala manzil bor).</summary>
    [Ignore] public bool HasLocalServer => !string.IsNullOrEmpty(LocalServerUrl) && LocalServerUrl != ServerUrl;
    [Ignore] public DateTimeOffset? LastSyncAt => FromTicks(LastSyncAtUtcTicks);
    [Ignore] public DateTimeOffset? NextAttemptAt => FromTicks(NextAttemptAtUtcTicks);
    [Ignore] public DateTimeOffset? EmployeesSince => FromTicks(EmployeesSinceUtcTicks);

    [Ignore]
    public bool ClockWrong => ClockOffsetSeconds is { } s && Math.Abs(s) > Api.ClockSkewWarning.TotalSeconds;

    private static DateTimeOffset? FromTicks(long? t) => t is { } v ? new DateTimeOffset(v, TimeSpan.Zero) : null;
}
