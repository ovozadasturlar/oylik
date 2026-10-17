using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;

namespace Oylik.Desktop.ViewModels;

public abstract partial class DialogViewModel : ObservableObject
{
    public event Action? CloseRequested;
    protected void Close() => CloseRequested?.Invoke();
}

// ---------- 5.3 Avanslar oynasi ----------

public sealed class AdvanceItemViewModel(Advance advance, PayrollPeriod? source)
{
    public Advance Advance { get; } = advance;
    public string DateText => Advance.Date.ToString("dd.MM.yyyy");
    public string AmountText => NumberText.Money(Advance.Amount);
    public bool IsDebt => Advance.Kind == AdvanceKind.CarryOverDebt;
    public bool IsCash => !IsDebt;
    public string KindText => IsDebt ? Strings.KindDebt : Strings.KindCash;
    public string Note => Advance.Note ?? "—";
    public string? Tooltip => IsDebt ? Strings.DebtTooltip(source is null ? Strings.PreviousMonthFallback : Strings.MonthName(source.Month)) : null;
}

public sealed partial class AdvancesViewModel : DialogViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly PayrollPeriod _period;
    private readonly PayrollLine _line;

    public AdvancesViewModel(MainWindowViewModel main, PayrollPeriod period, PayrollLine line, Employee employee)
    {
        _main = main;
        _period = period;
        _line = line;
        Title = Strings.AdvancesTitle(employee.FullName, Strings.Month(period.Key));
        var today = DateTime.Today;
        NewDate = today.Year == period.Year && today.Month == period.Month ? today : period.Key.FirstDay.ToDateTime(TimeOnly.MinValue);
        Reload();
    }

    public string Title { get; }
    public bool IsOpen => !_period.IsClosed;
    public ObservableCollection<AdvanceItemViewModel> Items { get; } = [];

    [ObservableProperty] private DateTime? _newDate;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AmountPreview))] private string _newAmountText = "";
    [ObservableProperty] private string _newNote = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _totalText = "";

    public string AmountPreview =>
        ShortInput.TryParse(NewAmountText, AmountField.Advance, _main.Service.Settings.ShortInputEnabled, out var v) && v > 0
            ? "→ " + NumberText.Money(v)
            : "";

    private void Reload()
    {
        Items.Clear();
        foreach (var a in _line.Advances.OrderBy(a => a.Date).ThenBy(a => a.CreatedAt))
        {
            var source = a.SourcePeriodId is { } id ? _main.Service.Periods.FirstOrDefault(p => p.Id == id) : null;
            Items.Add(new AdvanceItemViewModel(a, source));
        }
        TotalText = Strings.AdvancesTotal(NumberText.Money(_line.AdvanceTotal));
        Summary = Strings.AdvancesSummary(NumberText.Money(_line.Total), NumberText.Money(_line.AdvanceTotal), NumberText.Money(_line.Remainder));
    }

    [RelayCommand]
    private async Task Add()
    {
        Error = null;
        if (!ShortInput.TryParse(NewAmountText, AmountField.Advance, _main.Service.Settings.ShortInputEnabled, out var amount) || amount <= 0)
        {
            Error = Strings.InvalidNumber;
            return;
        }
        var date = DateOnly.FromDateTime(NewDate ?? DateTime.Today);
        var note = NewNote;
        if (await _main.RunAsync(() => _main.Service.AddAdvanceAsync(_period, _line, date, amount, note)))
        {
            NewAmountText = "";
            NewNote = "";
            Reload();
        }
        else
        {
            Error = _main.StatusError;
        }
    }

    [RelayCommand]
    private async Task Remove(AdvanceItemViewModel? item)
    {
        if (item is null || item.IsDebt)
            return;
        if (!await _main.Dialogs.ConfirmAsync(Strings.ConfirmDeleteAdvance(item.DateText, item.AmountText)))
            return;
        if (await _main.RunAsync(() => _main.Service.RemoveAdvanceAsync(_period, _line, item.Advance)))
            Reload();
        else
            Error = _main.StatusError;
    }

    [RelayCommand]
    private void Done() => Close();
}

// ---------- Yangi xodim ----------

public sealed partial class NewEmployeeViewModel(MainWindowViewModel main, PayrollPeriod period) : DialogViewModel
{
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SalaryPreview))] private string _salaryText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DailyPreview))] private string _dailyText = "";
    [ObservableProperty] private string? _error;

    public PayrollLine? CreatedLine { get; private set; }
    public string Title => $"{Strings.NewEmployeeTitle} — {Strings.Month(period.Key)}";

    private bool ShortInputOn => main.Service.Settings.ShortInputEnabled;

    public string SalaryPreview =>
        ShortInput.TryParse(SalaryText, AmountField.MonthlySalary, ShortInputOn, out var v)
            ? Strings.SalaryPreview(NumberText.Money(v), NumberText.Money(PayrollMath.DailyRate(v, main.Service.Settings.DaysInMonth)))
            : "";

    public string DailyPreview =>
        ShortInput.TryParse(DailyText, AmountField.DailyRate, ShortInputOn, out var v) ? Strings.DailyPreviewManual(NumberText.Money(v)) : Strings.FieldDailyHint;

    [RelayCommand]
    private async Task Add()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(FullName))
        {
            Error = Strings.EnterName;
            return;
        }
        long salary = 0;
        if (!string.IsNullOrWhiteSpace(SalaryText) && !ShortInput.TryParse(SalaryText, AmountField.MonthlySalary, ShortInputOn, out salary))
        {
            Error = Strings.SalaryInvalid;
            return;
        }
        long? daily = null;
        if (!string.IsNullOrWhiteSpace(DailyText))
        {
            if (!ShortInput.TryParse(DailyText, AmountField.DailyRate, ShortInputOn, out var d))
            {
                Error = Strings.DailyInvalid;
                return;
            }
            daily = d;
        }

        var existing = main.Service.Employees.FirstOrDefault(e => NameComparer.Instance.Equals(e.FullName, FullName.Trim()));
        if (existing is not null && period.FindLine(existing.Id) is null &&
            await main.Dialogs.ConfirmAsync(Strings.ConfirmLinkExisting(existing.FullName)))
        {
            if (await main.RunAsync(async () => CreatedLine = await main.Service.AddExistingEmployeeAsync(period, existing)))
                Close();
            else
                Error = main.StatusError;
            return;
        }

        var name = FullName;
        if (await main.RunAsync(async () => CreatedLine = await main.Service.AddNewEmployeeAsync(period, name, salary, daily, main.DefaultBranchId)))
            Close();
        else
            Error = main.StatusError;
    }

    [RelayCommand]
    private void Cancel() => Close();
}

// ---------- 5.7 Excel'dan import ----------

public sealed partial class ImportViewModel : DialogViewModel
{
    private readonly MainWindowViewModel _main;

    public ImportViewModel(MainWindowViewModel main, MonthKey? currentPeriod)
    {
        _main = main;
        var start = currentPeriod ?? main.Service.NextPeriodKey();
        for (var i = -6; i <= 3; i++)
        {
            var k = start;
            if (i < 0) for (var j = 0; j < -i; j++) k = k.Previous();
            else for (var j = 0; j < i; j++) k = k.Next();
            MonthOptions.Add(new MonthOption(k));
        }
        SelectedMonth = MonthOptions.First(m => m.Key == start);
    }

    public sealed record MonthOption(MonthKey Key)
    {
        public override string ToString() => Strings.Month(Key);
    }

    public ObservableCollection<MonthOption> MonthOptions { get; } = [];
    public ObservableCollection<ImportRow> Rows { get; } = [];
    public ObservableCollection<string> Messages { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPreview))] private ImportPreview? _preview;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ImportButtonText))] private bool _asPeriod = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ImportButtonText))] private MonthOption? _selectedMonth;
    [ObservableProperty] private bool _linkDuplicates = true;
    [ObservableProperty] private string? _error;

    public bool HasPreview => Preview is not null;
    public MonthKey? ImportedPeriod { get; private set; }

    public string ImportButtonText => Preview is null
        ? Strings.Import
        : Strings.ImportButton(AsPeriod && SelectedMonth is not null ? Strings.Month(SelectedMonth.Key) : null, Preview.Rows.Count);

    [RelayCommand]
    private async Task ChooseFile()
    {
        var path = await _main.Dialogs.PickOpenExcelAsync();
        if (path is null)
            return;
        Error = null;
        try
        {
            var preview = ExcelImporter.Read(path, _main.Service.Settings.DaysInMonth);
            Preview = preview;
            FileName = $"{Path.GetFileName(path)} · {preview.SheetName}";
            Rows.Clear();
            foreach (var row in preview.Rows)
                Rows.Add(row);

            var names = _main.Service.Employees.Select(e => e.FullName).ToHashSet(NameComparer.Instance);
            var existing = preview.Rows.Count(r => names.Contains(r.FullName));
            Summary = Strings.ImportSummary(preview.Rows.Count, preview.Rows.Count - existing, existing, preview.SkippedEmpty);

            Messages.Clear();
            foreach (var e in preview.Errors)
                Messages.Add(Strings.ImportError(e));
            var warnings = preview.Warnings.ToList();
            var bothEmpty = warnings.Where(w => w.Warnings.All(i => i.Kind == ImportIssueKind.SalaryAndDailyEmpty)).ToList();
            foreach (var w in warnings.Except(bothEmpty))
                Messages.Add($"{w.FullName} — {string.Join("; ", w.Warnings.Select(Strings.ImportIssueText))}");
            if (bothEmpty.Count > 0)
                Messages.Add(Strings.ImportBothEmpty(bothEmpty.Count));
            OnPropertyChanged(nameof(ImportButtonText));
        }
        catch (Exception ex)
        {
            Preview = null;
            Error = Strings.ReadFileFailed(ex.Message);
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (Preview is null)
            return;
        var key = AsPeriod ? SelectedMonth?.Key : null;
        if (key is { } k && _main.Service.FindPeriod(k) is { IsClosed: true })
        {
            Error = Strings.ImportPeriodClosed(Strings.Month(k));
            return;
        }
        ImportResult? result = null;
        var preview = Preview;
        var options = new ImportOptions(key, LinkDuplicates);
        if (await _main.RunAsync(async () => result = await _main.Service.ImportAsync(preview, options)))
        {
            ImportedPeriod = key;
            await _main.Dialogs.MessageAsync(Strings.ImportTitle, Strings.ImportDone(result!.Created, result.Linked, result.Lines));
            Close();
        }
        else
        {
            Error = _main.StatusError;
        }
    }

    [RelayCommand]
    private void Cancel() => Close();
}
