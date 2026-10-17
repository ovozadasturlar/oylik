using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;

using Oylik.Desktop.Views;

namespace Oylik.Desktop.ViewModels;

// ---------- 8-bo'lim: Davomat ekrani ----------

public sealed class AttendanceDayHeader(DateOnly date, bool isToday)
{
    public string Number => date.Day.ToString(CultureInfo.InvariantCulture);
    public string WeekDay => Strings.WeekDayShort(date.DayOfWeek);
    public IBrush Background => isToday ? AttendanceBrushes.TodayBg : AttendanceBrushes.HeaderBg;
    public IBrush Foreground => isToday ? AttendanceBrushes.TodayText : date.DayOfWeek == DayOfWeek.Sunday ? AttendanceBrushes.Sunday : AttendanceBrushes.Muted;
}

/// <summary>Davomat kataklari ranglari — mavzuga qarab (AppStyles.axaml, <see cref="Palette"/>).</summary>
public static class AttendanceBrushes
{
    public static readonly IBrush Full = Palette.Get("AttFullBrush");
    public static readonly IBrush Partial = Palette.Get("AttPartialBrush");
    public static readonly IBrush Short = Palette.Get("AttShortBrush");
    public static readonly IBrush NameLate = Palette.Get("NameLateBrush");
    public static readonly IBrush NameAbsent = Palette.Get("NameAbsentBrush");
    public static readonly IBrush Open = Palette.Get("AttOpenBrush");
    public static readonly IBrush OpenText = Palette.Get("DebtText");
    public static readonly IBrush Inside = Palette.Get("OkBg");
    public static readonly IBrush InsideText = Palette.Get("SuccessBrush");
    public static readonly IBrush Empty = Palette.Get("SurfaceBrush");
    public static readonly IBrush SundayBg = Palette.Get("AttSundayBrush");
    public static readonly IBrush HeaderBg = Palette.Get("SurfaceAltBrush");
    public static readonly IBrush TodayBg = Palette.Get("TodayBrush");
    public static readonly IBrush TodayText = Palette.Get("TodayText");
    public static readonly IBrush Sunday = Palette.Get("FaintBrush");
    public static readonly IBrush Muted = Palette.Get("MutedBrush");
    public static readonly IBrush Ink = Palette.Get("InkBrush");
    public static readonly IBrush Selected = Palette.Get("SelectedCellBrush");
    public static readonly IBrush CellBorder = Palette.Get("CellBorderBrush");
    public static readonly IBrush NavActive = Palette.Get("NavActiveBrush");
    public static readonly IBrush NavActiveText = Palette.Get("NavActiveText");
}

public sealed partial class AttendanceCellViewModel(Employee employee, DateOnly date) : ObservableObject
{
    public Employee Employee { get; } = employee;
    public DateOnly Date { get; } = date;

    public string Text { get; init; } = "";
    public IBrush Background { get; init; } = AttendanceBrushes.Empty;
    public IBrush Foreground { get; init; } = AttendanceBrushes.Ink;
    public FontWeight Weight { get; init; } = FontWeight.Normal;
    public string? Tip { get; init; }

    /// <summary>5.10: kontekst menyu (Dam / Boshqa vaqt / Sababli) — faqat Schedule xodimlar.</summary>
    public bool IsSchedule { get; init; }
    public ScheduleOverride? Override { get; init; }
    public ExcusedDay? Excused { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderBrush))]
    private bool _isSelected;

    public IBrush BorderBrush => IsSelected ? AttendanceBrushes.Selected : AttendanceBrushes.CellBorder;
}

/// <summary>
/// H12: joriy oyda Schedule xodimining ism katagi — bugun kechikmoqda (sariq) yoki kelmadi (qizil; smena tugagach umuman kelmagan ham);
/// keldi, boshlanmagan, dam, sababli, kun oxirida qisman ishlagan — belgi yo'q.
/// </summary>
public sealed record LiveNameMark(IBrush Background, string Tip)
{
    public static LiveNameMark? From(ShiftStatus? status) => status?.State switch
    {
        ShiftState.Late => new(AttendanceBrushes.NameLate, Strings.StatusLate(status.Minutes)),
        ShiftState.Absent => new(AttendanceBrushes.NameAbsent, Strings.StatusAbsent),
        _ when status!.IsNoShow => new(AttendanceBrushes.NameAbsent, Strings.StatusAbsent),
        _ => null,
    };
}

public sealed class AttendanceRowViewModel
{
    public required Employee Employee { get; init; }
    public required IReadOnlyList<AttendanceCellViewModel> Cells { get; init; }
    public required AttendanceMonthSummary Summary { get; init; }
    public string Name => Employee.FullName;
    public LiveNameMark? Mark { get; init; }
    public IBrush NameBackground => Mark?.Background ?? AttendanceBrushes.Empty;
    public string NameTip => Mark is { } m ? $"{Name}\n{m.Tip}" : Name;
    public string DaysText => Summary.AttendedDays == 0 ? "" : Summary.AttendedDays.ToString(CultureInfo.InvariantCulture);
    public string HoursText => Summary.TotalHours == 0 ? "" : NumberText.Days(Summary.TotalHours);
    public string EquivalentText => Summary.DayEquivalent == 0 ? "" : NumberText.Days(Summary.DayEquivalent);

    /// <summary>H12: Schedule xodimi uchun «ishlangan / reja · %» (kun ekv. ÷8 ular uchun ma'nosiz).</summary>
    public ScheduleMonthResult? Schedule { get; init; }
    public bool IsSchedule => Schedule is not null;
    public bool IsNotSchedule => Schedule is null;
    public string PlanText => Schedule is { } s ? Strings.PlanActual(s.WorkedMinutes, s.PlannedMinutes) : "";
}

public sealed class DayEventViewModel(AttendanceEvent e, string time, string source, Func<AttendanceEvent, Task> voidAction)
{
    public AttendanceEvent Event { get; } = e;
    public string Time { get; } = time;
    public string TypeText => Event.Type == AttendanceEventType.In ? Strings.EventIn : Strings.EventOut;
    public IBrush TypeBrush => Event.Type == AttendanceEventType.In ? AttendanceBrushes.InsideText : AttendanceBrushes.OpenText;
    public string Source { get; } = source;
    public string? Note => Event.IsVoided ? Strings.VoidedBecause(Event.VoidReason) : Event.Note;
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool IsVoided => Event.IsVoided;
    public bool CanVoid => !Event.IsVoided;
    public bool HasClockSkew => Event.HasClockSkew;
    public TextDecorationCollection? Decorations => Event.IsVoided ? TextDecorations.Strikethrough : null;
    public double Opacity => Event.IsVoided ? 0.55 : 1;
    public IAsyncRelayCommand VoidCommand { get; } = new AsyncRelayCommand(() => voidAction(e));
}

/// <summary>"Tasdiqlash kerak" qatori: 16 soatdan oshgan ochiq In (Ketdi vaqti kiritiladi) yoki yetim Out (Keldi vaqti).</summary>
public sealed partial class IssueViewModel(AttendanceViewModel parent, AttendanceIssue issue, Employee employee, TimeZoneInfo zone) : ObservableObject
{
    public AttendanceIssue Issue { get; } = issue;
    public Employee Employee { get; } = employee;
    private DateTimeOffset Local => TimeZoneInfo.ConvertTime(Issue.Event.OccurredAt, zone);

    public bool IsOpenIn => Issue.Kind == AttendanceIssueKind.OpenNoOut;
    public string Name => Employee.FullName;
    public string When => $"{Local:dd.MM} {(IsOpenIn ? Strings.EventIn : Strings.EventOut)} {Local:HH:mm}";
    public string Problem => IsOpenIn ? Strings.IssueOpenNoOut : Strings.IssueOrphanOut;
    public string InputLabel => IsOpenIn ? Strings.EventOut : Strings.EventIn;
    public string SaveText => IsOpenIn ? Strings.Save : Strings.AddIn;

    /// <summary>Til almashdi — kiritilgan vaqt (Time) saqlanadi.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(When));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(InputLabel));
        OnPropertyChanged(nameof(SaveText));
    }

    [ObservableProperty] private string _time = issue.Kind == AttendanceIssueKind.OpenNoOut ? "18:00" : "08:00";

    [RelayCommand]
    private Task SaveAsync() => parent.ResolveIssueAsync(this);

    [RelayCommand]
    private Task VoidAsync() => parent.VoidEventAsync(Issue.Event);

    [RelayCommand]
    private Task ShowAsync() => parent.ShowIssueAsync(this);

    /// <summary>
    /// Kiritilgan HH:mm dan voqea vaqti: Ketdi — In'dan keyingi birinchi shu vaqt (tungi smena: ertasi kun);
    /// Keldi — Out'dan oldingi oxirgi shu vaqt.
    /// </summary>
    public DateTimeOffset? ResolveTime()
    {
        if (!TimeOnly.TryParseExact(Time.Trim(), ["H:mm", "HH:mm", "H.mm", "HH.mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;
        var local = Local;
        var candidate = new DateTimeOffset(local.Date + t.ToTimeSpan(), local.Offset);
        if (IsOpenIn && candidate <= local) candidate = candidate.AddDays(1);
        if (!IsOpenIn && candidate >= local) candidate = candidate.AddDays(-1);
        return candidate;
    }
}

public sealed partial class AttendanceViewModel(MainWindowViewModel main) : ObservableObject
{
    private PayrollService Service => main.Service;
    private TimeZoneInfo Zone => Service.LocalZone;
    private DispatcherTimer? _todayTimer;
    private IReadOnlyDictionary<Guid, EmployeeAttendance> _analysis = new Dictionary<Guid, EmployeeAttendance>();
    private AttendanceCellViewModel? _selected;

    public ObservableCollection<AttendanceDayHeader> DayHeaders { get; } = [];
    public ObservableCollection<AttendanceRowViewModel> Rows { get; } = [];
    public ObservableCollection<IssueViewModel> Issues { get; } = [];
    public ObservableCollection<DayEventViewModel> DayEvents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel), nameof(EquivalentHeader))]
    private MonthKey _month = CurrentMonth();

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private string _todayTitle = "";
    [ObservableProperty] private int _insideCount;
    [ObservableProperty] private int _leftCount;
    [ObservableProperty] private int _absentCount;
    [ObservableProperty] private string _insideNames = "";
    [ObservableProperty] private string _leftNames = "";
    [ObservableProperty] private string _absentNames = "";
    [ObservableProperty] private int _lateCount;
    [ObservableProperty] private string _lateNames = "";
    [ObservableProperty] private string? _dayPlan;

    [ObservableProperty] private string _issuesTitle = "";
    [ObservableProperty] private string _dayTitle = Strings.DayDetailHint;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty] private bool _addIsIn = true;
    [ObservableProperty] private DateTime? _addDate;
    [ObservableProperty] private string _addTime = "";
    [ObservableProperty] private string _addNote = "";
    [ObservableProperty] private string? _addError;
    [ObservableProperty] private bool _hasEvents;

    public string MonthLabel => Strings.MonthLabel(Month);
    public string EquivalentHeader => HasScheduleRows
        ? $"{Strings.ColDayEquivalentDivisor(Service.Settings.HoursPerDay)} · {Strings.ColPlanPercent}"
        : Strings.ColDayEquivalentDivisor(Service.Settings.HoursPerDay);

    /// <summary>H12 legendasi — faqat ro'yxatda Schedule xodimi bo'lsa; foizlar Sozlamalardan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EquivalentHeader))]
    private bool _hasScheduleRows;
    public string LegendDoneGreen => Strings.LegendDoneAtLeast(Service.Settings.GreenPercent);
    public string LegendDoneYellow => Strings.LegendDoneAtLeast(Service.Settings.YellowPercent);
    public string LegendDoneRed => Strings.LegendDoneBelow(Service.Settings.YellowPercent);
    public bool HasIssues => Issues.Count > 0;
    public bool HasNoDayEvents => HasSelection && DayEvents.Count == 0;

    private static MonthKey CurrentMonth()
    {
        var now = DateTime.Now;
        return new MonthKey(now.Year, now.Month);
    }

    private DateTimeOffset Now => Service.Clock.GetUtcNow();

    /// <summary>H12: Schedule xodimlari uchun shu yangilanishdagi hisob (BuildGrid'da to'ldiriladi).</summary>
    private readonly Dictionary<Guid, (ScheduleContext Ctx, ScheduleMonthResult Result)> _schedules = [];

    private (ScheduleContext Ctx, ScheduleMonthResult Result)? ScheduleOf(Employee employee)
    {
        if (employee.PayMode != PayMode.Schedule)
            return null;
        if (_schedules.TryGetValue(employee.Id, out var cached))
            return cached;
        var ctx = Service.ScheduleContextFor(employee.Id);
        var pairs = _analysis.GetValueOrDefault(employee.Id)?.Pairs ?? [];
        var result = ScheduleCalculator.Calculate(Month, employee.MonthlySalary, pairs, ctx, Service.PolicyFor(employee));
        return _schedules[employee.Id] = (ctx, result);
    }

    private ShiftStatus StatusOf(Employee employee, (ScheduleContext Ctx, ScheduleMonthResult Result) s, DateOnly date) =>
        ScheduleCalculator.Status(date, s.Ctx, _analysis.GetValueOrDefault(employee.Id)?.Pairs ?? [], Now,
            Service.PolicyFor(employee).GraceMinutes, Service.Thresholds);

    /// <summary>`Reja: 08:00–20:00 · Kechikmoqda · 12 daq`.</summary>
    private string StatusText(ShiftStatus status, ScheduleDayResult? day)
    {
        var plan = status.Shift is { } w ? Strings.PlanText(TimeZoneInfo.ConvertTime(w.Start, Zone), TimeZoneInfo.ConvertTime(w.End, Zone)) : Strings.PlanOff;
        var state = status.State switch
        {
            ShiftState.NotStarted => Strings.StatusNotStarted,
            ShiftState.Late => Strings.StatusLate(status.Minutes),
            ShiftState.Absent => Strings.StatusAbsent,
            ShiftState.Present => status.Minutes > 0 ? $"{Strings.StatusPresent} · {Strings.StatusLateMark(status.Minutes)}" : Strings.StatusPresent,
            ShiftState.DoneGreen or ShiftState.DoneYellow or ShiftState.DoneRed =>
                Strings.StatusDone(day is { PlannedMinutes: > 0 } d ? (int)Math.Round(100m * d.WorkedMinutes / d.PlannedMinutes) : 0)
                + (status.Minutes > 0 ? " · " + Strings.StatusLateMark(status.Minutes) : ""),
            ShiftState.Excused => Strings.ExcusedName(status.Excused!.Kind) + " — " + status.Excused.Note,
            _ => "",
        };
        var extra = day is { OvertimeMinutes: > 0 } ? $" · {Strings.OvertimePolicyLabel} {Strings.HoursMinutes(day.OvertimeMinutes)}" : "";
        return state.Length == 0 ? plan + extra : $"{plan} · {state}{extra}";
    }
    private DateOnly Today => AttendanceRules.LocalDate(Now, Zone);

    partial void OnSearchTextChanged(string value) => BuildGrid();

    public MainWindowViewModel Main => main;

    /// <summary>Tarmoq filtri almashdi: jadval, Bugun paneli va tasdiqlash ro'yxati shu tarmoq bo'yicha.</summary>
    public void OnBranchFilterChanged()
    {
        if (!main.IsReady || DayHeaders.Count == 0)
            return;
        BuildGrid();
        BuildToday();
        BuildIssues();
        RefreshDetail();
    }

    /// <summary>Til almashdi: hafta kunlari, katak maslahatlari, Bugun paneli, tafsilot — tanlangan katak va kiritishlar saqlanadi.</summary>
    public void RefreshTexts()
    {
        if (!main.IsReady || DayHeaders.Count == 0)
            return;
        BuildGrid();
        BuildToday();
        foreach (var issue in Issues)
            issue.RefreshTexts();
        IssuesTitle = Strings.IssuesTitle(Issues.Count);
        RefreshDetail();
        OnPropertyChanged(nameof(MonthLabel));
    }

    public async Task LoadAsync()
    {
        if (!main.IsReady)
            return;
        await Service.EnsureEventsLoadedAsync(Month);
        _analysis = Service.AnalyzeAttendance();
        HasEvents = Service.AttendanceEvents.Any();
        BuildGrid();
        BuildToday();
        BuildIssues();
        RefreshDetail();
        OnPropertyChanged(nameof(EquivalentHeader));
        StartTodayTimer();
    }

    private void StartTodayTimer()
    {
        if (_todayTimer is not null)
            return;
        _todayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _todayTimer.Tick += (_, _) =>
        {
            if (!main.IsAttendance)
                return;
            _analysis = Service.AnalyzeAttendance();
            if (Month == new MonthKey(Today.Year, Today.Month))
                BuildGrid(); // H12: ranglar jonli (tanlov saqlanadi)
            BuildToday();
            BuildIssues();
        };
        _todayTimer.Start();
    }

    [RelayCommand]
    private async Task PrevMonth()
    {
        Month = Month.Previous();
        ClearSelection();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextMonth()
    {
        Month = Month.Next();
        ClearSelection();
        await LoadAsync();
    }

    // ---------- Jadval ----------

    private IEnumerable<Employee> VisibleEmployees()
    {
        var withEvents = Service.AttendanceEvents
            .Where(e => AttendanceRules.LocalDate(e.OccurredAt, Zone) is var d && d.Year == Month.Year && d.Month == Month.Month)
            .Select(e => e.EmployeeId)
            .ToHashSet();
        var query = main.BranchFilter.Apply(Service.Employees).Where(e => e.IsActive || withEvents.Contains(e.Id));
        var search = SearchText.Trim();
        if (search.Length > 0)
            query = query.Where(e => e.FullName.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        return query;
    }

    private void BuildGrid()
    {
        _schedules.Clear();
        var days = DateTime.DaysInMonth(Month.Year, Month.Month);
        var today = Today;
        DayHeaders.Clear();
        for (int d = 1; d <= days; d++)
        {
            var date = new DateOnly(Month.Year, Month.Month, d);
            DayHeaders.Add(new AttendanceDayHeader(date, date == today));
        }

        Rows.Clear();
        var isCurrentMonth = Month == new MonthKey(today.Year, today.Month);
        foreach (var employee in VisibleEmployees())
        {
            var analysis = _analysis.GetValueOrDefault(employee.Id);
            var orphanDays = analysis?.OrphanOuts.Select(o => AttendanceRules.LocalDate(o.OccurredAt, Zone)).ToHashSet() ?? [];
            var cells = new List<AttendanceCellViewModel>(days);
            for (int d = 1; d <= days; d++)
            {
                var date = new DateOnly(Month.Year, Month.Month, d);
                var cell = BuildCell(employee, date, analysis?.Day(date), orphanDays.Contains(date));
                if (_selected is { } s && s.Employee.Id == employee.Id && s.Date == date)
                {
                    cell.IsSelected = true;
                    _selected = cell;
                }
                cells.Add(cell);
            }
            Rows.Add(new AttendanceRowViewModel
            {
                Employee = employee,
                Cells = cells,
                Summary = analysis?.Summarize(Month, Service.Settings.HoursPerDay) ?? AttendanceMonthSummary.Empty,
                Schedule = ScheduleOf(employee)?.Result,
                Mark = isCurrentMonth && ScheduleOf(employee) is { } sch ? LiveNameMark.From(StatusOf(employee, sch, today)) : null,
            });
        }
        HasScheduleRows = Rows.Any(r => r.IsSchedule);
        OnPropertyChanged(nameof(LegendDoneGreen));
        OnPropertyChanged(nameof(LegendDoneYellow));
        OnPropertyChanged(nameof(LegendDoneRed));
    }

    /// <summary>Rang: ≥ 8 yashil · 4–8 sariq · &lt; 4 qizil · ochiq juftlik `?` to'q sariq · bo'sh — kelmagan.</summary>
    private AttendanceCellViewModel BuildCell(Employee employee, DateOnly date, AttendanceDay? day, bool hasOrphan)
    {
        var blank = date.DayOfWeek == DayOfWeek.Sunday ? AttendanceBrushes.SundayBg : AttendanceBrushes.Empty;
        // H12: Schedule xodimi — holat ranglari (tasdiqlanmagan «?» bundan mustasno)
        if (ScheduleOf(employee) is { } schedule && !hasOrphan && day?.Pairs.Any(p => p.Status == PairStatus.OpenNoOut) != true)
            return BuildScheduleCell(employee, date, schedule, day?.Hours ?? 0);
        if (day is null && !hasOrphan)
            return new AttendanceCellViewModel(employee, date) { Background = blank };

        var hours = day?.Hours ?? 0;
        var needsConfirm = hasOrphan || day!.Pairs.Any(p => p.Status == PairStatus.OpenNoOut);
        if (needsConfirm)
        {
            return new AttendanceCellViewModel(employee, date)
            {
                Text = hours > 0 ? CellHours(hours) : "?",
                Background = AttendanceBrushes.Open,
                Foreground = AttendanceBrushes.OpenText,
                Weight = FontWeight.Bold,
                Tip = Strings.CellNeedsConfirm,
            };
        }
        if (day!.Pairs.Any(p => p.Status == PairStatus.Inside))
        {
            return new AttendanceCellViewModel(employee, date)
            {
                Text = hours > 0 ? CellHours(hours) + "•" : "•",
                Background = AttendanceBrushes.Inside,
                Foreground = AttendanceBrushes.InsideText,
                Weight = FontWeight.Bold,
                Tip = Strings.CellInside,
            };
        }
        var bg = hours >= Service.Settings.HoursPerDay ? AttendanceBrushes.Full
            : hours >= Service.Settings.HoursPerDay / 2m ? AttendanceBrushes.Partial
            : AttendanceBrushes.Short;
        return new AttendanceCellViewModel(employee, date) { Text = CellHours(hours), Background = bg, Tip = Strings.HoursTip(hours) };
    }

    /// <summary>
    /// H12 (faqat Schedule xodimlar): reja boshlanmagan — kulrang; kelmagan — sariq, keyin qizil; keldi — yashil;
    /// kun tugagach — bajarilgan foiz bo'yicha; dam — bo'sh; sababli — belgi bilan. Ochiq juftlik «?» yuqorida.
    /// </summary>
    private AttendanceCellViewModel BuildScheduleCell(Employee employee, DateOnly date, (ScheduleContext Ctx, ScheduleMonthResult Result) s, decimal hours)
    {
        var status = StatusOf(employee, s, date);
        var day = s.Result.Day(date);
        s.Ctx.Overrides.TryGetValue(date, out var o);
        var tip = StatusText(status, day);
        var worked = day is { WorkedMinutes: > 0 } ? CellHours(day.WorkedMinutes / 60m) : "";
        var (bg, text, fg) = status.State switch
        {
            ShiftState.Off => hours > 0
                ? (AttendanceBrushes.Full, CellHours(hours), AttendanceBrushes.Ink)
                : (date.DayOfWeek == DayOfWeek.Sunday ? AttendanceBrushes.SundayBg : AttendanceBrushes.Empty, "", AttendanceBrushes.Ink),
            ShiftState.NotStarted => (AttendanceBrushes.HeaderBg, "", AttendanceBrushes.Muted),
            ShiftState.Late => (AttendanceBrushes.Partial, "", AttendanceBrushes.Ink),
            ShiftState.Absent => (AttendanceBrushes.Short, "", AttendanceBrushes.Ink),
            ShiftState.Present => (AttendanceBrushes.Inside, "•", status.Minutes > 0 ? AttendanceBrushes.OpenText : AttendanceBrushes.InsideText),
            ShiftState.DoneGreen => (AttendanceBrushes.Full, worked, status.Minutes > 0 ? AttendanceBrushes.OpenText : AttendanceBrushes.Ink),
            ShiftState.DoneYellow => (AttendanceBrushes.Partial, worked, status.Minutes > 0 ? AttendanceBrushes.OpenText : AttendanceBrushes.Ink),
            ShiftState.DoneRed => (AttendanceBrushes.Short, worked, status.Minutes > 0 ? AttendanceBrushes.OpenText : AttendanceBrushes.Ink),
            _ => (AttendanceBrushes.NavActive, Strings.ExcusedMark(status.Excused!.Kind), AttendanceBrushes.NavActiveText),
        };
        return new AttendanceCellViewModel(employee, date)
        {
            Text = text,
            Background = bg,
            Foreground = fg,
            Weight = status.State is ShiftState.Present or ShiftState.Excused ? FontWeight.Bold : FontWeight.Normal,
            Tip = o is not null ? $"{tip}\n{Strings.MenuOtherTime.TrimEnd('…')}: {o.Note}" : tip,
            IsSchedule = true,
            Override = o,
            Excused = status.Excused,
        };
    }

    /// <summary>Katakka sig'ishi uchun 1 xona (9,72 → 9,7); aniq qiymat tooltip'da va kun tafsilotida.</summary>
    private static string CellHours(decimal hours) => NumberText.Days(Math.Round(hours, 1, MidpointRounding.AwayFromZero));

    // ---------- Bugun paneli ----------

    private void BuildToday()
    {
        var today = Today;
        TodayTitle = Strings.TodayTitle(today);
        var inside = new List<string>();
        var left = new List<string>();
        var absent = new List<string>();
        var late = new List<string>();
        foreach (var employee in main.BranchFilter.Apply(Service.Employees).Where(e => e.IsActive))
        {
            var a = _analysis.GetValueOrDefault(employee.Id);
            // H12: Schedule xodimi — smena boshlanmagan / dam / sababli bo'lsa «kelmagan» emas; kechikayotganlar alohida
            var status = ScheduleOf(employee) is { } sch ? StatusOf(employee, sch, today) : null;
            var lateMark = status is { State: ShiftState.Present, Minutes: > 0 } ? $" ({Strings.StatusLateMark(status.Minutes)})" : "";
            if (a?.CurrentInside is { } pair)
                inside.Add($"{employee.FullName} {TimeZoneInfo.ConvertTime(pair.In.OccurredAt, Zone):HH:mm}{lateMark}");
            else if (a?.Day(today) is { HasComplete: true } day)
                left.Add($"{employee.FullName} {TimeZoneInfo.ConvertTime(day.Pairs.Last(p => p.Out is not null).Out!.OccurredAt, Zone):HH:mm}");
            else if (status is { State: ShiftState.Late })
                late.Add($"{employee.FullName} ({status.Minutes} {Strings.MinutesShort})");
            else if (status is null or { State: ShiftState.Absent or ShiftState.DoneRed })
                absent.Add(employee.FullName);
        }
        LateCount = late.Count;
        LateNames = string.Join(", ", late);
        InsideCount = inside.Count;
        LeftCount = left.Count;
        AbsentCount = absent.Count;
        InsideNames = string.Join(", ", inside);
        LeftNames = string.Join(", ", left);
        AbsentNames = string.Join(", ", absent);
    }

    // ---------- Tasdiqlash kerak ----------

    private void BuildIssues()
    {
        var employees = main.BranchFilter.Apply(Service.Employees).ToDictionary(e => e.Id);
        Issues.Clear();
        foreach (var issue in _analysis.Values.SelectMany(a => a.Issues).OrderByDescending(i => i.Event.OccurredAt))
        {
            if (employees.TryGetValue(issue.EmployeeId, out var employee))
                Issues.Add(new IssueViewModel(this, issue, employee, Zone));
        }
        IssuesTitle = Strings.IssuesTitle(Issues.Count);
        OnPropertyChanged(nameof(HasIssues));
    }

    /// <summary>Muammoli voqea kunini jadvalda tanlash (kerak bo'lsa oy almashadi).</summary>
    public async Task ShowIssueAsync(IssueViewModel item)
    {
        var date = AttendanceRules.LocalDate(item.Issue.Event.OccurredAt, Zone);
        var month = new MonthKey(date.Year, date.Month);
        if (month != Month)
        {
            Month = month;
            await LoadAsync();
        }
        var cell = Rows.FirstOrDefault(r => r.Employee.Id == item.Employee.Id)?.Cells.FirstOrDefault(c => c.Date == date);
        if (cell is not null)
            SelectCell(cell);
    }

    public async Task ResolveIssueAsync(IssueViewModel item)
    {
        if (item.ResolveTime() is not { } at)
        {
            main.ShowError(Strings.InvalidTime);
            return;
        }
        var type = item.IsOpenIn ? AttendanceEventType.Out : AttendanceEventType.In;
        // H14: Schedule xodimida chegara — o'sha kun smenasi + 4 soat (sutkalik — 28 soat)
        var inDate = AttendanceRules.LocalDate(item.IsOpenIn ? item.Issue.Event.OccurredAt : at, Zone);
        var limit = item.Employee.PayMode == PayMode.Schedule
            ? AttendanceRules.ScheduleMaxPair(Service.ScheduleContextFor(item.Employee.Id))(inDate)
            : AttendanceRules.MaxShift;
        if ((at - item.Issue.Event.OccurredAt).Duration() > limit)
        {
            main.ShowError(Strings.ShiftTooLong((int)limit.TotalHours));
            return;
        }
        if (await main.RunAsync(() => Service.AddManualEventAsync(item.Employee, type, at, Strings.ConfirmedNote)))
            await AfterChangeAsync();
    }

    public async Task VoidEventAsync(AttendanceEvent e)
    {
        var employee = Service.Employees.FirstOrDefault(x => x.Id == e.EmployeeId);
        var local = TimeZoneInfo.ConvertTime(e.OccurredAt, Zone);
        var what = $"{employee?.FullName} — {(e.Type == AttendanceEventType.In ? Strings.EventIn : Strings.EventOut)} {local:dd.MM HH:mm}";
        var reason = await main.Dialogs.PromptAsync(Strings.VoidTitle, Strings.VoidPrompt(what), Strings.VoidReasonWatermark);
        if (string.IsNullOrWhiteSpace(reason))
            return;
        if (await main.RunAsync(() => Service.VoidEventAsync(e, reason)))
            await AfterChangeAsync();
    }

    private async Task AfterChangeAsync()
    {
        await LoadAsync();
        _ = main.Sync.SyncNowAsync();
    }

    // ---------- Kun tafsiloti ----------

    public void SelectCell(AttendanceCellViewModel cell)
    {
        if (_selected is not null)
            _selected.IsSelected = false;
        _selected = cell;
        cell.IsSelected = true;
        IsAdding = false;
        RefreshDetail();
    }

    private void ClearSelection()
    {
        _selected = null;
        IsAdding = false;
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        DayEvents.Clear();
        if (_selected is not { } cell)
        {
            HasSelection = false;
            DayTitle = Strings.DayDetailHint;
            DayPlan = null;
            OnPropertyChanged(nameof(HasNoDayEvents));
            return;
        }
        HasSelection = true;
        var analysis = _analysis.GetValueOrDefault(cell.Employee.Id);
        // 5.10: reja vaqti va H12 holati (Schedule xodimlari)
        DayPlan = ScheduleOf(cell.Employee) is { } sch ? StatusText(StatusOf(cell.Employee, sch, cell.Date), sch.Result.Day(cell.Date)) : null;
        var hours = analysis?.Day(cell.Date)?.Hours ?? 0;
        DayTitle = Strings.DayTitle(cell.Employee.FullName, cell.Date, hours);
        foreach (var e in Service.EventsOf(cell.Employee.Id, cell.Date, analysis))
        {
            var local = TimeZoneInfo.ConvertTime(e.OccurredAt, Zone);
            var time = DateOnly.FromDateTime(local.DateTime) == cell.Date ? local.ToString("HH:mm") : local.ToString("dd.MM HH:mm");
            var source = Service.FindDevice(e.DeviceId)?.Name ?? (e.IsManual ? Strings.ManualSource : Strings.UnknownDevice);
            DayEvents.Add(new DayEventViewModel(e, time, source, VoidEventAsync));
        }
        OnPropertyChanged(nameof(HasNoDayEvents));
    }

    [RelayCommand]
    private void StartAdd()
    {
        if (_selected is null)
            return;
        AddDate = _selected.Date.ToDateTime(TimeOnly.MinValue);
        var hasIn = DayEvents.Any(e => !e.IsVoided && e.Event.Type == AttendanceEventType.In);
        AddIsIn = !hasIn;
        AddTime = hasIn ? "18:00" : "08:00";
        AddNote = "";
        AddError = null;
        IsAdding = true;
    }

    [RelayCommand]
    private void CancelAdd() => IsAdding = false;

    [RelayCommand]
    private async Task SaveAdd()
    {
        if (_selected is null || AddDate is not { } date)
            return;
        if (!TimeOnly.TryParseExact(AddTime.Trim(), ["H:mm", "HH:mm", "H.mm", "HH.mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            AddError = Strings.InvalidTime;
            return;
        }
        var local = date.Date + t.ToTimeSpan();
        var at = new DateTimeOffset(local, Zone.GetUtcOffset(local));
        var employee = _selected.Employee;
        var type = AddIsIn ? AttendanceEventType.In : AttendanceEventType.Out;
        if (await main.RunAsync(() => Service.AddManualEventAsync(employee, type, at, AddNote)))
        {
            IsAdding = false;
            await AfterChangeAsync();
        }
    }

    // ---------- 5.10: kontekst menyu — Dam / Boshqa vaqt (H2) va Sababli (H8); izoh majburiy, audit ----------

    private Task<string?> AskNoteAsync(string title, AttendanceCellViewModel cell) =>
        main.Dialogs.PromptAsync(title, Strings.NoteRequiredPrompt(Strings.DayLine(cell.Employee.FullName, cell.Date)), Strings.NoteWatermark);

    private static string Plain(string menu) => menu.TrimEnd('\u2026');

    private async Task AfterScheduleChangeAsync(Func<Task> action)
    {
        if (await main.RunAsync(action))
        {
            _analysis = Service.AnalyzeAttendance();
            BuildGrid();
            BuildToday();
            RefreshDetail();
        }
    }

    public async Task SetDayOffAsync(AttendanceCellViewModel cell)
    {
        if (await AskNoteAsync(Plain(Strings.MenuDayOff), cell) is not { } note)
            return;
        await AfterScheduleChangeAsync(() => Service.SetOverrideAsync(cell.Employee, cell.Date, true, null, null, null, note));
    }

    public async Task SetOtherTimeAsync(AttendanceCellViewModel cell)
    {
        var shift = Service.ScheduleContextFor(cell.Employee.Id).ShiftFor(cell.Date);
        var start = shift is null ? "08:00" : TimeZoneInfo.ConvertTime(shift.Start, Zone).ToString("HH:mm");
        var duration = Strings.HoursMinutes(shift?.DurationMinutes ?? 9 * 60);
        var values = await main.Dialogs.PromptFieldsAsync(Strings.OtherTimeTitle, Strings.NoteRequiredPrompt(Strings.DayLine(cell.Employee.FullName, cell.Date)),
            (Strings.FieldStart, start, "08:00"), (Strings.FieldDuration, duration, "9:00"),
            (Strings.FieldBreak, shift?.BreakMinutes.ToString(CultureInfo.InvariantCulture) ?? "", "0"), (Strings.Note, "", Strings.NoteWatermark));
        if (values is null)
            return;
        if (!TimeInput.TryParseTime(values[0], out var t))
        {
            main.ShowError(Strings.InvalidTime);
            return;
        }
        if (!TimeInput.TryParseDuration(values[1], out var minutes))
        {
            main.ShowError(Strings.InvalidDuration);
            return;
        }
        int? breakMinutes = int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : null;
        await AfterScheduleChangeAsync(() => Service.SetOverrideAsync(cell.Employee, cell.Date, false, t, minutes, breakMinutes, values[3]));
    }

    public async Task SetExcusedAsync(AttendanceCellViewModel cell, ExcusedKind kind)
    {
        var title = kind == ExcusedKind.Paid ? Strings.MenuExcusedPaid : Strings.MenuExcusedUnpaid;
        if (await AskNoteAsync(Plain(title), cell) is not { } note)
            return;
        await AfterScheduleChangeAsync(() => Service.SetExcusedAsync(cell.Employee, cell.Date, kind, note));
    }

    public async Task RemoveOverrideAsync(AttendanceCellViewModel cell)
    {
        if (cell.Override is not { } o || await AskNoteAsync(Plain(Strings.MenuRemoveOverride), cell) is not { } note)
            return;
        await AfterScheduleChangeAsync(() => Service.RemoveOverrideAsync(cell.Employee, o, note));
    }

    public async Task RemoveExcusedAsync(AttendanceCellViewModel cell)
    {
        if (cell.Excused is not { } x || await AskNoteAsync(Plain(Strings.MenuRemoveExcused), cell) is not { } note)
            return;
        await AfterScheduleChangeAsync(() => Service.RemoveExcusedAsync(cell.Employee, x, note));
    }
}
