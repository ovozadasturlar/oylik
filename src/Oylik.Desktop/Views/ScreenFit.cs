using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Oylik.Desktop.Views;

/// <summary>
/// Barcha dialog oynalari joriy monitorning ish maydoni (WorkingArea, DPI hisobga olingan) ichida ochiladi:
/// o'lcham = min(kerakli, ish maydoni − 2×<see cref="Margin"/>), markazda; balandlik kontentga qarab bo'lsa — kontent ichida scroll.
/// </summary>
public static class ScreenFit
{
    /// <summary>Ekran chetidan bo'sh joy (har tomonda), DIP.</summary>
    public const double Margin = 16;

    /// <summary><paramref name="reference"/> turgan monitorning ish maydoni — DIP'da (Scaling'ga bo'lingan).</summary>
    public static Rect? WorkingArea(Window reference)
    {
        var screen = reference.Screens.ScreenFromWindow(reference) ?? reference.Screens.Primary;
        if (screen is null)
            return null;
        var s = screen.Scaling;
        var wa = screen.WorkingArea;
        return new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
    }

    /// <summary>Ko'rsatishdan oldin: o'lcham chegaralari, scroll; ochilgach — joylashuv ish maydoni ichiga.</summary>
    public static void Apply(Window dialog, Window owner)
    {
        if (WorkingArea(owner) is { } area)
            Apply(dialog, area);
        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(() => KeepInside(dialog), DispatcherPriority.Loaded);
    }

    /// <summary>O'lchamni berilgan ish maydoniga moslash (snapshot'da kichik ekranni simulyatsiya qilish uchun ham).</summary>
    public static void Apply(Window dialog, Rect area)
    {
        var maxWidth = Math.Max(240, area.Width - 2 * Margin);
        var maxHeight = Math.Max(200, area.Height - 2 * Margin);
        dialog.MaxWidth = Math.Min(dialog.MaxWidth, maxWidth);
        dialog.MaxHeight = Math.Min(dialog.MaxHeight, maxHeight);
        dialog.MinWidth = Math.Min(dialog.MinWidth, maxWidth);
        dialog.MinHeight = Math.Min(dialog.MinHeight, maxHeight);
        if (!double.IsNaN(dialog.Width))
            dialog.Width = Math.Min(dialog.Width, maxWidth);
        if (!double.IsNaN(dialog.Height))
            dialog.Height = Math.Min(dialog.Height, maxHeight);
        // Balandlik kontentdan — sig'masa kontent ichida scroll (tugmalar ham scroll bilan ko'rinadi)
        if (dialog.SizeToContent.HasFlag(SizeToContent.Height) && dialog.Content is Control content and not ScrollViewer)
        {
            dialog.Content = null;
            dialog.Content = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
        }
    }

    /// <summary>Oyna ish maydonidan chiqib ketgan bo'lsa — ichiga suriladi (markazga yaqin qoladi).</summary>
    public static void KeepInside(Window dialog)
    {
        var screen = dialog.Screens.ScreenFromWindow(dialog) ?? dialog.Screens.Primary;
        if (screen is null)
            return;
        var s = screen.Scaling;
        var wa = screen.WorkingArea;
        var frame = dialog.FrameSize ?? dialog.ClientSize;
        var width = (int)Math.Ceiling(frame.Width * s);
        var height = (int)Math.Ceiling(frame.Height * s);
        var margin = (int)(Margin * s);
        static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);
        var x = Clamp(dialog.Position.X, wa.X + margin, wa.Right - margin - width);
        var y = Clamp(dialog.Position.Y, wa.Y + margin, wa.Bottom - margin - height);
        if (x != dialog.Position.X || y != dialog.Position.Y)
            dialog.Position = new PixelPoint(x, y);
    }
}
