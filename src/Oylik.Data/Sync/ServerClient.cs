using System.Net;
using System.Net.Http.Json;
using Oylik.Contracts;
using Oylik.Core.Domain;

namespace Oylik.Data.Sync;

public sealed class ServerException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>TZ 6 endpointlari (desktop tomoni). DTO'lar faqat Oylik.Contracts dan.</summary>
public sealed class ServerClient
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly string? _apiKey;

    /// <param name="http">Testlar uchun (WebApplicationFactory); null — umumiy HttpClient.</param>
    public ServerClient(string baseUrl, string? apiKey, HttpClient? http = null)
    {
        _base = new Uri(NormalizeUrl(baseUrl) + "/");
        _apiKey = apiKey;
        _http = http ?? SharedHttp;
    }

    /// <summary>Oxiridagi "/" olib tashlanadi; sxema bo'lmasa http:// qo'shiladi.</summary>
    public static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "http://" + url;
        return url;
    }

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(NormalizeUrl(url), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, new Uri(_base, path.TrimStart('/')));
        if (_apiKey is not null)
            req.Headers.Add(Api.ApiKeyHeader, _apiKey);
        if (body is not null)
            req.Content = JsonContent.Create(body, body.GetType(), options: Api.Json);
        return req;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct = default)
    {
        using var res = await _http.SendAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
        return (await res.Content.ReadFromJsonAsync<T>(Api.Json, ct))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode)
            return;
        string? title = null;
        try
        {
            var problem = await res.Content.ReadFromJsonAsync<ProblemBody>(Api.Json, ct);
            title = problem?.Title;
        }
        catch (Exception)
        {
            // matn JSON emas
        }
        throw new ServerException(res.StatusCode, title ?? $"{(int)res.StatusCode} {res.ReasonPhrase}");
    }

    private sealed record ProblemBody(string? Title);

    public Task<HealthResponse> HealthAsync(CancellationToken ct = default) =>
        SendAsync<HealthResponse>(Request(HttpMethod.Get, Api.Routes.Health), ct);

    public Task<RegisterInstallationResponse> RegisterAsync(string name, string registrationKey, Guid installationId) =>
        SendAsync<RegisterInstallationResponse>(Request(HttpMethod.Post, Api.Routes.Installations,
            new RegisterInstallationRequest(name, registrationKey, installationId)));

    public Task<PairingCodeResponse> CreatePairingCodeAsync(Guid installationId) =>
        SendAsync<PairingCodeResponse>(Request(HttpMethod.Post, Api.Routes.PairingCodes(installationId)));

    public Task<List<DeviceDto>> GetDevicesAsync() =>
        SendAsync<List<DeviceDto>>(Request(HttpMethod.Get, Api.Routes.Devices));

    public Task<DeviceDto> DeactivateDeviceAsync(Guid id) =>
        SendAsync<DeviceDto>(Request(HttpMethod.Post, Api.Routes.DeactivateDevice(id)));

    /// <param name="maxShiftMinutes">H14: xodim juftlik chegarasi (telefon «Ichkarida» uchun); null — 16 soat.</param>
    public async Task PutEmployeesAsync(IEnumerable<Employee> employees, Func<Employee, int?>? maxShiftMinutes = null)
    {
        var body = employees.Select(e => new EmployeeDto(e.Id, e.FullName, e.Note, e.IsActive, e.UpdatedAt, maxShiftMinutes?.Invoke(e))).ToList();
        using var res = await _http.SendAsync(Request(HttpMethod.Put, Api.Routes.Employees, body));
        await EnsureSuccessAsync(res, default);
    }

    public Task<PostEventsResponse> PostEventsAsync(IEnumerable<AttendanceEvent> events) =>
        SendAsync<PostEventsResponse>(Request(HttpMethod.Post, Api.Routes.Events, events.Select(ToDto).ToList()));

    public Task<ServerEventDto> VoidEventAsync(Guid id, string reason) =>
        SendAsync<ServerEventDto>(Request(HttpMethod.Post, Api.Routes.VoidEvent(id), new VoidEventRequest(reason)));

    public Task<EventsPageResponse> GetEventsAsync(long sinceSeq, int limit = Api.MaxEventsPageSize) =>
        SendAsync<EventsPageResponse>(Request(HttpMethod.Get, $"{Api.Routes.Events}?sinceSeq={sinceSeq}&limit={limit}"));

    // ---------- Xaritalash ----------

    public static AttendanceEventDto ToDto(AttendanceEvent e) =>
        new(e.Id, e.EmployeeId, (AttendanceType)(int)e.Type, e.OccurredAt, e.DeviceId);

    public static AttendanceEvent FromServer(ServerEventDto s) => new()
    {
        Id = s.Id,
        EmployeeId = s.EmployeeId,
        Type = (AttendanceEventType)(int)s.Type,
        OccurredAt = s.OccurredAt,
        DeviceId = s.DeviceId,
        ReceivedAt = s.ReceivedAt,
        ServerSeq = s.Seq,
        IsVoided = s.IsVoided,
        VoidedAt = s.VoidedAt,
        VoidReason = s.VoidReason,
    };

    public static Device FromServer(DeviceDto d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        PairedAt = d.PairedAt,
        LastSeenAt = d.LastSeenAt,
        IsActive = d.IsActive,
    };
}
