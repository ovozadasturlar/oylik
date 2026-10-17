using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>
/// Desktop sessiyasi: bitta DbContext, butun ma'lumot xotirada (60–150 xodim uchun yetarli).
/// Har amal <see cref="PayrollEngine"/> orqali o'tadi va darhol saqlanadi (5.1: Saqlash tugmasi yo'q).
/// </summary>
public sealed partial class PayrollService : IDisposable
{
    private readonly OylikDbContext _db;
    private readonly BackupService _backup;
    private readonly TimeProvider _clock;

    public PayrollService(AppPaths paths, TimeProvider clock)
        : this(new OylikDbContext(OylikDbContext.CreateOptions(paths.Database)), new BackupService(paths, clock), clock)
    {
    }

    internal PayrollService(OylikDbContext db, BackupService backup, TimeProvider clock)
    {
        _db = db;
        _backup = backup;
        _clock = clock;
    }

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Hozirgi vaqt manbai (H12 jonli holat; snapshot'da simulyatsiya qilinadi).</summary>
    public TimeProvider Clock => _clock;
    public IReadOnlyList<Employee> Employees => _db.Employees.Local.OrderBy(e => e.FullName, NameComparer.Instance).ToList();
    public IReadOnlyList<PayrollPeriod> Periods => _db.Periods.Local.OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToList();
    public DateTimeOffset? LastSavedAt { get; private set; }
    public BackupService Backup => _backup;

    /// <summary>Migratsiya, WAL, sozlamalar, kunlik backup, hammasini yuklash.</summary>
    public async Task InitializeAsync(bool dailyBackup = true)
    {
        await _db.Database.MigrateAsync();
        await _db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        Settings = await _db.Settings.FirstOrDefaultAsync() ?? await CreateSettingsAsync();

        if (dailyBackup)
        {
            SqliteConnection.ClearAllPools();
            _backup.CreateDailyIfNeeded(Settings.BackupFolder);
        }

        await _db.Employees.LoadAsync();
        await _db.Periods.Include(p => p.Lines).ThenInclude(l => l.Advances).LoadAsync();
        await LoadAttendanceAsync();
        await LoadBranchesAsync();
        await LoadSchedulesAsync();
        foreach (var open in _db.Periods.Local.Where(p => !p.IsClosed).ToList())
            await EnsureEventsLoadedAsync(open.Key);
        RecalculateOpenSchedules(); // H10: ochiq oy — joriy davomat va sozlamalar bilan
        if (_db.ChangeTracker.HasChanges())
            await _db.SaveChangesAsync();
    }

    private async Task<AppSettings> CreateSettingsAsync()
    {
        var settings = new AppSettings { InstallationId = Guid.NewGuid() };
        _db.Settings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }

    private PayrollEngine NewEngine() => new(Settings, _clock);

    private Task SaveAsync(PayrollEngine engine) => SaveAsync(engine.Audit);

    private async Task SaveAsync(IEnumerable<AuditEntry> audit)
    {
        _db.Audit.AddRange(audit);
        RecalculateOpenSchedules(); // H10: har o'zgarishda ochiq oy Schedule qatorlari jonli
        var employeesChanged = _db.ChangeTracker.Entries<Employee>()
            .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        await _db.SaveChangesAsync();
        LastSavedAt = _clock.GetLocalNow();
        if (employeesChanged)
            EmployeesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Xodimlar ro'yxati o'zgardi (yaratish, ism, izoh, faollik) — server sinxronizatsiyasi uchun.</summary>
    public event EventHandler? EmployeesChanged;

    private async Task<T> RunAsync<T>(Func<PayrollEngine, T> action)
    {
        var engine = NewEngine();
        T result;
        try
        {
            result = action(engine);
        }
        catch
        {
            DiscardChanges();
            throw;
        }
        await SaveAsync(engine);
        return result;
    }

    private Task RunAsync(Action<PayrollEngine> action) => RunAsync(e => { action(e); return true; });

    /// <summary>Xato bo'lsa xotiradagi o'zgarishlarni bazadagi holatga qaytaradi.</summary>
    public void DiscardChanges()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    entry.Reload();
                    break;
            }
        }
    }

    // ---------- Oylar ----------

    public PayrollPeriod? FindPeriod(MonthKey key) => _db.Periods.Local.FirstOrDefault(p => p.Year == key.Year && p.Month == key.Month);

    public MonthKey NextPeriodKey() => NewEngine().NextPeriodKey(_db.Periods.Local);

    public Task<PayrollPeriod> CreatePeriodAsync(MonthKey key) => RunAsync(engine =>
    {
        var period = engine.CreatePeriod(key, _db.Employees.Local, _db.Periods.Local);
        _db.Periods.Add(period);
        return period;
    });

    /// <summary>R6: yopishdan oldin backup.</summary>
    public async Task<CloseResult> ClosePeriodAsync(PayrollPeriod period)
    {
        period.EnsureOpen();
        var next = FindPeriod(period.Key.Next());
        if (next is { IsClosed: true })
            throw new DomainException(DomainError.NextPeriodClosed, $"{next.Key.DisplayName} yopilgan — avval uni qayta oching.", next.Key);

        SqliteConnection.ClearAllPools();
        _backup.Create(Settings.BackupFolder);

        await EnsureEventsLoadedAsync(period.Key);
        RecalculateOpenSchedules(); // H10: oxirgi holat bilan — R6 qarz ko'chishi to'g'ri Jami bilan; yopilgach muzlaydi
        return await RunAsync(engine =>
        {
            var result = engine.Close(period, next, _db.Employees.Local);
            if (result.NextPeriodCreated)
                _db.Periods.Add(result.NextPeriod!);
            return result;
        });
    }

    /// <summary>R8.</summary>
    public Task ReopenPeriodAsync(PayrollPeriod period) => RunAsync(engine =>
    {
        var removed = engine.Reopen(period, FindPeriod(period.Key.Next()));
        _db.Advances.RemoveRange(removed);
    });

    // ---------- Qator ----------

    public Task SetDaysWorkedAsync(PayrollPeriod period, PayrollLine line, decimal days) =>
        RunAsync(e => e.SetDaysWorked(period, line, days));

    public Task SetMonthlySalaryAsync(PayrollPeriod period, PayrollLine line, long salary) =>
        RunAsync(e => e.SetMonthlySalary(period, line, EmployeeOf(line), salary));

    public Task SetDailyRateAsync(PayrollPeriod period, PayrollLine line, long dailyRate) =>
        RunAsync(e => e.SetDailyRate(period, line, EmployeeOf(line), dailyRate));

    public Task ResetDailyRateAsync(PayrollPeriod period, PayrollLine line) =>
        RunAsync(e => e.ResetDailyRate(period, line, EmployeeOf(line)));

    public Task RenameAsync(Employee employee, string fullName) =>
        RunAsync(e => e.RenameEmployee(employee, fullName));

    public Task<Advance> AddAdvanceAsync(PayrollPeriod period, PayrollLine line, DateOnly date, long amount, string? note) =>
        RunAsync(e => e.AddAdvance(period, line, date, amount, note));

    public Task RemoveAdvanceAsync(PayrollPeriod period, PayrollLine line, Advance advance) => RunAsync(e =>
    {
        e.RemoveAdvance(period, line, advance);
        _db.Advances.Remove(advance);
    });

    public Task RemoveLineAsync(PayrollPeriod period, PayrollLine line) => RunAsync(e =>
    {
        e.RemoveLine(period, line);
        _db.Lines.Remove(line);
    });

    /// <summary>Jadval ostidagi "+ Yangi xodim": xodim yaratiladi va ochiq oyga qator qo'shiladi.</summary>
    public Task<PayrollLine> AddNewEmployeeAsync(PayrollPeriod period, string fullName, long salary, long? dailyRate, Guid? branchId = null) => RunAsync(e =>
    {
        period.EnsureOpen();
        var employee = e.CreateEmployee(fullName, salary, dailyRate);
        employee.BranchId = FindBranch(branchId)?.Id;
        employee.TabelNo = NextTabelNo();
        _db.Employees.Add(employee);
        return e.AddLine(period, employee);
    });

    public Task<PayrollLine> AddExistingEmployeeAsync(PayrollPeriod period, Employee employee) =>
        RunAsync(e => e.AddLine(period, employee));

    private Employee EmployeeOf(PayrollLine line) =>
        line.Employee ?? _db.Employees.Local.First(x => x.Id == line.EmployeeId);

    // ---------- Xodimlar ----------

    public Task<Employee> CreateEmployeeAsync(string fullName, long salary, long? dailyRate, string? note, Guid? branchId = null) => RunAsync(e =>
    {
        var employee = e.CreateEmployee(fullName, salary, dailyRate, note);
        employee.BranchId = FindBranch(branchId)?.Id;
        employee.TabelNo = NextTabelNo();
        _db.Employees.Add(employee);
        return employee;
    });

    public Task UpdateEmployeeAsync(Employee employee, string fullName, string? note, long salary, long dailyRate, bool dailyManual, bool active) =>
        RunAsync(e =>
        {
            e.RenameEmployee(employee, fullName);
            var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (employee.Note != trimmedNote)
            {
                e.Log("Employee", employee.Id, "Note", employee.Note, trimmedNote);
                employee.Note = trimmedNote;
            }
            e.SetEmployeeSalary(employee, salary);
            if (dailyManual)
                e.SetEmployeeDailyRate(employee, dailyRate);
            else if (employee.DailyRateIsManual)
                employee.ResetDailyRateToAuto(Settings.DaysInMonth);
            e.SetEmployeeActive(employee, active);
        });

    public bool HasHistory(Employee employee) => _db.Lines.Local.Any(l => l.EmployeeId == employee.Id);

    /// <summary>5.4: o'chirish — faqat birorta oyda qatori bo'lmasa.</summary>
    public Task DeleteEmployeeAsync(Employee employee) => RunAsync(e =>
    {
        if (HasHistory(employee))
            throw new DomainException(DomainError.EmployeeHasHistory, "Xodimning oylarda qatori bor — faqat nofaol qilish mumkin.");
        e.Log("Employee", employee.Id, "Deleted", employee.FullName, null);
        _db.Employees.Remove(employee);
    });

    // ---------- Sozlamalar ----------

    public Task SaveSettingsAsync(Action<AppSettings> update) => RunAsync(e =>
    {
        update(Settings);
        e.Log("AppSettings", Settings.InstallationId, "Updated", null, null);
    });

    // ---------- Audit ----------

    public async Task<IReadOnlyList<AuditEntry>> GetAuditAsync(Guid? entityId = null, int take = 1000)
    {
        var query = _db.Audit.AsNoTracking();
        if (entityId is { } id)
            query = query.Where(a => a.EntityId == id);
        return await query.OrderByDescending(a => a.Id).Take(take).ToListAsync();
    }

    /// <summary>Audit filtri uchun: xodim va uning qatorlari/avanslari Id'lari.</summary>
    public IReadOnlySet<Guid> RelatedIds(Employee? employee, PayrollPeriod? period)
    {
        var lines = _db.Lines.Local.AsEnumerable();
        if (employee is not null)
            lines = lines.Where(l => l.EmployeeId == employee.Id);
        if (period is not null)
            lines = lines.Where(l => l.PeriodId == period.Id);
        var ids = new HashSet<Guid>();
        foreach (var line in lines)
        {
            ids.Add(line.Id);
            foreach (var a in line.Advances)
                ids.Add(a.Id);
        }
        if (employee is not null) ids.Add(employee.Id);
        if (period is not null && employee is null) ids.Add(period.Id);
        return ids;
    }

    // ---------- Import ----------

    public async Task<ImportResult> ImportAsync(ImportPreview preview, ImportOptions options)
    {
        SqliteConnection.ClearAllPools();
        _backup.Create(Settings.BackupFolder);
        return await RunAsync(e => Import(e, preview, options));
    }

    private ImportResult Import(PayrollEngine e, ImportPreview preview, ImportOptions options)
    {
        var byName = _db.Employees.Local.ToDictionary(x => x.FullName, NameComparer.Instance);
        PayrollPeriod? period = null;
        if (options.Period is { } key)
        {
            period = FindPeriod(key);
            if (period is null)
            {
                period = new PayrollPeriod { Year = key.Year, Month = key.Month };
                e.Log("PayrollPeriod", period.Id, "Status", null, "Open", $"{key.DisplayName} — Excel'dan import");
                _db.Periods.Add(period);
            }
            period.EnsureOpen();
        }

        int created = 0, linked = 0, lines = 0;
        foreach (var row in preview.Rows)
        {
            if (byName.TryGetValue(row.FullName, out var employee) && options.LinkDuplicates)
            {
                linked++;
            }
            else
            {
                employee = e.CreateEmployee(row.FullName, row.MonthlySalary, row.DailyRateIsManual ? row.DailyRate : null);
                employee.TabelNo = NextTabelNo();
                _db.Employees.Add(employee);
                byName[employee.FullName] = employee;
                created++;
            }

            if (period is null)
                continue;

            var line = period.FindLine(employee.Id) ?? e.AddLine(period, employee);
            line.MonthlySalary = row.MonthlySalary;
            line.DailyRate = row.DailyRate;
            line.DaysWorked = row.DaysWorked;
            if (row.Advance > 0)
                e.AddAdvance(period, line, period.Key.FirstDay, row.Advance, "Excel'dan");
            lines++;
        }
        return new ImportResult(created, linked, lines);
    }

    /// <summary>H13.10: doimiy tabel raqami — yaratilish tartibida.</summary>
    private int NextTabelNo() => _db.Employees.Local.Select(x => x.TabelNo).DefaultIfEmpty(0).Max() + 1;

    public void Dispose() => _db.Dispose();
}
