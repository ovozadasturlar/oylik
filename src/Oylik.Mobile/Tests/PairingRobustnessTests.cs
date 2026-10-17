using Oylik.Contracts;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Sync;
using static Oylik.Mobile.Tests.TestClock;

namespace Oylik.Mobile.Tests;

/// <summary>
/// Haqiqiy telefondagi xato (v3): kod serverda ishlatilgan, telefon esa bog'lash ekranida "Kod noto'g'ri".
/// Talab: PairDevice bir marta; bir xil manzilga ikki marta urinilmaydi; token olingach bog'lash bekor qilinmaydi;
/// sinxron bog'lash natijasini eski nusxa bilan ustidan yozmaydi.
/// </summary>
public sealed class PairingRobustnessTests : IAsyncLifetime
{
    private const string Local = "http://192.168.1.19:5000";
    private const string Global = "https://oylik.example.uz";
    private static readonly Guid Inst = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly TempDb _db = new();
    private readonly TestClock _clock = new(At(2026, 10, 13, 10, 54));
    private readonly MemoryTokenStore _tokens = new();
    private FakeOylikApi _server = null!;
    private SyncEngine _sync = null!;
    private PairingService _pairing = null!;

    private LocalStore Store => _db.Store;

    public Task InitializeAsync()
    {
        _server = new FakeOylikApi(_clock) { SingleUseCodes = true }.SeedDemo();
        _sync = new SyncEngine(Store, _server, _tokens, _clock) { LocalProbeTimeout = TimeSpan.FromMilliseconds(300) };
        _pairing = new PairingService(Store, _server, _tokens, _sync);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private int PairCalls => _server.Log.Count(l => l.EndsWith(" pair", StringComparison.Ordinal));

    private Task<string?> PairQr(string server, string? local, string code = "627085", bool syncNow = true) =>
        _pairing.PairAsync(server, Inst, code, "Qorovul-1", localServerInput: local, syncNow: syncNow);

    // ---------- Bir marta ----------

    [Fact]
    public async Task Qr_with_only_local_server_sends_pair_exactly_once()
    {
        // Haqiqiy holat: desktop faqat lokal sozlagan → Server = lokal, LocalServer = null.
        (await PairQr(Local, null)).Should().BeNull();
        PairCalls.Should().Be(1);
        (await Store.GetSettingsAsync()).IsPaired.Should().BeTrue();
    }

    [Theory]
    [InlineData(Local)]
    [InlineData(Local + "/")]
    [InlineData("HTTP://192.168.1.19:5000")]
    [InlineData("192.168.1.19:5000")]
    public async Task Same_address_in_server_and_local_is_tried_once(string localSpelling)
    {
        (await PairQr(Local, localSpelling, syncNow: false)).Should().BeNull();
        _server.Log.Should().Equal($"{Local} pair"); // lokal tekshiruvi ham, ikkinchi urinish ham yo'q
        (await Store.GetSettingsAsync()).LocalServerUrl.Should().BeNull();
    }

    [Fact]
    public async Task Local_answers_health_but_pair_fails_on_network_global_is_not_tried()
    {
        // Lokal health javob berdi → kod lokalga yuborildi → javob yo'qoldi. Kod ishlatilgan bo'lishi mumkin — globalga qayta yubormaymiz.
        var flaky = new FlakyPairApi(_server);
        var pairing = new PairingService(Store, flaky, _tokens, _sync);
        var err = await pairing.PairAsync(Global, Inst, "627085", "Qorovul-1", localServerInput: Local);
        err.Should().Be(Strings.ErrNoNetwork);
        flaky.PairCalls.Should().Equal(Local);
    }

    [Fact]
    public async Task Concurrent_pair_attempts_send_code_once()
    {
        var (arrived, release) = _server.Hold(Local);
        var first = PairQr(Local, null);
        await arrived.WaitAsync(TimeSpan.FromSeconds(10)); // birinchisi PairDevice ichida
        var second = await PairQr(Local, null); // skaner ikkinchi marta / tugma ikki marta
        second.Should().Be(Strings.ErrPairInProgress);
        release();
        (await first).Should().BeNull();
        PairCalls.Should().Be(1);
        (await Store.GetSettingsAsync()).IsPaired.Should().BeTrue();
    }

    [Fact]
    public async Task Scanning_same_qr_again_after_success_does_not_reuse_code()
    {
        (await PairQr(Local, null)).Should().BeNull();
        (await PairQr(Local, null)).Should().BeNull("allaqachon shu korxonaga bog'langan — xato emas");
        PairCalls.Should().Be(1);
        (await Store.GetSettingsAsync()).IsPaired.Should().BeTrue();
    }

    [Fact]
    public async Task Reusing_code_after_unpair_gets_rejected_and_says_so()
    {
        (await PairQr(Local, null)).Should().BeNull();
        await _pairing.UnpairAsync();
        (await PairQr(Local, null)).Should().Be(Strings.ErrCodeRejected);
        PairCalls.Should().Be(2);
    }

    // ---------- Token olingach bekor qilinmaydi ----------

    [Fact]
    public async Task Unexpected_error_in_first_sync_keeps_pairing()
    {
        _server.HealthThrows = new InvalidOperationException("kutilmagan");
        (await PairQr(Local, null)).Should().BeNull();
        var s = await Store.GetSettingsAsync();
        s.IsPaired.Should().BeTrue();
        (await _tokens.GetAsync()).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Network_failure_right_after_pair_keeps_pairing_and_uses_backoff()
    {
        var api = new OfflineAfterPairApi(_server, Local);
        var pairing = new PairingService(Store, api, _tokens, _sync);
        var sync = new SyncEngine(Store, api, _tokens, _clock);
        (await pairing.PairAsync(Local, Inst, "627085", "Qorovul-1", syncNow: false)).Should().BeNull();

        (await sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Failed);
        var s = await Store.GetSettingsAsync();
        s.IsPaired.Should().BeTrue();
        s.Revoked.Should().BeFalse();
        s.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task SyncNow_false_returns_without_waiting_for_employees()
    {
        (await PairQr(Local, null, syncNow: false)).Should().BeNull();
        _server.Log.Should().ContainSingle().Which.Should().EndWith(" pair");
        (await Store.GetSettingsAsync()).IsPaired.Should().BeTrue();
    }

    [Fact]
    public async Task Running_sync_does_not_overwrite_new_pairing_with_stale_settings()
    {
        // Eski bog'lanish (A, global) bo'yicha sinxron server javobini kutyapti; shu payt yangi bog'lash (B, lokal) javob oldi.
        // Aniq tartib: 1) sinxron A sozlamalarini o'qib, health'da turibdi; 2) B tokeni olindi va saqlashni kutyapti;
        // 3) sinxron davom etib A nusxasini saqlaydi; 4) B saqlanadi. Natija — B.
        (await _pairing.PairAsync(Global, null, "111111", "Qorovul-1")).Should().BeNull();
        var (syncArrived, releaseSync) = _server.Hold(Global);

        var running = _sync.SyncAsync(force: true);
        await syncArrived.WaitAsync(TimeSpan.FromSeconds(10));        // 1

        var pair = PairQr(Local, null, syncNow: false);
        await WaitUntil(() => _sync.ExclusiveWaiters == 1 || pair.IsCompleted); // 2
        pair.IsCompleted.Should().BeFalse("bog'lash sinxron tugashini kutishi kerak");

        releaseSync();                                                // 3
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Outcome.Should().Be(SyncOutcome.Ok);
        (await pair.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeNull(); // 4

        var s = await Store.GetSettingsAsync();
        s.ServerUrl.Should().Be(Local);
        s.InstallationId.Should().Be(Inst);
        s.IsPaired.Should().BeTrue();
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok, "yangi token bilan ishlaydi");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("shart bajarilmadi");
            await Task.Delay(5);
        }
    }

    // ---------- Xato matnlari ----------

    [Theory]
    [InlineData("409409", Strings.ErrCodeUsed)]
    [InlineData("410410", Strings.ErrCodeExpired)]
    [InlineData("000000", Strings.ErrCodeRejected)]
    [InlineData("429429", Strings.ErrTooManyAttempts)]
    public async Task Pair_errors_are_distinguished(string code, string expected)
    {
        (await PairQr(Local, null, code)).Should().Be(expected);
        (await Store.GetSettingsAsync()).IsPaired.Should().BeFalse();
    }

    [Fact]
    public async Task Bad_manual_local_address_is_reported()
    {
        (await _pairing.PairAsync(Global, null, "627085", "Qorovul-1", localServerInput: "bu manzil emas")).Should().Be(Strings.ErrBadLocalServer);
        PairCalls.Should().Be(0);
    }

    [Fact]
    public async Task Manual_pair_with_optional_local_saves_both()
    {
        (await _pairing.PairAsync(Global, null, "627085", "Qorovul-1", localServerInput: "192.168.1.19:5000")).Should().BeNull();
        var s = await Store.GetSettingsAsync();
        s.ServerUrl.Should().Be(Global);
        s.LocalServerUrl.Should().Be(Local);
        s.ActiveServerUrl.Should().Be(Local);
    }

    // ---------- Manzillarni o'zgartirish ----------

    private async Task PairedOnLocalOnly() => (await PairQr(Local, null)).Should().BeNull();

    [Fact]
    public async Task Addresses_can_be_changed_after_check_without_repairing()
    {
        await PairedOnLocalOnly();
        var device = (await Store.GetSettingsAsync()).DeviceId;

        (await _pairing.UpdateAddressesAsync(Global, Local)).Should().BeNull();

        var s = await Store.GetSettingsAsync();
        s.ServerUrl.Should().Be(Global);
        s.LocalServerUrl.Should().Be(Local);
        s.DeviceId.Should().Be(device);
        _server.Log.Should().Contain($"{Global} pull").And.Contain($"{Local} pull");
        PairCalls.Should().Be(1, "qayta bog'lanmadi");
        (await _sync.SyncAsync(force: true)).Outcome.Should().Be(SyncOutcome.Ok);
    }

    [Fact]
    public async Task Unreachable_new_address_is_not_saved()
    {
        await PairedOnLocalOnly();
        _server.Unreachable.Add(Global);
        var err = await _pairing.UpdateAddressesAsync(Global, Local);
        err.Should().Be($"{Strings.AddrServer}: {Strings.ErrAddrUnreachable}");
        var s = await Store.GetSettingsAsync();
        s.ServerUrl.Should().Be(Local);
        s.LocalServerUrl.Should().BeNull();
    }

    [Fact]
    public async Task Address_of_another_server_is_not_saved()
    {
        await PairedOnLocalOnly();
        _server.Foreign.Add("http://192.168.1.50:5000");
        var err = await _pairing.UpdateAddressesAsync(Global, "192.168.1.50:5000");
        err.Should().Be($"{Strings.AddrLocal}: {Strings.ErrAddrOtherServer}");
        (await Store.GetSettingsAsync()).ServerUrl.Should().Be(Local);
    }

    [Fact]
    public async Task Clearing_local_address_keeps_single_url()
    {
        (await _pairing.PairAsync(Global, Inst, "627085", "Qorovul-1", localServerInput: Local)).Should().BeNull();
        (await _pairing.UpdateAddressesAsync(Global, "")).Should().BeNull();
        var s = await Store.GetSettingsAsync();
        s.LocalServerUrl.Should().BeNull();
        s.HasLocalServer.Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_address_input_is_rejected_before_any_request()
    {
        await PairedOnLocalOnly();
        _server.Log.Clear();
        (await _pairing.UpdateAddressesAsync("", Local)).Should().Be(Strings.ErrBadServer);
        (await _pairing.UpdateAddressesAsync(Global, "x")).Should().Be(Strings.ErrBadLocalServer);
        _server.Log.Should().BeEmpty();
    }

    /// <summary>PairDevice lokalga yetib boradi, lekin javob yo'qoladi (tarmoq uzildi).</summary>
    private sealed class FlakyPairApi(FakeOylikApi inner) : IOylikApi
    {
        public List<string> PairCalls { get; } = [];
        public Task<HealthResponse> HealthAsync(string server, CancellationToken ct = default) => inner.HealthAsync(server, ct);
        public async Task<PairDeviceResponse> PairAsync(string server, PairDeviceRequest request, CancellationToken ct = default)
        {
            PairCalls.Add(server);
            await inner.PairAsync(server, request, ct); // server kodni ishlatdi
            throw new ApiException(null, "javob yo'qoldi");
        }
        public Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(ApiSession s, DateTimeOffset? since, CancellationToken ct = default) => inner.GetEmployeesAsync(s, since, ct);
        public Task<PostEventsResponse> PostEventsAsync(ApiSession s, IReadOnlyList<AttendanceEventDto> e, CancellationToken ct = default) => inner.PostEventsAsync(s, e, ct);
        public Task VoidEventAsync(ApiSession s, Guid id, VoidEventRequest r, CancellationToken ct = default) => inner.VoidEventAsync(s, id, r, ct);
        public Task<EventsPageResponse> GetEventsAsync(ApiSession s, long since, int limit, CancellationToken ct = default) => inner.GetEventsAsync(s, since, limit, ct);
    }

    /// <summary>Bog'lash o'tadi, keyin aloqa yo'q.</summary>
    private sealed class OfflineAfterPairApi(FakeOylikApi inner, string server) : IOylikApi
    {
        private bool _paired;
        private Task Gate() => _paired ? Task.FromException(new ApiException(null, "oflayn")) : Task.CompletedTask;
        public async Task<HealthResponse> HealthAsync(string s, CancellationToken ct = default) { await Gate(); return await inner.HealthAsync(s, ct); }
        public async Task<PairDeviceResponse> PairAsync(string s, PairDeviceRequest r, CancellationToken ct = default)
        {
            var res = await inner.PairAsync(server, r, ct);
            _paired = true;
            return res;
        }
        public async Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(ApiSession s, DateTimeOffset? since, CancellationToken ct = default) { await Gate(); return await inner.GetEmployeesAsync(s, since, ct); }
        public async Task<PostEventsResponse> PostEventsAsync(ApiSession s, IReadOnlyList<AttendanceEventDto> e, CancellationToken ct = default) { await Gate(); return await inner.PostEventsAsync(s, e, ct); }
        public async Task VoidEventAsync(ApiSession s, Guid id, VoidEventRequest r, CancellationToken ct = default) { await Gate(); await inner.VoidEventAsync(s, id, r, ct); }
        public async Task<EventsPageResponse> GetEventsAsync(ApiSession s, long since, int limit, CancellationToken ct = default) { await Gate(); return await inner.GetEventsAsync(s, since, limit, ct); }
    }
}
