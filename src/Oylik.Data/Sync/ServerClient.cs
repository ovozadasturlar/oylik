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

    /// <summary>Bosqich 3: ikkinchi mashina (zavod) uchun ulanish kodi (asosiy mashina, X-Api-Key).</summary>
    public Task<JoinCodeResponse> CreateJoinCodeAsync(Guid installationId) =>
        SendAsync<JoinCodeResponse>(Request(HttpMethod.Post, Api.Routes.JoinCodes(installationId)));

    /// <summary>Bosqich 3: mavjud o'rnatmaga ulanish (kalitsiz) — o'z ApiKey'i va tabel diapazoni.</summary>
    public Task<JoinInstallationResponse> JoinAsync(Guid installationId, string code, string name) =>
        SendAsync<JoinInstallationResponse>(Request(HttpMethod.Post, Api.Routes.JoinInstallation,
            new JoinInstallationRequest(installationId, code, name)));

    /// <summary>Bosqich 3: xodimlar pull — <paramref name="updatedSince"/> (shu jumladan) dan keyin o'zgarganlar; null — hammasi.</summary>
    public Task<List<EmployeeDto>> GetEmployeesAsync(DateTimeOffset? updatedSince)
    {
        var path = updatedSince is { } since
            ? $"{Api.Routes.Employees}?updatedSince={Uri.EscapeDataString(since.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}"
            : Api.Routes.Employees;
        return SendAsync<List<EmployeeDto>>(Request(HttpMethod.Get, path));
    }

    public Task<List<DeviceDto>> GetDevicesAsync() =>
        SendAsync<List<DeviceDto>>(Request(HttpMethod.Get, Api.Routes.Devices));

    public Task<DeviceDto> DeactivateDeviceAsync(Guid id) =>
        SendAsync<DeviceDto>(Request(HttpMethod.Post, Api.Routes.DeactivateDevice(id)));

    /// <param name="maxShiftMinutes">H14: xodim juftlik chegarasi (telefon «Ichkarida» uchun); null — 16 soat.</param>
    /// <param name="tombstones">Bosqich 3: o'chirilgan xodimlar — IsActive=false bilan (server upsert-only).</param>
    public async Task PutEmployeesAsync(IEnumerable<Employee> employees, Func<Employee, int?>? maxShiftMinutes = null,
        IEnumerable<EmployeeTombstone>? tombstones = null)
    {
        // O'chirilgan xodim ustun: boshqa jarayon xotirasida hali ro'yxatda bo'lsa ham IsActive=false ketadi.
        var dead = (tombstones ?? []).ToList();
        var deadIds = dead.Select(t => t.Id).ToHashSet();
        var body = employees.Where(e => !deadIds.Contains(e.Id)).Select(e => ToDto(e, maxShiftMinutes?.Invoke(e))).ToList();
        body.AddRange(dead.Select(t => new EmployeeDto(t.Id, t.FullName, null, false, t.DeletedAt, null, TabelNoText(t.TabelNo))));
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

    /// <summary>Bosqich 3: TabelNo ham ko'chadi — boshqa zavod terminali xodimni shu raqam bilan taniydi.</summary>
    public static EmployeeDto ToDto(Employee e, int? maxShiftMinutes) =>
        new(e.Id, e.FullName, e.Note, e.IsActive, e.UpdatedAt, maxShiftMinutes, TabelNoText(e.TabelNo));

    /// <summary>Lokal TabelNo (int, 0 — berilmagan) → DTO matni.</summary>
    public static string? TabelNoText(int tabelNo) =>
        tabelNo > 0 ? tabelNo.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    public static RemoteEmployee FromServer(EmployeeDto d) =>
        new(d.Id, d.FullName, d.Note, d.IsActive, ParseTabelNo(d.TabelNo), d.UpdatedAt);

    /// <summary>DTO matni → lokal TabelNo; raqam bo'lmasa yoki berilmagan bo'lsa null.</summary>
    public static int? ParseTabelNo(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

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
