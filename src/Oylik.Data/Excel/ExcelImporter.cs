using ClosedXML.Excel;
using Oylik.Core.Domain;

namespace Oylik.Data;

public sealed record ImportRow(
    int RowNumber,
    string FullName,
    long MonthlySalary,
    long DailyRate,
    bool DailyRateIsManual,
    decimal DaysWorked,
    long Advance,
    IReadOnlyList<ImportIssue> Warnings);

public enum ImportIssueKind
{
    /// <summary>Оклад bo'sh, Сумма bor → Oklad 0, kunlik qo'lda (Daily).</summary>
    SalaryEmpty,
    /// <summary>Оклад va Сумма bo'sh → 0.</summary>
    SalaryAndDailyEmpty,
    /// <summary>Сумма bo'sh → Salary ÷ DaysInMonth = Daily.</summary>
    DailyComputed,
    /// <summary>Кун noto'g'ri (Value) — 0 olindi.</summary>
    InvalidDays,
    /// <summary>Column raqam emas (Text) — e'tiborsiz qoldirildi.</summary>
    NotANumber,
}

/// <summary>Import ogohlantirishi yoki xatosi — matni UI tilida Desktop'da tuziladi.</summary>
public sealed record ImportIssue(
    ImportIssueKind Kind,
    int Row,
    string? Column = null,
    string? Text = null,
    decimal Value = 0,
    long Salary = 0,
    long Daily = 0,
    int DaysInMonth = 0);

public sealed record ImportPreview(
    string FilePath,
    string SheetName,
    IReadOnlyList<ImportRow> Rows,
    int SkippedEmpty,
    IReadOnlyList<ImportIssue> Errors)
{
    public IEnumerable<ImportRow> Warnings => Rows.Where(r => r.Warnings.Count > 0);
}

public sealed record ImportOptions(MonthKey? Period, bool LinkDuplicates = true);

public sealed record ImportResult(int Created, int Linked, int Lines);

/// <summary>5.7: mavjud Excel (Лист1, A1:H…) ni o'qish.</summary>
public static class ExcelImporter
{
    private const int ColSalary = 2, ColName = 3, ColDaily = 4, ColDays = 5, ColAdvance = 7;

    public static ImportPreview Read(string path, int daysInMonth)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "Лист1") ?? workbook.Worksheet(1);

        var rows = new List<ImportRow>();
        var errors = new List<ImportIssue>();
        int skipped = 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var r = 2; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var name = row.Cell(ColName).GetString().Trim();
            if (name.Length == 0)
            {
                skipped++;
                continue;
            }
            if (name.Length > 100)
                name = name[..100];

            var warnings = new List<ImportIssue>();
            var salaryRaw = ReadNumber(row.Cell(ColSalary), r, "Оклад", errors);
            var dailyRaw = ReadNumber(row.Cell(ColDaily), r, "Сумма", errors);
            var daysRaw = ReadNumber(row.Cell(ColDays), r, "Кун", errors);
            var advanceRaw = ReadNumber(row.Cell(ColAdvance), r, "Аванс", errors);

            long salary = 0;
            if (salaryRaw is { } s)
                salary = s < ShortInput.SalaryThreshold
                    ? PayrollMath.RoundToSum(s * ShortInput.SalaryMultiplier)
                    : PayrollMath.RoundToSum(s);

            long daily;
            bool manual;
            if (dailyRaw is { } d)
            {
                daily = PayrollMath.RoundToSum(d);
                manual = daily != PayrollMath.DailyRate(salary, daysInMonth);
                if (salaryRaw is null)
                    warnings.Add(new(ImportIssueKind.SalaryEmpty, r, Daily: daily));
            }
            else
            {
                daily = PayrollMath.DailyRate(salary, daysInMonth);
                manual = false;
                warnings.Add(salaryRaw is null
                    ? new(ImportIssueKind.SalaryAndDailyEmpty, r)
                    : new(ImportIssueKind.DailyComputed, r, Salary: salary, Daily: daily, DaysInMonth: daysInMonth));
            }

            decimal days = 0;
            if (daysRaw is { } dd)
            {
                if (PayrollMath.IsValidDaysWorked(dd))
                    days = dd;
                else
                    errors.Add(new(ImportIssueKind.InvalidDays, r, "Кун", Value: dd));
            }

            long advance = advanceRaw is { } a && a > 0 ? PayrollMath.RoundToSum(a) : 0;

            rows.Add(new ImportRow(r, name, salary, daily, manual, days, advance, warnings));
        }

        return new ImportPreview(path, sheet.Name, rows, skipped, errors);
    }

    private static decimal? ReadNumber(IXLCell cell, int row, string column, List<ImportIssue> errors)
    {
        if (cell.IsEmpty())
            return null;
        if (cell.TryGetValue<decimal>(out var value))
            return value;
        var text = cell.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (NumberText.TryParseDecimal(text, out value))
            return value;
        errors.Add(new(ImportIssueKind.NotANumber, row, column, text));
        return null;
    }
}
