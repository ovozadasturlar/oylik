using System.ComponentModel;
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
        GridEditing.Attach(Grid, () => _subscribed?.Main.CanEdit == true);
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.EditNameRequested -= EditName;
                _subscribed.Main.PropertyChanged -= OnMainChanged;
            }
            _subscribed = DataContext as EmployeesViewModel;
            if (_subscribed is not null)
            {
                _subscribed.EditNameRequested += EditName;
                _subscribed.Main.PropertyChanged += OnMainChanged;
            }
            ApplySalaryColumns();
        };
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CanSeeSalary))
            ApplySalaryColumns();
    }

    /// <summary>Bosqich 1: maosh ustunlari (Tag="salary") faqat maoshni ko'ra oladigan rolga (Qorovul'da yashirin).</summary>
    private void ApplySalaryColumns()
    {
        var visible = _subscribed?.Main.CanSeeSalary ?? true;
        foreach (var column in Grid.Columns.Where(c => Equals(c.Tag, "salary")))
            column.IsVisible = visible;
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
