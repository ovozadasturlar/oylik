using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;

namespace Oylik.Mobile.Logic.Sync;

/// <summary>TZ 7: muvaffaqiyatsiz urinishlardan keyin kutish 1 → 2 → 5 → 15 daqiqa; muvaffaqiyatdan keyin — 60 s taymer.</summary>
public static class Backoff
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public static TimeSpan Delay(int consecutiveFailures) => consecutiveFailures switch
    {
        <= 0 => Interval,
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(15),
    };
}

public enum SyncOutcome
{
    Ok,
    Skipped,      // vaqti kelmagan (backoff) yoki allaqachon ishlayapti
    NotPaired,
    Failed,
    Revoked,      // 401 — qurilma uzilgan
}

public sealed record SyncResult(SyncOutcome Outcome, string? Error = null);

/// <summary>
/// Outbox sinxronizatsiya. Ketma-ketlik (TZ 7): health (soat farqi) → GET employees?updatedSince → POST pending events →
/// void'lar → GET events?sinceSeq. Bir vaqtda bittasi ishlaydi. Ikki manzil bo'lsa — avval lokal (2 s), ulanmasa global.
/// </summary>
public sealed class SyncEngine(LocalStore store, IOylikApi api, ITokenStore tokens, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Lokal manzil oxirgi marta ishlamagan vaqt; null — darhol tekshirish mumkin.</summary>
    private DateTimeOffset? _localFailedAt;

    /// <summary>Lokal serverga ulanish tekshiruvi (<see cref="Api.LocalServerProbeTimeout"/>); testlarda qisqartiriladi.</summary>
    public TimeSpan LocalProbeTimeout { get; init; } = Api.LocalServerProbeTimeout;

    /// <summary>Wi-Fi'ga ulandi — keyingi sinxronda lokal manzil 5 daqiqani kutmasdan tekshiriladi.</summary>
    public void RecheckLocal() => _localFailedAt = null;

    /// <summary>Har sinxronizatsiyadan keyin (natijadan qat'i nazar) — UI yangilanadi.</summary>
    public event Action<SyncResult>? Completed;

    public bool IsRunning => _gate.CurrentCount == 0;

    /// <summary>
    /// Sozlamalarni sinxron bilan bir vaqtda o'zgartirmaslik uchun (bog'lash, manzil o'zgartirish):
    /// ishlayotgan sinxron eski nusxani saqlab, yangisini ustidan yozib yubormasin.
    /// </summary>
    public async Task<T> ExclusiveAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _exclusiveWaiters);
        try { await _gate.WaitAsync(ct); }
        finally { Interlocked.Decrement(ref _exclusiveWaiters); }
        try { return await action(); }
        finally { _gate.Release(); }
    }

    private int _exclusiveWaiters;

    /// <summary>Sinxron tugashini kutayotgan <see cref="ExclusiveAsync{T}"/> chaqiruvlari (testlar tartibni aniq kutishi uchun).</summary>
    internal int ExclusiveWaiters => Volatile.Read(ref _exclusiveWaiters);

    /// <param name="force">true — backoff'ni e'tiborsiz qoldiradi ([Hozir sinxronlash], yangi voqea).</param>
    public async Task<SyncResult> SyncAsync(bool force, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            if (!force) return new SyncResult(SyncOutcome.Skipped);
            await _gate.WaitAsync(ct); // majburiy — navbatdagi yangi voqea ham jo'natilsin
        }

        SyncResult result;
        try
        {
            result = await RunAsync(force, ct);
        }
        finally
        {
            _gate.Release();
        }
        if (result.Outcome != SyncOutcome.Skipped) Completed?.Invoke(result);
        return result;
    }

    private async Task<SyncResult> RunAsync(bool force, CancellationToken ct)
    {
        var s = await store.GetSettingsAsync();
        if (!s.IsPaired) return new SyncResult(SyncOutcome.NotPaired);
        var now = clock.GetUtcNow();
        if (!force && s.NextAttemptAt is { } next && now < next) return new SyncResult(SyncOutcome.Skipped);

        var token = await tokens.GetAsync();
        if (string.IsNullOrEmpty(token)) return await MarkRevokedAsync(s, "token topilmadi (SecureStorage bo'sh)");

        Exception? error = null;
        foreach (var (server, isLocal) in Candidates(s, now))
        {
            try
            {
                await RunOnAsync(s, new ApiSession(server, token), isLocal, ct);
                if (isLocal) _localFailedAt = null;
                now = clock.GetUtcNow();
                s.ActiveServerUrl = server;
                s.LastSyncAtUtcTicks = now.UtcTicks;
                s.ConsecutiveFailures = 0;
                s.LastError = null;
                s.NextAttemptAtUtcTicks = (now + Backoff.Interval).UtcTicks;
                await store.SaveSettingsAsync(s);
                return new SyncResult(SyncOutcome.Ok);
            }
            catch (ApiException ex) when (ex.IsUnauthorized)
            {
                return await MarkRevokedAsync(s, $"401 ({ServerRoutes.Host(server)})");
            }
            catch (Exception ex) when (ServerRoutes.IsUnreachable(ex, ct))
            {
                // Bu manzil bilan aloqa yo'q — keyingisini sinaymiz (lokal → global yoki aksincha).
                if (isLocal) _localFailedAt = clock.GetUtcNow();
                error = ex;
            }
            catch (ApiException ex)
            {
                // Server javob berdi, lekin xato (5xx va h.k.) — ikkinchi manzil ham o'sha server, almashtirmaymiz.
                error = ex;
                break;
            }
        }

        // Hech bir manzil ishlamadi — backoff faqat shu holda o'sadi.
        var serverError = error is ApiException { IsNetwork: false };
        var message = serverError ? $"{Strings.ErrServer}: {error!.Message}" : Strings.ErrNoNetwork;

        // Jo'natilmay qolganlar Pending'da qoladi — hech narsa yo'qolmaydi.
        var unsent = await store.GetOutboxAsync(s.InstallationId);
        foreach (var e in unsent)
        {
            e.Attempts++;
            e.LastError = message;
        }
        if (unsent.Count > 0) await store.UpdateEventsAsync(unsent);

        now = clock.GetUtcNow();
        if (!serverError) s.ActiveServerUrl = null;
        s.ConsecutiveFailures++;
        s.LastError = message;
        s.NextAttemptAtUtcTicks = (now + Backoff.Delay(s.ConsecutiveFailures)).UtcTicks;
        await store.SaveSettingsAsync(s);
        Diag.Log($"sync: xato #{s.ConsecutiveFailures}: {error?.GetType().Name} {error?.Message}");
        return new SyncResult(SyncOutcome.Failed, message);
    }

    /// <summary>
    /// Urinish tartibi. Bitta manzil — faqat o'sha. Ikkalasi: lokal faol (yoki hali noma'lum) bo'lsa — lokal, keyin global.
    /// Globalda ishlayotganda — global; lokal esa har <see cref="Api.LocalServerRecheckInterval"/> da
    /// (yoki Wi-Fi'ga ulanganda, <see cref="RecheckLocal"/>) birinchi tekshiriladi. Global ham ishlamasa — lokal oxirgi umid.
    /// </summary>
    private IEnumerable<(string Server, bool IsLocal)> Candidates(DeviceSettings s, DateTimeOffset now)
    {
        var global = s.ServerUrl!;
        if (!s.HasLocalServer)
        {
            yield return (global, false);
            yield break;
        }
        var local = s.LocalServerUrl!;
        var recheckDue = _localFailedAt is not { } at || now - at >= Api.LocalServerRecheckInterval;
        if (recheckDue || s.ActiveServerUrl != global)
        {
            yield return (local, true);
            yield return (global, false);
        }
        else
        {
            yield return (global, false);
            yield return (local, true);
        }
    }

    /// <summary>Bitta manzil bilan to'liq sinxronizatsiya. Lokal bo'lsa — health qisqa timeout bilan (ulanish tekshiruvi).</summary>
    private async Task RunOnAsync(DeviceSettings s, ApiSession session, bool isLocal, CancellationToken ct)
    {
        // 1. Server soati (A12 / "Telefon soati noto'g'ri").
        var t0 = clock.GetUtcNow();
        var health = await ServerRoutes.HealthAsync(api, session.Server, isLocal ? LocalProbeTimeout : null, ct);
        var t1 = clock.GetUtcNow();
        var mid = t0 + (t1 - t0) / 2;
        s.ClockOffsetSeconds = (health.ServerTime - mid).TotalSeconds;

        // 2. Xodimlar ro'yxati — avval (server noma'lum xodimli voqeani rad etadi).
        var employees = await api.GetEmployeesAsync(session, s.EmployeesSince, ct);
        if (employees.Count > 0)
        {
            await store.UpsertEmployeesAsync(employees);
            var max = employees.Max(e => e.UpdatedAt).UtcTicks;
            if (s.EmployeesSinceUtcTicks is null || max > s.EmployeesSinceUtcTicks) s.EmployeesSinceUtcTicks = max;
        }

        // 3. Jo'natilmagan voqealar — partiyalar bilan.
        var outbox = await store.GetOutboxAsync(s.InstallationId);
        foreach (var chunk in outbox.Chunk(Api.MaxEventsPageSize))
            await PostChunkAsync(session, chunk, ct);

        // 4. Bekor qilishlar (BEKOR) — faqat voqea Sent bo'lgandan keyin.
        foreach (var e in await store.GetPendingVoidsAsync(s.InstallationId))
        {
            try
            {
                await api.VoidEventAsync(session, e.Id, new VoidEventRequest(e.VoidReason ?? Strings.UndoReason), ct);
                e.LastError = null;
            }
            catch (ApiException ex) when (IsPermanent(ex))
            {
                e.LastError = $"{Strings.ErrRejected}: {ex.Message}";
            }
            e.VoidPending = false;
            await store.UpdateEventAsync(e);
        }

        // 5. Boshqa qurilmalar / desktop voqealari — kursor bo'yicha.
        while (true)
        {
            var page = await api.GetEventsAsync(session, s.LastSeq, Api.MaxEventsPageSize, ct);
            if (page.Events.Count > 0) await store.UpsertServerEventsAsync(s.InstallationId, page.Events);
            if (page.LastSeq <= s.LastSeq) break;
            s.LastSeq = page.LastSeq;
            if (page.Events.Count < Api.MaxEventsPageSize) break;
        }
    }

    /// <summary>
    /// Server butun partiyani 400 bilan rad etsa (noma'lum xodim va h.k.) — har voqea alohida yuboriladi,
    /// rad etilgani <see cref="SyncState.Failed"/> bo'ladi va qayta urinilmaydi; qolganlari jo'natiladi.
    /// </summary>
    private async Task PostChunkAsync(ApiSession session, IReadOnlyList<LocalEvent> chunk, CancellationToken ct)
    {
        PostEventsResponse res;
        try
        {
            res = await api.PostEventsAsync(session, chunk.Select(e => e.ToDto()).ToList(), ct);
        }
        catch (ApiException ex) when (IsPermanent(ex))
        {
            if (chunk.Count > 1)
            {
                foreach (var one in chunk) await PostChunkAsync(session, [one], ct);
                return;
            }
            var bad = chunk[0];
            bad.Attempts++;
            bad.SyncState = SyncState.Failed;
            bad.LastError = $"{Strings.ErrRejected}: {ex.Message}";
            await store.UpdateEventAsync(bad);
            return;
        }

        var ok = res.Accepted.Concat(res.Duplicate).ToHashSet();
        foreach (var e in chunk)
        {
            e.Attempts++;
            if (ok.Contains(e.Id))
            {
                e.SyncState = SyncState.Sent;
                e.LastError = null;
            }
            else
            {
                e.SyncState = SyncState.Failed;
                e.LastError = Strings.ErrRejected;
            }
        }
        await store.UpdateEventsAsync(chunk);
    }

    /// <summary>4xx (401/408/429 dan tashqari) — qayta urinish foyda bermaydi.</summary>
    private static bool IsPermanent(ApiException ex) => ex.StatusCode is { } c
        && (int)c is >= 400 and < 500
        && c is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests);

    private async Task<SyncResult> MarkRevokedAsync(DeviceSettings s, string reason)
    {
        // Token o'chadi, qurilma Bog'lash ekraniga qaytadi; lokal Pending voqealar SAQLANADI.
        Diag.Log($"sync: qurilma uzildi — {reason}");
        await tokens.ClearAsync();
        s.Revoked = true;
        s.LastError = Strings.DeviceRevoked;
        await store.SaveSettingsAsync(s);
        return new SyncResult(SyncOutcome.Revoked, Strings.DeviceRevoked);
    }
}
