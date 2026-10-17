using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Oylik.Core.Domain;

namespace Oylik.Data;

public sealed class OylikDbContext(DbContextOptions<OylikDbContext> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PayrollPeriod> Periods => Set<PayrollPeriod>();
    public DbSet<PayrollLine> Lines => Set<PayrollLine>();
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<AttendanceEvent> AttendanceEvents => Set<AttendanceEvent>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<WorkSchedule> Schedules => Set<WorkSchedule>();
    public DbSet<EmployeeScheduleAssignment> ScheduleAssignments => Set<EmployeeScheduleAssignment>();
    public DbSet<ScheduleOverride> ScheduleOverrides => Set<ScheduleOverride>();
    public DbSet<ExcusedDay> ExcusedDays => Set<ExcusedDay>();

    public static DbContextOptions<OylikDbContext> CreateOptions(string dbPath) =>
        new DbContextOptionsBuilder<OylikDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // UTC saqlanadi; tartiblash mumkin bo'lishi uchun ticks (long).
        builder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<UtcTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Employee>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Note).HasMaxLength(100);
            e.HasIndex(x => x.FullName);
            e.HasIndex(x => x.BranchId);
            e.HasIndex(x => x.TabelNo);
            e.Property(x => x.OvertimeValue).HasPrecision(18, 2);
        });

        b.Entity<PayrollPeriod>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.Year, x.Month }).IsUnique();
            e.Ignore(x => x.Key);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PayrollLine>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.DaysWorked).HasPrecision(5, 2);
            e.HasIndex(x => new { x.PeriodId, x.EmployeeId }).IsUnique();
            e.HasIndex(x => x.BranchId);
            e.Property(x => x.OvertimeValue).HasPrecision(18, 2);
            e.Ignore(x => x.IsSchedule);
            e.Ignore(x => x.HourlyRate);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Advances).WithOne().HasForeignKey(x => x.PayrollLineId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Advance>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => x.SourcePeriodId);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.EntityId);
        });

        b.Entity<AppSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.CompanyName).HasMaxLength(200);
            e.Property(x => x.Language).HasMaxLength(10).HasDefaultValue(AppSettings.LanguageUzLatn);
            e.Property(x => x.BranchFilter).HasMaxLength(40);
            e.Property(x => x.LocalServerUrl).HasMaxLength(300);
            e.Property(x => x.OvertimeValue).HasPrecision(18, 2);
        });

        b.Entity<AttendanceEvent>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.VoidReason).HasMaxLength(200);
            e.Ignore(x => x.IsManual);
            e.Ignore(x => x.HasClockSkew);
            e.HasIndex(x => new { x.EmployeeId, x.OccurredAt });
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.ServerSeq);
            e.HasIndex(x => x.PendingPush);
        });

        b.Entity<Device>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        // 3.9: tarmoq o'chirilganda xodim/qator bog'lanishi kod darajasida tekshiriladi (EnsureBranchUnused) — FK yo'q.
        b.Entity<Branch>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasData(
                new Branch { Id = Branch.DefaultFirstId, Name = "1-tarmoq", SortOrder = 1 },
                new Branch { Id = Branch.DefaultSecondId, Name = "2-tarmoq", SortOrder = 2 });
        });

        // 4.3 / 5.10: ish jadvallari — 3 ta namuna
        b.Entity<WorkSchedule>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasData(
                new WorkSchedule
                {
                    Id = WorkSchedule.SampleDaytimeId, Name = "Kunduzgi 08:00–17:00", Kind = ScheduleKind.Weekly,
                    WeekDays = WorkSchedule.MondayToSaturday, StartTime = new TimeOnly(8, 0), DurationMinutes = 9 * 60, BreakMinutes = 60, SortOrder = 1,
                },
                new WorkSchedule
                {
                    Id = WorkSchedule.SampleTwelveHoursId, Name = "12 soat 08:00–20:00", Kind = ScheduleKind.Cycle,
                    CycleWorkDays = 2, CycleOffDays = 2, StartTime = new TimeOnly(8, 0), DurationMinutes = 12 * 60, BreakMinutes = 0, SortOrder = 2,
                },
                new WorkSchedule
                {
                    Id = WorkSchedule.SampleDailyId, Name = "Sutkalik 08:00–08:00, kun ora", Kind = ScheduleKind.Cycle,
                    CycleWorkDays = 1, CycleOffDays = 1, StartTime = new TimeOnly(8, 0), DurationMinutes = 24 * 60, BreakMinutes = 0, SortOrder = 3,
                });
        });

        b.Entity<EmployeeScheduleAssignment>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
        });

        b.Entity<ScheduleOverride>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.EmployeeId, x.Date });
        });

        b.Entity<ExcusedDay>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.EmployeeId, x.Date });
        });
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.UtcTicks,
        v => new DateTimeOffset(v, TimeSpan.Zero));
}

/// <summary>`dotnet ef` uchun.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<OylikDbContext>
{
    public OylikDbContext CreateDbContext(string[] args) =>
        new(OylikDbContext.CreateOptions(Path.Combine(Path.GetTempPath(), "oylik-design.db")));
}
