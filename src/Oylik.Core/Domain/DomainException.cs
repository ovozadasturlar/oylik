namespace Oylik.Core.Domain;

public enum DomainError
{
    /// <summary>Args: oy (MonthKey).</summary>
    PeriodClosed,
    /// <summary>Args: oy (MonthKey).</summary>
    PeriodExists,
    /// <summary>Args: keyingi oy (MonthKey) [, qayta ochilayotgan oy (MonthKey)].</summary>
    NextPeriodClosed,
    InvalidDaysWorked,
    InvalidAmount,
    LineNotEmpty,
    CarryOverReadOnly,
    /// <summary>Args: xodim ismi.</summary>
    LineExists,
    EmployeeHasHistory,
    EmptyName,
    ReasonRequired,
    AlreadyVoided,
    InvalidEvent,
    /// <summary>Tarmoqda xodim yoki oylarda qator bor — o'chirib bo'lmaydi (nofaol qilish mumkin).</summary>
    BranchInUse,
    /// <summary>Jadval shabloni noto'g'ri (nom, davomiylik 30–1440, tanaffus, hafta kunlari, Cycle). Args: maydon nomi.</summary>
    InvalidSchedule,
    /// <summary>Jadval xodimga biriktirilgan — o'chirib bo'lmaydi.</summary>
    ScheduleInUse,
    /// <summary>Cycle jadval uchun langar sana kerak.</summary>
    CycleAnchorRequired,
}

/// <summary>
/// Message — log uchun (o'zbekcha). UI matni <see cref="Error"/> va <see cref="Args"/> dan tuziladi (Desktop: Strings.Domain).
/// </summary>
public sealed class DomainException(DomainError error, string message, params object[] args) : Exception(message)
{
    public DomainError Error { get; } = error;

    /// <summary>Xabar parametrlari: <see cref="MonthKey"/>, xodim ismi va h.k. (har xato uchun tartibi DomainError izohida).</summary>
    public IReadOnlyList<object> Args { get; } = args;
}
