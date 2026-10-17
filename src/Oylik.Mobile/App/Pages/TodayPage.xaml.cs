using Oylik.Mobile.ViewModels;

namespace Oylik.Mobile.Pages;

public partial class TodayPage : ContentPage
{
    private readonly TodayViewModel _vm;
    private IDispatcherTimer? _live;

    public TodayPage(TodayViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
        // Ichkaridagilar soati jonli — ekran ochiq ekan har daqiqada yangilanadi.
        _live ??= Dispatcher.CreateTimer();
        _live.Interval = TimeSpan.FromSeconds(60);
        _live.Tick -= OnTick;
        _live.Tick += OnTick;
        _live.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _live?.Stop();
    }

    private async void OnTick(object? sender, EventArgs e) => await _vm.RefreshAsync();
}
