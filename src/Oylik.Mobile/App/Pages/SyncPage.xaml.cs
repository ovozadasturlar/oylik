using Oylik.Mobile.ViewModels;

namespace Oylik.Mobile.Pages;

public partial class SyncPage : ContentPage
{
    private readonly SyncViewModel _vm;

    public SyncPage(SyncViewModel vm)
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
