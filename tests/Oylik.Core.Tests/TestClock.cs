namespace Oylik.Core.Tests;

internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("UZ", TimeSpan.FromHours(5), "UZ", "UZ");

    public static TestClock Default() => new(new DateTimeOffset(2026, 10, 9, 10, 42, 0, TimeSpan.FromHours(5)));
}
