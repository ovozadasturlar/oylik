using Avalonia.Controls;
using Avalonia.Input;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Views;

public partial class PeriodsView : UserControl
{
    public PeriodsView() => InitializeComponent();

    private void OpenSelected(object? sender, TappedEventArgs e)
    {
        if (DataContext is PeriodsViewModel vm && vm.OpenCommand.CanExecute(null))
            vm.OpenCommand.Execute(null);
    }
}

public partial class AuditView : UserControl
{
    public AuditView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void Commit(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            await vm.Save();
    }
}
