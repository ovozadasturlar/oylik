using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services.Payslips;
using Oylik.Desktop.Views;
using Serilog;

namespace Oylik.Desktop.ViewModels;

public sealed partial class PayrollViewModel(MainWindowViewModel main) : ObservableObject
{
    public MainWindowViewModel Main { get; } = main;
    private PayrollService Service => Main.Service;

    public ObservableCollection<PayrollRowViewModel> Rows { get; } = [];

    [ObservableProperty] private PayrollPeriod? _period;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private PayrollRowViewModel? _selectedRow;

    [ObservableProperty] private string _sumCount = "0";
    [ObservableProperty] private string _sumDays = "0";
    [ObservableProperty] private string _sumTotal = "0";
    [ObservableProperty] private string _sumAdvance = "0";
    [ObservableProperty] private string _sumRemainder = "0";
    [ObservableProperty] private string _sumHours = "0:00";
    [ObservableProperty] private bool _hasScheduleRows;
    [ObservableProperty] private bool _sumNegative;

    public MonthKey? SelectedKey { get; set; }

    public bool ShortInputEnabled => Service.Settings.ShortInputEnabled;
    public bool HasPeriod => Period is not null;
    public bool HasNoPeriod => Period is null && Main.IsReady;
    public bool IsOpen => Period is { IsClosed: false };
    public bool IsClosed => Period is { IsClosed: true };
    public string MonthLabel => Period is null ? "—" : Strings.MonthLabel(Period.Key);
    public string ClosedAtText => Period?.ClosedAt is { } at ? Strings.ClosedAt(at) : "";

    partial void OnPeriodChanged(PayrollPeriod? value)
    {
        SelectedKey = value?.Key;
        RaisePeriodState();
    }

    private void RaisePeriodState()
    {
        OnPropertyChanged(nameof(HasPeriod));
        OnPropertyChanged(nameof(HasNoPeriod));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(MonthLabel));
        OnPropertyChanged(nameof(ClosedAtText));
        PrevMonthCommand.NotifyCanExecuteChanged();
        NextMonthCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => BuildRows();

    /// <summary>Til almashdi: oy nomi, holat, qatorlardagi A9 maslahati — tanlangan oy/qator va scroll saqlanadi.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(MonthLabel));
        OnPropertyChanged(nameof(ClosedAtText));
        foreach (var row in Rows)
            row.Refresh();
    }

    // ---------- 5.11: hisob varaqasi (PDF) ----------

    [RelayCommand]
    private async Task ShowPayslip(PayrollRowViewModel? row)
    {
        row ??= SelectedRow;
        if (Period is not { } period || row is null)
            return;
        var model = BuildPayslip(period, row.Line);
        await Main.Dialogs.ShowDialogAsync(new PayslipPreviewWindow($"{Strings.Payslip} · {row.Employee.FullName}", [model], null, period.Key,
            Strings.PdfFileName(period.Key, row.Employee.FullName, row.Employee.TabelNo)));
    }

    /// <summary>Barcha xodimlar (joriy tarmoq filtri bo'yicha) — har biri alohida sahifada + umumiy vedomost (imzo ustuni).</summary>
    [RelayCommand]
    private async Task ShowPayslips()
    {
        if (Period is not { } period)
            return;
        var lines = Main.BranchFilter.Apply(period.Lines).OrderBy(l => l.Employee?.FullName, NameComparer.Instance).ToList();
        if (lines.Count == 0)
            return;
        var branch = Main.BranchFilter.IsAll ? null : Main.SelectedBranchOption?.Label;
        var slips = lines.Select(l => BuildPayslip(period, l)).ToList();
        var sheet = PayslipBuilder.Sheet(Service, period, lines, branch, Strings.PdfSheetMeasure);
        await Main.Dialogs.ShowDialogAsync(new PayslipPreviewWindow(Strings.Payslips, slips, sheet, period.Key, Strings.PdfSheetFileName(period.Key, branch)));
    }

    public PayslipModel BuildPayslip(PayrollPeriod period, PayrollLine line) =>
        PayslipBuilder.Build(Service, period, line) with { ScheduleName = line.IsSchedule ? ScheduleNameOf(line.EmployeeId) : null };

    /// <summary>Oyning oxirgi kunidagi jadval nomi (tooltip va PDF uchun).</summary>
    public string? ScheduleNameOf(Guid employeeId)
    {
        if (Period is null)
            return null;
        var last = Period.Key.FirstDay.AddMonths(1).AddDays(-1);
        return Strings.ScheduleName(Service.FindSchedule(Service.ScheduleContextFor(employeeId).AssignmentOn(last)?.ScheduleId));
    }

    /// <summary>Jami/Qoldiq qayta o'qiladi (Schedule qatorlari jonli hisoblanganda) — qatorlar qayta qurilmaydi.</summary>
    public void RefreshTotals()
    {
        foreach (var row in Rows)
            row.Refresh();
        RefreshSummary();
    }

    /// <summary>Tarmoq filtri almashdi (MainWindowViewModel).</summary>
    public void OnBranchFilterChanged()
    {
        if (Main.IsReady)
            BuildRows();
    }

    private static string SafeFileName(string text) =>
        string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private DispatcherTimer? _liveTimer;

    /// <summary>H12: faqat joriy (ochiq, bugungi) oyda — Schedule xodimining ism katagi bugungi holat bo'yicha.</summary>
    public void RefreshLiveMarks()
    {
        var today = Service.Today;
        var live = Period is { IsClosed: false } p && p.Key == new MonthKey(today.Year, today.Month);
        var analysis = live && Rows.Any(r => r.IsSchedule) ? Service.AnalyzeAttendance() : null;
        foreach (var row in Rows)
            row.Mark = analysis is not null && row.IsSchedule ? LiveNameMark.From(Service.TodayStatus(row.Employee, analysis)) : null;
        if (_liveTimer is null)
        {
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _liveTimer.Tick += (_, _) =>
            {
                if (Main.IsPayroll)
                    RefreshLiveMarks();
            };
            _liveTimer.Start();
        }
    }

    public void Load()
    {
        if (!Main.IsReady)
            return;
        _attendanceMonth = null; // davomat yangilangan bo'lishi mumkin
        var periods = Service.Periods;
        Period = (SelectedKey is { } key ? Service.FindPeriod(key) : null)
                 ?? periods.FirstOrDefault(p => !p.IsClosed)
                 ?? periods.FirstOrDefault();
        RaisePeriodState();
        BuildRows();
    }

    private void BuildRows()
    {
        Rows.Clear();
        if (Period is null)
        {
            RefreshSummary();
            return;
        }
        var employees = Service.Employees.ToDictionary(e => e.Id);
        var query = Main.BranchFilter.Apply(Period.Lines)
            .Select(l => new { Line = l, Employee = l.Employee ?? employees[l.EmployeeId] })
            .OrderBy(x => x.Employee.FullName, NameComparer.Instance)
            .AsEnumerable();
        var search = SearchText.Trim();
        if (search.Length > 0)
            query = query.Where(x => x.Employee.FullName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                                     || (x.Employee.Note?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false));
        var no = 1;
        foreach (var x in query)
            Rows.Add(new PayrollRowViewModel(this, Period, x.Line, x.Employee) { No = no++ });
        RefreshLiveMarks();
        RefreshSummary();
        if (_attendanceMonth != Period.Key)
            _ = RefreshAttendanceAsync();
        else
            CopyAllAttendanceCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSummary()
    {
        // 3.9: xulosa va JAMI — joriy tarmoq filtri bo'yicha (qidiruvdan qat'i nazar)
        var lines = Main.BranchFilter.Apply(Period?.Lines ?? []).ToList();
        var s = PeriodSummary.Of(lines);
        // H13.8: Kun Σ — faqat Daily qatorlar; Schedule qatorlari bo'lsa yonida Soat Σ
        var daily = lines.Where(l => !l.IsSchedule).ToList();
        var scheduled = lines.Where(l => l.IsSchedule).ToList();
        HasScheduleRows = scheduled.Count > 0;
        SumHours = Strings.HoursMinutes(scheduled.Sum(l => l.WorkedMinutes));
        SumCount = s.EmployeeCount.ToString();
        SumDays = NumberText.Days(daily.Sum(l => l.DaysWorked));
        SumTotal = NumberText.Money(s.Total);
        SumAdvance = NumberText.Money(s.AdvanceTotal);
        SumRemainder = NumberText.Money(s.Remainder);
        SumNegative = s.Remainder < 0;
    }

    /// <summary>Qator katagi tasdiqlanganda chaqiriladi.</summary>
    public async void Save(PayrollRowViewModel row, Func<PayrollService, Task> action)
    {
        try
        {
            await Main.RunAsync(() => action(Service));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Save");
        }
        row.Refresh();
        RefreshSummary();
    }

    // ---------- Oy navigatsiyasi ----------

    private bool CanPrev() => Period is not null && Service.Periods.Any(p => p.Key < Period.Key);
    private bool CanNext() => Period is not null && Service.Periods.Any(p => p.Key > Period.Key);

    [RelayCommand(CanExecute = nameof(CanPrev))]
    private void PrevMonth()
    {
        Period = Service.Periods.Where(p => p.Key < Period!.Key).MaxBy(p => p.Key);
        BuildRows();
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void NextMonth()
    {
        Period = Service.Periods.Where(p => p.Key > Period!.Key).MinBy(p => p.Key);
        BuildRows();
    }

    // ---------- Oy amallari ----------

    [RelayCommand]
    private async Task NewPeriod()
    {
        var key = Service.NextPeriodKey();
        if (!await Main.Dialogs.ConfirmAsync(Strings.ConfirmNewPeriod(Strings.Month(key))))
            return;
        PayrollPeriod? created = null;
        if (await Main.RunAsync(async () => created = await Service.CreatePeriodAsync(key)))
        {
            Period = created;
            BuildRows();
        }
    }

    [RelayCommand]
    private async Task ClosePeriod()
    {
        if (Period is not { IsClosed: false } period)
            return;
        if (!await Main.ConfirmUnconfirmedEventsAsync(period))
            return;
        var debtors = period.Lines.Count(l => l.Remainder < 0);
        if (!await Main.Dialogs.ConfirmAsync(Strings.ConfirmClose(Strings.Month(period.Key), debtors), Strings.ClosePeriod, Strings.Cancel))
            return;
        if (await Main.RunAsync(() => Service.ClosePeriodAsync(period)))
        {
            Main.RefreshBackupText();
            RaisePeriodState();
            BuildRows();
        }
    }

    [RelayCommand]
    private async Task Reopen()
    {
        if (Period is not { IsClosed: true } period)
            return;
        if (!await Main.Dialogs.ConfirmAsync(Strings.ConfirmReopen(Strings.Month(period.Key)), Strings.ReopenPeriod, Strings.Cancel))
            return;
        if (await Main.RunAsync(() => Service.ReopenPeriodAsync(period)))
        {
            RaisePeriodState();
            BuildRows();
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        if (Period is null)
            return;
        var name = ExcelExporter.DefaultFileName(Period.Key);
        if (!Main.BranchFilter.IsAll)
            name = $"{Path.GetFileNameWithoutExtension(name)} - {SafeFileName(Main.SelectedBranchOption!.Label)}.xlsx";
        var path = await Main.Dialogs.PickSaveExcelAsync(name);
        if (path is null)
            return;
        try
        {
            // Eksport — joriy tarmoq filtri bo'yicha; shablon ustunlari o'zgarmaydi
            var lines = Main.BranchFilter.Apply(Period.Lines).OrderBy(l => l.Employee?.FullName, NameComparer.Instance);
            ExcelExporter.Export(Period, lines, path, Strings.ExcelScheduleComment);
            Main.StatusError = null;
            Main.SavedText = Strings.Exported(Path.GetFileName(path));
        }
        catch (IOException ex)
        {
            Main.ShowError(Strings.ExportFailed(ex.Message));
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        var vm = new ImportViewModel(Main, Period?.Key);
        await Main.Dialogs.ShowDialogAsync(new ImportWindow { DataContext = vm });
        if (vm.ImportedPeriod is { } key)
            SelectedKey = key;
        Load();
    }

    // ---------- Qatorlar ----------

    [RelayCommand]
    private async Task NewEmployee()
    {
        if (Period is not { IsClosed: false } period)
            return;
        var vm = new NewEmployeeViewModel(Main, period);
        await Main.Dialogs.ShowDialogAsync(new NewEmployeeWindow { DataContext = vm });
        if (vm.CreatedLine is not null)
        {
            BuildRows();
            SelectedRow = Rows.FirstOrDefault(r => r.Line == vm.CreatedLine);
        }
    }

    [RelayCommand]
    private async Task OpenAdvances(PayrollRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null)
            return;
        var vm = new AdvancesViewModel(Main, row.Period, row.Line, row.Employee);
        await Main.Dialogs.ShowDialogAsync(new AdvancesWindow { DataContext = vm });
        row.Refresh();
        RefreshSummary();
    }

    [RelayCommand]
    private async Task RemoveLine(PayrollRowViewModel? row)
    {
        row ??= SelectedRow;
        if (row is null || Period is not { IsClosed: false } period)
            return;
        if (!PayrollEngine.CanRemoveLine(row.Line))
        {
            Main.ShowError(Strings.LineNotEmpty);
            return;
        }
        if (!await Main.Dialogs.ConfirmAsync(Strings.ConfirmRemoveLine(row.Employee.FullName, Strings.Month(period.Key))))
            return;
        if (await Main.RunAsync(() => Service.RemoveLineAsync(period, row.Line)))
            BuildRows();
    }
}
