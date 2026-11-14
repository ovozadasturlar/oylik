using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;

namespace Oylik.Desktop.ViewModels;

/// <summary>A9: Kun yonida davomat maslahati va [Ko'chirish] — faqat ochiq oyda, audit bilan, avtomatik yozilmaydi.</summary>
public sealed partial class PayrollViewModel
{
    private IReadOnlyDictionary<Guid, AttendanceMonthSummary> _attendance = new Dictionary<Guid, AttendanceMonthSummary>();
    private MonthKey? _attendanceMonth;

    /// <summary>Tanlangan oyda davomat voqealari bor — "Davomat" ustuni ko'rinadi.</summary>
    public bool HasAttendance => _attendance.Values.Any(s => s.AttendedDays > 0);

    public AttendanceMonthSummary? AttendanceOf(Guid employeeId) =>
        _attendance.TryGetValue(employeeId, out var s) && s.AttendedDays > 0 ? s : null;

    public async Task RefreshAttendanceAsync()
    {
        if (!Main.IsReady)
            return;
        if (Period is not { } period)
        {
            _attendance = new Dictionary<Guid, AttendanceMonthSummary>();
            _attendanceMonth = null;
        }
        else
        {
            IReadOnlyDictionary<Guid, AttendanceMonthSummary> result = _attendance;
            await Main.RunDbAsync(async () => result = await Service.GetMonthSummariesAsync(period.Key));
            if (Period != period)
                return;
            _attendance = result;
            _attendanceMonth = period.Key;
        }
        foreach (var row in Rows)
            row.RefreshAttendance();
        OnPropertyChanged(nameof(HasAttendance));
        CopyAllAttendanceCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CopyAttendance(PayrollRowViewModel? row)
    {
        if (!Main.Demand(Access.Edit))
            return;
        if (row?.Attendance is not { } summary || Period is not { } period)
            return;
        if (period.IsClosed)
        {
            Main.ShowError(Strings.CopyTipClosed(summary.AttendedDays, summary.TotalHours));
            return;
        }
        if (await Main.RunAsync(() => Service.CopyAttendanceToDaysAsync(period, row.Line, summary.AttendedDays)))
        {
            row.Refresh();
            RefreshSummary();
        }
    }

    private bool CanCopyAll() => IsOpen && Rows.Any(r => r.CanCopyAttendance);

    /// <summary>Barcha farq qiladigan qatorlar uchun [Ko'chirish] — tasdiq bilan.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyAll))]
    private async Task CopyAllAttendance()
    {
        if (!Main.Demand(Access.Edit))
            return;
        if (Period is not { IsClosed: false } period)
            return;
        var rows = Rows.Where(r => r.CanCopyAttendance).ToList();
        if (rows.Count == 0 || !await Main.Dialogs.ConfirmAsync(Strings.ConfirmCopyAll(rows.Count, Strings.Month(period.Key))))
            return;
        foreach (var row in rows)
        {
            var days = row.Attendance!.AttendedDays;
            if (!await Main.RunAsync(() => Service.CopyAttendanceToDaysAsync(period, row.Line, days)))
                break;
            row.Refresh();
        }
        RefreshSummary();
        CopyAllAttendanceCommand.NotifyCanExecuteChanged();
    }
}
