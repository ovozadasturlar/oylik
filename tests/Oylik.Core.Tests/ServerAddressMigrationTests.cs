using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Oylik.Data;

namespace Oylik.Core.Tests;

/// <summary>6.1 migratsiya: eski bitta ServerUrl — http:// va IP/localhost bo'lsa lokal, aks holda global.</summary>
public sealed class ServerAddressMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oylik-migration-tests-" + Guid.NewGuid().ToString("N"));

    public ServerAddressMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("http://192.168.1.10:5000", "http://192.168.1.10:5000", null)]
    [InlineData("http://localhost:5000", "http://localhost:5000", null)]
    [InlineData("https://oylik.example.uz", null, "https://oylik.example.uz")]
    [InlineData("http://oylik.example.uz", null, "http://oylik.example.uz")]
    [InlineData(null, null, null)]
    public async Task Old_single_address_is_split_into_local_or_global(string? old, string? local, string? global)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");
        await using (var db = new OylikDbContext(OylikDbContext.CreateOptions(path)))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync("Branches");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Settings (Id, CompanyName, DaysInMonth, HoursPerDay, ShortInputEnabled, InstallationId, ServerUrl, ApiKey, LastEventSeq, Language, Theme) " +
                "VALUES (1, '', 30, 8, 1, @id, @url, 'k', 0, 'uz-Latn', 0)",
                new SqliteParameter("@id", Guid.NewGuid()), new SqliteParameter("@url", (object?)old ?? DBNull.Value));
            await migrator.MigrateAsync();
        }
        SqliteConnection.ClearAllPools();

        await using var check = new OylikDbContext(OylikDbContext.CreateOptions(path));
        var settings = await check.Settings.SingleAsync();
        settings.LocalServerUrl.Should().Be(local);
        settings.ServerUrl.Should().Be(global);
        if (old is not null)
            Oylik.Data.Sync.ServerConnection.IsLocalAddress(old).Should().Be(local is not null, "C# qoidasi migratsiya bilan bir xil");
    }
}
