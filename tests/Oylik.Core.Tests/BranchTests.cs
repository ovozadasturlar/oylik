using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;

namespace Oylik.Core.Tests;

/// <summary>3.9 Tarmoqlar: migratsiya, qatorga nusxa, R9 kabi yangilanish, filtr xulosasi, o'chirish taqiqi.</summary>
public sealed class BranchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-branch-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = TestClock.Default();
    private AppPaths Paths => new(_root);

    public BranchTests() => Paths.EnsureCreated();

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

    [Fact]
    public async Task Migration_creates_two_default_branches_and_employees_stay_unassigned()
    {
        using var service = await OpenAsync();
        service.Branches.Select(b => (b.Id, b.Name)).Should().Equal(
            (Branch.DefaultFirstId, "1-tarmoq"), (Branch.DefaultSecondId, "2-tarmoq"));
        var e = await service.CreateEmployeeAsync("Олим", 6_000_000, null, null);
        e.BranchId.Should().BeNull();
        service.HasUnassigned.Should().BeTrue();
    }

    [Fact]
    public async Task Line_copies_branch_and_open_month_follows_employee_but_closed_month_does_not()
    {
        using var service = await OpenAsync();
        var olim = await service.CreateEmployeeAsync("Олим", 6_000_000, null, null, Branch.DefaultFirstId);
        var sep = await service.CreatePeriodAsync(new MonthKey(2026, 9));
        sep.FindLine(olim.Id)!.BranchId.Should().Be(Branch.DefaultFirstId);
        await service.ClosePeriodAsync(sep);
        var oct = service.FindPeriod(new MonthKey(2026, 10)) ?? await service.CreatePeriodAsync(new MonthKey(2026, 10));
        oct.FindLine(olim.Id)!.BranchId.Should().Be(Branch.DefaultFirstId, "keyingi oy qatori ham xodimdan nusxa");

        await service.SetEmployeeBranchAsync(olim, Branch.DefaultSecondId);

        olim.BranchId.Should().Be(Branch.DefaultSecondId);
        oct.FindLine(olim.Id)!.BranchId.Should().Be(Branch.DefaultSecondId, "ochiq oy — yangilanadi");
        sep.FindLine(olim.Id)!.BranchId.Should().Be(Branch.DefaultFirstId, "yopiq oy — o'zgarmaydi");
        var audit = await service.GetAuditAsync();
        audit.Should().Contain(a => a.Entity == "Employee" && a.Field == "Branch" && a.OldValue == "1-tarmoq" && a.NewValue == "2-tarmoq");
        audit.Should().Contain(a => a.Entity == "PayrollLine" && a.Field == "Branch" && a.NewValue == "2-tarmoq");
    }

    [Fact]
    public async Task Filter_summary_counts_only_that_branch()
    {
        using var service = await OpenAsync();
        var a = await service.CreateEmployeeAsync("A", 3_000_000, null, null, Branch.DefaultFirstId);
        var b = await service.CreateEmployeeAsync("B", 6_000_000, null, null, Branch.DefaultSecondId);
        var c = await service.CreateEmployeeAsync("C", 9_000_000, null, null);
        var oct = await service.CreatePeriodAsync(new MonthKey(2026, 10));
        foreach (var line in oct.Lines)
            await service.SetDaysWorkedAsync(oct, line, 10);
        await service.AddAdvanceAsync(oct, oct.FindLine(b.Id)!, new DateOnly(2026, 10, 5), 500_000, null);

        PeriodSummary.Of(BranchFilter.All.Apply(oct.Lines)).Should().Be(new PeriodSummary(3, 30, 6_000_000, 500_000, 5_500_000));
        PeriodSummary.Of(BranchFilter.Of(Branch.DefaultFirstId).Apply(oct.Lines)).Should().Be(new PeriodSummary(1, 10, 1_000_000, 0, 1_000_000));
        PeriodSummary.Of(BranchFilter.Of(Branch.DefaultSecondId).Apply(oct.Lines)).Should().Be(new PeriodSummary(1, 10, 2_000_000, 500_000, 1_500_000));
        PeriodSummary.Of(BranchFilter.Unassigned.Apply(oct.Lines)).Should().Be(new PeriodSummary(1, 10, 3_000_000, 0, 3_000_000));
        BranchFilter.Of(Branch.DefaultFirstId).Apply(service.Employees).Should().Equal(a);
        BranchFilter.Unassigned.Apply(service.Employees).Should().Equal(c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    [InlineData("8f3c1a52-6b1e-4c43-9a5e-2f0d7c1b0a02")]
    public void Filter_setting_round_trips(string? setting) =>
        BranchFilter.Parse(setting).ToSetting().Should().Be(setting);

    [Fact]
    public async Task Branch_with_employees_cannot_be_deleted_only_deactivated()
    {
        using var service = await OpenAsync();
        var first = service.FindBranch(Branch.DefaultFirstId)!;
        await service.CreateEmployeeAsync("Олим", 6_000_000, null, null, first.Id);

        var act = () => service.DeleteBranchAsync(first);
        (await act.Should().ThrowAsync<DomainException>()).Which.Error.Should().Be(DomainError.BranchInUse);
        await service.SetBranchActiveAsync(first, false);
        first.IsActive.Should().BeFalse();

        var third = await service.CreateBranchAsync("  3-zavod ");
        third.Name.Should().Be("3-zavod");
        third.SortOrder.Should().Be(3);
        await service.RenameBranchAsync(third, "Yangi zavod");
        await service.DeleteBranchAsync(third);
        service.Branches.Should().HaveCount(2);
    }

    [Fact]
    public async Task Employee_grid_salary_change_updates_card_and_open_line_r9()
    {
        using var service = await OpenAsync();
        var olim = await service.CreateEmployeeAsync("Олим", 6_000_000, null, null);
        var sep = await service.CreatePeriodAsync(new MonthKey(2026, 9));
        await service.ClosePeriodAsync(sep);
        var oct = service.FindPeriod(new MonthKey(2026, 10)) ?? await service.CreatePeriodAsync(new MonthKey(2026, 10));

        await service.SetEmployeeSalaryAsync(olim, 9_000_000);
        olim.MonthlySalary.Should().Be(9_000_000);
        olim.DailyRate.Should().Be(300_000);
        oct.FindLine(olim.Id)!.MonthlySalary.Should().Be(9_000_000);
        oct.FindLine(olim.Id)!.DailyRate.Should().Be(300_000);
        sep.FindLine(olim.Id)!.MonthlySalary.Should().Be(6_000_000, "yopiq oy o'zgarmaydi");

        await service.SetEmployeeDailyRateAsync(olim, 250_000);
        olim.DailyRateIsManual.Should().BeTrue();
        oct.FindLine(olim.Id)!.DailyRate.Should().Be(250_000);

        await service.ResetEmployeeDailyRateAsync(olim);
        olim.DailyRateIsManual.Should().BeFalse();
        oct.FindLine(olim.Id)!.DailyRate.Should().Be(300_000);
    }

    [Fact]
    public async Task Branch_and_filter_survive_restart()
    {
        using (var service = await OpenAsync())
        {
            var e = await service.CreateEmployeeAsync("Олим", 6_000_000, null, null, Branch.DefaultSecondId);
            await service.SaveSettingsAsync(s => s.BranchFilter = BranchFilter.Of(Branch.DefaultSecondId).ToSetting());
        }
        SqliteConnection.ClearAllPools();
        UiPreferences.Read(Paths).BranchFilter.Should().Be(Branch.DefaultSecondId.ToString());
        using var reopened = await OpenAsync();
        reopened.Employees.Single().BranchId.Should().Be(Branch.DefaultSecondId);
    }
}
