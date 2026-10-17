using Avalonia.Controls;
using Avalonia.Interactivity;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void DismissError(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusError = null;
    }
}
