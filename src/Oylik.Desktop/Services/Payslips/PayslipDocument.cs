using System.Reflection;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Oylik.Desktop.Services.Payslips;

/// <summary>
/// 5.11: hisob varaqasi va umumiy vedomost — A4 PDF (QuestPDF, Community litsenziya).
/// Shrift (Noto Sans, OFL) dasturga joylangan — lotin va kirill (ў, қ, ғ, ҳ) to'g'ri chiqadi;
/// zaxira shriftlar (Symbols 2, Math, Emoji) belgilar uchun; qolgan noma'lum belgilar <see cref="PdfText.Safe"/> bilan almashtiriladi.
/// </summary>
public static class PayslipPdf
{
    public const string FontFamily = "Noto Sans";
    private static readonly string[] Fallbacks = ["Noto Sans Symbols 2", "Noto Sans Math", "Noto Emoji"];
    private const string Accent = "#0B57D0";
    private const string Ink = "#1C1F23";
    private const string Muted = "#5B6270";
    private const string Line = "#DDE1E6";
    private const string Header = "#F3F5F8";
    private const string Zebra = "#F8F9FB";
    private const string Negative = "#C0262D";

    private static readonly Lazy<bool> Initialized = new(() =>
    {
        // QuestPDF Community litsenziyasi: yillik daromad $1M dan oshsa — tijorat litsenziyasi kerak (README).
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        // Matn oldindan tozalanadi (PdfText); baribir qolgan belgi bo'lsa — hujjat yiqilmasin, belgi bo'sh joy bo'lib chiqsin
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        var assembly = typeof(PayslipPdf).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            QuestPDF.Drawing.FontManager.RegisterFontFromStream(new MemoryStream(bytes));
            PdfText.Register(bytes);
        }
        return true;
    });

    public static void EnsureInitialized() => _ = Initialized.Value;

    public static IDocument Payslips(IReadOnlyList<PayslipModel> slips, PayrollSheetModel? sheet = null)
    {
        EnsureInitialized();
        slips = slips.Select(Safe).ToList();
        sheet = sheet is null ? null : Safe(sheet);
        return Document.Create(doc =>
        {
            foreach (var slip in slips)
                doc.Page(page => PayslipPage(page, slip));
            if (sheet is not null)
                doc.Page(page => SheetPage(page, sheet));
        }).WithMetadata(new DocumentMetadata
        {
            Title = slips.Count == 1 ? $"{Strings.Payslip} {slips[0].DocumentNumber}" : Strings.Payslips,
            Author = slips.FirstOrDefault()?.Company ?? sheet?.Company ?? "Oylik",
            Creator = "Oylik",
        });
    }

    public static byte[] Pdf(IReadOnlyList<PayslipModel> slips, PayrollSheetModel? sheet = null) => Payslips(slips, sheet).GeneratePdf();

    /// <summary>Foydalanuvchi kiritgan matnlar — shriftda yo'q belgilarsiz (PdfText).</summary>
    private static PayslipModel Safe(PayslipModel m) => m with
    {
        Company = PdfText.Safe(m.Company),
        Branch = PdfText.SafeOrNull(m.Branch),
        EmployeeName = PdfText.Safe(m.EmployeeName),
        ScheduleName = PdfText.SafeOrNull(m.ScheduleName),
        ScheduleSummary = PdfText.SafeOrNull(m.ScheduleSummary),
        Days = m.Days.Select(d => d with { Plan = PdfText.SafeOrNull(d.Plan), Note = PdfText.SafeOrNull(d.Note) }).ToList(),
        Advances = m.Advances.Select(a => a with { Note = PdfText.SafeOrNull(a.Note) }).ToList(),
    };

    private static PayrollSheetModel Safe(PayrollSheetModel s) => s with
    {
        Company = PdfText.Safe(s.Company),
        Branch = PdfText.SafeOrNull(s.Branch),
        Rows = s.Rows.Select(r => r with { Name = PdfText.Safe(r.Name), Measure = PdfText.Safe(r.Measure) }).ToList(),
    };

    /// <summary>Oldindan ko'rish va chop etish uchun sahifa rasmlari (PNG).</summary>
    public static IReadOnlyList<byte[]> Images(IReadOnlyList<PayslipModel> slips, PayrollSheetModel? sheet, int dpi) =>
        Payslips(slips, sheet).GenerateImages(new ImageGenerationSettings { RasterDpi = dpi, ImageFormat = ImageFormat.Png, ImageCompressionQuality = ImageCompressionQuality.High }).ToList();

    // ---------- umumiy bezak ----------

    private static void Setup(PageDescriptor page, bool isFinal)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(32);
        page.MarginVertical(28);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily([FontFamily, .. Fallbacks]).FontSize(9).FontColor(Ink));
        if (!isFinal)
        {
            page.Foreground().AlignCenter().AlignMiddle().Rotate(-35)
                .Text(Strings.PdfDraftWatermark).FontSize(64).Bold().FontColor("#1A0B57D0");
        }
    }

    private static void Footer(PageDescriptor page, string number, DateTimeOffset generatedAt) =>
        page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor(Line).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text($"{number} · {Strings.PdfGenerated}: {generatedAt:dd.MM.yyyy HH:mm}").FontSize(7.5f).FontColor(Muted);
            row.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });

    private static void Head(IContainer container, string title, string company, string? branch, string number, MonthKey month, bool isFinal) =>
        container.PaddingBottom(10).BorderBottom(1.5f).BorderColor(Accent).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(string.IsNullOrWhiteSpace(company) ? "—" : company).FontSize(14).Bold();
                if (!string.IsNullOrEmpty(branch))
                    c.Item().Text($"{Strings.PdfBranch}: {Strings.BranchName(branch)}").FontColor(Muted);
            });
            row.AutoItem().AlignRight().Column(c =>
            {
                c.Item().AlignRight().Text(title.ToUpperInvariant()).FontSize(13).Bold().FontColor(Accent);
                c.Item().AlignRight().Text($"{Strings.PdfNumber} {number}").SemiBold();
                c.Item().AlignRight().Text(Strings.Month(month)).FontColor(Muted);
                c.Item().AlignRight().PaddingTop(2).Element(e => Badge(e, isFinal));
            });
        });

    private static void Badge(IContainer container, bool isFinal) =>
        container.Background(isFinal ? "#E3F4E8" : "#FFF7ED").PaddingHorizontal(6).PaddingVertical(2)
            .Text(isFinal ? Strings.PdfFinal : Strings.PdfDraft).FontSize(8).SemiBold().FontColor(isFinal ? "#145C2B" : "#8A3B00");

    private static void Section(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(12).PaddingBottom(4).Text(title.ToUpperInvariant()).FontSize(8.5f).Bold().FontColor(Accent).LetterSpacing(0.04f);

    private static void KeyValues(ColumnDescriptor col, IEnumerable<(string Key, string Value)> rows, float keyWidth = 120) =>
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(keyWidth);
                c.RelativeColumn();
            });
            foreach (var (key, value) in rows)
            {
                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(2).Text(key).FontColor(Muted);
                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(2).Text(value).SemiBold();
            }
        });

    private static IContainer HeadCell(IContainer c) => c.Background(Header).BorderBottom(0.75f).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(2.5f);

    private static IContainer BodyCell(IContainer c, bool zebra) =>
        c.Background(zebra ? Zebra : Colors.White).BorderBottom(0.5f).BorderColor(Line).PaddingVertical(1.6f).PaddingHorizontal(2.5f);

    // ---------- hisob varaqasi ----------

    private static void PayslipPage(PageDescriptor page, PayslipModel m)
    {
        Setup(page, m.IsFinal);
        page.Header().Element(c => Head(c, Strings.Payslip, m.Company, m.Branch, m.DocumentNumber, m.Month, m.IsFinal));
        Footer(page, m.DocumentNumber, m.GeneratedAt);
        page.Content().PaddingTop(2).Column(col =>
        {
            var terms = new List<(string, string)> { (Strings.PdfPayMode, Strings.PayModeName(m.Mode)), (Strings.PdfSalary, NumberText.Money(m.MonthlySalary)) };
            if (m.Mode == PayMode.Schedule)
            {
                terms.Add((Strings.PdfSchedule, m.ScheduleName ?? Strings.ScheduleNone));
                terms.Add((Strings.PdfPlan, Strings.PdfPlanValue(m.PlannedDays, m.PlannedMinutes)));
                terms.Add((Strings.PdfHourlyRate, m.PlannedMinutes > 0 ? Strings.PdfRateFormula(m.MonthlySalary, m.PlannedMinutes, m.HourlyRate) : Strings.NoPlanWarning));
                terms.Add((Strings.PdfGrace, Strings.PdfMinutes(m.GraceMinutes)));
                terms.Add((Strings.PdfOvertime, Strings.OvertimePolicyName(m.OvertimePolicy, m.OvertimeValue)));
            }
            else
            {
                terms.Add((Strings.PdfDailyRate, Strings.PdfDailyFormula(m.MonthlySalary, m.DaysInMonth, m.DailyRate)));
            }
            // Xodim va shartlar yonma-yon — bir oylik jadval A4 ga sig'ishi uchun
            col.Item().Row(row =>
            {
                row.RelativeItem(4).Column(left =>
                {
                    Section(left, Strings.PdfEmployee);
                    KeyValues(left,
                    [
                        (Strings.PdfEmployee, m.EmployeeName),
                        (Strings.ColTabelNo, m.TabelNo.ToString()),
                    ], 70);
                });
                row.ConstantItem(16);
                row.RelativeItem(6).Column(right =>
                {
                    Section(right, Strings.PdfTerms);
                    KeyValues(right, terms, 116);
                });
            });

            Section(col, Strings.PdfDays);
            if (m.Days.Count == 0)
                col.Item().Text(Strings.PdfNoAttendance).FontColor(Muted);
            else if (m.Mode == PayMode.Schedule)
                ScheduleDays(col, m);
            else
                DailyDays(col, m);
            if (m.Days.Any(d => d.IsCorrected))
                col.Item().PaddingTop(3).Text(Strings.PdfCorrectedLegend).FontSize(7.5f).FontColor(Muted);

            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem(11).Column(left =>
                {
                    Section(left, Strings.PdfAdvances);
                    Advances(left, m);
                    Section(left, Strings.PdfExplanation);
                    left.Item().Background(Header).Padding(7).Column(e => Explanation(e, m));
                });
                row.ConstantItem(16);
                row.RelativeItem(10).Column(right =>
                {
                    Section(right, Strings.PdfTotals);
                    Totals(right, m);
                });
            });

            col.Item().PaddingTop(18).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span($"{Strings.PdfSignAccountant}  ").FontColor(Muted);
                    t.Span("______________________");
                });
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span($"{Strings.PdfSignEmployee}  ").FontColor(Muted);
                    t.Span("________________   ");
                    t.Span($"{Strings.PdfSignDate}  ").FontColor(Muted);
                    t.Span("__________");
                });
            });
        });
    }

    /// <summary>Hisoblash formulasi oddiy tilda (5.11, 5-band).</summary>
    private static void Explanation(ColumnDescriptor e, PayslipModel m)
    {
        if (m.Mode == PayMode.Schedule)
        {
            e.Item().Text(m.PlannedMinutes > 0
                ? Strings.PdfScheduleExplanation(new PayslipLike(m.MonthlySalary, m.PlannedMinutes, m.WorkedMinutes, m.HourlyRate, m.BasePay))
                : Strings.NoPlanWarning);
            if (m.OvertimeMinutes > 0)
                e.Item().Text(Strings.PdfOvertimeExplanation(m.OvertimeMinutes, Strings.OvertimePolicyName(m.OvertimePolicy, m.OvertimeValue), m.OvertimePay));
        }
        else
        {
            e.Item().Text(Strings.PdfDailyExplanation(m.DailyRate, m.DaysWorked, m.Total));
        }
        e.Item().Text(Strings.PdfRemainderExplanation(m.Total, m.AdvanceTotal, m.Remainder)).SemiBold();
        if (m.IsFinal && m.Mode == PayMode.Schedule)
            e.Item().PaddingTop(2).Text(Strings.PdfFrozenNote).FontSize(7.5f).FontColor(Muted);
    }

    private static string Time(DateTimeOffset? t) => t is { } v ? $"{v.ToLocalTime():HH:mm}" : "";

    private static void ScheduleDays(ColumnDescriptor col, PayslipModel m) =>
        col.Item().DefaultTextStyle(t => t.FontSize(7.2f)).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(34);  // sana
                c.ConstantColumn(20);  // kun
                c.ConstantColumn(54);  // reja
                c.ConstantColumn(34);  // keldi
                c.ConstantColumn(34);  // ketdi
                c.ConstantColumn(48);  // ishlangan
                c.ConstantColumn(46);  // kechikish
                c.ConstantColumn(50);  // erta
                c.ConstantColumn(44);  // ortiqcha
                c.ConstantColumn(52);  // summa
                c.RelativeColumn();    // izoh
            });
            table.Header(h =>
            {
                foreach (var title in new[] { Strings.PdfColDate, Strings.PdfColWeekDay, Strings.PdfColPlan, Strings.PdfColIn, Strings.PdfColOut,
                             Strings.PdfColWorked, Strings.PdfColLate, Strings.PdfColEarly, Strings.PdfColOvertime, Strings.PdfColPay, Strings.PdfColNote })
                    h.Cell().Element(HeadCell).Text(title).FontSize(6.6f).SemiBold().FontColor(Muted);
            });
            var i = 0;
            foreach (var d in m.Days)
            {
                var zebra = i++ % 2 == 1;
                IContainer Cell(IContainer c) => BodyCell(c, zebra);
                string Mins(int v) => v > 0 ? Strings.HoursMinutes(v) : "";
                table.Cell().Element(Cell).Text($"{d.Date:dd.MM}{(d.IsCorrected ? "*" : "")}");
                table.Cell().Element(Cell).Text(Strings.WeekDayShort(d.Date.DayOfWeek)).FontColor(Muted);
                table.Cell().Element(Cell).Text(d.Plan ?? Strings.PdfDayOff).FontColor(d.Plan is null ? Muted : Ink);
                table.Cell().Element(Cell).Text(Time(d.In));
                table.Cell().Element(Cell).Text(Time(d.Out));
                table.Cell().Element(Cell).AlignRight().Text(Mins(d.WorkedMinutes)).SemiBold();
                table.Cell().Element(Cell).AlignRight().Text(Mins(d.LateMinutes)).FontColor(Negative);
                table.Cell().Element(Cell).AlignRight().Text(Mins(d.EarlyLeaveMinutes)).FontColor(Negative);
                table.Cell().Element(Cell).AlignRight().Text(Mins(d.OvertimeMinutes)).FontColor(Accent);
                table.Cell().Element(Cell).AlignRight().Text(d.Pay > 0 ? NumberText.Money(d.Pay) : "");
                table.Cell().Element(Cell).Text(d.Excused is { } x ? $"{Strings.ExcusedName(x)}: {d.Note}" : d.Note ?? "").FontColor(Muted);
            }
            IContainer Total(IContainer c) => c.Background(Header).BorderTop(0.75f).BorderColor(Muted).PaddingVertical(3).PaddingHorizontal(3);
            table.Cell().ColumnSpan(5).Element(Total).Text(Strings.PdfTotals).Bold();
            table.Cell().Element(Total).AlignRight().Text(Strings.HoursMinutes(m.WorkedMinutes)).Bold();
            table.Cell().Element(Total).AlignRight().Text(Strings.HoursMinutes(m.LateMinutes)).Bold().FontColor(Negative);
            table.Cell().Element(Total).AlignRight().Text(Strings.HoursMinutes(m.EarlyLeaveMinutes)).Bold().FontColor(Negative);
            table.Cell().Element(Total).AlignRight().Text(Strings.HoursMinutes(m.OvertimeMinutes)).Bold().FontColor(Accent);
            table.Cell().Element(Total).AlignRight().Text(NumberText.Money(m.BasePay)).Bold();
            table.Cell().Element(Total).Text("");
        });

    private static void DailyDays(ColumnDescriptor col, PayslipModel m) =>
        col.Item().DefaultTextStyle(t => t.FontSize(7.5f)).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(44);
                c.ConstantColumn(26);
                c.ConstantColumn(44);
                c.ConstantColumn(44);
                c.ConstantColumn(56);
                c.RelativeColumn();
            });
            table.Header(h =>
            {
                foreach (var title in new[] { Strings.PdfColDate, Strings.PdfColWeekDay, Strings.PdfColIn, Strings.PdfColOut, Strings.PdfColWorked, Strings.PdfColNote })
                    h.Cell().Element(HeadCell).Text(title).SemiBold().FontColor(Muted);
            });
            var i = 0;
            foreach (var d in m.Days)
            {
                var zebra = i++ % 2 == 1;
                IContainer Cell(IContainer c) => BodyCell(c, zebra);
                table.Cell().Element(Cell).Text($"{d.Date:dd.MM}{(d.IsCorrected ? "*" : "")}");
                table.Cell().Element(Cell).Text(Strings.WeekDayShort(d.Date.DayOfWeek)).FontColor(Muted);
                table.Cell().Element(Cell).Text(Time(d.In));
                table.Cell().Element(Cell).Text(Time(d.Out));
                table.Cell().Element(Cell).AlignRight().Text(d.Out is null && d.WorkedMinutes == 0 ? "" : Strings.HoursMinutes(d.WorkedMinutes)).SemiBold();
                table.Cell().Element(Cell).Text(d.Note ?? "").FontColor(Muted);
            }
        });

    private static void Advances(ColumnDescriptor col, PayslipModel m)
    {
        if (m.Advances.Count == 0)
        {
            col.Item().Text(Strings.PdfNoAdvances).FontColor(Muted);
            return;
        }
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(58);
                c.ConstantColumn(64);
                c.ConstantColumn(60);
                c.RelativeColumn();
            });
            table.Header(h =>
            {
                foreach (var title in new[] { Strings.AdvDate, Strings.AdvAmount, Strings.AdvKind, Strings.AdvNote })
                    h.Cell().Element(HeadCell).Text(title).SemiBold().FontColor(Muted);
            });
            var i = 0;
            foreach (var a in m.Advances)
            {
                var zebra = i++ % 2 == 1;
                IContainer Cell(IContainer c) => BodyCell(c, zebra);
                table.Cell().Element(Cell).Text($"{a.Date:dd.MM.yyyy}");
                table.Cell().Element(Cell).AlignRight().Text(NumberText.Money(a.Amount));
                table.Cell().Element(Cell).Text(a.Kind == AdvanceKind.CarryOverDebt ? Strings.KindDebt : Strings.KindCash);
                table.Cell().Element(Cell).Text(a.Note ?? "").FontColor(Muted);
            }
        });
    }

    private static void Totals(ColumnDescriptor col, PayslipModel m)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.ConstantColumn(90);
            });
            void Row(string key, string value, bool strong = false, string? color = null)
            {
                table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(2).Text(key).FontColor(strong ? Ink : Muted);
                var text = table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(2).AlignRight().Text(value);
                if (strong) text.Bold();
                if (color is not null) text.FontColor(color);
            }
            if (m.Mode == PayMode.Schedule)
            {
                Row(Strings.PdfPlanActual, $"{Strings.HoursMinutes(m.PlannedMinutes)} / {Strings.HoursMinutes(m.WorkedMinutes)}");
                Row(Strings.PdfLateEarly, $"{Strings.HoursMinutes(m.LateMinutes)} / {Strings.HoursMinutes(m.EarlyLeaveMinutes)}");
                Row(Strings.PdfBasePay, NumberText.Money(m.BasePay));
                Row(Strings.PdfOvertimePay, NumberText.Money(m.OvertimePay));
            }
            else
            {
                Row(Strings.PdfDaysWorked, NumberText.Days(m.DaysWorked));
            }
            Row(Strings.PdfAccrued, NumberText.Money(m.Total), strong: true);
            Row(Strings.PdfAdvances, "−" + NumberText.Money(m.AdvanceTotal));
            if (m.CarryOverDebt > 0)
                Row("   " + Strings.PdfCarryDebt, NumberText.Money(m.CarryOverDebt));
        });
        var negative = m.Remainder < 0;
        col.Item().PaddingTop(6).Background(negative ? "#FDECEC" : "#E8F0FE").Padding(8).Row(row =>
        {
            row.RelativeItem().Text(negative ? Strings.PdfDebt : Strings.PdfToPay).Bold();
            row.AutoItem().Text(NumberText.Money(m.Remainder)).FontSize(13).Bold().FontColor(negative ? Negative : Accent);
        });
    }

    // ---------- umumiy vedomost ----------

    private static void SheetPage(PageDescriptor page, PayrollSheetModel s)
    {
        Setup(page, s.IsFinal);
        page.Header().Element(c => Head(c, Strings.PayrollSheet, s.Company, s.Branch, s.DocumentNumber, s.Month, s.IsFinal));
        Footer(page, s.DocumentNumber, s.GeneratedAt);
        page.Content().PaddingTop(8).DefaultTextStyle(t => t.FontSize(8.5f)).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(22);
                c.ConstantColumn(38);
                c.RelativeColumn(3);
                c.ConstantColumn(52);
                c.ConstantColumn(60);
                c.ConstantColumn(62);
                c.ConstantColumn(62);
                c.ConstantColumn(80);
            });
            table.Header(h =>
            {
                foreach (var title in new[] { Strings.ColNo, Strings.ColTabelNo, Strings.PdfColName, Strings.PdfColMeasure, Strings.PdfColAccrued,
                             Strings.PdfAdvances, Strings.PdfColRemainder, Strings.PdfSignature })
                    h.Cell().Element(HeadCell).Text(title).SemiBold().FontColor(Muted);
            });
            var i = 0;
            foreach (var r in s.Rows)
            {
                var zebra = i++ % 2 == 1;
                IContainer Cell(IContainer c) => BodyCell(c, zebra).MinHeight(20);
                table.Cell().Element(Cell).Text(r.No.ToString());
                table.Cell().Element(Cell).Text(r.TabelNo.ToString()).FontColor(Muted);
                table.Cell().Element(Cell).Text(r.Name);
                table.Cell().Element(Cell).AlignRight().Text(r.Measure);
                table.Cell().Element(Cell).AlignRight().Text(NumberText.Money(r.Total));
                table.Cell().Element(Cell).AlignRight().Text(NumberText.Money(r.Advance));
                table.Cell().Element(Cell).AlignRight().Text(NumberText.Money(r.Remainder)).SemiBold().FontColor(r.Remainder < 0 ? Negative : Ink);
                table.Cell().Element(Cell).BorderBottom(0.75f).BorderColor(Muted).Text("");
            }
            IContainer Total(IContainer c) => c.Background(Header).BorderTop(1).BorderColor(Ink).PaddingVertical(4).PaddingHorizontal(3);
            table.Cell().ColumnSpan(4).Element(Total).Text(Strings.Total).Bold();
            table.Cell().Element(Total).AlignRight().Text(NumberText.Money(s.Total)).Bold();
            table.Cell().Element(Total).AlignRight().Text(NumberText.Money(s.Advance)).Bold();
            table.Cell().Element(Total).AlignRight().Text(NumberText.Money(s.Remainder)).Bold();
            table.Cell().Element(Total).Text("");
        });
    }
}
