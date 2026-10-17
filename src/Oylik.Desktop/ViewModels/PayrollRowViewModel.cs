using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Views;

namespace Oylik.Desktop.ViewModels;

/// <summary>Oylik jadvalining bitta qatori. Tahrirlanadigan ustunlar matn sifatida bog'lanadi va tasdiqlanganda darhol saqlanadi.</summary>
public sealed partial class PayrollRowViewModel(PayrollViewModel parent, PayrollPeriod period, PayrollLine line, Employee employee)
    : ObservableObject
{
    private static readonly IBrush Ink = Palette.Get("InkBrush");
    private static readonly IBrush Manual = Palette.Get("ManualBrush");
    private static readonly IBrush Negative = Palette.Get("NegativeBrush");

    public PayrollLine Line { get; } = line;
    public Employee Employee { get; } = employee;
    public PayrollPeriod Period { get; } = period;

    [ObservableProperty] private int _no;

    /// <summary>H12: joriy ochiq oyda — bugun kechikmoqda / kelmadi bo'lsa ism katagi bo'yaladi (60 s da yangilanadi).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameBackground), nameof(NameTip))]
    private LiveNameMark? _mark;

    public IBrush? NameBackground => Mark?.Background;
    public string? NameTip => Mark?.Tip;

    public string SalaryText
    {
        get => NumberText.Money(Line.MonthlySalary);
        set
        {
            if (!ShortInput.TryParse(value, AmountField.MonthlySalary, parent.ShortInputEnabled, out var salary))
            {
                Reject(Strings.InvalidNumber);
                return;
            }
            parent.Save(this, s => s.SetMonthlySalaryAsync(Period, Line, salary));
        }
    }

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

    /// <summary>H9: Schedule qatorida Summa = soatlik stavka, Kun = ishlangan soat (s:dd) — tahrirlanmaydi.</summary>
    public bool IsSchedule => Line.IsSchedule;

    public string? ScheduleTip => IsSchedule
        ? Strings.HourlyRateTip(Line.HourlyRate) + "\n" + Strings.ScheduleRowTip(Line, parent.ScheduleNameOf(Employee.Id))
        : null;

    public string DailyText
    {
        get => IsSchedule ? Strings.Rate(Line.HourlyRate) : NumberText.Money(Line.DailyRate);
        set
        {
            if (IsSchedule)
                return;
            if (!ShortInput.TryParse(value, AmountField.DailyRate, parent.ShortInputEnabled, out var daily))
            {
                Reject(Strings.InvalidNumber);
                return;
            }
            if (daily == Line.DailyRate && Employee.DailyRateIsManual)
                return;
            parent.Save(this, s => s.SetDailyRateAsync(Period, Line, daily));
        }
    }

    public string DaysText
    {
        get => IsSchedule ? Strings.HoursMinutes(Line.WorkedMinutes) : NumberText.Days(Line.DaysWorked);
        set
        {
            if (IsSchedule)
                return;
            if (string.IsNullOrWhiteSpace(value))
                value = "0";
            if (!NumberText.TryParseDays(value, out var days))
            {
                Reject(Strings.InvalidDays);
                return;
            }
            parent.Save(this, s => s.SetDaysWorkedAsync(Period, Line, days));
        }
    }

    public string TotalText => NumberText.Money(Line.Total);
    public string AdvanceText => NumberText.Money(Line.AdvanceTotal);
    public string RemainderText => NumberText.Money(Line.Remainder);
    public bool HasDebt => Line.HasCarryOverDebt;
    public bool IsZeroAdvance => Line.AdvanceTotal == 0;
    public bool IsManualDaily => Employee.DailyRateIsManual;
    public bool IsNegative => Line.Remainder < 0;

    public IBrush DailyBrush => IsManualDaily && !IsSchedule ? Manual : Ink;
    public FontStyle DailyFontStyle => IsManualDaily && !IsSchedule ? FontStyle.Italic : FontStyle.Normal;
    public IBrush RemainderBrush => IsNegative ? Negative : Ink;
    public FontWeight RemainderWeight => IsNegative ? FontWeight.SemiBold : FontWeight.Normal;

    public string SearchKey => Employee.FullName;

    // ---------- A9 ----------

    public AttendanceMonthSummary? Attendance => parent.AttendanceOf(Employee.Id);
    public bool HasAttendanceHint => Attendance is not null && !IsSchedule; // A9 — faqat Kunbay qatorlar
    public bool AttendanceMatches => Attendance is { } a && a.AttendedDays == Line.DaysWorked;
    public bool CanCopyAttendance => HasAttendanceHint && !Period.IsClosed && !AttendanceMatches;
    public string AttendanceText => Attendance is { } a ? Strings.AttendanceShort(a.AttendedDays, a.TotalHours) : "";
    public string? AttendanceTip => Attendance is not { } a ? null
        : Period.IsClosed ? Strings.CopyTipClosed(a.AttendedDays, a.TotalHours)
        : AttendanceMatches ? Strings.CopyTipSame(a.AttendedDays, a.TotalHours)
        : Strings.CopyTip(a.AttendedDays, a.TotalHours);

    public void RefreshAttendance()
    {
        OnPropertyChanged(nameof(Attendance));
        OnPropertyChanged(nameof(HasAttendanceHint));
        OnPropertyChanged(nameof(AttendanceMatches));
        OnPropertyChanged(nameof(CanCopyAttendance));
        OnPropertyChanged(nameof(AttendanceText));
        OnPropertyChanged(nameof(AttendanceTip));
    }

    public void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>Noto'g'ri qiymat — eski qiymatni qaytarish (binding tugagach).</summary>
    private void Reject(string message)
    {
        parent.Main.ShowError(message);
        Dispatcher.UIThread.Post(Refresh);
    }
}
