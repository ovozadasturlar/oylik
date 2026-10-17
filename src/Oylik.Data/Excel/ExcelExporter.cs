using ClosedXML.Excel;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>5.8: `Oylik_2026-10.xlsx`, Лист1, aynan 8 ustun, real formulalar.</summary>
public static class ExcelExporter
{
    public static readonly string[] Headers = ["№", "Оклад", "Ишчилар", "Сумма", "Кун", "Жами", "Аванс", "Колдик"];

    public static string DefaultFileName(MonthKey key) => $"Oylik_{key.Year:0000}-{key.Month:00}.xlsx";

    /// <param name="scheduleComment">H13.9: Schedule qatorining Жами katagi izohi (UI tilida), masalan `Soatbay: 172:30 × 33 333,33 so'm/soat`.</param>
    public static void Export(PayrollPeriod period, IEnumerable<PayrollLine> sortedLines, string path, Func<PayrollLine, string>? scheduleComment = null)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Лист1");

        for (var c = 0; c < Headers.Length; c++)
            ws.Cell(1, c + 1).Value = Headers[c];
        var header = ws.Range(1, 1, 1, Headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var r = 2;
        var no = 1;
        foreach (var line in sortedLines)
        {
            ws.Cell(r, 1).Value = no++;
            ws.Cell(r, 2).Value = line.MonthlySalary;
            ws.Cell(r, 3).Value = line.Employee?.FullName ?? "";
            if (line.IsSchedule)
            {
                // H13.9: Сумма va Кун bo'sh, Жами — qiymat (formula emas) + izoh; JAMI SUM'lari o'zgarmaydi
                ws.Cell(r, 6).Value = line.Total;
                if (scheduleComment?.Invoke(line) is { Length: > 0 } comment)
                    ws.Cell(r, 6).GetComment().AddText(comment);
            }
            else
            {
                ws.Cell(r, 4).Value = line.DailyRate;
                ws.Cell(r, 5).Value = line.DaysWorked;
                ws.Cell(r, 6).FormulaA1 = $"=E{r}*D{r}";
            }
            ws.Cell(r, 7).Value = line.AdvanceTotal;
            ws.Cell(r, 8).FormulaA1 = $"=F{r}-G{r}";
            r++;
        }

        var last = r - 1;
        ws.Cell(r, 3).Value = "JAMI";
        if (last >= 2)
        {
            foreach (var col in new[] { "E", "F", "G", "H" })
                ws.Cell($"{col}{r}").FormulaA1 = $"=SUM({col}2:{col}{last})";
        }
        else
        {
            foreach (var col in new[] { "E", "F", "G", "H" })
                ws.Cell($"{col}{r}").Value = 0;
        }
        ws.Row(r).Style.Font.Bold = true;
        ws.Range(r, 1, r, Headers.Length).Style.Border.TopBorder = XLBorderStyleValues.Thin;

        ws.Range(2, 2, r, 2).Style.NumberFormat.Format = "#,##0";
        ws.Range(2, 4, r, 4).Style.NumberFormat.Format = "#,##0";
        ws.Range(2, 5, r, 5).Style.NumberFormat.Format = "0.##";
        ws.Range(2, 6, r, 8).Style.NumberFormat.Format = "#,##0";

        ws.Column(1).Width = 5;
        ws.Column(2).Width = 13;
        ws.Column(3).Width = 28;
        ws.Column(4).Width = 11;
        ws.Column(5).Width = 7;
        ws.Columns(6, 8).Width = 14;
        ws.SheetView.FreezeRows(1);

        workbook.SaveAs(path);
    }
}
