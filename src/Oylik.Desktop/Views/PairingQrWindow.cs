using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Oylik.Contracts;
using Oylik.Desktop.Resources;
using QRCoder;

namespace Oylik.Desktop.Views;

/// <summary>Telefon bog'lash QR'i: payload JSON, katta kod va qolgan vaqt.</summary>
public sealed class PairingQrWindow : DialogWindow
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public PairingQrWindow(string json, PairingQrPayload payload, DateTimeOffset expiresAt)
    {
        Title = Strings.PairTitle;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var remaining = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Foreground = Palette.Get("MutedBrush") };
        void Tick()
        {
            var left = expiresAt - DateTimeOffset.UtcNow;
            remaining.Text = left > TimeSpan.Zero ? Strings.PairExpiresIn(left) : Strings.PairExpired;
            if (left <= TimeSpan.Zero)
                _timer.Stop();
        }
        _timer.Tick += (_, _) => Tick();
        Tick();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();

        var close = new Button { Content = Strings.Close, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = Strings.PairHint, TextWrapping = TextWrapping.Wrap, LineHeight = 20 },
                new Border
                {
                    Background = Brushes.White, // QR doim oq fonda — skaner o'qishi uchun (qorong'i mavzuda ham)
                    BorderBrush = Palette.Get("BorderBrushLight"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new Image { Source = Render(json), Width = 280, Height = 280 },
                },
                new TextBlock
                {
                    Text = $"{payload.Code[..3]} {payload.Code[3..]}",
                    FontSize = 34,
                    FontWeight = FontWeight.Bold,
                    FontFamily = new FontFamily("Consolas, monospace"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                remaining,
                new SelectableTextBlock { Text = Strings.PairManual(payload.Server), TextWrapping = TextWrapping.Wrap, Foreground = Palette.Get("SubtleTextBrush") },
                new SelectableTextBlock
                {
                    Text = payload.LocalServer is { } local ? Strings.PairManualLocal(local) : "",
                    IsVisible = payload.LocalServer is not null,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Palette.Get("SubtleTextBrush"),
                },
            },
        };
        if (IsLocalhost(payload.Server))
        {
            panel.Children.Add(new Border
            {
                Background = Palette.Get("WarnBg"),
                BorderBrush = Palette.Get("WarnBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8),
                Child = new TextBlock { Text = Strings.PairLocalhostWarning, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Get("WarnText") },
            });
        }
        panel.Children.Add(close);
        Content = panel;
    }

    private static bool IsLocalhost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    public static Bitmap Render(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(10);
        return new Bitmap(new MemoryStream(png));
    }
}
