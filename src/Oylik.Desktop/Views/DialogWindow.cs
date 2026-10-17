using Avalonia.Controls;
using Avalonia.Input;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Views;

/// <summary>ViewModel.CloseRequested → Close; Esc → yopish.</summary>
public class DialogWindow : Window
{
    protected override Type StyleKeyOverride => typeof(Window);

    public DialogWindow() => Icon = AppIcon.Get();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DialogViewModel vm)
            vm.CloseRequested += Close;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}

public partial class AdvancesWindow : DialogWindow
{
    public AdvancesWindow()
    {
        InitializeComponent();
        Opened += (_, _) => AmountBox.Focus();
    }
}

public partial class NewEmployeeWindow : DialogWindow
{
    public NewEmployeeWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
    }
}

public partial class ImportWindow : DialogWindow
{
    public ImportWindow() => InitializeComponent();
}

public partial class ScheduleEditWindow : DialogWindow
{
    public ScheduleEditWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
    }
}

public partial class AssignScheduleWindow : DialogWindow
{
    public AssignScheduleWindow() => InitializeComponent();
}
