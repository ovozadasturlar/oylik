namespace Oylik.Data.Agent;

/// <summary>Agent holatining umumiy darajasi (tray ikonasi rangi).</summary>
public enum AgentHealth
{
    /// <summary>Hali birinchi tick bo'lmagan.</summary>
    Starting,

    /// <summary>Terminal ulangan, server (sozlangan bo'lsa) sinxron.</summary>
    Ok,

    /// <summary>Ishlayapti, lekin nimadir yetishmaydi: terminal o'chiq yoki server xatosi.</summary>
    Warning,

    /// <summary>Terminal yoqilgan, lekin topilmadi/ulanmadi.</summary>
    Error,
}

/// <summary>
/// Bosqich 2: agentning joriy holati — o'zgarmas surat (boshqa oqimdan, masalan tray UI'dan, xavfsiz o'qiladi).
/// Parol/kalit/ism yo'q: faqat vaqt, son va xato matni.
/// </summary>
public sealed record AgentStatus
{
    public static readonly AgentStatus Initial = new();

    /// <summary>Settings.Language ("uz-Latn"/"uz-Cyrl"/"ru") — tray matnlari shu tilda.</summary>
    public string? Language { get; init; }

    public bool TerminalEnabled { get; init; }
    public bool TerminalConnected { get; init; }

    /// <summary>Oxirgi muvaffaqiyatli terminal so'rovi.</summary>
    public DateTimeOffset? LastPollAt { get; init; }

    public string? TerminalError { get; init; }

    /// <summary>Bugun (mahalliy sana) bazadagi bekor qilinmagan davomat voqealari.</summary>
    public int TodayEvents { get; init; }

    public bool ServerConfigured { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public string? SyncError { get; init; }

    /// <summary>Holat yangilangan vaqt (null — hali tick bo'lmagan).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    public AgentHealth Health =>
        UpdatedAt is null ? AgentHealth.Starting
        : TerminalEnabled && !TerminalConnected ? AgentHealth.Error
        : !TerminalEnabled || SyncError is not null ? AgentHealth.Warning
        : AgentHealth.Ok;
}
