using System.Net;
using Oylik.Contracts;

namespace Oylik.Mobile.Logic.Sync;

/// <summary>Telefon ishlatadigan server endpointlari (TZ 6). Ulanish ma'lumoti (URL, token) chaqiruvchidan keladi.</summary>
public interface IOylikApi
{
    Task<HealthResponse> HealthAsync(string server, CancellationToken ct = default);

    Task<PairDeviceResponse> PairAsync(string server, PairDeviceRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(ApiSession s, DateTimeOffset? updatedSince, CancellationToken ct = default);

    Task<PostEventsResponse> PostEventsAsync(ApiSession s, IReadOnlyList<AttendanceEventDto> events, CancellationToken ct = default);

    Task VoidEventAsync(ApiSession s, Guid eventId, VoidEventRequest request, CancellationToken ct = default);

    Task<EventsPageResponse> GetEventsAsync(ApiSession s, long sinceSeq, int limit, CancellationToken ct = default);
}

/// <summary>Bog'langan qurilma: server manzili + Bearer token.</summary>
public sealed record ApiSession(string Server, string Token);

/// <summary>Server javob berdi, lekin xato bilan. <see cref="StatusCode"/> = null — aloqa yo'q.</summary>
public sealed class ApiException(HttpStatusCode? statusCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>Qurilma desktopdan uzilgan yoki token bekor qilingan.</summary>
    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    public bool IsNetwork => StatusCode is null;
}

/// <summary>Token SecureStorage'da (MAUI); testlarda — xotirada.</summary>
public interface ITokenStore
{
    Task<string?> GetAsync();
    Task SetAsync(string token);
    Task ClearAsync();
}

public sealed class MemoryTokenStore : ITokenStore
{
    private string? _token;
    public Task<string?> GetAsync() => Task.FromResult(_token);
    public Task SetAsync(string token) { _token = token; return Task.CompletedTask; }
    public Task ClearAsync() { _token = null; return Task.CompletedTask; }
}
