using System.Net.Http.Json;
using Oylik.Contracts;
using Oylik.Data.Sync;

namespace Oylik.Server.Tests;

/// <summary>6.1: ikki manzil — avval lokal (2 s), javob bo'lmasa global; globalda har 5 daqiqada lokal qayta tekshiriladi.</summary>
public sealed class ServerConnectionTests : IAsyncLifetime
{
    private const string Local = "http://192.168.1.19:5000";
    private const string Global = "https://oylik.example.uz";

    private readonly TestServer _server = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.FromHours(5)));
    private SwitchHandler _local = null!;
    private CountingHandler _global = null!;
    private string _apiKey = null!;

    /// <summary>Lokal tarmoq: Down — ulanib bo'lmaydi (Wi-Fi yo'q); aks holda xuddi o'sha server.</summary>
    private sealed class SwitchHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public bool Down { get; set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Down ? throw new HttpRequestException("Lokal server javob bermadi") : base.SendAsync(request, ct);
        }
    }

    private sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return base.SendAsync(request, ct);
        }
    }

    public async Task InitializeAsync()
    {
        _local = new SwitchHandler(_server.Server.CreateHandler());
        _global = new CountingHandler(_server.Server.CreateHandler());
        var res = await _server.CreateClient().PostAsJsonAsync(Api.Routes.Installations,
            new RegisterInstallationRequest("Korxona", TestServer.RegistrationKey, Guid.NewGuid()), Api.Json);
        _apiKey = (await res.Content.ReadFromJsonAsync<RegisterInstallationResponse>(Api.Json))!.ApiKey;
    }

    public Task DisposeAsync()
    {
        _server.Dispose();
        return Task.CompletedTask;
    }

    private ServerConnection Connection(string? local = Local, string? global = Global) =>
        new(local, global, _apiKey, _clock, url => new HttpClient(url == Local ? _local : _global));

    private static async Task<List<DeviceDto>> Devices(ServerClient c) => [.. await c.GetDevicesAsync()];

    [Fact]
    public async Task Local_is_preferred_when_it_answers()
    {
        var connection = Connection();
        await connection.RunAsync(Devices);
        connection.Route.Should().Be(ServerRoute.Local);
        _global.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Falls_back_to_global_when_local_is_down_and_returns_to_local_after_recheck_interval()
    {
        _local.Down = true;
        var connection = Connection();

        await connection.RunAsync(Devices);
        connection.Route.Should().Be(ServerRoute.Global);
        var localCalls = _local.Calls;

        // 5 daqiqa ichida lokal qayta tekshirilmaydi — to'g'ridan-to'g'ri global
        _local.Down = false;
        _clock.Advance(TimeSpan.FromMinutes(4));
        await connection.RunAsync(Devices);
        connection.Route.Should().Be(ServerRoute.Global);
        _local.Calls.Should().Be(localCalls);

        // interval o'tdi — lokal qaytdi
        _clock.Advance(Api.LocalServerRecheckInterval);
        await connection.RunAsync(Devices);
        connection.Route.Should().Be(ServerRoute.Local);
    }

    [Fact]
    public async Task Local_failing_mid_session_switches_to_global_in_the_same_call()
    {
        var connection = Connection();
        await connection.RunAsync(Devices);
        connection.Route.Should().Be(ServerRoute.Local);

        _local.Down = true;
        var devices = await connection.RunAsync(Devices);
        devices.Should().NotBeNull();
        connection.Route.Should().Be(ServerRoute.Global);
    }

    [Fact]
    public async Task Only_local_address_is_used_alone_and_its_failure_is_reported()
    {
        var connection = Connection(global: null);
        await connection.RunAsync(Devices);
        connection.Route.Should().Be(ServerRoute.Local);

        _local.Down = true;
        var act = () => connection.RunAsync(Devices);
        await act.Should().ThrowAsync<HttpRequestException>();
        connection.Route.Should().Be(ServerRoute.None);
        _global.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Only_global_address_never_probes_local()
    {
        var connection = Connection(local: null);
        await connection.RunAsync(Devices);
        connection.Route.Should().Be(ServerRoute.Global);
        _local.Calls.Should().Be(0);
    }

    [Fact]
    public void At_least_one_address_is_required()
    {
        var act = () => new ServerConnection(" ", null, _apiKey);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("http://192.168.1.19:5000", true)]
    [InlineData("192.168.1.19:5000", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://[::1]:5000", true)]
    [InlineData("https://192.168.1.19", false)]
    [InlineData("https://oylik.example.uz", false)]
    [InlineData("http://oylik.example.uz", false)]
    public void Migration_rule_for_old_single_address(string url, bool local) =>
        ServerConnection.IsLocalAddress(url).Should().Be(local);

    [Fact]
    public void Qr_payload_old_phone_reads_server_only()
    {
        // Ikkalasi bo'lsa: Server — global, LocalServer — lokal; eski ilova faqat Server'ni o'qiydi
        var json = System.Text.Json.JsonSerializer.Serialize(new PairingQrPayload(Global, Guid.NewGuid(), "123456", Local), Api.Json);
        json.Should().Contain("\"server\":\"https://oylik.example.uz\"").And.Contain("\"localServer\":\"http://192.168.1.19:5000\"");
    }
}
