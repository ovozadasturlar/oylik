using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Views;
using Serilog;

namespace Oylik.Desktop.ViewModels;

// ---------- 5.4 Xodimlar: Excel kabi — tahrir jadvalning o'zida ----------

public enum EmployeeFilter { Active, Inactive, All }

/// <summary>Xodimlar jadvalining qatori. Har katak tasdiqlanganda darhol saqlanadi (Oylik jadvali bilan bir xil).</summary>
public sealed partial class EmployeeRowViewModel(EmployeesViewModel parent, Employee employee) : ObservableObject
{
    private static readonly IBrush Ink = Palette.Get("InkBrush");
    private static readonly IBrush Manual = Palette.Get("ManualBrush");

    public Employee Employee { get; } = employee;

    [ObservableProperty] private int _no;

    public string NameText
    {
        get => Employee.FullName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Reject(Strings.NameRequired);
                return;
            }
            parent.Save(this, s => s.RenameAsync(Employee, value));
        }
    }

    public string NoteText
    {
        get => Employee.Note ?? "";
        set => parent.Save(this, s => s.SetEmployeeNoteAsync(Employee, value));
    }

    /// <summary>R11 qisqa kiritish; R1 + R9 — karta va ochiq oy qatori.</summary>
    public string SalaryText
    {
        get => NumberText.Money(Employee.MonthlySalary);
        set
        {
            if (!ShortInput.TryParse(value, AmountField.MonthlySalary, parent.ShortInputEnabled, out var salary))
            {
                Reject(Strings.InvalidNumber);
                return;
            }
            if (salary != Employee.MonthlySalary)
                parent.Save(this, s => s.SetEmployeeSalaryAsync(Employee, salary));
        }
    }

    /// <summary>Kunlik qo'lda o'zgartirilsa — DailyRateIsManual (ko'k kursiv); R9.</summary>
    public string DailyText
    {
        get => NumberText.Money(Employee.DailyRate);
        set
        {
            if (!ShortInput.TryParse(value, AmountField.DailyRate, parent.ShortInputEnabled, out var daily))
            {
                Reject(Strings.InvalidNumber);
                return;
            }
            if (daily != Employee.DailyRate)
                parent.Save(this, s => s.SetEmployeeDailyRateAsync(Employee, daily));
        }
    }

    public bool IsManualDaily => Employee.DailyRateIsManual;
    public IBrush DailyBrush => IsManualDaily ? Manual : Ink;
    public FontStyle DailyFontStyle => IsManualDaily ? FontStyle.Italic : FontStyle.Normal;
    public string? DailyTip => IsManualDaily ? Strings.ManualDailyTip : null;

    public bool IsActive
    {
        get => Employee.IsActive;
        set
        {
            if (value != Employee.IsActive)
                parent.Save(this, s => s.SetEmployeeActiveAsync(Employee, value));
        }
    }

    public IReadOnlyList<BranchOption> BranchChoices => parent.BranchChoices;

    public BranchOption? Branch
    {
        get => BranchChoices.FirstOrDefault(o => o.Filter.BranchId == Employee.BranchId);
        set
        {
            if (value is not null && value.Filter.BranchId != Employee.BranchId)
                parent.Save(this, s => s.SetEmployeeBranchAsync(Employee, value.Filter.BranchId), branchChanged: true);
        }
    }

    public bool CanDelete => !parent.HasHistory(Employee);

    // ---------- 2.2: hisob usuli va jadval (5.10) ----------

    public int TabelNo => Employee.TabelNo;

    public IReadOnlyList<string> PayModeChoices => parent.PayModeChoices;

    /// <summary>Kunbay (0) / Jadval bo'yicha (1) — karta va ochiq oy qatori (R9 kabi).</summary>
    public int PayModeIndex
    {
        get => (int)Employee.PayMode;
        set
        {
            if (value >= 0 && (PayMode)value != Employee.PayMode)
                parent.Save(this, s => s.SetPayModeAsync(Employee, (PayMode)value));
        }
    }

    public bool IsSchedule => Employee.PayMode == PayMode.Schedule;

    public string ScheduleText => parent.ScheduleTextOf(Employee);

    [RelayCommand]
    private Task OpenSchedule() => parent.OpenScheduleAsync(this);

    [RelayCommand]
    private void ResetDaily() => parent.Save(this, s => s.ResetEmployeeDailyRateAsync(Employee));

    [RelayCommand]
    private Task Delete() => parent.DeleteAsync(this);

    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Noto'g'ri qiymat — eski qiymatni qaytarish (binding tugagach).</summary>
    private void Reject(string message)
    {
        parent.Main.ShowError(message);
        Dispatcher.UIThread.Post(Refresh);
    }
}

public sealed partial class EmployeesViewModel(MainWindowViewModel main) : ObservableObject
{
    public MainWindowViewModel Main => main;
    public ObservableCollection<EmployeeRowViewModel> Rows { get; } = [];

    [ObservableProperty] private EmployeeFilter _filter = EmployeeFilter.Active;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private EmployeeRowViewModel? _selectedRow;
    [ObservableProperty] private string _countActive = "";
    [ObservableProperty] private string _countInactive = "";

    public bool IsFilterActive => Filter == EmployeeFilter.Active;
    public bool IsFilterInactive => Filter == EmployeeFilter.Inactive;
    public bool IsFilterAll => Filter == EmployeeFilter.All;
    public bool ShortInputEnabled => main.Service.Settings.ShortInputEnabled;

    /// <summary>Tarmoq katagi tanlovlari: Belgilanmagan + faol tarmoqlar (+ xodimi bor nofaol tarmoqlar).</summary>
    public IReadOnlyList<BranchOption> BranchChoices { get; private set; } = [];

    public IReadOnlyList<string> PayModeChoices => [Strings.PayModeDaily, Strings.PayModeSchedule];

    internal string ScheduleTextOf(Employee employee)
    {
        var assignment = main.Service.CurrentAssignment(employee.Id);
        var schedule = main.Service.FindSchedule(assignment?.ScheduleId);
        return schedule is null ? Strings.ScheduleChoose : Strings.ScheduleCell(schedule, assignment?.CycleAnchor);
    }

    internal async Task OpenScheduleAsync(EmployeeRowViewModel row)
    {
        var vm = new AssignScheduleViewModel(main, row.Employee);
        await main.Dialogs.ShowDialogAsync(new Views.AssignScheduleWindow { DataContext = vm });
        if (vm.Changed)
        {
            row.Refresh();
            main.Payroll.RefreshTotals();
        }
    }

    /// <summary>Yangi qator yaratilganda — View Ism katagini tahrirga ochadi.</summary>
    public event Action<EmployeeRowViewModel>? EditNameRequested;

    partial void OnFilterChanged(EmployeeFilter value)
    {
        OnPropertyChanged(nameof(IsFilterActive));
        OnPropertyChanged(nameof(IsFilterInactive));
        OnPropertyChanged(nameof(IsFilterAll));
        Load();
    }

    partial void OnSearchTextChanged(string value) => Load();

    [RelayCommand]
    private void SetFilter(EmployeeFilter filter) => Filter = filter;

    internal bool HasHistory(Employee employee) => main.Service.HasHistory(employee);

    private void RefreshCounts()
    {
        var all = main.Service.Employees;
        CountActive = $"{Strings.FilterActive} · {all.Count(e => e.IsActive && main.BranchFilter.Matches(e.BranchId))}";
        CountInactive = $"{Strings.FilterInactive} · {all.Count(e => !e.IsActive && main.BranchFilter.Matches(e.BranchId))}";
    }

    private void RebuildBranchChoices()
    {
        var used = main.Service.Employees.Select(e => e.BranchId).ToHashSet();
        BranchChoices =
        [
            new BranchOption(BranchFilter.Unassigned, null),
            .. main.Service.Branches.Where(b => b.IsActive || used.Contains(b.Id)).Select(b => new BranchOption(BranchFilter.Of(b.Id), b)),
        ];
    }

    public void Load()
    {
        if (!main.IsReady)
            return;
        RefreshCounts();
        RebuildBranchChoices();
        var selectedId = SelectedRow?.Employee.Id;
        var q = main.BranchFilter.Apply(main.Service.Employees);
        q = Filter switch
        {
            EmployeeFilter.Active => q.Where(e => e.IsActive),
            EmployeeFilter.Inactive => q.Where(e => !e.IsActive),
            _ => q,
        };
        var s = SearchText.Trim();
        if (s.Length > 0)
            q = q.Where(e => e.FullName.Contains(s, StringComparison.CurrentCultureIgnoreCase)
                             || (e.Note?.Contains(s, StringComparison.CurrentCultureIgnoreCase) ?? false));
        Rows.Clear();
        var no = 1;
        foreach (var e in q)
            Rows.Add(new EmployeeRowViewModel(this, e) { No = no++ });
        SelectedRow = Rows.FirstOrDefault(r => r.Employee.Id == selectedId) ?? Rows.FirstOrDefault();
    }

    /// <summary>Til almashdi — ro'yxat qayta qurilmaydi (tanlov, scroll va tahrir saqlanadi).</summary>
    public void RefreshTexts()
    {
        if (!main.IsReady)
            return;
        RefreshCounts();
        foreach (var option in BranchChoices)
            option.RefreshTexts();
        OnPropertyChanged(nameof(PayModeChoices));
        foreach (var row in Rows)
            row.Refresh();
    }

    /// <summary>Katak tasdiqlanganda (Oylik jadvalidagi Save bilan bir xil).</summary>
    public async void Save(EmployeeRowViewModel row, Func<PayrollService, Task> action, bool branchChanged = false)
    {
        try
        {
            await main.RunAsync(() => action(main.Service));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Xodimni saqlash");
        }
        row.Refresh();
        RefreshCounts();
        if (branchChanged)
            main.RefreshBranchOptions(); // «Belgilanmagan» paydo bo'lishi/yo'qolishi mumkin
    }

    public async Task DeleteAsync(EmployeeRowViewModel row)
    {
        if (!row.CanDelete || !await main.Dialogs.ConfirmAsync(Strings.ConfirmDeleteEmployee(row.Employee.FullName)))
            return;
        if (await main.RunAsync(() => main.Service.DeleteEmployeeAsync(row.Employee)))
        {
            Load();
            main.RefreshBranchOptions();
        }
    }

    /// <summary>Jadval ostidagi «+ Yangi xodim»: filtrdagi tarmoq default; Ism katagi tahrirga ochiladi.</summary>
    [RelayCommand]
    private async Task NewEmployee()
    {
        Employee? created = null;
        if (!await main.RunAsync(async () => created = await main.Service.CreateEmployeeAsync(Strings.NewEmployeeTitle, 0, null, null, main.DefaultBranchId)))
            return;
        if (Filter == EmployeeFilter.Inactive)
            Filter = EmployeeFilter.Active;
        SearchText = "";
        Load();
        main.RefreshBranchOptions();
        if (Rows.FirstOrDefault(r => r.Employee == created) is { } row)
        {
            SelectedRow = row;
            EditNameRequested?.Invoke(row);
        }
    }
}
