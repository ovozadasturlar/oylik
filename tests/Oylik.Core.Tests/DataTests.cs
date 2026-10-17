using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;

namespace Oylik.Core.Tests;

public sealed class DataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = TestClock.Default();
    private AppPaths Paths => new(_root);

    public DataTests() => Paths.EnsureCreated();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<PayrollService> OpenAsync()
    {
        var service = new PayrollService(Paths, _clock);
        await service.InitializeAsync();
        return service;
    }

    private static string SamplePath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docs", "sample.xlsx")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new FileNotFoundException("docs/sample.xlsx"), "docs", "sample.xlsx");
    }

    [Fact]
    public void Import_reads_sample_workbook()
    {
        var preview = ExcelImporter.Read(SamplePath(), 30);

        preview.Rows.Should().HaveCount(57);
        preview.SkippedEmpty.Should().Be(47);
        preview.Errors.Should().BeEmpty();

        var olim = preview.Rows.First(r => r.FullName == "Олим ака (тест)");
        olim.MonthlySalary.Should().Be(9_000_000);
        olim.DailyRate.Should().Be(300_000);
        olim.DailyRateIsManual.Should().BeFalse();

        var sardor = preview.Rows.First(r => r.FullName == "Сардор");
        sardor.MonthlySalary.Should().Be(8_000_000);
        sardor.DailyRate.Should().Be(266_667);

        var timur = preview.Rows.First(r => r.FullName == "Тимур (бух)");
        timur.MonthlySalary.Should().Be(0);
        timur.DailyRate.Should().Be(250_000);
        timur.DailyRateIsManual.Should().BeTrue();

        preview.Rows.Should().OnlyContain(r => r.FullName == r.FullName.Trim());
    }

    [Fact]
    public async Task Import_as_period_and_changes_survive_restart()
    {
        var key = new MonthKey(2026, 10);
        using (var service = await OpenAsync())
        {
            var preview = ExcelImporter.Read(SamplePath(), 30);
            var result = await service.ImportAsync(preview, new ImportOptions(key));
            result.Created.Should().Be(57);

            var period = service.FindPeriod(key)!;
            var line = period.Lines.First(l => l.Employee!.FullName == "Олим ака (тест)");
            await service.SetDaysWorkedAsync(period, line, 26);
            await service.AddAdvanceAsync(period, line, new DateOnly(2026, 10, 5), 500_000, null);
            await service.AddAdvanceAsync(period, line, new DateOnly(2026, 10, 15), 1_000_000, "Oy o'rtasi");
        }

        SqliteConnection.ClearAllPools();
        using (var service = await OpenAsync())
        {
            var period = service.FindPeriod(key)!;
            period.Lines.Should().HaveCount(57);
            var line = period.Lines.First(l => l.Employee!.FullName == "Олим ака (тест)");
            line.DaysWorked.Should().Be(26);
            line.Total.Should().Be(7_800_000);
            line.AdvanceTotal.Should().Be(1_500_000);
            line.Remainder.Should().Be(6_300_000);
            (await service.GetAuditAsync()).Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task September_debt_carries_over_once_through_reopen_cycle()
    {
        using var service = await OpenAsync();
        var sep = await service.CreatePeriodAsync(new MonthKey(2026, 9));
        var line = await service.AddNewEmployeeAsync(sep, "Олим ака", 6_000_000, null);
        await service.SetDaysWorkedAsync(sep, line, 5);
        await service.AddAdvanceAsync(sep, line, new DateOnly(2026, 9, 10), 2_200_000, null);

        var oct = (await service.ClosePeriodAsync(sep)).NextPeriod!;
        DebtIn(oct, line.EmployeeId).Should().ContainSingle().Which.Amount.Should().Be(1_200_000);

        await service.ReopenPeriodAsync(sep);
        DebtIn(oct, line.EmployeeId).Should().BeEmpty();

        await service.ClosePeriodAsync(sep);
        await service.ReopenPeriodAsync(sep);
        await service.ClosePeriodAsync(sep);
        DebtIn(oct, line.EmployeeId).Should().ContainSingle();

        Directory.EnumerateFiles(Paths.Backups, "oylik-*.db").Should().NotBeEmpty();

        SqliteConnection.ClearAllPools();
        using var reopened = await OpenAsync();
        var octAgain = reopened.FindPeriod(new MonthKey(2026, 10))!;
        DebtIn(octAgain, line.EmployeeId).Should().ContainSingle().Which.Note.Should().Be("Sentyabr qarzi");
        reopened.FindPeriod(new MonthKey(2026, 9))!.IsClosed.Should().BeTrue();
    }

    private static IEnumerable<Advance> DebtIn(PayrollPeriod period, Guid employeeId) =>
        period.FindLine(employeeId)?.Advances.Where(a => a.Kind == AdvanceKind.CarryOverDebt) ?? [];

    [Fact]
    public async Task Failed_operation_does_not_leave_partial_changes()
    {
        using var service = await OpenAsync();
        var period = await service.CreatePeriodAsync(new MonthKey(2026, 10));
        var line = await service.AddNewEmployeeAsync(period, "Бахтиёр", 6_000_000, null);

        var act = () => service.SetDaysWorkedAsync(period, line, 40);
        await act.Should().ThrowAsync<DomainException>();

        line.DaysWorked.Should().Be(0);
        await service.SetDaysWorkedAsync(period, line, 20);
        line.DaysWorked.Should().Be(20);
    }

    [Fact]
    public async Task Export_writes_real_formulas()
    {
        using var service = await OpenAsync();
        var period = await service.CreatePeriodAsync(new MonthKey(2026, 10));
        var line = await service.AddNewEmployeeAsync(period, "Бахтиёр", 6_000_000, null);
        await service.SetDaysWorkedAsync(period, line, 26);
        await service.AddAdvanceAsync(period, line, new DateOnly(2026, 10, 1), 1_000_000, null);

        var path = Path.Combine(_root, ExcelExporter.DefaultFileName(period.Key));
        ExcelExporter.Export(period, period.Lines, path);

        Path.GetFileName(path).Should().Be("Oylik_2026-10.xlsx");
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Лист1");
        ws.Cell("A1").GetString().Should().Be("№");
        ws.Cell("H1").GetString().Should().Be("Колдик");
        ws.Cell("F2").FormulaA1.Should().Be("E2*D2");
        ws.Cell("H2").FormulaA1.Should().Be("F2-G2");
        ws.Cell("F3").FormulaA1.Should().Be("SUM(F2:F2)");
        ws.Cell("F2").Value.GetNumber().Should().Be(5_200_000);
        ws.Cell("H2").Value.GetNumber().Should().Be(4_200_000);
    }

    [Fact]
    public void Daily_backup_is_created_once_per_day_and_pruned()
    {
        File.WriteAllBytes(Paths.Database, []);
        using (var c = new SqliteConnection($"Data Source={Paths.Database}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE t(x)";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var backup = new BackupService(Paths, _clock);

        backup.CreateDailyIfNeeded().Should().NotBeNull();
        backup.CreateDailyIfNeeded().Should().BeNull();

        for (var i = 0; i < 95; i++)
            File.WriteAllBytes(Path.Combine(Paths.Backups, $"oylik-2020{i:0000}-0000.db"), []);
        backup.Create();
        Directory.EnumerateFiles(Paths.Backups, "oylik-*.db").Should().HaveCount(BackupService.KeepCount);
    }
}
