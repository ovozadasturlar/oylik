using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oylik.Contracts;

namespace Oylik.Server.Tests;

/// <summary>Bosqich 3: ikkinchi mashina (zavod) mavjud o'rnatmaga ulanish kodi bilan ulanadi va o'z ApiKey'ini oladi.</summary>
public sealed class JoinTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>(Api.Json))!;
    }

    private async Task<string> JoinCodeAsync(HttpClient desktop, Guid installationId) =>
        (await Read<JoinCodeResponse>(await desktop.PostAsync(Api.Routes.JoinCodes(installationId), null))).Code;

    private Task<HttpResponseMessage> JoinAsync(Guid installationId, string code, string name = "2-zavod") =>
        _server.CreateClient().PostAsJsonAsync(Api.Routes.JoinInstallation, new JoinInstallationRequest(installationId, code, name), Api.Json);

    private HttpClient Desktop(string apiKey)
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add(Api.ApiKeyHeader, apiKey);
        return client;
    }

    [Fact]
    public async Task Joined_machine_gets_its_own_key_and_a_monotonic_tabel_range()
    {
        var (main, installationId) = await _server.RegisterAsync();

        var code = await JoinCodeAsync(main, installationId);
        code.Should().MatchRegex("^[0-9]{6}$");
        var second = await Read<JoinInstallationResponse>(await JoinAsync(installationId, code));
        second.InstallationId.Should().Be(installationId);
        second.TabelBase.Should().Be(1_000_001);

        var third = await Read<JoinInstallationResponse>(await JoinAsync(installationId, await JoinCodeAsync(main, installationId), "3-zavod"));
        third.TabelBase.Should().Be(2_000_001);
        third.ApiKey.Should().NotBe(second.ApiKey);

        // Har kalit o'sha o'rnatmaga ishlaydi: 2-zavod yozgan xodimni asosiy mashina ko'radi (upsert-only — o'chmaydi).
        var olim = TestServer.Employee("Олим ака") with { TabelNo = "1000001" };
        await TestServer.PutEmployeesAsync(Desktop(second.ApiKey), olim);
        await TestServer.PutEmployeesAsync(main, TestServer.Employee("Санжар") with { TabelNo = "1" });
        var list = await Read<List<EmployeeDto>>(await Desktop(third.ApiKey).GetAsync(Api.Routes.Employees));
        list.Select(e => (e.FullName, e.TabelNo, e.IsActive)).Should().BeEquivalentTo([("Олим ака", "1000001", true), ("Санжар", "1", true)]);

        // 3-zavod kalitini o'chirsak ham sanagich kamaymaydi — keyingi ulanish yangi diapazon oladi.
        using (var scope = _server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDb>();
            db.ApiKeys.Remove(db.ApiKeys.Single(k => k.SiteIndex == 2));
            await db.SaveChangesAsync();
        }
        (await Desktop(third.ApiKey).GetAsync(Api.Routes.Employees)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var fourth = await Read<JoinInstallationResponse>(await JoinAsync(installationId, await JoinCodeAsync(main, installationId), "4-zavod"));
        fourth.TabelBase.Should().Be(3_000_001);
    }

    [Fact]
    public async Task Join_code_is_single_use_expires_and_is_bound_to_its_installation()
    {
        var (main, installationId) = await _server.RegisterAsync();
        var (_, otherId) = await _server.RegisterAsync("Boshqa");

        var code = await JoinCodeAsync(main, installationId);
        (await JoinAsync(installationId, code, name: " ")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "nom bo'sh");
        (await JoinAsync(otherId, code)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "kod boshqa o'rnatmaniki");
                (await JoinAsync(installationId, code)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JoinAsync(installationId, code)).StatusCode.Should().Be(HttpStatusCode.Conflict, "bir martalik");

        var stale = await JoinCodeAsync(main, installationId);
        _server.Clock.Advance(Api.PairingCodeLifetime + TimeSpan.FromSeconds(1));
        (await JoinAsync(installationId, stale)).StatusCode.Should().Be(HttpStatusCode.Gone);

    }

    [Fact]
    public async Task Phone_pairing_codes_and_join_codes_are_not_interchangeable()
    {
        var (main, installationId) = await _server.RegisterAsync();

        // Telefon kodi bilan desktop ApiKey olib bo'lmaydi.
        var phoneCode = await _server.PairingCodeAsync(main, installationId);
        (await JoinAsync(installationId, phoneCode)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Ulanish kodi bilan telefon bog'lab bo'lmaydi.
        var joinCode = await JoinCodeAsync(main, installationId);
        (await _server.CreateClient().PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(installationId, joinCode, "Qorovul-1"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_the_installations_desktop_can_create_join_codes()
    {
        var (main, installationId) = await _server.RegisterAsync();
        var (other, otherId) = await _server.RegisterAsync("Boshqa");
        var (phone, _) = await _server.PairAsync(main, installationId);

        (await _server.CreateClient().PostAsync(Api.Routes.JoinCodes(installationId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await phone.PostAsync(Api.Routes.JoinCodes(installationId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await other.PostAsync(Api.Routes.JoinCodes(installationId), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await main.PostAsync(Api.Routes.JoinCodes(otherId), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Join_attempts_are_rate_limited()
    {
        var (_, installationId) = await _server.RegisterAsync();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
            statuses.Add((await JoinAsync(installationId, $"{i:000000}")).StatusCode);
        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }
}
