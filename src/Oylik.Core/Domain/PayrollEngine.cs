namespace Oylik.Core.Domain;

public sealed record CloseResult(PayrollPeriod? NextPeriod, bool NextPeriodCreated, IReadOnlyList<Advance> CarriedDebts);

/// <summary>
/// 4.1 hisob qoidalari (R1–R10). Saqlashni bilmaydi: entity'larni o'zgartiradi va audit yozuvlarini
/// <see cref="Audit"/> ga yig'adi — chaqiruvchi ularni saqlaydi.
/// </summary>
public sealed partial class PayrollEngine(AppSettings settings, TimeProvider clock)
{
    public const string CarryOverNoteSuffix = "qarzi";

    public List<AuditEntry> Audit { get; } = [];

    private DateTimeOffset Now => clock.GetUtcNow();

    // ---------- Oy yaratish (R5) ----------

    /// <summary>R5: eng oxirgi oydan keyingi oy; oy yo'q bo'lsa — joriy oy.</summary>
    public MonthKey NextPeriodKey(IEnumerable<PayrollPeriod> existing)
    {
        var keys = existing.Select(p => p.Key).ToList();
        if (keys.Count == 0)
        {
            var local = clock.GetLocalNow();
            return new MonthKey(local.Year, local.Month);
        }
        return keys.Max().Next();
    }

    public PayrollPeriod CreatePeriod(MonthKey key, IEnumerable<Employee> employees, IEnumerable<PayrollPeriod> existing)
    {
        if (existing.Any(p => p.Key == key))
            throw new DomainException(DomainError.PeriodExists, $"{key.DisplayName} allaqachon mavjud.", key);

        var period = new PayrollPeriod { Year = key.Year, Month = key.Month };
        foreach (var employee in employees.Where(e => e.IsActive))
            period.Lines.Add(NewLine(period, employee));

        Log("PayrollPeriod", period.Id, "Status", null, "Open", $"{key.DisplayName} ochildi");
        return period;
    }

    private static PayrollLine NewLine(PayrollPeriod period, Employee employee) => new()
    {
        PeriodId = period.Id,
        EmployeeId = employee.Id,
        Employee = employee,
        MonthlySalary = employee.MonthlySalary,
        DailyRate = employee.DailyRate,
        DaysWorked = 0,
        BranchId = employee.BranchId,
        PayMode = employee.PayMode,
    };

    // ---------- Oyni yopish (R6) ----------

    /// <param name="period">Yopilayotgan oy.</param>
    /// <param name="next">Keyingi oy, agar mavjud bo'lsa.</param>
    /// <param name="employees">Barcha xodimlar (nofaollar ham) — keyingi oyda qator yaratish uchun.</param>
    public CloseResult Close(PayrollPeriod period, PayrollPeriod? next, IEnumerable<Employee> employees)
    {
        period.EnsureOpen();
        if (next is not null && next.Key != period.Key.Next())
            throw new ArgumentException("Keyingi oy noto'g'ri berilgan.", nameof(next));
        if (next is { IsClosed: true })
            throw new DomainException(DomainError.NextPeriodClosed, $"{next.Key.DisplayName} yopilgan — avval uni qayta oching.", next.Key);

        var employeeList = employees as IReadOnlyCollection<Employee> ?? employees.ToList();
        var employeesById = employeeList.ToDictionary(e => e.Id);

        period.Status = PeriodStatus.Closed;
        period.ClosedAt = Now;
        Log("PayrollPeriod", period.Id, "Status", "Open", "Closed", $"{period.Key.DisplayName} yopildi");

        var debtors = period.Lines.Where(l => l.Remainder < 0).ToList();
        bool created = false;
        var carried = new List<Advance>();

        if (debtors.Count > 0 && next is null)
        {
            next = CreatePeriod(period.Key.Next(), employeeList, [period]);
            created = true;
        }

        if (next is not null)
        {
            foreach (var line in period.Lines)
            {
                var nextLine = next.FindLine(line.EmployeeId);
                var existing = nextLine?.Advances.FirstOrDefault(a =>
                    a.Kind == AdvanceKind.CarryOverDebt && a.SourcePeriodId == period.Id);

                if (line.Remainder >= 0)
                {
                    // R6.4: to'langan — hech narsa. Eskidan qolgan yozuv bo'lsa olib tashlanadi.
                    if (existing is not null)
                        nextLine!.Advances.Remove(existing);
                    continue;
                }

                if (nextLine is null)
                {
                    var employee = line.Employee ?? employeesById.GetValueOrDefault(line.EmployeeId)
                        ?? throw new InvalidOperationException($"Xodim topilmadi: {line.EmployeeId}");
                    nextLine = NewLine(next, employee);
                    next.Lines.Add(nextLine);
                }

                var debt = -line.Remainder;
                if (existing is not null)
                {
                    // R6.3: idempotent — summa yangilanadi.
                    existing.Amount = debt;
                    carried.Add(existing);
                    continue;
                }

                var advance = new Advance
                {
                    PayrollLineId = nextLine.Id,
                    Kind = AdvanceKind.CarryOverDebt,
                    Amount = debt,
                    Date = next.Key.FirstDay,
                    Note = $"{period.Key.Name} {CarryOverNoteSuffix}",
                    SourcePeriodId = period.Id,
                    CreatedAt = Now,
                };
                nextLine.Advances.Add(advance);
                carried.Add(advance);
                Log("Advance", advance.Id, "Amount", null, NumberText.Money(debt), advance.Note);
            }
        }

        return new CloseResult(next, created, carried);
    }

    // ---------- Qayta ochish (R8) ----------

    public IReadOnlyList<Advance> Reopen(PayrollPeriod period, PayrollPeriod? next)
    {
        if (!period.IsClosed)
            return [];
        if (next is { IsClosed: true })
            throw new DomainException(DomainError.NextPeriodClosed,
                $"{next.Key.DisplayName} yopilgan — {period.Key.Name}ni qayta ochib bo'lmaydi.", next.Key, period.Key);

        var removed = new List<Advance>();
        if (next is not null)
        {
            foreach (var line in next.Lines)
            {
                var own = line.Advances
                    .Where(a => a.Kind == AdvanceKind.CarryOverDebt && a.SourcePeriodId == period.Id)
                    .ToList();
                foreach (var advance in own)
                {
                    line.Advances.Remove(advance);
                    removed.Add(advance);
                }
            }
        }

        period.Status = PeriodStatus.Open;
        period.ClosedAt = null;
        Log("PayrollPeriod", period.Id, "Status", "Closed", "Open", $"{period.Key.DisplayName} qayta ochildi");
        return removed;
    }

    // ---------- Qatorni tahrirlash (R1, R7, R9) ----------

    public void SetDaysWorked(PayrollPeriod period, PayrollLine line, decimal days, string? comment = null)
    {
        period.EnsureOpen();
        if (!PayrollMath.IsValidDaysWorked(days))
            throw new DomainException(DomainError.InvalidDaysWorked, "Kun 0–31 oralig'ida, 0,5 qadam bilan bo'lishi kerak.");
        if (line.DaysWorked == days)
            return;
        Log("PayrollLine", line.Id, "DaysWorked", NumberText.Days(line.DaysWorked), NumberText.Days(days), comment);
        line.DaysWorked = days;
    }

    /// <summary>R9 + R1: qatordagi Oklad — shu oy nusxasi va xodim kartasi yangilanadi.</summary>
    public void SetMonthlySalary(PayrollPeriod period, PayrollLine line, Employee employee, long salary)
    {
        period.EnsureOpen();
        EnsureAmount(salary, allowZero: true);
        if (line.MonthlySalary == salary && employee.MonthlySalary == salary)
            return;

        Log("PayrollLine", line.Id, "MonthlySalary", NumberText.Money(line.MonthlySalary), NumberText.Money(salary));
        line.MonthlySalary = salary;
        if (employee.MonthlySalary != salary)
            Log("Employee", employee.Id, "MonthlySalary", NumberText.Money(employee.MonthlySalary), NumberText.Money(salary));
        employee.SetMonthlySalary(salary, settings.DaysInMonth);
        employee.UpdatedAt = Now;

        if (!employee.DailyRateIsManual && line.DailyRate != employee.DailyRate)
        {
            Log("PayrollLine", line.Id, "DailyRate", NumberText.Money(line.DailyRate), NumberText.Money(employee.DailyRate), "avtomatik (÷" + settings.DaysInMonth + ")");
            line.DailyRate = employee.DailyRate;
        }
    }

    /// <summary>R9 + R1: Kunlik qo'lda — DailyRateIsManual = true.</summary>
    public void SetDailyRate(PayrollPeriod period, PayrollLine line, Employee employee, long dailyRate)
    {
        period.EnsureOpen();
        EnsureAmount(dailyRate, allowZero: true);
        if (line.DailyRate == dailyRate && employee.DailyRate == dailyRate && employee.DailyRateIsManual)
            return;

        Log("PayrollLine", line.Id, "DailyRate", NumberText.Money(line.DailyRate), NumberText.Money(dailyRate), "qo'lda");
        line.DailyRate = dailyRate;
        if (employee.DailyRate != dailyRate)
            Log("Employee", employee.Id, "DailyRate", NumberText.Money(employee.DailyRate), NumberText.Money(dailyRate), "qo'lda");
        employee.SetDailyRateManual(dailyRate);
        employee.UpdatedAt = Now;
    }

    /// <summary>Kunlikni qayta avtomatik (Oklad ÷ DaysInMonth) qilish.</summary>
    public void ResetDailyRate(PayrollPeriod period, PayrollLine line, Employee employee)
    {
        period.EnsureOpen();
        employee.ResetDailyRateToAuto(settings.DaysInMonth);
        employee.UpdatedAt = Now;
        Log("Employee", employee.Id, "DailyRateIsManual", "true", "false");
        if (line.DailyRate != employee.DailyRate)
        {
            Log("PayrollLine", line.Id, "DailyRate", NumberText.Money(line.DailyRate), NumberText.Money(employee.DailyRate), "avtomatik");
            line.DailyRate = employee.DailyRate;
        }
    }

    public void SetNote(PayrollPeriod period, PayrollLine line, string? note)
    {
        period.EnsureOpen();
        line.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    // ---------- Avanslar ----------

    public Advance AddAdvance(PayrollPeriod period, PayrollLine line, DateOnly date, long amount, string? note)
    {
        period.EnsureOpen();
        if (amount <= 0)
            throw new DomainException(DomainError.InvalidAmount, "Avans summasi 0 dan katta bo'lishi kerak.");

        var advance = new Advance
        {
            PayrollLineId = line.Id,
            Date = date,
            Amount = amount,
            Kind = AdvanceKind.Cash,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = Now,
        };
        line.Advances.Add(advance);
        Log("Advance", advance.Id, "Amount", null, NumberText.Money(amount), $"qo'shildi {date:dd.MM.yyyy}");
        return advance;
    }

    public void RemoveAdvance(PayrollPeriod period, PayrollLine line, Advance advance)
    {
        period.EnsureOpen();
        if (advance.Kind == AdvanceKind.CarryOverDebt)
            throw new DomainException(DomainError.CarryOverReadOnly,
                "O'tgan oydan qarz yozuvini o'chirib bo'lmaydi — manba oyni qayta oching.");
        if (line.Advances.Remove(advance))
            Log("Advance", advance.Id, "Amount", NumberText.Money(advance.Amount), null, $"o'chirildi {advance.Date:dd.MM.yyyy}");
    }

    // ---------- Qator qo'shish / olib tashlash ----------

    public PayrollLine AddLine(PayrollPeriod period, Employee employee)
    {
        period.EnsureOpen();
        if (period.FindLine(employee.Id) is not null)
            throw new DomainException(DomainError.LineExists, $"{employee.FullName} bu oyda allaqachon bor.", employee.FullName);
        var line = NewLine(period, employee);
        period.Lines.Add(line);
        Log("PayrollLine", line.Id, "Added", null, employee.FullName, period.Key.DisplayName);
        return line;
    }

    /// <summary>R10: faqat DaysWorked = 0 va avans yo'q bo'lsa.</summary>
    public void RemoveLine(PayrollPeriod period, PayrollLine line)
    {
        period.EnsureOpen();
        if (!CanRemoveLine(line))
            throw new DomainException(DomainError.LineNotEmpty,
                "Qatorda kun yoki avans bor — oydan olib tashlab bo'lmaydi.");
        period.Lines.Remove(line);
        Log("PayrollLine", line.Id, "Removed", line.Employee?.FullName, null, period.Key.DisplayName);
    }

    public static bool CanRemoveLine(PayrollLine line) => line.DaysWorked == 0 && line.Advances.Count == 0;

    // ---------- Xodimlar ----------

    public Employee CreateEmployee(string fullName, long monthlySalary, long? dailyRate = null, string? note = null)
    {
        var name = NormalizeName(fullName);
        var employee = new Employee
        {
            FullName = name,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        employee.SetMonthlySalary(monthlySalary, settings.DaysInMonth);
        if (dailyRate is { } manual && manual != employee.DailyRate)
            employee.SetDailyRateManual(manual);
        Log("Employee", employee.Id, "Created", null, name);
        return employee;
    }

    public void RenameEmployee(Employee employee, string fullName)
    {
        var name = NormalizeName(fullName);
        if (employee.FullName == name)
            return;
        Log("Employee", employee.Id, "FullName", employee.FullName, name);
        employee.FullName = name;
        employee.UpdatedAt = Now;
    }

    /// <summary>Xodim kartasidan Oklad (faqat kelgusi oylar uchun).</summary>
    public void SetEmployeeSalary(Employee employee, long salary)
    {
        EnsureAmount(salary, allowZero: true);
        if (employee.MonthlySalary == salary)
            return;
        Log("Employee", employee.Id, "MonthlySalary", NumberText.Money(employee.MonthlySalary), NumberText.Money(salary));
        employee.SetMonthlySalary(salary, settings.DaysInMonth);
        employee.UpdatedAt = Now;
    }

    public void SetEmployeeDailyRate(Employee employee, long dailyRate)
    {
        EnsureAmount(dailyRate, allowZero: true);
        if (employee.DailyRate == dailyRate && employee.DailyRateIsManual)
            return;
        Log("Employee", employee.Id, "DailyRate", NumberText.Money(employee.DailyRate), NumberText.Money(dailyRate), "qo'lda");
        employee.SetDailyRateManual(dailyRate);
        employee.UpdatedAt = Now;
    }

    // ---------- Tarmoqlar (3.9) ----------

    /// <summary>Xodim tarmog'i: karta va ochiq oylardagi qatorlar (R9 kabi); yopiq oylar o'zgarmaydi.</summary>
    public void SetEmployeeBranch(Employee employee, Branch? branch, IEnumerable<PayrollPeriod> periods, Func<Guid?, string?> branchName)
    {
        var id = branch?.Id;
        if (employee.BranchId != id)
        {
            Log("Employee", employee.Id, "Branch", branchName(employee.BranchId), branch?.Name);
            employee.BranchId = id;
            employee.UpdatedAt = Now;
        }
        foreach (var period in periods.Where(p => !p.IsClosed))
        {
            if (period.FindLine(employee.Id) is { } line && line.BranchId != id)
            {
                Log("PayrollLine", line.Id, "Branch", branchName(line.BranchId), branch?.Name, period.Key.DisplayName);
                line.BranchId = id;
            }
        }
    }

    public Branch CreateBranch(string name, int sortOrder)
    {
        var branch = new Branch { Name = NormalizeName(name), SortOrder = sortOrder };
        Log("Branch", branch.Id, "Created", null, branch.Name);
        return branch;
    }

    public void RenameBranch(Branch branch, string name)
    {
        var normalized = NormalizeName(name);
        if (branch.Name == normalized)
            return;
        Log("Branch", branch.Id, "Name", branch.Name, normalized);
        branch.Name = normalized;
    }

    public void SetBranchActive(Branch branch, bool active)
    {
        if (branch.IsActive == active)
            return;
        Log("Branch", branch.Id, "IsActive", branch.IsActive.ToString(), active.ToString());
        branch.IsActive = active;
    }

    /// <summary>Faqat xodimi ham, oylarda qatori ham bo'lmasa.</summary>
    public void EnsureBranchUnused(Branch branch, IEnumerable<Employee> employees, IEnumerable<PayrollPeriod> periods)
    {
        if (employees.Any(e => e.BranchId == branch.Id) || periods.Any(p => p.Lines.Any(l => l.BranchId == branch.Id)))
            throw new DomainException(DomainError.BranchInUse, $"{branch.Name}: xodimlari bor — faqat nofaol qilish mumkin.", branch.Name);
        Log("Branch", branch.Id, "Deleted", branch.Name, null);
    }

    public void SetEmployeeActive(Employee employee, bool active)
    {
        if (employee.IsActive == active)
            return;
        Log("Employee", employee.Id, "IsActive", employee.IsActive.ToString(), active.ToString(), active ? "faol qilindi" : "nofaol qilindi");
        employee.IsActive = active;
        employee.UpdatedAt = Now;
    }

    private static string NormalizeName(string fullName)
    {
        var name = fullName?.Trim() ?? "";
        if (name.Length == 0)
            throw new DomainException(DomainError.EmptyName, "Ism bo'sh bo'lmasligi kerak.");
        return name.Length > 100 ? name[..100] : name;
    }

    private static void EnsureAmount(long value, bool allowZero)
    {
        if (value < 0 || (!allowZero && value == 0))
            throw new DomainException(DomainError.InvalidAmount, "Summa noto'g'ri.");
    }

    public void Log(string entity, Guid id, string field, string? oldValue, string? newValue, string? comment = null) =>
        Audit.Add(new AuditEntry
        {
            At = Now,
            Entity = entity,
            EntityId = id,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Comment = comment,
        });
}
