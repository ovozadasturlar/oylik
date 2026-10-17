using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;

namespace Oylik.Desktop.ViewModels;

/// <summary>Jadval tanlovi (Jadvalsiz + shablonlar).</summary>
public sealed record ScheduleChoice(WorkSchedule? Schedule)
{
    public string Label => Schedule is null ? Strings.ScheduleNone : Strings.ScheduleName(Schedule);
    public string Summary => Schedule is null ? "" : Strings.ScheduleSummary(Schedule);
    public override string ToString() => Label;
}

/// <summary>Tarixdagi biriktirish (H13.5).</summary>
public sealed partial class AssignmentItem(AssignScheduleViewModel parent, EmployeeScheduleAssignment assignment, WorkSchedule? schedule) : ObservableObject
{
    public EmployeeScheduleAssignment Assignment { get; } = assignment;
    public string From => Strings.FromDate(Assignment.EffectiveFrom);
    public string Text => schedule is null ? Strings.ScheduleNone : Strings.ScheduleCell(schedule, Assignment.CycleAnchor);

    [RelayCommand]
    private Task Remove() => parent.RemoveAsync(this);
}

/// <summary>Xodimlar → Jadval katagi: yangi jadval «… dan boshlab», tarix, ortiqcha ish siyosati (H2, H7, H13.5, H13.7).</summary>
public sealed partial class AssignScheduleViewModel : DialogViewModel
{
    private readonly MainWindowViewModel _main;

    public AssignScheduleViewModel(MainWindowViewModel main, Employee employee)
    {
        _main = main;
        Employee = employee;
        Choices = [new ScheduleChoice(null), .. main.Service.Schedules.Select(s => new ScheduleChoice(s))];
        var today = main.Service.Today;
        var current = main.Service.CurrentAssignment(employee.Id);
        SelectedChoice = Choices.FirstOrDefault(c => c.Schedule?.Id == current?.ScheduleId) ?? Choices[0];
        // Default: birinchi biriktirish — oy boshidan, keyingilari — bugundan
        EffectiveFrom = (current is null ? new DateOnly(today.Year, today.Month, 1) : today).ToDateTime(TimeOnly.MinValue);
        CycleAnchor = (current?.CycleAnchor ?? today).ToDateTime(TimeOnly.MinValue);
        OvertimeIndex = employee.OvertimePolicy is { } p ? (int)p + 1 : 0;
        OvertimeValue = employee.OvertimeValue;
        LoadHistory();
    }

    public Employee Employee { get; }
    public string Title => Strings.AssignTitle(Employee.FullName);
    public IReadOnlyList<ScheduleChoice> Choices { get; }
    public ObservableCollection<AssignmentItem> History { get; } = [];
    public IReadOnlyList<string> OvertimeOptions => [Strings.OvertimeDefault, Strings.OvertimeNone, Strings.OvertimeMultiplier, Strings.OvertimeFixed];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCycle))] private ScheduleChoice? _selectedChoice;
    [ObservableProperty] private DateTime? _effectiveFrom;
    [ObservableProperty] private DateTime? _cycleAnchor;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasOvertimeValue))] private int _overtimeIndex;
    [ObservableProperty] private decimal? _overtimeValue;
    [ObservableProperty] private string? _error;

    public bool IsCycle => SelectedChoice?.Schedule?.Kind == ScheduleKind.Cycle;
    public bool HasOvertimeValue => OvertimeIndex >= 2;
    public bool HasHistory => History.Count > 0;

    public bool Changed { get; private set; }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var a in _main.Service.AssignmentsOf(Employee.Id).Reverse())
            History.Add(new AssignmentItem(this, a, _main.Service.FindSchedule(a.ScheduleId)));
        OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand]
    private async Task Assign()
    {
        Error = null;
        if (EffectiveFrom is not { } from)
        {
            Error = Strings.InvalidDate;
            return;
        }
        var schedule = SelectedChoice?.Schedule;
        DateOnly? anchor = IsCycle && CycleAnchor is { } a ? DateOnly.FromDateTime(a) : null;
        if (await _main.RunAsync(() => _main.Service.AssignScheduleAsync(Employee, schedule, anchor, DateOnly.FromDateTime(from))))
        {
            Changed = true;
            LoadHistory();
        }
        else
        {
            Error = _main.StatusError;
        }
    }

    internal async Task RemoveAsync(AssignmentItem item)
    {
        var what = $"{item.Text} · {item.From}";
        if (!await _main.Dialogs.ConfirmAsync(Strings.ConfirmDeleteSchedule(what)))
            return;
        if (await _main.RunAsync(() => _main.Service.RemoveAssignmentAsync(Employee, item.Assignment)))
        {
            Changed = true;
            LoadHistory();
        }
    }

    [RelayCommand]
    private async Task Done()
    {
        OvertimePolicy? policy = OvertimeIndex == 0 ? null : (OvertimePolicy)(OvertimeIndex - 1);
        decimal? value = policy is null or OvertimePolicy.None ? null : OvertimeValue ?? 1;
        if (policy != Employee.OvertimePolicy || value != Employee.OvertimeValue)
        {
            if (!await _main.RunAsync(() => _main.Service.SetEmployeeOvertimeAsync(Employee, policy, value)))
            {
                Error = _main.StatusError;
                return;
            }
            Changed = true;
        }
        Close();
    }
}
