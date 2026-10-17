using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Oylik.Contracts;

namespace Oylik.Mobile.Logic.Sync;

/// <summary>Haqiqiy server: faqat <see cref="Api.Routes"/> va <see cref="Api.Json"/>.</summary>
public sealed class HttpOylikApi(HttpClient http) : IOylikApi
{
    public Task<HealthResponse> HealthAsync(string server, CancellationToken ct = default) =>
        SendAsync<HealthResponse>(HttpMethod.Get, server, Api.Routes.Health, null, null, ct);

    public Task<PairDeviceResponse> PairAsync(string server, PairDeviceRequest request, CancellationToken ct = default) =>
        SendAsync<PairDeviceResponse>(HttpMethod.Post, server, Api.Routes.PairDevice, null, request, ct);

    public async Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(ApiSession s, DateTimeOffset? updatedSince, CancellationToken ct = default)
    {
        var path = Api.Routes.Employees;
        if (updatedSince is { } since)
            path += "?updatedSince=" + Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture));
        return await SendAsync<List<EmployeeDto>>(HttpMethod.Get, s.Server, path, s.Token, null, ct);
    }

    public Task<PostEventsResponse> PostEventsAsync(ApiSession s, IReadOnlyList<AttendanceEventDto> events, CancellationToken ct = default) =>
        SendAsync<PostEventsResponse>(HttpMethod.Post, s.Server, Api.Routes.Events, s.Token, events, ct);

    public Task VoidEventAsync(ApiSession s, Guid eventId, VoidEventRequest request, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Post, s.Server, Api.Routes.VoidEvent(eventId), s.Token, request, ct, readBody: false);

    public Task<EventsPageResponse> GetEventsAsync(ApiSession s, long sinceSeq, int limit, CancellationToken ct = default) =>
        SendAsync<EventsPageResponse>(HttpMethod.Get, s.Server,
            $"{Api.Routes.Events}?sinceSeq={sinceSeq}&limit={limit}", s.Token, null, ct);

    private async Task<T> SendAsync<T>(
        HttpMethod method, string server, string path, string? token, object? body, CancellationToken ct, bool readBody = true)
    {
        if (!Uri.TryCreate(server.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new ApiException(null, "Server manzili noto'g'ri: " + server);
        using var req = new HttpRequestMessage(method, new Uri(baseUri, path.TrimStart('/')));
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Api.Json);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ApiException(null, ex.Message, ex);
        }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                string detail = "";
                try { detail = await res.Content.ReadAsStringAsync(ct); } catch { /* muhim emas */ }
                if (detail.Length > 300) detail = detail[..300];
                throw new ApiException(res.StatusCode, $"{(int)res.StatusCode} {res.ReasonPhrase} {detail}".Trim());
            }
            if (!readBody) return default!;
            try
            {
                return (await res.Content.ReadFromJsonAsync<T>(Api.Json, ct))!;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                throw new ApiException(res.StatusCode, "Server javobi tushunarsiz: " + ex.Message, ex);
            }
        }
    }
}
