using System.Text.Json;
using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Sync;

namespace Oylik.Mobile.Logic;

/// <summary>Bog'lash: QR (<see cref="PairingQrPayload"/>, InstallationId bilan) yoki qo'lda (server + kod; o'rnatmani server kod bo'yicha topadi).</summary>
public sealed class PairingService(LocalStore store, IOylikApi api, ITokenStore tokens, SyncEngine sync)
{
    /// <summary>"demo" (soxta server) qabul qilinadimi. Ilova Release'da false qiladi.</summary>
    public static bool AllowDemo { get; set; } = true;

    public static PairingQrPayload? ParseQr(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var p = JsonSerializer.Deserialize<PairingQrPayload>(text, Api.Json);
            if (p is null || p.InstallationId == Guid.Empty || NormalizeServer(p.Server) is null || !IsValidCode(p.Code)) return null;
            // Eski QR'da "localServer" yo'q; noto'g'ri bo'lsa — e'tiborsiz, faqat asosiy manzil ishlatiladi.
            return NormalizeServer(p.LocalServer) is null ? p with { LocalServer = null } : p;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsValidCode(string? code) =>
        code is { Length: Api.PairingCodeLength } && code.All(char.IsAsciiDigit);

    /// <summary>"192.168.1.10:5000" → "http://192.168.1.10:5000"; "demo" o'zgarmaydi; noto'g'ri bo'lsa null.</summary>
    public static string? NormalizeServer(string? server)
    {
        var s = server?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (AllowDemo && FakeOylikApi.IsDemo(s)) return FakeOylikApi.DemoServer;
        if (!s.Contains("://", StringComparison.Ordinal)) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host) || (!uri.Host.Contains('.') && !uri.Host.Contains(':') && uri.Host != "localhost"))
            return null;
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    /// <summary>
    /// Muvaffaqiyatli bo'lsa null, aks holda foydalanuvchiga ko'rsatiladigan xato.
    /// <paramref name="localServerInput"/> (QR "localServer" yoki qo'lda ixtiyoriy maydon) berilsa — avval lokal (2 s tekshiruv), ulanmasa asosiy manzil.
    /// Kod BIR MARTALIK: PairDevice so'rovi faqat bitta manzilga, bir marta yuboriladi. Javob kelgach (token olindi) —
    /// bog'lash bekor qilinmaydi: keyingi xatolar (sinxron va h.k.) faqat backoff bilan qayta urinadi.
    /// </summary>
    /// <param name="syncNow">true — xodimlar shu yerning o'zida yuklanadi (testlar); ilova false beradi va darhol asosiy ekranga o'tadi.</param>
    public async Task<string?> PairAsync(string? serverInput, Guid? installationId, string? code, string? deviceName,
        CancellationToken ct = default, string? localServerInput = null, bool syncNow = true)
    {
        var server = NormalizeServer(serverInput);
        if (server is null) return Strings.ErrBadServer;
        string? local = null;
        if (!string.IsNullOrWhiteSpace(localServerInput))
        {
            local = NormalizeServer(localServerInput);
            if (local is null) return Strings.ErrBadLocalServer;
            if (SameServer(local, server)) local = null; // bir xil manzil — ikki marta urinilmaydi
        }
        if (installationId == Guid.Empty) installationId = null;
        code = code?.Trim();
        if (!IsValidCode(code)) return Strings.ErrBadCode;
        deviceName = deviceName?.Trim();
        if (string.IsNullOrEmpty(deviceName)) return Strings.ErrBadDeviceName;

        // Bir vaqtda faqat bitta bog'lash (skaner/tugma ikki marta ishlasa ham kod bir marta yuboriladi).
        if (!await _pairGate.WaitAsync(0, ct)) return Strings.ErrPairInProgress;
        try
        {
            var current = await store.GetSettingsAsync();
            if (current.IsPaired && installationId is { } inst && current.InstallationId == inst && !string.IsNullOrEmpty(await tokens.GetAsync()))
            {
                // Shu korxonaga allaqachon bog'langan (masalan, QR ikkinchi marta skanerlandi) — kodni qayta ishlatmaymiz.
                Diag.Log("pair: allaqachon bog'langan, so'rov yuborilmadi");
                return null;
            }

            var request = new PairDeviceRequest(installationId, code!, deviceName);
            PairDeviceResponse res;
            string used;
            try
            {
                (res, used) = await PairViaAsync(server, local, request, ct);
            }
            catch (ApiException ex)
            {
                Diag.Log($"pair: server xatosi {(int?)ex.StatusCode} {ex.Message}");
                return Describe(ex);
            }
            Diag.Log($"pair: OK qurilma {res.DeviceId} ({ServerRoutes.Host(used)})");

            // Token olindi — endi bog'lash bekor qilinmaydi. Sinxron bilan navbatda (eski nusxa ustidan yozilmasin):
            // avval token, keyin sozlamalar.
            await sync.ExclusiveAsync(() => SaveAsync(res, server, local, used, deviceName), CancellationToken.None);
        }
        finally
        {
            _pairGate.Release();
        }

        if (syncNow)
        {
            try
            {
                await sync.SyncAsync(force: true, ct); // xodimlar darhol yuklansin; xato bo'lsa keyin qayta urinadi
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Diag.Log($"pair: birinchi sinxron xatosi (bog'lash saqlandi): {ex.GetType().Name} {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>Token va sozlamalar (sinxron navbatida). Token birinchi — sozlamalar "bog'langan" bo'lsa, token albatta bor.</summary>
    private async Task<bool> SaveAsync(PairDeviceResponse res, string server, string? local, string used, string deviceName)
    {
        await tokens.SetAsync(res.DeviceToken);
        var s = await store.GetSettingsAsync();
        if (s.InstallationId != res.InstallationId)
        {
            // Boshqa korxona — eski xodimlar va kursor kerak emas (jo'natilmagan voqealar o'z InstallationId'si bilan qoladi).
            await store.ClearForUnpairAsync();
            s.LastSeq = 0;
            s.EmployeesSinceUtcTicks = null;
        }
        s.ServerUrl = server;
        s.LocalServerUrl = local;
        s.ActiveServerUrl = used;
        s.InstallationId = res.InstallationId;
        s.DeviceId = res.DeviceId;
        s.DeviceName = deviceName;
        s.Revoked = false;
        s.LastError = null;
        s.ConsecutiveFailures = 0;
        s.NextAttemptAtUtcTicks = null;
        await store.SaveSettingsAsync(s);
        return true;
    }

    private readonly SemaphoreSlim _pairGate = new(1, 1);

    public static bool SameServer(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lokal bor bo'lsa: avval health (qisqa timeout) — javob bermasa, kod umuman lokalga yuborilmaydi, asosiy manzilga ketadi.
    /// Health javob bersa — kod FAQAT lokalga (oddiy timeout bilan) yuboriladi; u yerda xato bo'lsa globalga qayta yuborilmaydi
    /// (kod ishlatilgan bo'lishi mumkin).
    /// </summary>
    private async Task<(PairDeviceResponse Response, string Server)> PairViaAsync(
        string server, string? local, PairDeviceRequest request, CancellationToken ct)
    {
        var target = server;
        if (local is not null)
        {
            try
            {
                await ServerRoutes.HealthAsync(api, local, sync.LocalProbeTimeout, ct);
                target = local;
            }
            catch (Exception ex) when (ServerRoutes.IsUnreachable(ex, ct))
            {
                Diag.Log("pair: lokal javob bermadi — asosiy manzil");
            }
        }
        Diag.Log($"pair: so'rov {ServerRoutes.Host(target)}");
        return (await api.PairAsync(target, request, ct), target);
    }

    public static string Describe(ApiException ex) => ex switch
    {
        { IsNetwork: true } => Strings.ErrNoNetwork,
        { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => Strings.ErrTooManyAttempts,
        { StatusCode: System.Net.HttpStatusCode.Conflict } => Strings.ErrCodeUsed,
        { StatusCode: System.Net.HttpStatusCode.Gone } => Strings.ErrCodeExpired,
        { StatusCode: System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound }
            => Strings.ErrCodeRejected,
        _ => $"{Strings.ErrServer}: {ex.Message}",
    };

    /// <summary>
    /// Bog'langandan keyin manzillarni o'zgartirish (qayta bog'lashsiz). Har berilgan manzil tekshiriladi:
    /// /api/health + token bilan so'rov (GET events limit=1). Biror tekshiruv o'tmasa — hech narsa saqlanmaydi.
    /// </summary>
    public async Task<string?> UpdateAddressesAsync(string? serverInput, string? localServerInput, CancellationToken ct = default)
    {
        var server = NormalizeServer(serverInput);
        if (server is null) return Strings.ErrBadServer;
        string? local = null;
        if (!string.IsNullOrWhiteSpace(localServerInput))
        {
            local = NormalizeServer(localServerInput);
            if (local is null) return Strings.ErrBadLocalServer;
            if (SameServer(local, server)) local = null;
        }

        var s = await store.GetSettingsAsync();
        var token = await tokens.GetAsync();
        if (!s.IsPaired || string.IsNullOrEmpty(token)) return Strings.DeviceRevoked;

        foreach (var (address, isLocal) in new[] { (local, true), (server, false) })
        {
            if (address is null) continue;
            var label = isLocal ? Strings.AddrLocal : Strings.AddrServer;
            try
            {
                await ServerRoutes.HealthAsync(api, address, isLocal ? sync.LocalProbeTimeout : null, ct);
                await api.GetEventsAsync(new ApiSession(address, token), 0, 1, ct);
            }
            catch (ApiException ex) when (ex.IsUnauthorized)
            {
                return $"{label}: {Strings.ErrAddrOtherServer}";
            }
            catch (Exception ex) when (ex is ApiException || ServerRoutes.IsUnreachable(ex, ct))
            {
                Diag.Log($"manzil tekshiruvi: {ServerRoutes.Host(address)} — {ex.Message}");
                return $"{label}: {(ex is ApiException { IsNetwork: false } a ? $"{Strings.ErrServer}: {a.Message}" : Strings.ErrAddrUnreachable)}";
            }
        }

        await sync.ExclusiveAsync(async () =>
        {
            var fresh = await store.GetSettingsAsync();
            fresh.ServerUrl = server;
            fresh.LocalServerUrl = local;
            fresh.ActiveServerUrl = null;
            fresh.LastError = null;
            fresh.ConsecutiveFailures = 0;
            fresh.NextAttemptAtUtcTicks = null;
            await store.SaveSettingsAsync(fresh);
            return true;
        }, ct);
        sync.RecheckLocal();
        Diag.Log($"manzillar saqlandi: {ServerRoutes.Host(server)} / {(local is null ? "-" : ServerRoutes.Host(local))}");
        return null;
    }

    /// <summary>[Uzish]: token o'chadi; jo'natilmagan voqealar telefonda qoladi.</summary>
    public async Task UnpairAsync()
    {
        Diag.Log("uzish: foydalanuvchi [Uzish] ni tasdiqladi");
        await tokens.ClearAsync();
        var s = await store.GetSettingsAsync();
        await store.ClearForUnpairAsync();
        s.DeviceId = Guid.Empty;
        s.ActiveServerUrl = null;
        s.LastSeq = 0;
        s.EmployeesSinceUtcTicks = null;
        s.LastSyncAtUtcTicks = null;
        s.LastError = null;
        s.ConsecutiveFailures = 0;
        s.NextAttemptAtUtcTicks = null;
        s.Revoked = false;
        await store.SaveSettingsAsync(s);
    }
}
