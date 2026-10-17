using System.Net;
using System.Net.Sockets;
using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;

namespace Oylik.Mobile.Logic.Sync;

/// <summary>
/// Ikki manzil (lokal Wi-Fi + global domen): qaysi tartibda urinish, lokalni tez (2 s) tekshirish va holat matni.
/// </summary>
public static class ServerRoutes
{
    /// <summary>
    /// /api/health — lokal uchun qisqa <paramref name="timeout"/> bilan (<see cref="Api.LocalServerProbeTimeout"/>).
    /// Ulanmasa (timeout ham) <see cref="ApiException"/> (IsNetwork) tashlaydi.
    /// </summary>
    public static async Task<HealthResponse> HealthAsync(IOylikApi api, string server, TimeSpan? timeout, CancellationToken ct)
    {
        if (timeout is not { } t) return await api.HealthAsync(server, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(t);
        try
        {
            return await api.HealthAsync(server, cts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(null, $"Lokal server {t.TotalSeconds:0.#} s ichida javob bermadi", ex);
        }
    }

    /// <summary>Tarmoq xatosi (server topilmadi / javob yo'q) — keyingi manzilni sinash mumkin.</summary>
    public static bool IsUnreachable(Exception ex, CancellationToken ct) => ex switch
    {
        ApiException a => a.IsNetwork,
        HttpRequestException => true,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false,
    };

    /// <summary>"Server: …" holati: "lokal (192.168.1.19)" / "internet orqali" / "ulanmagan".</summary>
    public static string Describe(DeviceSettings s)
    {
        var active = s.ActiveServerUrl;
        if (string.IsNullOrEmpty(active) || s.LastError is not null) return Strings.ServerNotConnected;
        if (FakeOylikApi.IsDemo(active)) return Strings.Demo;
        var isLocal = s.HasLocalServer ? active == s.LocalServerUrl : IsLocalAddress(active);
        return isLocal ? $"{Strings.ServerLocal} ({Host(active)})" : Strings.ServerInternet;
    }

    public static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;

    /// <summary>LAN manzil: localhost, 10/8, 172.16/12, 192.168/16, 169.254/16, *.local.</summary>
    public static bool IsLocalAddress(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.IsLoopback || u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(u.Host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }
}
