using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Oylik.Desktop.Views;

/// <summary>
/// Excel kabi tahrir (Oylik va Xodimlar jadvallari):
/// - tahrirga o'tganda (bosish, F2, Enter/Tab bilan kelish) matn to'liq belgilanadi — yozilgan qiymat eskisini almashtiradi;
/// - tahrirlanadigan katakda yozish tahrirni boshlaydi (DataGrid buni o'zi qilmaydi), birinchi belgi eski qiymat o'rniga;
/// - tahrir TextBox'i katakni to'liq egallaydi (raqamli ustunlarda katak o'ngga tekislangan — tahrir paytida Stretch).
/// Saqlash binding'da: UpdateSourceTrigger=LostFocus (Enter/Tab/boshqa katak — saqlaydi, Esc — bekor).
/// </summary>
public static class GridEditing
{
    public static void Attach(DataGrid grid, Func<bool> canEdit)
    {
        var editing = false;
        DataGridCell? cell = null;

        grid.BeginningEdit += (_, _) => editing = true;
        grid.CellEditEnded += (_, _) =>
        {
            editing = false;
            cell?.ClearValue(ContentControl.HorizontalContentAlignmentProperty);
            cell?.ClearValue(ContentControl.VerticalContentAlignmentProperty);
            cell = null;
        };
        grid.PreparingCellForEdit += (_, e) =>
        {
            if (e.EditingElement is not TextBox box)
                return;
            cell = box.Parent as DataGridCell ?? box.FindAncestorOfType<DataGridCell>();
            Stretch(cell);
            var typed = (e.EditingEventArgs as TextInputEventArgs)?.Text;
            Dispatcher.UIThread.Post(() =>
            {
                if (!box.IsAttachedToVisualTree())
                    return;
                if (cell is null)
                    Stretch(cell = box.FindAncestorOfType<DataGridCell>());
                box.Focus();
                if (typed is null)
                {
                    box.SelectAll();
                    return;
                }
                box.Text = typed;
                box.CaretIndex = typed.Length;
            }, DispatcherPriority.Background);
        };
        grid.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (editing || e.Source is TextBox || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
                return;
            if (!canEdit() || grid.IsReadOnly || grid.CurrentColumn is not DataGridTextColumn { IsReadOnly: false })
                return;
            if (grid.BeginEdit(e))
                e.Handled = true;
        }, RoutingStrategies.Bubble);
    }

    private static void Stretch(DataGridCell? cell)
    {
        if (cell is null)
            return;
        cell.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        cell.VerticalContentAlignment = VerticalAlignment.Stretch;
    }
}
