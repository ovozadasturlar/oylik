using Avalonia.Controls;
using Avalonia.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Views;

public partial class AttendanceView : UserControl
{
    public AttendanceView() => InitializeComponent();

    /// <summary>Katakka bosish → kun tafsiloti.</summary>
    private void OnCellTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AttendanceViewModel vm && (sender as Control)?.DataContext is AttendanceCellViewModel cell)
            vm.SelectCell(cell);
    }

    /// <summary>5.10: xodim-kun katagi kontekst menyusi — Dam / Boshqa vaqt (H2), Sababli (H8). Faqat «Jadval bo'yicha» xodimlar.</summary>
    private void OnCellContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not AttendanceViewModel vm || sender is not Control control || control.DataContext is not AttendanceCellViewModel cell)
            return;
        vm.SelectCell(cell);
        MenuItem Item(string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await action();
            return item;
        }
        var menu = new ContextMenu();
        if (!cell.IsSchedule)
        {
            menu.Items.Add(new MenuItem { Header = Strings.ScheduleOnlyHint, IsEnabled = false });
        }
        else
        {
            menu.Items.Add(Item(Strings.MenuDayOff, () => vm.SetDayOffAsync(cell)));
            menu.Items.Add(Item(Strings.MenuOtherTime, () => vm.SetOtherTimeAsync(cell)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Strings.MenuExcusedPaid, () => vm.SetExcusedAsync(cell, ExcusedKind.Paid)));
            menu.Items.Add(Item(Strings.MenuExcusedUnpaid, () => vm.SetExcusedAsync(cell, ExcusedKind.Unpaid)));
            if (cell.Override is not null || cell.Excused is not null)
                menu.Items.Add(new Separator());
            if (cell.Override is not null)
                menu.Items.Add(Item(Strings.MenuRemoveOverride, () => vm.RemoveOverrideAsync(cell)));
            if (cell.Excused is not null)
                menu.Items.Add(Item(Strings.MenuRemoveExcused, () => vm.RemoveExcusedAsync(cell)));
        }
        menu.Open(control);
        e.Handled = true;
    }

    /// <summary>"Tasdiqlash kerak" kartasidagi matnga bosish → o'sha kun jadvalda tanlanadi.</summary>
    private void OnIssueTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is IssueViewModel issue)
            issue.ShowCommand.Execute(null);
    }
}
