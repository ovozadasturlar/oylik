using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Oylik.Desktop.Resources;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Views;

public partial class PayrollView : UserControl
{
    private bool _editing;
    private readonly DataGridColumn _advanceColumn;
    private readonly DataGridColumn _attendanceColumn;

    public PayrollView()
    {
        InitializeComponent();
        _advanceColumn = Grid.Columns.First(c => Equals(c.Header, Strings.ColAdvance));
        _attendanceColumn = Grid.Columns.First(c => Equals(c.Tag, "attendance")); // sarlavha tilga qarab o'zgaradi
        Grid.BeginningEdit += (_, e) =>
        {
            // H9: Schedule qatorida Summa (soatlik stavka) va Kun (ishlangan soat) hisoblanadi — tahrirlanmaydi
            if (e.Row.DataContext is PayrollRowViewModel { IsSchedule: true } && (e.Column == Grid.Columns[3] || e.Column == Grid.Columns[4]))
            {
                e.Cancel = true;
                return;
            }
            _editing = true;
        };
        GridEditing.Attach(Grid, () => Vm is { IsOpen: true });
        Grid.CellEditEnded += (_, _) => _editing = false;
        Grid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Grid.CellPointerPressed += OnCellPointerPressed;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Grid.LayoutUpdated += (_, _) => SyncFooter();
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm)
                return;
            _attendanceColumn.IsVisible = vm.HasAttendance;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(PayrollViewModel.HasAttendance))
                    _attendanceColumn.IsVisible = vm.HasAttendance;
            };
        };
    }

    private PayrollViewModel? Vm => DataContext as PayrollViewModel;

    /// <summary>
    /// JAMI qatori ustunlari DataGrid ustunlari bilan bir xil kenglikda: Davomat ustuni ko'rinsa/yashirilsa
    /// yoki foydalanuvchi ustun kengligini o'zgartirsa ham yig'indilar o'z ustuni ostida turadi.
    /// </summary>
    private void SyncFooter()
    {
        var defs = Footer.ColumnDefinitions;
        for (int i = 0; i < Grid.Columns.Count && i < defs.Count; i++)
        {
            var column = Grid.Columns[i];
            var width = column.IsVisible ? column.ActualWidth : 0;
            if (double.IsNaN(width) || width < 0)
                continue;
            if (defs[i].Width.IsAbsolute && Math.Abs(defs[i].Width.Value - width) < 0.5)
                continue;
            defs[i].Width = new GridLength(width);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>Аванс katagiga bir marta bosish → Avanslar oynasi.</summary>
    private void OnCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (Vm is null || e.Column != _advanceColumn || e.Row.DataContext is not PayrollRowViewModel row)
            return;
        if (!e.PointerPressedEventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.PointerPressedEventArgs.Handled = true;
        // Bosish hodisasi tugagach ochamiz (aks holda DataGrid sichqonchani ushlab qoladi).
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Vm.OpenAdvancesCommand.Execute(row));
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null || _editing || e.Source is TextBox)
            return;

        // F2 Аванс ustunida → Avanslar oynasi.
        if (e.Key == Key.F2 && Grid.CurrentColumn == _advanceColumn && Grid.SelectedItem is PayrollRowViewModel row)
        {
            Vm.OpenAdvancesCommand.Execute(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && Grid.SelectedItem is PayrollRowViewModel toRemove && Vm.IsOpen)
        {
            Vm.RemoveLineCommand.Execute(toRemove);
            e.Handled = true;
        }
    }
}
