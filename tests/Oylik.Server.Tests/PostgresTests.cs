using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Oylik.Contracts;

namespace Oylik.Server.Tests;

/// <summary>Faqat <c>OYLIK_TEST_PG</c> (PostgreSQL ulanish satri, CREATE DATABASE huquqi bilan) berilganda ishlaydi.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string Variable = "OYLIK_TEST_PG";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"{Variable} berilmagan — PostgreSQL integratsiya testi o'tkazib yuborildi";
    }
}

/// <summary>
/// Prod provayderi: haqiqiy PostgreSQL'da migratsiyalar + asosiy oqim (UtcTicks/OccurredAt konversiyalari, since-filtr).
/// Har test o'z vaqtinchalik bazasini yaratadi va oxirida o'chiradi.
/// </summary>
public sealed class PostgresTests : IAsyncLifetime
{
    private readonly string _database = "oylik_test_" + Guid.NewGuid().ToString("N");
    private TestServer _server = null!;

    private static string Admin => Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable)!;

    public Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFactAttribute.Variable)))
            return Task.CompletedTask;
        _server = new TestServer(new NpgsqlConnectionStringBuilder(Admin) { Database = _database }.ConnectionString);
        return Task.CompletedTask; // baza — server ishga tushganda MigrateAsync yaratadi
    }

    public async Task DisposeAsync()
    {
        if (_server is null)
            return;
        _server.Dispose();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(Admin);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Migrations_apply_and_core_flow_round_trips()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        using (var scope = _server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServerDb>();
            db.Database.IsNpgsql().Should().BeTrue();
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }

        // Xodimlar: UpdatedAt (UTC ticks) bo'yicha since-filtr
        var olim = TestServer.Employee("Олим ака") with { TabelNo = "7", MaxShiftMinutes = 720 };
        await TestServer.PutEmployeesAsync(desktop, olim);
        var all = (await desktop.GetFromJsonAsync<List<EmployeeDto>>(Api.Routes.Employees, Api.Json))!;
        all.Should().ContainSingle().Which.Should().Match<EmployeeDto>(e => e.TabelNo == "7" && e.MaxShiftMinutes == 720);
        var stamp = all[0].UpdatedAt;
        var since = Uri.EscapeDataString(stamp.ToString("O"));
        (await desktop.GetFromJsonAsync<List<EmployeeDto>>($"{Api.Routes.Employees}?updatedSince={since}", Api.Json))!
            .Should().ContainSingle("kursor shu jumladan");
        var later = Uri.EscapeDataString(stamp.AddTicks(1).ToString("O"));
        (await desktop.GetFromJsonAsync<List<EmployeeDto>>($"{Api.Routes.Employees}?updatedSince={later}", Api.Json))!
            .Should().BeEmpty();

        // Voqea: OccurredAt offset (+05:00) saqlanadi, Seq ketma-ket
        var occurred = new DateTimeOffset(2026, 10, 12, 8, 3, 0, TimeSpan.FromHours(5));
        var evt = new AttendanceEventDto(Guid.NewGuid(), olim.Id, AttendanceType.In, occurred, null);
        (await desktop.PostAsJsonAsync(Api.Routes.Events, new[] { evt }, Api.Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await desktop.GetFromJsonAsync<EventsPageResponse>($"{Api.Routes.Events}?sinceSeq=0", Api.Json))!;
        var stored = page.Events.Should().ContainSingle().Subject;
        stored.OccurredAt.Should().Be(occurred);
        stored.OccurredAt.Offset.Should().Be(TimeSpan.FromHours(5));

        // Ikkinchi zavod ulanishi (ApiKeys, JoinCodes — identity ustunlari)
        var code = (await (await desktop.PostAsync(Api.Routes.JoinCodes(installationId), null))
            .Content.ReadFromJsonAsync<JoinCodeResponse>(Api.Json))!.Code;
        var join = await _server.CreateClient().PostAsJsonAsync(Api.Routes.JoinInstallation,
            new JoinInstallationRequest(installationId, code, "2-zavod"), Api.Json);
        join.StatusCode.Should().Be(HttpStatusCode.OK, await join.Content.ReadAsStringAsync());
        (await join.Content.ReadFromJsonAsync<JoinInstallationResponse>(Api.Json))!.TabelBase.Should().Be(1_000_001);
    }
}
