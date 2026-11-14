using System.Text;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;

namespace Oylik.Core.Tests;

/// <summary>Bosqich 1: rol PINlari — faqat xesh, qayta ishga tushirishdan keyin ham tekshiriladi, ochiq matn bazada yo'q.</summary>
public sealed class RolePinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-pin-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = TestClock.Default();
    private AppPaths Paths => new(_root);

    public RolePinTests() => Paths.EnsureCreated();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<PayrollService> OpenAsync()
    {
        var service = new PayrollService(Paths, _clock);
        await service.InitializeAsync(dailyBackup: false);
        return service;
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("١٢٣٤", false)] // faqat ASCII raqamlar
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Pin_format(string? pin, bool valid) => PinHasher.IsValid(pin).Should().Be(valid);

    [Fact]
    public void Hash_is_salted_and_verifies_only_the_same_pin()
    {
        var a = PinHasher.Create("2580", iterations: 1_000);
        var b = PinHasher.Create("2580", iterations: 1_000);
        a.Salt.Should().NotEqual(b.Salt);
        a.Hash.Should().NotEqual(b.Hash); // bir xil PIN — har xil xesh
        var stored = new RolePin { Salt = a.Salt, Hash = a.Hash, Iterations = a.Iterations };
        PinHasher.Verify("2580", stored).Should().BeTrue();
        PinHasher.Verify("2581", stored).Should().BeFalse();
        PinHasher.Verify("", stored).Should().BeFalse();
        PinHasher.Verify(null, stored).Should().BeFalse();
        FluentActions.Invoking(() => PinHasher.Create("12")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Pins_survive_restart_and_are_not_stored_in_plain_text()
    {
        using (var service = await OpenAsync())
        {
            service.LoginRequired.Should().BeFalse();
            await service.SetPinAsync(UserRole.Accountant, "97531");
            service.LoginRequired.Should().BeFalse(); // Admin PINi yo'q — kirish so'ralmaydi
            await service.SetPinAsync(UserRole.Admin, "86420");
            service.LoginRequired.Should().BeTrue();
        }
        SqliteConnection.ClearAllPools();

        using (var service = await OpenAsync())
        {
            service.RolesWithPin.Should().Equal(UserRole.Admin, UserRole.Accountant);
            service.VerifyPin(UserRole.Admin, "86420").Should().BeTrue();
            service.VerifyPin(UserRole.Admin, "97531").Should().BeFalse();
            (await service.VerifyPinAsync(UserRole.Accountant, "97531")).Should().BeTrue();
            service.VerifyPin(UserRole.Guard, "97531").Should().BeFalse(); // PINsiz rol bilan kirib bo'lmaydi

            var audit = await service.GetAuditAsync();
            audit.Where(a => a.Field == "RolePinSet").Should().HaveCount(2);
            audit.Should().NotContain(a => (a.OldValue ?? "").Contains("86420") || (a.NewValue ?? "").Contains("86420"));

            await service.ClearPinAsync(UserRole.Admin);
            service.LoginRequired.Should().BeFalse();
            service.HasPin(UserRole.Accountant).Should().BeTrue();
        }
        SqliteConnection.ClearAllPools();

        // Baza faylida (WAL bilan birga) PIN ochiq matnda yo'q.
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
        {
            var bytes = await File.ReadAllBytesAsync(file);
            Encoding.ASCII.GetString(bytes).Should().NotContain("97531", file);
            Encoding.Unicode.GetString(bytes).Should().NotContain("97531", file);
        }
    }
}
