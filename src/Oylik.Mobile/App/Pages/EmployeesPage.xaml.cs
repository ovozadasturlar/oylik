using Oylik.Mobile.ViewModels;

namespace Oylik.Mobile.Pages;

public partial class EmployeesPage : ContentPage
{
    private readonly EmployeesViewModel _vm;

    public EmployeesPage(EmployeesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}
