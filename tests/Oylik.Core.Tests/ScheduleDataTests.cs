using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Oylik.Core.Domain;
using Oylik.Data;

namespace Oylik.Core.Tests;

/// <summary>4.3 Data: jonli hisob (H10), yopilganda muzlash, migratsiya (mavjud xodimlar Daily, TabelNo).</summary>
public sealed class ScheduleDataTests : IDisposable
{
    private static readonly TimeSpan Uz = TimeSpan.FromHours(5);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-schedule-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(5)));
    private AppPaths Paths => new(_root);

    public ScheduleDataTests() => Paths.EnsureCreated();

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

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, Uz);

    private static async Task Work(PayrollService s, Employee e, DateTimeOffset from, DateTimeOffset to)
    {
        await s.AddManualEventAsync(e, AttendanceEventType.In, from, "test");
        await s.AddManualEventAsync(e, AttendanceEventType.Out, to, "test");
    }

    [Fact]
    public async Task Migration_seeds_three_templates_and_keeps_employees_daily()
    {
        using var service = await OpenAsync();
        service.Schedules.Select(s => s.Id).Should().Equal(WorkSchedule.SampleDaytimeId, WorkSchedule.SampleTwelveHoursId, WorkSchedule.SampleDailyId);
        service.FindSchedule(WorkSchedule.SampleDailyId)!.Should().Match<WorkSchedule>(s =>
            s.Kind == ScheduleKind.Cycle && s.CycleWorkDays == 1 && s.CycleOffDays == 1 && s.DurationMinutes == 1440);
        service.Settings.Should().Match<AppSettings>(x => x.LateGraceMinutes == 5 && x.AbsentAfterMinutes == 60
            && x.GreenPercent == 95 && x.YellowPercent == 75 && x.OvertimePolicy == OvertimePolicy.None);

        var a = await service.CreateEmployeeAsync("A", 6_000_000, null, null);
        var b = await service.CreateEmployeeAsync("B", 6_000_000, null, null);
        a.PayMode.Should().Be(PayMode.Daily);
        (a.TabelNo, b.TabelNo).Should().Be((1, 2));
    }

    [Fact]
    public async Task Migration_backfills_tabel_numbers_by_creation_order_and_defaults_settings()
    {
        var path = Path.Combine(_root, "old.db");
        await using (var db = new OylikDbContext(OylikDbContext.CreateOptions(path)))
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync("ServerAddresses");
            foreach (var (name, ticks) in new[] { ("Ikkinchi", 200L), ("Birinchi", 100L), ("Uchinchi", 300L) })
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Employees (Id, FullName, MonthlySalary, DailyRate, DailyRateIsManual, IsActive, CreatedAt, UpdatedAt) VALUES (@id, @n, 1, 1, 0, 1, @t, @t)",
                    new SqliteParameter("@id", Guid.NewGuid()), new SqliteParameter("@n", name), new SqliteParameter("@t", ticks));
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Settings (Id, CompanyName, DaysInMonth, HoursPerDay, ShortInputEnabled, InstallationId, ApiKey, LastEventSeq, Language, Theme) " +
                "VALUES (1, '', 30, 8, 1, @id, NULL, 0, 'uz-Latn', 0)", new SqliteParameter("@id", Guid.NewGuid()));
            await migrator.MigrateAsync();
        }
        SqliteConnection.ClearAllPools();
        await using var check = new OylikDbContext(OylikDbContext.CreateOptions(path));
        (await check.Employees.OrderBy(e => e.TabelNo).Select(e => e.FullName).ToListAsync()).Should().Equal("Birinchi", "Ikkinchi", "Uchinchi");
        (await check.Employees.AllAsync(e => e.PayMode == PayMode.Daily)).Should().BeTrue();
        (await check.Settings.SingleAsync()).Should().Match<AppSettings>(x => x.LateGraceMinutes == 5 && x.AbsentAfterMinutes == 60
            && x.GreenPercent == 95 && x.YellowPercent == 75 && x.OvertimeValue == 1m);
    }

    [Fact]
    public async Task Open_month_recalculates_live_closing_freezes_and_reopen_recalculates_h10()
    {
        using var service = await OpenAsync();
        var guard = await service.CreateEmployeeAsync("Qorovul", 6_400_000, null, null);
        var office = await service.CreateEmployeeAsync("Hisobchi", 6_000_000, null, null);
        await service.SetPayModeAsync(guard, PayMode.Schedule);
        await service.AssignScheduleAsync(guard, service.FindSchedule(WorkSchedule.SampleDailyId), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));
        var oct = await service.CreatePeriodAsync(new MonthKey(2026, 10));
        var line = oct.FindLine(guard.Id)!;
        line.PayMode.Should().Be(PayMode.Schedule);
        line.PlannedMinutes.Should().Be(16 * 1440);
        line.Total.Should().Be(0);

        // 24 soatlik juftlik (H14) — sutkalik smena, 10 daqiqa kech
        await Work(service, guard, At(1, 8, 10), At(2, 8));
        line.WorkedMinutes.Should().Be(1430);
        line.LateMinutes.Should().Be(10);
        line.Total.Should().Be(PayrollMath.RoundToSum(6_400_000m * 1430 / (16 * 1440)));

        await service.SetDaysWorkedAsync(oct, oct.FindLine(office.Id)!, 20);
        oct.FindLine(office.Id)!.Total.Should().Be(4_000_000, "Daily qator o'zgarmaydi");

        await service.ClosePeriodAsync(oct);
        var frozen = line.Total;
        line.GraceMinutes.Should().Be(5);

        // yopilgandan keyin: grace, jadval va davomat o'zgarsa ham — muzlatilgan
        await service.SaveSettingsAsync(s => s.LateGraceMinutes = 30);
        await service.AssignScheduleAsync(guard, service.FindSchedule(WorkSchedule.SampleDaytimeId), null, new DateOnly(2026, 10, 1));
        await Work(service, guard, At(3, 8), At(4, 8));
        line.Total.Should().Be(frozen);
        line.LateMinutes.Should().Be(10);
        line.GraceMinutes.Should().Be(5);

        // qayta ochish — joriy sozlamalar bilan qayta hisoblanadi
        await service.ReopenPeriodAsync(oct);
        line.GraceMinutes.Should().Be(30);
        line.PlannedMinutes.Should().Be(27 * 480, "endi Kunduzgi jadval (1-oktabrdan)");
    }

    [Fact]
    public async Task Closing_uses_schedule_total_for_carry_over_debt_r6()
    {
        using var service = await OpenAsync();
        var guard = await service.CreateEmployeeAsync("Qorovul", 3_000_000, null, null);
        await service.SetPayModeAsync(guard, PayMode.Schedule);
        await service.AssignScheduleAsync(guard, service.FindSchedule(WorkSchedule.SampleDailyId), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));
        var oct = await service.CreatePeriodAsync(new MonthKey(2026, 10));
        await Work(service, guard, At(1, 8), At(2, 8));
        var line = oct.FindLine(guard.Id)!;
        await service.AddAdvanceAsync(oct, line, new DateOnly(2026, 10, 5), 1_000_000, null);
        var total = line.Total;
        total.Should().Be(PayrollMath.RoundToSum(3_000_000m / 16));

        await service.ClosePeriodAsync(oct);
        var nov = service.FindPeriod(new MonthKey(2026, 11))!;
        nov.FindLine(guard.Id)!.Advances.Single().Amount.Should().Be(1_000_000 - total);
    }

    [Fact]
    public async Task Excused_and_override_change_the_open_line_with_audit()
    {
        using var service = await OpenAsync();
        var e = await service.CreateEmployeeAsync("Hisobchi", 5_400_000, null, null);
        await service.SetPayModeAsync(e, PayMode.Schedule);
        await service.AssignScheduleAsync(e, service.FindSchedule(WorkSchedule.SampleDaytimeId), null, new DateOnly(2026, 10, 1));
        var oct = await service.CreatePeriodAsync(new MonthKey(2026, 10));
        var line = oct.FindLine(e.Id)!;

        await service.SetExcusedAsync(e, new DateOnly(2026, 10, 5), ExcusedKind.Paid, "Kasallik varaqasi");
        line.WorkedMinutes.Should().Be(480);
        await service.SetOverrideAsync(e, new DateOnly(2026, 10, 6), true, null, null, null, "Bayram");
        line.PlannedMinutes.Should().Be(26 * 480);

        var audit = await service.GetAuditAsync();
        audit.Should().Contain(a => a.Entity == "ExcusedDay" && a.Comment == "Kasallik varaqasi");
        audit.Should().Contain(a => a.Entity == "ScheduleOverride" && a.NewValue == "Dam" && a.Comment == "Bayram");
        audit.Should().Contain(a => a.Entity == "Employee" && a.Field == "Schedule");
    }
}
