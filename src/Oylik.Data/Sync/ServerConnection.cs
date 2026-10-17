using System.Net.Sockets;
using Oylik.Contracts;

namespace Oylik.Data.Sync;

/// <summary>Hozir qaysi manzil orqali ulangan.</summary>
public enum ServerRoute
{
    None,
    /// <summary>Lokal manzil (Wi-Fi/LAN).</summary>
    Local,
    /// <summary>Global manzil (internet orqali).</summary>
    Global,
}

/// <summary>
/// 6.1: bitta server, ikki manzil — lokal (Wi-Fi/LAN) va global (domen); ApiKey bitta.
/// Avval lokalga urinadi (<see cref="Api.LocalServerProbeTimeout"/>, /api/health); javob bermasa — globalga o'tadi.
/// Globalda ishlayotganda har <see cref="Api.LocalServerRecheckInterval"/> lokal qayta tekshiriladi.
/// Faqat bitta manzil berilgan bo'lsa — faqat o'sha.
/// </summary>
public sealed class ServerConnection
{
    private readonly TimeProvider _clock;
    private readonly Func<string, HttpClient?>? _http;
    private DateTimeOffset? _lastLocalProbe;

    /// <param name="http">Testlar uchun: manzil → HttpClient (WebApplicationFactory); null — umumiy HttpClient.</param>
    public ServerConnection(string? localUrl, string? globalUrl, string? apiKey, TimeProvider? clock = null, Func<string, HttpClient?>? http = null)
    {
        LocalUrl = Normalize(localUrl);
        GlobalUrl = Normalize(globalUrl);
        if (LocalUrl is null && GlobalUrl is null)
            throw new ArgumentException("Kamida bitta manzil kerak.");
        ApiKey = apiKey;
        _clock = clock ?? TimeProvider.System;
        _http = http;
    }

    public string? LocalUrl { get; }
    public string? GlobalUrl { get; }
    public string? ApiKey { get; }

    /// <summary>Oxirgi muvaffaqiyatli so'rov qaysi manzil orqali bo'lgan; xato bo'lsa — None.</summary>
    public ServerRoute Route { get; private set; }

    private static string? Normalize(string? url) => string.IsNullOrWhiteSpace(url) ? null : ServerClient.NormalizeUrl(url);

    public ServerClient Client(ServerRoute route)
    {
        var url = route == ServerRoute.Local ? LocalUrl : GlobalUrl;
        if (url is null)
            throw new InvalidOperationException($"{route} manzil sozlanmagan.");
        return new ServerClient(url, ApiKey, _http?.Invoke(url));
    }

    /// <summary>So'rovni tanlangan manzil orqali bajaradi; lokal tarmoq xatosida — bir marta global orqali qayta urinadi.</summary>
    public async Task<T> RunAsync<T>(Func<ServerClient, Task<T>> action, CancellationToken ct = default)
    {
        var route = await ChooseAsync(ct);
        try
        {
            var result = await action(Client(route));
            Route = route;
            return result;
        }
        catch (Exception ex) when (route == ServerRoute.Local && GlobalUrl is not null && IsNetworkError(ex))
        {
            _lastLocalProbe = _clock.GetUtcNow(); // lokal ishlamadi — keyingi tekshiruv intervaldan keyin
            Route = ServerRoute.None;
        }
        catch
        {
            Route = ServerRoute.None;
            throw;
        }

        try
        {
            var result = await action(Client(ServerRoute.Global));
            Route = ServerRoute.Global;
            return result;
        }
        catch
        {
            Route = ServerRoute.None;
            throw;
        }
    }

    public Task RunAsync(Func<ServerClient, Task> action, CancellationToken ct = default) =>
        RunAsync(async c => { await action(c); return true; }, ct);

    private async Task<ServerRoute> ChooseAsync(CancellationToken ct)
    {
        if (LocalUrl is null)
            return ServerRoute.Global;
        if (GlobalUrl is null)
            return ServerRoute.Local;
        if (Route == ServerRoute.Local)
            return ServerRoute.Local;

        var now = _clock.GetUtcNow();
        if (_lastLocalProbe is { } last && now - last < Api.LocalServerRecheckInterval)
            return ServerRoute.Global;
        _lastLocalProbe = now;
        return await ProbeLocalAsync(ct) ? ServerRoute.Local : ServerRoute.Global;
    }

    /// <summary>Lokal manzil /api/health ga <see cref="Api.LocalServerProbeTimeout"/> ichida javob beradimi.</summary>
    public async Task<bool> ProbeLocalAsync(CancellationToken ct = default)
    {
        if (LocalUrl is null)
            return false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Api.LocalServerProbeTimeout);
        try
        {
            await Client(ServerRoute.Local).HealthAsync(cts.Token);
            return true;
        }
        catch (Exception ex) when (IsNetworkError(ex) || ex is ServerException)
        {
            return false;
        }
    }

    private static bool IsNetworkError(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException or SocketException or TimeoutException;

    /// <summary>Eski bitta manzildan migratsiya: http:// va IP yoki localhost — lokal, aks holda global.</summary>
    public static bool IsLocalAddress(string url) =>
        Uri.TryCreate(ServerClient.NormalizeUrl(url), UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttp
        && (u.IsLoopback || u.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6);
}
