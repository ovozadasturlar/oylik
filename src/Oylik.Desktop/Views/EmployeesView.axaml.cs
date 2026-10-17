using Avalonia.Controls;
using Avalonia.Threading;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Views;

public partial class EmployeesView : UserControl
{
    private EmployeesViewModel? _subscribed;

    public EmployeesView()
    {
        InitializeComponent();
        GridEditing.Attach(Grid, () => true);
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
                _subscribed.EditNameRequested -= EditName;
            _subscribed = DataContext as EmployeesViewModel;
            if (_subscribed is not null)
                _subscribed.EditNameRequested += EditName;
        };
    }

    /// <summary>Yangi xodim: qator ko'rinadi va Ism katagi tahrirga ochiladi (matn belgilangan — darhol yoziladi).</summary>
    private void EditName(EmployeeRowViewModel row) => Dispatcher.UIThread.Post(() =>
    {
        Grid.ScrollIntoView(row, Grid.Columns[1]);
        Grid.SelectedItem = row;
        Grid.CurrentColumn = Grid.Columns[1];
        Grid.Focus();
        Grid.BeginEdit();
    }, DispatcherPriority.Background);
}
