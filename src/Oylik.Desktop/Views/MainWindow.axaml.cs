using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Kirish ekrani ochilganda — kursor darhol PIN maydonida.
        LoginOverlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && LoginOverlay.IsVisible)
                Dispatcher.UIThread.Post(() => PinBox.Focus(), DispatcherPriority.Input);
        };
    }

    private void DismissError(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StatusError = null;
    }
}
