using Oylik.Contracts;
using Oylik.Mobile.Logic;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Oylik.Mobile.Pages;

/// <summary>Bog'lash QR'ini skanerlash (ZXing — faqat shu yerda).</summary>
public sealed class ScanPage : ContentPage
{
    private readonly CameraBarcodeReaderView _reader;
    private readonly Label _hint;
    private int _done; // 0/1 — Interlocked

    public Func<PairingQrPayload, Task>? Scanned { get; set; }

    public ScanPage()
    {
        Title = Strings.ScanTitle;
        BackgroundColor = Colors.Black;

        _reader = new CameraBarcodeReaderView
        {
            Options = new BarcodeReaderOptions { Formats = BarcodeFormat.QrCode, AutoRotate = true, Multiple = false },
            IsDetecting = true,
        };
        _reader.BarcodesDetected += OnDetected;

        _hint = new Label
        {
            Text = Strings.ScanTitle,
            TextColor = Colors.White,
            FontSize = 17,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(16, 20),
        };

        var grid = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)] };
        grid.Add(_reader, 0, 0);
        grid.Add(_hint, 0, 1);
        Content = grid;
    }

    private void OnDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        // Kamera oqimidan (fon oqimi) ketma-ket keladi — birinchi to'g'ri QR'dan keyin darhol to'xtaymiz.
        if (Volatile.Read(ref _done) != 0) return;
        var text = e.Results?.FirstOrDefault()?.Value;
        var payload = PairingService.ParseQr(text);
        if (payload is null)
        {
            MainThread.BeginInvokeOnMainThread(() => _hint.Text = Strings.ErrBadQr);
            return;
        }
        if (Interlocked.Exchange(ref _done, 1) != 0) return;
        Diag.Log("scan: QR o'qildi");
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _reader.IsDetecting = false;
            _reader.BarcodesDetected -= OnDetected;
            if (Scanned is { } cb) await cb(payload);
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _reader.IsDetecting = false;
    }
}
