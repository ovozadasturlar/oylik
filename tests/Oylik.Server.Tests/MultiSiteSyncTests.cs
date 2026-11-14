using Microsoft.Data.Sqlite;
using Oylik.Contracts;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Data.Sync;

namespace Oylik.Server.Tests;

/// <summary>
/// Bosqich 3: ikki zavod (ikki desktop bazasi) bitta o'rnatmada — join, tabel diapazoni, xodim push/pull, nofaol qilish,
/// o'chirish izi (tombstone), push↔pull halqasi yo'qligi.
/// </summary>
public sealed class MultiSiteSyncTests : IAsyncLifetime
{
    private readonly TestServer _server = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-multisite-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<PayrollService> _open = [];
    private HttpClient _http = null!;
    private string _url = null!;

    private sealed record Site(PayrollService Service, ServerSync Sync, List<EmployeePullResult> Pulled);

    public Task InitializeAsync()
    {
        _http = _server.CreateClient();
        _url = _http.BaseAddress!.ToString();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        foreach (var s in _open)
            s.Dispose();
        _server.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private async Task<Site> OpenSiteAsync(string name)
    {
        var paths = new AppPaths(Path.Combine(_root, name));
        paths.EnsureCreated();
        var service = new PayrollService(paths, _server.Clock);
        await service.InitializeAsync(dailyBackup: false);
        _open.Add(service);
        var pulled = new List<EmployeePullResult>();
        var sync = new ServerSync(service) { EmployeesPulled = r => { pulled.Add(r); return Task.CompletedTask; } };
        service.EmployeesChanged += (_, _) => sync.EmployeesDirty = true; // SyncService kabi (pull o'zgartirgani ham)
        return new Site(service, sync, pulled);
    }

    private ServerClient Client(Site site) => new(site.Service.Settings.ServerUrl!, site.Service.Settings.ApiKey, _http);

    /// <summary>1-zavod — asosiy (ro'yxatdan o'tadi), 2-zavod — ulanish kodi bilan qo'shiladi.</summary>
    private async Task<(Site Main, Site Second)> TwoSitesAsync()
    {
        var main = await OpenSiteAsync("main");
        var reg = await new ServerClient(_url, null, _http).RegisterAsync("Korxona", TestServer.RegistrationKey, main.Service.Settings.InstallationId);
        await main.Service.SaveServerConnectionAsync(null, _url, reg.ApiKey);

        var second = await OpenSiteAsync("second");
        var code = await Client(main).CreateJoinCodeAsync(main.Service.Settings.InstallationId);
        var join = await new ServerClient(_url, null, _http).JoinAsync(main.Service.Settings.InstallationId, code.Code, "2-zavod");
        await second.Service.SaveJoinedInstallationAsync(null, _url, join.InstallationId, join.ApiKey, join.TabelBase);
        return (main, second);
    }

    private Task SyncAsync(Site site) => site.Sync.RunAsync(Client(site));

    private async Task<List<EmployeeDto>> ServerEmployeesAsync(Site site) => await Client(site).GetEmployeesAsync(null);

    [Fact]
    public async Task Sites_exchange_employees_with_their_own_tabel_ranges_and_settle_without_a_loop()
    {
        var (main, second) = await TwoSitesAsync();
        second.Service.Settings.InstallationId.Should().Be(main.Service.Settings.InstallationId);
        second.Service.Settings.TabelBase.Should().Be(1_000_001);
        await main.Service.CreatePeriodAsync(new MonthKey(2026, 10));
        await second.Service.CreatePeriodAsync(new MonthKey(2026, 10));

        var olim = await main.Service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        var sanjar = await second.Service.CreateEmployeeAsync("Санжар", 5_000_000, null, null);
        olim.TabelNo.Should().Be(1);
        sanjar.TabelNo.Should().Be(1_000_001, "2-zavod o'z diapazonidan beradi");

        await SyncAsync(main);
        await SyncAsync(second); // 2-zavod Санжар'ni jo'natadi, Олим'ni oladi
        await SyncAsync(main);   // 1-zavod Санжар'ni oladi

        foreach (var site in new[] { main, second })
        {
            site.Service.Employees.Select(e => (e.FullName, e.TabelNo, e.IsActive))
                .Should().BeEquivalentTo([("Олим ака", 1, true), ("Санжар", 1_000_001, true)]);
            site.Service.FindPeriod(new MonthKey(2026, 10))!.Lines.Should().HaveCount(2, "pull'dan kelgan xodim ochiq oyga kiradi");
        }
        var pulledOnMain = main.Service.Employees.Single(e => e.Id == sanjar.Id);
        pulledOnMain.MonthlySalary.Should().Be(0, "server pul ma'lumotini uzatmaydi — Oklad shu zavodda kiritiladi");
        main.Pulled.SelectMany(p => p.Created).Select(e => e.Id).Should().Equal(sanjar.Id);
        second.Pulled.SelectMany(p => p.Created).Select(e => e.Id).Should().Equal(olim.Id);

        // Yangi xodim: 2-zavodda 1-zavod raqamlari (1) hisobga olinmaydi — diapazonda davom etadi.
        (await second.Service.CreateEmployeeAsync("Сардор", 5_000_000, null, null)).TabelNo.Should().Be(1_000_002);
        (await main.Service.CreateEmployeeAsync("Нодир", 5_000_000, null, null)).TabelNo.Should().Be(2);

        // Barqarorlik: yana bir necha aylanish — server yozuvlari qayta o'zgarmaydi, terminalga qayta yozilmaydi.
        await SyncAsync(main);
        await SyncAsync(second);
        await SyncAsync(main);
        var settled = (await ServerEmployeesAsync(main)).ToDictionary(e => e.Id, e => e.UpdatedAt);
        var pulledBefore = main.Pulled.Count + second.Pulled.Count;
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 3; i++)
        {
            second.Sync.EmployeesDirty = true; // desktop/agent har xil sabab bilan qayta jo'natsa ham
            main.Sync.EmployeesDirty = true;
            await SyncAsync(second);
            await SyncAsync(main);
        }
        (await ServerEmployeesAsync(main)).ToDictionary(e => e.Id, e => e.UpdatedAt).Should().BeEquivalentTo(settled);
        (main.Pulled.Count + second.Pulled.Count).Should().Be(pulledBefore, "push↔pull halqasi yo'q");
        main.Service.Employees.Should().HaveCount(4);
        second.Service.Employees.Should().HaveCount(4);
    }

    [Fact]
    public async Task Deactivation_travels_but_an_unknown_inactive_employee_is_not_created()
    {
        var (main, second) = await TwoSitesAsync();
        var olim = await main.Service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        var gone = await main.Service.CreateEmployeeAsync("Сардор", 5_000_000, null, null);
        await main.Service.UpdateEmployeeAsync(gone, gone.FullName, null, gone.MonthlySalary, gone.DailyRate, false, active: false);
        await SyncAsync(main);
        await SyncAsync(second);
        second.Service.Employees.Select(e => e.Id).Should().Equal([olim.Id], "nofaol va 2-zavodga noma'lum — yaratilmaydi");

        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await main.Service.UpdateEmployeeAsync(olim, olim.FullName, "ombor", olim.MonthlySalary, olim.DailyRate, false, active: false);
        await SyncAsync(main);
        await SyncAsync(second);
        var onSecond = second.Service.Employees.Single();
        onSecond.IsActive.Should().BeFalse();
        onSecond.Note.Should().Be("ombor");
        second.Pulled.Last().Changed.Should().ContainSingle(e => e.Id == olim.Id && !e.IsActive, "terminalda bloklanadi");
    }

    [Fact]
    public async Task Deleted_employee_is_sent_as_inactive_once_and_does_not_come_back()
    {
        var (main, second) = await TwoSitesAsync();
        var temp = await main.Service.CreateEmployeeAsync("Нодир", 5_000_000, null, null);
        await SyncAsync(main);
        await SyncAsync(second);
        second.Service.Employees.Should().ContainSingle(e => e.Id == temp.Id && e.IsActive);

        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await main.Service.DeleteEmployeeAsync(temp); // tarixsiz — qattiq o'chirish, iz qoladi
        (await main.Service.GetEmployeeTombstonesAsync()).Should().ContainSingle(t => t.Id == temp.Id && t.FullName == "Нодир");
        main.Sync.EmployeesDirty = false; // iz o'zi push'ni majburlaydi

        await SyncAsync(main);
        (await main.Service.GetEmployeeTombstonesAsync()).Should().BeEmpty("server qabul qildi — iz tozalandi");
        (await ServerEmployeesAsync(main)).Single(e => e.Id == temp.Id).IsActive.Should().BeFalse();
        main.Service.Employees.Should().NotContain(e => e.Id == temp.Id, "o'z o'chirgan xodimi pull'da tirilmaydi");

        await SyncAsync(second);
        second.Service.Employees.Single(e => e.Id == temp.Id).IsActive.Should().BeFalse();

        // Yangi ulangan uchinchi zavod — o'chirilgan xodimni umuman yaratmaydi.
        var third = await OpenSiteAsync("third");
        var code = await Client(main).CreateJoinCodeAsync(main.Service.Settings.InstallationId);
        var join = await new ServerClient(_url, null, _http).JoinAsync(main.Service.Settings.InstallationId, code.Code, "3-zavod");
        join.TabelBase.Should().Be(2_000_001);
        await third.Service.SaveJoinedInstallationAsync(null, _url, join.InstallationId, join.ApiKey, join.TabelBase);
        await SyncAsync(third);
        third.Service.Employees.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_edits_on_different_employees_both_survive_and_same_employee_converges()
    {
        var (main, second) = await TwoSitesAsync();
        var olim = await main.Service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        var nodir = await main.Service.CreateEmployeeAsync("Нодир", 5_000_000, null, null);
        await SyncAsync(main);
        await SyncAsync(second);

        // Bir vaqtda (oflayn): 1-zavod Олим'ni nofaol qiladi, 2-zavod Нодир'ni qayta nomlaydi.
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await main.Service.UpdateEmployeeAsync(olim, olim.FullName, null, olim.MonthlySalary, olim.DailyRate, false, active: false);
        await second.Service.RenameAsync(second.Service.Employees.Single(e => e.Id == nodir.Id), "Нодир (омбор)");
        second.Service.EmployeesPendingPush().Should().ContainSingle(p => p.Employee.Id == nodir.Id);

        await SyncAsync(main);
        await SyncAsync(second); // faqat O'Z o'zgarishini jo'natadi — Олим'ning nofaolligi bosilmaydi
        await SyncAsync(main);

        var server = (await ServerEmployeesAsync(main)).ToDictionary(e => e.Id);
        server[olim.Id].IsActive.Should().BeFalse("eskirgan zavod butun ro'yxatni emas, faqat o'zgarganini jo'natadi");
        server[nodir.Id].FullName.Should().Be("Нодир (омбор)");
        foreach (var site in new[] { main, second })
        {
            site.Service.Employees.Single(e => e.Id == olim.Id).IsActive.Should().BeFalse();
            site.Service.Employees.Single(e => e.Id == nodir.Id).FullName.Should().Be("Нодир (омбор)");
            site.Service.EmployeesPendingPush().Should().BeEmpty();
        }

        // Bitta xodimni ikkala zavod tahrirlasa — yozuv darajasida oxirgi push g'olib, ikkala zavod bir holatga keladi.
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await main.Service.RenameAsync(main.Service.Employees.Single(e => e.Id == nodir.Id), "Нодир (1)");
        await second.Service.RenameAsync(second.Service.Employees.Single(e => e.Id == nodir.Id), "Нодир (2)");
        await SyncAsync(main);
        await SyncAsync(second);
        await SyncAsync(main);
        main.Service.Employees.Single(e => e.Id == nodir.Id).FullName.Should().Be("Нодир (2)");
        second.Service.Employees.Single(e => e.Id == nodir.Id).FullName.Should().Be("Нодир (2)");
    }
}
