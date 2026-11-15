using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Oylik.Contracts;

namespace Oylik.Server.Tests;

public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public void Advance(TimeSpan by) => Now += by;
    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("UZ", TimeSpan.FromHours(5), "UZ", "UZ");
}

/// <summary>
/// Har test uchun alohida server va SQLite fayli (Oylik:Database=sqlite — EnsureCreated, migratsiyasiz).
/// <paramref name="postgres"/> berilsa — o'sha PostgreSQL'da migratsiyalar bilan (opt-in, <see cref="PostgresTests"/>).
/// </summary>
public sealed class TestServer(string? postgres = null) : WebApplicationFactory<Program>
{
    public const string RegistrationKey = "test-registration-key";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oylik-server-tests-" + Guid.NewGuid().ToString("N"));

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.FromHours(5)));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (postgres is null)
        {
            Directory.CreateDirectory(_dir);
            builder.UseSetting("Oylik:Database", "sqlite");
            builder.UseSetting("ConnectionStrings:Oylik", $"Data Source={Path.Combine(_dir, "server.db")}");
        }
        else
        {
            builder.UseSetting("Oylik:Database", "postgres");
            builder.UseSetting("ConnectionStrings:Oylik", postgres);
        }
        builder.UseSetting("Oylik:RegistrationKey", RegistrationKey);
        builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---------- yordamchilar ----------

    public async Task<(HttpClient Desktop, Guid InstallationId)> RegisterAsync(string name = "Korxona")
    {
        var id = Guid.NewGuid();
        var res = await CreateClient().PostAsJsonAsync(Api.Routes.Installations, new RegisterInstallationRequest(name, RegistrationKey, id), Api.Json);
        res.EnsureSuccessStatusCode();
        var body = (await res.Content.ReadFromJsonAsync<RegisterInstallationResponse>(Api.Json))!;
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(Api.ApiKeyHeader, body.ApiKey);
        return (client, body.InstallationId);
    }

    public async Task<string> PairingCodeAsync(HttpClient desktop, Guid installationId)
    {
        var res = await desktop.PostAsync(Api.Routes.PairingCodes(installationId), null);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PairingCodeResponse>(Api.Json))!.Code;
    }

    public async Task<(HttpClient Phone, Guid DeviceId)> PairAsync(HttpClient desktop, Guid installationId, string name = "Qorovul-1")
    {
        var code = await PairingCodeAsync(desktop, installationId);
        var res = await CreateClient().PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(installationId, code, name), Api.Json);
        res.EnsureSuccessStatusCode();
        var body = (await res.Content.ReadFromJsonAsync<PairDeviceResponse>(Api.Json))!;
        return (Bearer(body.DeviceToken), body.DeviceId);
    }

    public HttpClient Bearer(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task PutEmployeesAsync(HttpClient desktop, params EmployeeDto[] employees) =>
        (await desktop.PutAsJsonAsync(Api.Routes.Employees, employees, Api.Json)).EnsureSuccessStatusCode();

    public static EmployeeDto Employee(string name, Guid? id = null, bool active = true) =>
        new(id ?? Guid.NewGuid(), name, null, active, DateTimeOffset.UtcNow);
}
