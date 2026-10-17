using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;

namespace Oylik.Mobile.Tests;

/// <summary>Toshkent (+05:00) soati; qo'lda suriladi.</summary>
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public static readonly TimeZoneInfo Tashkent =
        TimeZoneInfo.CreateCustomTimeZone("UZ", TimeSpan.FromHours(5), "Toshkent", "Toshkent");

    public static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    private DateTimeOffset _now = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => Tashkent;
    public void Advance(TimeSpan by) => _now += by;
    public void Set(DateTimeOffset t) => _now = t.ToUniversalTime();

    /// <summary>Toshkent vaqti: At(2026, 10, 9, 8, 2).</summary>
    public static DateTimeOffset At(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, Offset);
}

public static class Ev
{
    public static LocalEvent In(Guid emp, DateTimeOffset at, bool voided = false) => Make(emp, AttendanceType.In, at, voided);
    public static LocalEvent Out(Guid emp, DateTimeOffset at, bool voided = false) => Make(emp, AttendanceType.Out, at, voided);

    private static LocalEvent Make(Guid emp, AttendanceType t, DateTimeOffset at, bool voided) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = emp,
        Type = t,
        OccurredAt = at,
        IsVoided = voided,
    };
}

/// <summary>Har test uchun vaqtinchalik SQLite fayl.</summary>
public sealed class TempDb : IAsyncDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oylik-mobile-tests", Guid.NewGuid().ToString("N"));

    public TempDb()
    {
        Directory.CreateDirectory(_dir);
        Store = new LocalStore(Path.Combine(_dir, "test.db3"));
    }

    public LocalStore Store { get; }

    public async ValueTask DisposeAsync()
    {
        await Store.CloseAsync();
        try { Directory.Delete(_dir, true); } catch { /* fayl band bo'lishi mumkin */ }
    }
}
