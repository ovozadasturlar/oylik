using Microsoft.EntityFrameworkCore;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>3.9 Tarmoqlar va Xodimlar jadvalidagi (5.4) to'g'ridan-to'g'ri tahrir.</summary>
public sealed partial class PayrollService
{
    public IReadOnlyList<Branch> Branches => _db.Branches.Local.OrderBy(b => b.SortOrder).ThenBy(b => b.Name).ToList();

    public Branch? FindBranch(Guid? id) => id is { } x ? _db.Branches.Local.FirstOrDefault(b => b.Id == x) : null;

    /// <summary>Belgilanmagan tarmoqli xodim yoki qator bormi (filtrda «Belgilanmagan» faqat shunda ko'rinadi).</summary>
    public bool HasUnassigned => _db.Employees.Local.Any(e => e.BranchId is null) || _db.Lines.Local.Any(l => l.BranchId is null);

    private Task LoadBranchesAsync() => _db.Branches.LoadAsync();

    private string? BranchName(Guid? id) => FindBranch(id)?.Name;

    public Task<Branch> CreateBranchAsync(string name) => RunAsync(e =>
    {
        var order = _db.Branches.Local.Select(b => b.SortOrder).DefaultIfEmpty(0).Max() + 1;
        var branch = e.CreateBranch(name, order);
        _db.Branches.Add(branch);
        return branch;
    });

    public Task RenameBranchAsync(Branch branch, string name) => RunAsync(e => e.RenameBranch(branch, name));

    public Task SetBranchActiveAsync(Branch branch, bool active) => RunAsync(e => e.SetBranchActive(branch, active));

    public bool IsBranchUsed(Branch branch) =>
        _db.Employees.Local.Any(x => x.BranchId == branch.Id) || _db.Lines.Local.Any(l => l.BranchId == branch.Id);

    public Task DeleteBranchAsync(Branch branch) => RunAsync(e =>
    {
        e.EnsureBranchUnused(branch, _db.Employees.Local, _db.Periods.Local);
        _db.Branches.Remove(branch);
    });

    // ---------- Xodimlar jadvali (5.4): har katak alohida saqlanadi ----------

    public Task SetEmployeeBranchAsync(Employee employee, Guid? branchId) =>
        RunAsync(e => e.SetEmployeeBranch(employee, FindBranch(branchId), _db.Periods.Local, BranchName));

    public Task SetEmployeeNoteAsync(Employee employee, string? note) => RunAsync(e =>
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > 100 })
            trimmed = trimmed[..100];
        if (employee.Note == trimmed)
            return;
        e.Log("Employee", employee.Id, "Note", employee.Note, trimmed);
        employee.Note = trimmed;
    });

    public Task SetEmployeeActiveAsync(Employee employee, bool active) => RunAsync(e => e.SetEmployeeActive(employee, active));

    /// <summary>Ochiq oylardagi shu xodim qatorlari (R9: karta bilan birga yangilanadi).</summary>
    private IEnumerable<(PayrollPeriod Period, PayrollLine Line)> OpenLines(Employee employee) =>
        _db.Periods.Local.Where(p => !p.IsClosed)
            .Select(p => (Period: p, Line: p.FindLine(employee.Id)))
            .Where(x => x.Line is not null)
            .Select(x => (x.Period, x.Line!))
            .ToList();

    /// <summary>R9 + R1: Oklad — karta va ochiq oy qatori; yopiq oylar o'zgarmaydi.</summary>
    public Task SetEmployeeSalaryAsync(Employee employee, long salary) => RunAsync(e =>
    {
        var open = OpenLines(employee).ToList();
        if (open.Count == 0)
            e.SetEmployeeSalary(employee, salary);
        foreach (var (period, line) in open)
            e.SetMonthlySalary(period, line, employee, salary);
    });

    /// <summary>R9 + R1: Kunlik qo'lda — karta va ochiq oy qatori.</summary>
    public Task SetEmployeeDailyRateAsync(Employee employee, long dailyRate) => RunAsync(e =>
    {
        var open = OpenLines(employee).ToList();
        if (open.Count == 0)
            e.SetEmployeeDailyRate(employee, dailyRate);
        foreach (var (period, line) in open)
            e.SetDailyRate(period, line, employee, dailyRate);
    });

    /// <summary>Kunlikni qayta avtomatik (Oklad ÷ N) — karta va ochiq oy qatori.</summary>
    public Task ResetEmployeeDailyRateAsync(Employee employee) => RunAsync(e =>
    {
        var open = OpenLines(employee).ToList();
        if (open.Count == 0)
        {
            if (!employee.DailyRateIsManual)
                return;
            employee.ResetDailyRateToAuto(Settings.DaysInMonth);
            e.Log("Employee", employee.Id, "DailyRateIsManual", "true", "false");
        }
        foreach (var (period, line) in open)
            e.ResetDailyRate(period, line, employee);
    });
}
