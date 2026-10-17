using Oylik.Mobile.Logic;
using Oylik.Mobile.ViewModels;

namespace Oylik.Mobile.Pages;

public partial class PairPage : ContentPage
{
    private readonly PairViewModel _vm;
    private readonly IServiceProvider _services;

    public PairPage(PairViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        DemoNote.IsVisible = PairingService.AllowDemo;
        BindingContext = _vm = vm;
        _services = services;
    }

    public void SetNotice(string? notice) => _vm.Notice = notice;

    private bool _scanOpen;

    private async void OnScanClicked(object? sender, EventArgs e)
    {
        if (_scanOpen || _vm.IsBusy) return; // ikki marta bosilsa ikkita skaner ochilmasin
        _scanOpen = true;
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                _vm.Error = Strings.ScanCameraDenied;
                _scanOpen = false;
                return;
            }

            var scan = _services.GetRequiredService<ScanPage>();
            scan.Scanned = async payload =>
            {
                try { await Navigation.PopAsync(); }
                catch (Exception ex) { Diag.Log($"scan: yopishda xato {ex.Message}"); }
                _scanOpen = false;
                await _vm.PairFromQrAsync(payload);
            };
            scan.Disappearing += (_, _) => _scanOpen = false;
            await Navigation.PushAsync(scan);
        }
        catch (Exception ex)
        {
            _scanOpen = false;
            Diag.Log($"scan: {ex}");
            _vm.Error = Strings.ScanCameraDenied;
        }
    }
}

public sealed class NotConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not true;
}
