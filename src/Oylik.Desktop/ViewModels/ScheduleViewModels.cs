using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Views;

namespace Oylik.Desktop.ViewModels;

/// <summary>Vaqt/davomiylik kiritish: `8:00`, `08:00`, `8.30`, `9` (soat), `24:00`.</summary>
public static class TimeInput
{
    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact((text ?? "").Trim(), ["H:mm", "HH:mm", "H.mm", "HH.mm", "H"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static bool TryParseDuration(string? text, out int minutes)
    {
        minutes = 0;
        var t = (text ?? "").Trim().Replace('.', ':');
        var parts = t.Split(':');
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out var h) || h < 0)
            return false;
        var m = 0;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out m) || m is < 0 or > 59))
            return false;
        minutes = h * 60 + m;
        return minutes is >= WorkSchedule.MinDuration and <= WorkSchedule.MaxDuration;
    }
}

/// <summary>Hafta kuni tugmasi (Du…Ya).</summary>
public sealed partial class WeekDayToggle(DayOfWeek day, bool isChecked) : ObservableObject
{
    public DayOfWeek Day { get; } = day;
    public string Label => Strings.WeekDayShort(Day);
    [ObservableProperty] private bool _isChecked = isChecked;
}

/// <summary>5.10: shablon qo'shish/tahrirlash oynasi (H1).</summary>
public sealed partial class ScheduleEditViewModel : DialogViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly WorkSchedule? _original;

    public ScheduleEditViewModel(MainWindowViewModel main, WorkSchedule? original, WorkSchedule? template = null)
    {
        _main = main;
        _original = original;
        var s = original ?? template ?? new WorkSchedule { Kind = ScheduleKind.Weekly, WeekDays = WorkSchedule.MondayToSaturday, BreakMinutes = 60 };
        Name = original is not null ? Strings.ScheduleName(s) : template is not null ? Strings.ScheduleCopyName(Strings.ScheduleName(template)) : "";
        IsCycle = s.Kind == ScheduleKind.Cycle;
        WorkDays = s.CycleWorkDays;
        OffDays = s.CycleOffDays;
        StartText = s.StartTime.ToString("HH:mm");
        DurationText = Strings.HoursMinutes(s.DurationMinutes);
        BreakMinutes = s.BreakMinutes;
        DayOfWeek[] order = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        foreach (var d in order)
            Days.Add(new WeekDayToggle(d, s.WorksOn(d)));
    }

    public string Title => _original is null ? Strings.NewScheduleTitle : Strings.EditScheduleTitle;
    public ObservableCollection<WeekDayToggle> Days { get; } = [];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsWeekly))] private bool _isCycle;
    [ObservableProperty] private decimal? _workDays = 1;
    [ObservableProperty] private decimal? _offDays = 1;
    [ObservableProperty] private string _startText = "08:00";
    [ObservableProperty] private string _durationText = "9:00";
    [ObservableProperty] private decimal? _breakMinutes;
    [ObservableProperty] private string? _error;

    public bool IsWeekly
    {
        get => !IsCycle;
        set => IsCycle = !value;
    }

    public WorkSchedule? Saved { get; private set; }

    [RelayCommand]
    private void Cancel() => Close();

    [RelayCommand]
    private async Task Save()
    {
        Error = null;
        if (!TimeInput.TryParseTime(StartText, out var start))
        {
            Error = Strings.InvalidTime;
            return;
        }
        if (!TimeInput.TryParseDuration(DurationText, out var duration))
        {
            Error = Strings.InvalidDuration;
            return;
        }
        var changes = new WorkSchedule
        {
            Name = Name,
            Kind = IsCycle ? ScheduleKind.Cycle : ScheduleKind.Weekly,
            WeekDays = Days.Where(d => d.IsChecked).Aggregate(0, (acc, d) => acc | WorkSchedule.Bit(d.Day)),
            CycleWorkDays = (int)(WorkDays ?? 1),
            CycleOffDays = (int)(OffDays ?? 0),
            StartTime = start,
            DurationMinutes = duration,
            BreakMinutes = (int)(BreakMinutes ?? 0),
        };
        // Namuna nomi tarjimada ko'rsatilgan bo'lsa va o'zgartirilmagan bo'lsa — bazadagi nom saqlanadi
        if (_original is not null && Name.Trim() == Strings.ScheduleName(_original))
            changes.Name = _original.Name;
        var ok = _original is null
            ? await _main.RunAsync(async () => Saved = await _main.Service.CreateScheduleAsync(changes))
            : await _main.RunAsync(async () => { await _main.Service.UpdateScheduleAsync(_original, changes); Saved = _original; });
        if (ok)
            Close();
        else
            Error = _main.StatusError;
    }
}

/// <summary>Sozlamalar → Ish jadvallari: bitta shablon qatori.</summary>
public sealed partial class ScheduleItem(SettingsViewModel parent, WorkSchedule schedule) : ObservableObject
{
    public WorkSchedule Schedule { get; } = schedule;
    public string Name => Strings.ScheduleName(Schedule);
    public string Summary => Strings.ScheduleSummary(Schedule);
    public bool CanDelete => !parent.Main.Service.IsScheduleUsed(Schedule);

    [RelayCommand]
    private Task Edit() => parent.EditScheduleAsync(this);

    [RelayCommand]
    private Task Copy() => parent.CopyScheduleAsync(this);

    [RelayCommand]
    private Task Delete() => parent.DeleteScheduleAsync(this);

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>5.10: Sozlamalar → Ish jadvallari va hisob siyosati (H5, H7, H12).</summary>
public sealed partial class SettingsViewModel
{
    public ObservableCollection<ScheduleItem> ScheduleItems { get; } = [];

    public IReadOnlyList<string> OvertimeOptions => [Strings.OvertimeNone, Strings.OvertimeMultiplier, Strings.OvertimeFixed];

    [ObservableProperty] private decimal? _lateGraceMinutes;
    [ObservableProperty] private decimal? _absentAfterMinutes;
    [ObservableProperty] private decimal? _greenPercent;
    [ObservableProperty] private decimal? _yellowPercent;
    [ObservableProperty] private int _overtimePolicyIndex;
    [ObservableProperty] private decimal? _overtimeValue;

    private void LoadSchedules()
    {
        ScheduleItems.Clear();
        foreach (var s in main.Service.Schedules)
            ScheduleItems.Add(new ScheduleItem(this, s));
        var settings = main.Service.Settings;
        LateGraceMinutes = settings.LateGraceMinutes;
        AbsentAfterMinutes = settings.AbsentAfterMinutes;
        GreenPercent = settings.GreenPercent;
        YellowPercent = settings.YellowPercent;
        OvertimePolicyIndex = (int)settings.OvertimePolicy;
        OvertimeValue = settings.OvertimeValue;
    }

    private void RefreshScheduleTexts()
    {
        foreach (var item in ScheduleItems)
            item.Refresh();
        OnPropertyChanged(nameof(OvertimeOptions));
    }

    partial void OnLateGraceMinutesChanged(decimal? value) => _ = SavePolicyAsync();
    partial void OnAbsentAfterMinutesChanged(decimal? value) => _ = SavePolicyAsync();
    partial void OnGreenPercentChanged(decimal? value) => _ = SavePolicyAsync();
    partial void OnYellowPercentChanged(decimal? value) => _ = SavePolicyAsync();
    partial void OnOvertimePolicyIndexChanged(int value) => _ = SavePolicyAsync();
    partial void OnOvertimeValueChanged(decimal? value) => _ = SavePolicyAsync();

    /// <summary>Ochiq oylar qayta hisoblanadi (har saqlashda); yopiq oylar muzlatilgan (H10).</summary>
    private async Task SavePolicyAsync()
    {
        if (_loading || !main.IsReady)
            return;
        var grace = (int)Math.Clamp(LateGraceMinutes ?? 5, 0, ScheduleCalculator.MaxGraceMinutes);
        var absent = (int)Math.Clamp(AbsentAfterMinutes ?? 60, 1, 24 * 60);
        var green = (int)Math.Clamp(GreenPercent ?? 95, 1, 100);
        var yellow = (int)Math.Clamp(YellowPercent ?? 75, 0, green);
        var policy = (OvertimePolicy)Math.Clamp(OvertimePolicyIndex, 0, 2);
        var value = Math.Max(0, OvertimeValue ?? 1);
        var s = main.Service.Settings;
        if (s.LateGraceMinutes == grace && s.AbsentAfterMinutes == absent && s.GreenPercent == green && s.YellowPercent == yellow
            && s.OvertimePolicy == policy && s.OvertimeValue == value)
            return;
        await main.RunAsync(() => main.Service.SaveSettingsAsync(x =>
        {
            x.LateGraceMinutes = grace;
            x.AbsentAfterMinutes = absent;
            x.GreenPercent = green;
            x.YellowPercent = yellow;
            x.OvertimePolicy = policy;
            x.OvertimeValue = value;
        }));
    }

    [RelayCommand]
    private Task AddSchedule() => OpenScheduleEditor(null, null);

    internal Task EditScheduleAsync(ScheduleItem item) => OpenScheduleEditor(item.Schedule, null);

    internal Task CopyScheduleAsync(ScheduleItem item) => OpenScheduleEditor(null, item.Schedule);

    private async Task OpenScheduleEditor(WorkSchedule? original, WorkSchedule? template)
    {
        var vm = new ScheduleEditViewModel(main, original, template);
        await main.Dialogs.ShowDialogAsync(new ScheduleEditWindow { DataContext = vm });
        if (vm.Saved is not null)
            LoadSchedules();
    }

    internal async Task DeleteScheduleAsync(ScheduleItem item)
    {
        if (!await main.Dialogs.ConfirmAsync(Strings.ConfirmDeleteSchedule(item.Name)))
            return;
        if (await main.RunAsync(() => main.Service.DeleteScheduleAsync(item.Schedule)))
            ScheduleItems.Remove(item);
    }
}
