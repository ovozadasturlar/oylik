namespace Oylik.Core.Domain;

public enum AttendanceEventType
{
    In = 0,   // Keldi
    Out = 1,  // Ketdi
}

/// <summary>3.7: davomat voqeasi. O'chirilmaydi — <see cref="IsVoided"/> bilan bekor qilinadi.</summary>
public sealed class AttendanceEvent
{
    /// <summary>Telefonda (yoki desktopda qo'lda) yaratiladi — idempotentlik kaliti.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public AttendanceEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>null = desktopdan qo'lda kiritilgan.</summary>
    public Guid? DeviceId { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }

    /// <summary>Server tartib raqami — sinxronizatsiya kursori. null = hali serverga yetmagan.</summary>
    public long? ServerSeq { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }

    /// <summary>A11: qo'lda qo'shilganda izoh.</summary>
    public string? Note { get; set; }

    /// <summary>Faqat desktop: serverga jo'natilishi kerak (yangi voqea yoki bekor qilish).</summary>
    public bool PendingPush { get; set; }

    public bool IsManual => DeviceId is null;

    /// <summary>A12: serverga kech yetgan (telefon soati noto'g'ri bo'lishi mumkin) — ogohlantirish, rad etilmaydi.</summary>
    public bool HasClockSkew => ReceivedAt is { } r && r - OccurredAt > AttendanceRules.ClockSkewWarning;
}

/// <summary>3.8: bog'langan telefon.</summary>
public sealed class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset PairedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;
}
