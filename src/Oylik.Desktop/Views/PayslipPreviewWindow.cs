using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services.Payslips;
using Serilog;

namespace Oylik.Desktop.Views;

/// <summary>5.11: hisob varaqasi oldindan ko'rish — [Chop etish] [Saqlash] [Telegram'ga yuborish].</summary>
public sealed class PayslipPreviewWindow : DialogWindow
{
    private readonly IReadOnlyList<PayslipModel> _slips;
    private readonly PayrollSheetModel? _sheet;
    private readonly MonthKey _month;
    private readonly string _fileName;
    private readonly StackPanel _pages = new() { Spacing = 16, Margin = new Thickness(24) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button[] _actions;
    private byte[]? _pdf;

    public PayslipPreviewWindow(string title, IReadOnlyList<PayslipModel> slips, PayrollSheetModel? sheet, MonthKey month, string fileName)
    {
        _slips = slips;
        _sheet = sheet;
        _month = month;
        _fileName = fileName;
        Title = title;
        Width = 900;
        Height = 860;
        MinWidth = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var print = new Button { Content = Strings.PrintAction, MinWidth = 120 };
        var save = new Button { Content = Strings.SavePdf, MinWidth = 120 };
        var telegram = new Button { Content = Strings.SendTelegram, MinWidth = 160, Classes = { "Primary" } };
        if (Application.Current?.TryGetResource("SolidButton", null, out var theme) == true)
            telegram.Theme = theme as Avalonia.Styling.ControlTheme;
        print.Click += async (_, _) => await Run(PrintAsync);
        save.Click += async (_, _) => await Run(SaveAsync);
        telegram.Click += async (_, _) => await Run(SendTelegramAsync);
        _actions = [print, save, telegram];
        foreach (var b in _actions)
            b.IsEnabled = false;

        var toolbar = new DockPanel { Margin = new Thickness(16, 12) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { print, save, telegram } };
        DockPanel.SetDock(buttons, Dock.Right);
        toolbar.Children.Add(buttons);
        toolbar.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var statusBar = new Border { Padding = new Thickness(16, 8), Child = _status };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(new ScrollViewer
        {
            Content = _pages,
            Background = Palette.Get("GroundBrush"),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        Content = root;
        Opened += async (_, _) => await RenderAsync();
    }

    private async Task RenderAsync()
    {
        try
        {
            _status.Text = "…";
            // Ekran masshtabida (125% → 120 dpi) — xira bo'lmasin; sahifa oynaga eni bo'yicha sig'adi (zoom-to-fit)
            var scaling = RenderScaling;
            var (pdf, images) = await Task.Run(() => (PayslipPdf.Pdf(_slips, _sheet), PayslipPdf.Images(_slips, _sheet, (int)Math.Round(96 * scaling))));
            _pdf = pdf;
            foreach (var png in images)
            {
                using var stream = new MemoryStream(png);
                var bitmap = new Bitmap(stream);
                _pages.Children.Add(new Border
                {
                    Background = Brushes.White, // qog'oz — mavzudan qat'i nazar oq
                    BoxShadow = BoxShadows.Parse("0 2 8 0 #33000000"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = bitmap.PixelSize.Width / scaling,
                    Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
                });
            }
            _status.Text = "";
            foreach (var b in _actions)
                b.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PDF");
            _status.Text = Strings.PdfFailed(ex.Message);
        }
    }

    private async Task Run(Func<Task> action)
    {
        foreach (var b in _actions)
            b.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hisob varaqasi");
            _status.Text = Strings.PrintFailed(ex.Message);
        }
        finally
        {
            foreach (var b in _actions)
                b.IsEnabled = true;
        }
    }

    private async Task PrintAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var pages = await Task.Run(() => PayslipPdf.Images(_slips, _sheet, 300));
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        WindowsShell.Print(handle, Path.GetFileNameWithoutExtension(_fileName), pages);
    }

    private async Task SaveAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SavePdf,
            SuggestedFileName = _fileName,
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not { } path)
            return;
        await File.WriteAllBytesAsync(path, _pdf!);
        _status.Text = Strings.PdfSaved(path);
    }

    /// <summary>Hujjatlar\Oylik\Vedomostlar\YYYY-MM\ ga saqlash → fayl buferga → Telegram Desktop; topilmasa — papka ochiladi.</summary>
    private async Task SendTelegramAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var folder = WindowsShell.PayslipFolder(_month);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, _fileName);
        await File.WriteAllBytesAsync(path, _pdf!);
        WindowsShell.CopyFileToClipboard(path);
        if (WindowsShell.OpenTelegram())
        {
            _status.Text = Strings.TelegramHint;
        }
        else
        {
            WindowsShell.ShowInFolder(path);
            _status.Text = Strings.TelegramNotFound(folder);
        }
    }
}
