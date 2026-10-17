using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;
using Oylik.Desktop.Services.Payslips;
using Oylik.Desktop.ViewModels;
using Oylik.Desktop.Views;

namespace Oylik.Desktop.Tests;

/// <summary>Soxta dialoglar: H11 tanlovi va tasdiqlar.</summary>
internal sealed class FakeDialogs : IDialogs
{
    public int Choice { get; set; } = 1;
    public List<string> Asked { get; } = [];

    public Task<bool> ConfirmAsync(string message, string? yes = null, string? no = null) { Asked.Add(message); return Task.FromResult(true); }
    public Task MessageAsync(string title, string message) => Task.CompletedTask;
    public Task<int> ChooseAsync(string title, string message, params string[] options) { Asked.Add(message); return Task.FromResult(Choice); }
    public Task<string?> PickOpenExcelAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickSaveExcelAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    public Task ShowDialogAsync(Window dialog) => Task.CompletedTask;
    public Task<string?> PromptAsync(string title, string message, string? watermark = null) => Task.FromResult<string?>("izoh");
    public Task<string[]?> PromptFieldsAsync(string title, string message, params (string Label, string Value, string? Watermark)[] fields) =>
        Task.FromResult<string[]?>(fields.Select(f => f.Value).ToArray());
}

/// <summary>2.2: hisob varaqasi (5.11), Excel eksport (H13.9), Schedule qatori (H9), H11 ogohlantirish.</summary>
public sealed class PayslipTests : IAsyncLifetime
{
    private static readonly TimeSpan Uz = TimeSpan.FromHours(5);
    private readonly string _home = Path.Combine(Path.GetTempPath(), "oylik-payslip-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private PayrollService _service = null!;
    private MainWindowViewModel _main = null!;
    private Employee _guard = null!;
    private Employee _office = null!;
    private PayrollPeriod _oct = null!;

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, Uz);

    public async Task InitializeAsync()
    {
        var paths = new AppPaths(_home);
        paths.EnsureCreated();
        _service = new PayrollService(paths, new FixedClock(new DateTimeOffset(2026, 10, 13, 12, 0, 0, Uz)));
        _main = new MainWindowViewModel(_service, paths, _dialogs) { NoSync = true };
        await _main.InitializeAsync();
        await _service.SaveSettingsAsync(s => s.CompanyName = "Demo korxona");
        _guard = await _service.CreateEmployeeAsync("Гайрат", 6_000_000, null, null, Branch.DefaultFirstId);
        _office = await _service.CreateEmployeeAsync("Бекзод", 6_000_000, null, null);
        await _service.SetPayModeAsync(_guard, PayMode.Schedule);
        await _service.AssignScheduleAsync(_guard, _service.FindSchedule(WorkSchedule.SampleDailyId), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));
        await _service.SetEmployeeOvertimeAsync(_guard, OvertimePolicy.Multiplier, 1.5m);
        _oct = await _service.CreatePeriodAsync(new MonthKey(2026, 10));
        await _service.AddManualEventAsync(_guard, AttendanceEventType.In, At(1, 8, 20), "telefon o'chgan");
        await _service.AddManualEventAsync(_guard, AttendanceEventType.Out, At(2, 8, 40), "telefon o'chgan");
        await _service.AddAdvanceAsync(_oct, _oct.FindLine(_guard.Id)!, new DateOnly(2026, 10, 5), 200_000, "Bayram");
        await _service.SetDaysWorkedAsync(_oct, _oct.FindLine(_office.Id)!, 10);
        _main.Payroll.Load();
    }

    public Task DisposeAsync()
    {
        Strings.SetLanguage(UiLanguage.UzLatn);
        _main.Dispose();
        _service.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_home, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("UZ", TimeSpan.FromHours(5), "UZ", "UZ");
    }

    [Fact]
    public void Payslip_model_matches_line_and_marks_manual_corrections()
    {
        var line = _oct.FindLine(_guard.Id)!;
        var m = _main.Payroll.BuildPayslip(_oct, line);

        m.DocumentNumber.Should().Be($"OY-2026-10-{_guard.TabelNo:0000}");
        m.IsFinal.Should().BeFalse();
        m.Mode.Should().Be(PayMode.Schedule);
        m.Total.Should().Be(line.Total).And.Be(line.BasePay + line.OvertimePay);
        m.LateMinutes.Should().Be(20);
        m.OvertimeMinutes.Should().Be(40);
        m.PlannedMinutes.Should().Be(16 * 1440);
        m.Remainder.Should().Be(line.Total - 200_000);
        m.ScheduleName.Should().Be("Sutkalik 08:00–08:00, kun ora");
        var first = m.Days.Single(d => d.Date == new DateOnly(2026, 10, 1));
        first.IsCorrected.Should().BeTrue();
        first.Note.Should().Contain("telefon o'chgan");
        first.WorkedMinutes.Should().Be(1440 - 20);
        m.Days.Count(d => d.Plan is not null).Should().Be(16);
        m.Days.Sum(d => d.Pay).Should().Be(m.BasePay, "kunlik Summa Σ = Jami (largest remainder)");
    }

    [Theory]
    [InlineData(UiLanguage.UzLatn)]
    [InlineData(UiLanguage.UzCyrl)]
    [InlineData(UiLanguage.Ru)]
    public void Pdf_renders_in_every_language_with_embedded_font(UiLanguage language)
    {
        Strings.SetLanguage(language);
        var slips = _oct.Lines.Select(l => _main.Payroll.BuildPayslip(_oct, l)).ToList();
        var sheet = PayslipBuilder.Sheet(_service, _oct, _oct.Lines, null, Strings.PdfSheetMeasure);

        // QuestPDF: matndagi belgi shriftda bo'lmasa — xato (ў, қ, ғ, ҳ, ё, ъ tekshiriladi)
        var pdf = PayslipPdf.Pdf(slips, sheet);
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        Encoding.Latin1.GetString(pdf).Should().Contain("NotoSans", "shrift PDF ichiga joylangan");
        PayslipPdf.Images(slips, sheet, 40).Should().HaveCount(3, "2 ta hisob varaqasi (har biri 1 sahifa) + vedomost");
    }

    /// <summary>Foydalanuvchi ma'lumotida emoji, ✕ ✓, arab, xitoy — PDF xatosiz (glyph tekshiruvi yoqilgan holda ham).</summary>
    [Theory]
    [InlineData(UiLanguage.UzLatn)]
    [InlineData(UiLanguage.UzCyrl)]
    [InlineData(UiLanguage.Ru)]
    public async Task Pdf_never_fails_on_exotic_characters_in_user_data(UiLanguage language)
    {
        const string exotic = "😀 ✕ ✓ ✔️ 👨‍👩‍👧 علي 张伟 ★ ↺ ≥ → \u0007";
        await _service.SaveSettingsAsync(s => s.CompanyName = "Demo 😀 " + exotic);
        var odd = await _service.CreateEmployeeAsync("Рустам " + exotic, 6_000_000, null, null, Branch.DefaultFirstId);
        await _service.AddExistingEmployeeAsync(_oct, odd);
        await _service.SetPayModeAsync(odd, PayMode.Schedule);
        await _service.AssignScheduleAsync(odd, _service.FindSchedule(WorkSchedule.SampleDailyId), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));
        var voided = await _service.AddManualEventAsync(odd, AttendanceEventType.In, At(1, 8), "келмаган эдида " + exotic);
        await _service.VoidEventAsync(voided, "sabab " + exotic);
        await _service.AddManualEventAsync(odd, AttendanceEventType.In, At(3, 8), Strings.ConfirmedNote + " " + exotic);
        await _service.AddManualEventAsync(odd, AttendanceEventType.Out, At(3, 14), exotic);
        await _service.AddAdvanceAsync(_oct, _oct.FindLine(odd.Id)!, new DateOnly(2026, 10, 5), 100_000, exotic);
        var dailyOdd = await _service.CreateEmployeeAsync("Ali " + exotic, 6_000_000, null, null);
        await _service.SetDaysWorkedAsync(_oct, await _service.AddExistingEmployeeAsync(_oct, dailyOdd), 3);

        Strings.SetLanguage(language);
        var slips = _oct.Lines.Select(l => _main.Payroll.BuildPayslip(_oct, l)).ToList();
        var sheet = PayslipBuilder.Sheet(_service, _oct, _oct.Lines, "1-tarmoq " + exotic, Strings.PdfSheetMeasure);
        slips.Single(s => s.EmployeeName.StartsWith("Рустам")).Days.Should().Contain(d => d.Note != null && d.Note.StartsWith("× 08:00"));

        PayslipPdf.EnsureInitialized();
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = true;
        try
        {
            var pdf = PayslipPdf.Pdf(slips, sheet);
            Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
            PayslipPdf.Images(slips, sheet, 30).Should().NotBeEmpty();
        }
        finally
        {
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        }
    }

    [Fact]
    public void Pdf_text_keeps_covered_symbols_and_replaces_the_rest()
    {
        PayslipPdf.EnsureInitialized();
        PdfText.IsCovered("Ўзбекистон қўғҳ ЁЪ ×−—…№·÷ ✕ ✓ ↺ ≥ ≤ → • 😀 ★").Should().BeTrue("Noto Sans + Symbols 2 + Math + Emoji");
        PdfText.Safe("张伟").Should().Be("??");
        PdfText.Safe("علي").Should().Be("???");
        PdfText.Safe("a\u0007b").Should().Be("ab");
        PdfText.Safe("Рустам 😀").Should().Be("Рустам 😀");
    }

    [Fact]
    public void Payroll_sheet_totals_and_measure()
    {
        var sheet = PayslipBuilder.Sheet(_service, _oct, _oct.Lines, "1-tarmoq", Strings.PdfSheetMeasure);
        sheet.Total.Should().Be(_oct.Lines.Sum(l => l.Total));
        sheet.Rows.Single(r => r.Name == "Гайрат").Measure.Should().EndWith(" s");
        sheet.Rows.Single(r => r.Name == "Бекзод").Measure.Should().Be("10 kun");
        sheet.DocumentNumber.Should().Be("OY-2026-10-V");
    }

    [Fact]
    public void Excel_export_schedule_row_has_empty_rate_and_days_value_total_with_comment()
    {
        var path = Path.Combine(_home, "export.xlsx");
        var lines = _oct.Lines.OrderBy(l => l.Employee!.FullName, NameComparer.Instance).ToList();
        ExcelExporter.Export(_oct, lines, path, Strings.ExcelScheduleComment);

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Лист1");
        ws.Row(1).Cells(1, 8).Select(c => c.GetString()).Should().Equal(ExcelExporter.Headers);
        var guardRow = lines.FindIndex(l => l.EmployeeId == _guard.Id) + 2;
        var officeRow = lines.FindIndex(l => l.EmployeeId == _office.Id) + 2;

        ws.Cell(guardRow, 4).IsEmpty().Should().BeTrue("Сумма bo'sh");
        ws.Cell(guardRow, 5).IsEmpty().Should().BeTrue("Кун bo'sh");
        ws.Cell(guardRow, 6).HasFormula.Should().BeFalse();
        ws.Cell(guardRow, 6).GetValue<long>().Should().Be(_oct.FindLine(_guard.Id)!.Total);
        ws.Cell(guardRow, 6).GetComment().Text.Should().StartWith("Soatbay: ");
        ws.Cell(guardRow, 8).FormulaA1.Should().Be($"F{guardRow}-G{guardRow}");

        ws.Cell(officeRow, 6).FormulaA1.Should().Be($"E{officeRow}*D{officeRow}");
        ws.Cell(lines.Count + 2, 5).FormulaA1.Should().Be($"SUM(E2:E{lines.Count + 1})");
    }

    [AvaloniaFact]
    public void Schedule_row_shows_hourly_rate_and_worked_hours_and_is_not_editable()
    {
        var row = _main.Payroll.Rows.Single(r => r.Employee == _guard);
        row.IsSchedule.Should().BeTrue();
        row.DailyText.Should().Be(Strings.Rate(row.Line.HourlyRate));
        row.DaysText.Should().Be(Strings.HoursMinutes(row.Line.WorkedMinutes));
        row.ScheduleTip.Should().Contain("Sutkalik");
        _main.Payroll.HasScheduleRows.Should().BeTrue();
        _main.Payroll.SumDays.Should().Be("10", "Kun Σ — faqat Daily qatorlar (H13.8)");

        var view = new PayrollView { DataContext = _main.Payroll };
        var window = new Window { Content = view, Width = 1400, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = view.GetVisualDescendants().OfType<DataGrid>().First();
        grid.SelectedItem = row;
        grid.CurrentColumn = grid.Columns[4];
        grid.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        Dispatcher.UIThread.RunJobs();
        grid.GetVisualDescendants().OfType<TextBox>().Any(t => t.FindAncestorOfType<DataGridCell>() is not null).Should().BeFalse();
        window.Close();
    }

    /// <summary>H12: joriy oyda Schedule xodimining ism katagi — bugun kelmadi (qizil) / kechikmoqda (sariq); boshqa oyda va Daily'da belgi yo'q.</summary>
    [AvaloniaFact]
    public async Task Name_cell_is_marked_by_today_status_only_in_current_month()
    {
        // Soat 13.10 12:00; Гайрат (sutkalik, kun ora 1-dan) — 13-da smena, belgi yo'q → Kelmadi
        _service.TodayStatus(_guard)!.State.Should().Be(ShiftState.Absent);
        _service.TodayStatus(_office).Should().BeNull();

        _main.Payroll.Load();
        var guardRow = _main.Payroll.Rows.Single(r => r.Employee == _guard);
        guardRow.Mark.Should().NotBeNull();
        guardRow.NameBackground.Should().Be(AttendanceBrushes.NameAbsent);
        guardRow.NameTip.Should().Be(Strings.StatusAbsent);
        _main.Payroll.Rows.Single(r => r.Employee == _office).Mark.Should().BeNull();

        _main.Attendance.Month = _oct.Key;
        await _main.Attendance.LoadAsync();
        var attendanceRow = _main.Attendance.Rows.Single(r => r.Employee == _guard);
        attendanceRow.NameBackground.Should().Be(AttendanceBrushes.NameAbsent);
        attendanceRow.NameTip.Should().Be($"Гайрат\n{Strings.StatusAbsent}");
        _main.Attendance.Rows.Single(r => r.Employee == _office).NameBackground.Should().Be(AttendanceBrushes.Empty);

        // Boshqa oy — bo'yalmaydi
        _main.Attendance.Month = _oct.Key.Previous();
        await _main.Attendance.LoadAsync();
        _main.Attendance.Rows.Single(r => r.Employee == _guard).Mark.Should().BeNull();

        // Kechikmoqda — sariq, izohda daqiqa
        var late = LiveNameMark.From(new ShiftStatus(ShiftState.Late, 12, null))!;
        late.Background.Should().Be(AttendanceBrushes.NameLate);
        late.Tip.Should().Be(Strings.StatusLate(12));
        LiveNameMark.From(new ShiftStatus(ShiftState.Present, 0, null)).Should().BeNull();
        LiveNameMark.From(new ShiftStatus(ShiftState.NotStarted, 0, null)).Should().BeNull();
        LiveNameMark.From(new ShiftStatus(ShiftState.Off, 0, null)).Should().BeNull();
        // Kun oxiri: umuman kelmagan — qizil «Kelmadi»; qisman ishlagan — bo'yalmaydi
        LiveNameMark.From(new ShiftStatus(ShiftState.DoneRed, 0, null, null, 0))!.Background.Should().Be(AttendanceBrushes.NameAbsent);
        LiveNameMark.From(new ShiftStatus(ShiftState.DoneRed, 0, null, null, 0))!.Tip.Should().Be(Strings.StatusAbsent);
        LiveNameMark.From(new ShiftStatus(ShiftState.DoneRed, 0, null, null, 90)).Should().BeNull();
        LiveNameMark.From(new ShiftStatus(ShiftState.DoneGreen, 0, null, null, 1440)).Should().BeNull();
    }

    [Fact]
    public async Task Closing_with_unconfirmed_events_asks_h11()
    {
        await _service.AddManualEventAsync(_office, AttendanceEventType.In, At(7, 8), "ochiq");
        _service.UnconfirmedEventsCount(_oct).Should().Be(1);

        _dialogs.Choice = 0; // Ro'yxatga o'tish
        (await _main.ConfirmUnconfirmedEventsAsync(_oct)).Should().BeFalse();
        _main.Page.Should().Be(Page.Attendance);
        _main.Attendance.Month.Should().Be(_oct.Key);

        _dialogs.Choice = 1; // Baribir yopish
        (await _main.ConfirmUnconfirmedEventsAsync(_oct)).Should().BeTrue();
        _dialogs.Asked.Should().Contain(m => m.Contains('1'));
    }
}
