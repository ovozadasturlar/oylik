using System.Net.Http.Json;
using Oylik.Contracts;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;
using Oylik.Mobile.Logic.Sync;

namespace Oylik.Mobile.Tests;

/// <summary>
/// Haqiqiy server (Oylik.Server) bilan: OYLIK_TEST_SERVER (default http://localhost:5000) javob bermasa yoki
/// ro'yxat kaliti topilmasa — o'tkazib yuboriladi. Kalit: OYLIK_REGISTRATION_KEY yoki src/Oylik.Server/.env (faqat o'qiladi, hech qayerga yozilmaydi).
/// </summary>
public sealed class LiveServerFactAttribute : FactAttribute
{
    public LiveServerFactAttribute()
    {
        if (LiveServer.Url is null) Skip = "Server ishlamayapti (" + LiveServer.Candidate + ")";
        else if (LiveServer.RegistrationKey is null) Skip = "Ro'yxat kaliti topilmadi";
    }
}

internal static class LiveServer
{
    public static readonly string Candidate =
        Environment.GetEnvironmentVariable("OYLIK_TEST_SERVER") is { Length: > 0 } u ? u.TrimEnd('/') : "http://localhost:5000";

    private static readonly Lazy<string?> _url = new(() =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return http.GetAsync(Candidate + Api.Routes.Health).GetAwaiter().GetResult().IsSuccessStatusCode ? Candidate : null;
        }
        catch { return null; }
    });

    private static readonly Lazy<string?> _key = new(() =>
    {
        if (Environment.GetEnvironmentVariable("OYLIK_REGISTRATION_KEY") is { Length: > 0 } k) return k;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var env = Path.Combine(dir.FullName, "src", "Oylik.Server", ".env");
            if (!File.Exists(env)) continue;
            foreach (var line in File.ReadLines(env))
                if (line.StartsWith("OYLIK_REGISTRATION_KEY=", StringComparison.Ordinal))
                    return line["OYLIK_REGISTRATION_KEY=".Length..].Trim().Trim('"');
        }
        return null;
    });

    public static string? Url => _url.Value;
    public static string? RegistrationKey => _key.Value;
}

public sealed class ServerIntegrationTests : IAsyncLifetime
{
    private readonly TempDb _db = new();
    private readonly HttpClient _desktop = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly MemoryTokenStore _tokens = new();
    private readonly TimeProvider _clock = new LocalClock();
    private SyncEngine _sync = null!;
    private AttendanceService _att = null!;
    private PairingService _pairing = null!;
    private Guid _installationId;
    private readonly Guid _employeeId = Guid.NewGuid();

    private string Server => LiveServer.Url!;

    /// <summary>Haqiqiy vaqt, Toshkent zonasi.</summary>
    private sealed class LocalClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TestClock.Tashkent;
    }

    public async Task InitializeAsync()
    {
        if (LiveServer.Url is null || LiveServer.RegistrationKey is null) return;

        // Desktop rolini o'ynaymiz: o'rnatma, xodim, bog'lash kodi.
        var reg = await PostJson<RegisterInstallationResponse>(Api.Routes.Installations,
            new RegisterInstallationRequest($"TEST oy-apk {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}", LiveServer.RegistrationKey, null));
        _installationId = reg.InstallationId;
        _desktop.DefaultRequestHeaders.Add(Api.ApiKeyHeader, reg.ApiKey);

        var put = await _desktop.PutAsJsonAsync(Server + Api.Routes.Employees,
            new[] { new EmployeeDto(_employeeId, "Тест Қоровул", null, true, DateTimeOffset.UtcNow) }, Api.Json);
        put.EnsureSuccessStatusCode();

        var api = new HttpOylikApi(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        _sync = new SyncEngine(_db.Store, api, _tokens, _clock);
        _att = new AttendanceService(_db.Store, _clock);
        _pairing = new PairingService(_db.Store, api, _tokens, _sync);
    }

    public async Task DisposeAsync()
    {
        // Test yaratgan qurilmalar serverda faol qolmasin.
        if (_desktop.DefaultRequestHeaders.Contains(Api.ApiKeyHeader))
        {
            try
            {
                var devices = await _desktop.GetFromJsonAsync<List<DeviceDto>>(Server + Api.Routes.Devices, Api.Json) ?? [];
                foreach (var d in devices.Where(d => d.IsActive))
                    await _desktop.PostAsync(Server + Api.Routes.DeactivateDevice(d.Id), null);
            }
            catch { /* tozalash — muhim emas */ }
        }
        await _db.DisposeAsync();
    }

    private async Task<T> PostJson<T>(string path, object? body)
    {
        var res = await _desktop.PostAsJsonAsync(Server + path, body, Api.Json);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>(Api.Json))!;
    }

    private async Task<string> NewCode() =>
        (await PostJson<PairingCodeResponse>(Api.Routes.PairingCodes(_installationId), null)).Code;

    private async Task<EventsPageResponse> DesktopEvents() =>
        (await _desktop.GetFromJsonAsync<EventsPageResponse>(Server + Api.Routes.Events + "?sinceSeq=0&limit=500", Api.Json))!;

    [LiveServerFact]
    public async Task Pair_by_qr_payload_sync_tap_undo_and_revoke()
    {
        // Bog'lash (QR'dagi kabi InstallationId bilan).
        (await _pairing.PairAsync(Server, _installationId, await NewCode(), "Qorovul-test")).Should().BeNull();
        var s = await _db.Store.GetSettingsAsync();
        s.InstallationId.Should().Be(_installationId);
        s.ClockWrong.Should().BeFalse();
        (await _db.Store.GetEmployeesAsync()).Should().ContainSingle(e => e.Id == _employeeId);

        // Keldi → serverga yetadi, desktop ko'radi (DeviceId — tokendagi qurilma).
        var tap = await _att.RecordAsync(_employeeId, AttendanceType.In);
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        var page = await DesktopEvents();
        page.Events.Should().ContainSingle(e => e.Id == tap.Recorded!.Id).Which.DeviceId.Should().Be(s.DeviceId);

        // Takror jo'natish zararsiz (idempotent).
        var again = await _db.Store.FindEventAsync(tap.Recorded!.Id);
        again!.SyncState = SyncState.Pending;
        await _db.Store.UpdateEventAsync(again);
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await DesktopEvents()).Events.Count(e => e.Id == tap.Recorded.Id).Should().Be(1);

        // BEKOR → serverda IsVoided.
        await _att.UndoAsync(tap.Recorded.Id);
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await DesktopEvents()).Events.Single(e => e.Id == tap.Recorded.Id).IsVoided.Should().BeTrue();
        (await _att.GetPresenceAsync([_employeeId]))[_employeeId].Kind.Should().Be(PresenceKind.None);

        // Noma'lum xodim → Failed, qolganlar jo'natiladi.
        var good = await _att.RecordAsync(_employeeId, AttendanceType.In);
        var bad = await _att.RecordAsync(Guid.NewGuid(), AttendanceType.In);
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
        (await _db.Store.FindEventAsync(good.Recorded!.Id))!.SyncState.Should().Be(SyncState.Sent);
        (await _db.Store.FindEventAsync(bad.Recorded!.Id))!.SyncState.Should().Be(SyncState.Failed);

        // Desktop qurilmani uzadi → 401 → Revoked, Pending saqlanadi.
        var pending = await _att.RecordAsync(_employeeId, AttendanceType.Out);
        (await _desktop.PostAsync(Server + Api.Routes.DeactivateDevice(s.DeviceId), null)).EnsureSuccessStatusCode();
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Revoked);
        (await _db.Store.FindEventAsync(pending.Recorded!.Id))!.SyncState.Should().Be(SyncState.Pending);

        // Qo'lda qayta bog'lash (InstallationId = null) → Pending jo'natiladi.
        (await _pairing.PairAsync(Server, null, await NewCode(), "Qorovul-test")).Should().BeNull();
        (await _db.Store.GetSettingsAsync()).InstallationId.Should().Be(_installationId);
        (await _db.Store.FindEventAsync(pending.Recorded.Id))!.SyncState.Should().Be(SyncState.Sent);
        (await DesktopEvents()).Events.Should().Contain(e => e.Id == pending.Recorded.Id);
    }

    [LiveServerFact]
    public async Task Wrong_code_is_rejected_with_message()
    {
        var err = await _pairing.PairAsync(Server, null, "000000", "Qorovul-test");
        err.Should().BeOneOf(Strings.ErrCodeRejected, Strings.ErrTooManyAttempts);
    }
}
