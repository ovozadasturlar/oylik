using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Oylik.Data;
using Oylik.Data.Agent;
using Oylik.Desktop.Resources;
using Serilog;

namespace Oylik.Agent;

/// <summary>
/// Agentning tizim tray ikonasi (asosiy oynasiz Avalonia ilova). Faqat interaktiv sessiyada (Startup rejimi) ishga tushadi.
/// Ikona rangi — holat (yashil/sariq/qizil/kulrang nuqta), tooltip — qisqa holat, o'ng tugma — menyu,
/// bosilganda — kichik holat oynasi. Ma'lumot worker'dan <see cref="AgentStatusFeed"/> orqali keladi.
/// </summary>
public sealed class TrayApp(AppPaths paths, AgentStatusFeed feed, IHostApplicationLifetime host) : Application
{
    /// <summary>Windows tray tooltip chegarasi (NOTIFYICONDATA.szTip = 128 belgi).</summary>
    private const int TooltipMax = 127;

    private readonly Dictionary<AgentHealth, WindowIcon> _icons = [];
    private TrayIcon? _tray;
    private NativeMenuItem? _terminalItem, _eventsItem, _serverItem, _statusItem, _logsItem, _exitItem;
    private StatusWindow? _window;
    private IClassicDesktopStyleApplicationLifetime? _lifetime;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Default;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
            lifetime.Exit += (_, _) => _tray?.Dispose(); // ikona tray'da "arvoh" bo'lib qolmasin
            // Host o'zi to'xtasa (service stop / yiqilish) — tray ham yopiladi.
            host.ApplicationStopping.Register(() => Dispatcher.UIThread.Post(() => lifetime.Shutdown()));
        }

        _terminalItem = new NativeMenuItem { IsEnabled = false };
        _eventsItem = new NativeMenuItem { IsEnabled = false };
        _serverItem = new NativeMenuItem { IsEnabled = false };
        _statusItem = new NativeMenuItem();
        _statusItem.Click += (_, _) => ShowStatus();
        _logsItem = new NativeMenuItem();
        _logsItem.Click += (_, _) => OpenLogs();
        _exitItem = new NativeMenuItem();
        _exitItem.Click += (_, _) => Exit();

        var menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[]
                 {
                     _terminalItem, _eventsItem, _serverItem, new NativeMenuItemSeparator(),
                     _statusItem, _logsItem, new NativeMenuItemSeparator(), _exitItem,
                 })
            menu.Add(item);

        _tray = new TrayIcon { Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => ShowStatus();
        TrayIcon.SetIcons(this, [_tray]);

        feed.Changed += status => Dispatcher.UIThread.Post(() => Update(status));
        Update(feed.Current);
        base.OnFrameworkInitializationCompleted();
    }

    private void Update(AgentStatus status)
    {
        Strings.SetLanguage(UiLanguages.Parse(status.Language));
        var lines = StatusLines(status);
        if (_tray is not null)
        {
            _tray.Icon = IconFor(status.Health);
            var tip = string.Join("\n", [Strings.AgentTrayTitle, .. lines]);
            _tray.ToolTipText = tip.Length <= TooltipMax ? tip : tip[..(TooltipMax - 1)] + "…";
        }
        _terminalItem!.Header = lines.ElementAtOrDefault(0) ?? "";
        _eventsItem!.Header = lines.ElementAtOrDefault(1) ?? "";
        _serverItem!.Header = lines.ElementAtOrDefault(2) ?? "";
        _serverItem.IsVisible = lines.Count > 2;
        _statusItem!.Header = Strings.AgentMenuStatus;
        _logsItem!.Header = Strings.AgentMenuLogs;
        _exitItem!.Header = Strings.AgentMenuExit;
        _window?.Show(status);
    }

    /// <summary>Qisqa holat: terminal, bugungi voqealar, server (hali tick bo'lmasa — «Ishga tushmoqda…»).</summary>
    internal static IReadOnlyList<string> StatusLines(AgentStatus s) => s.UpdatedAt is null
        ? [Strings.AgentStarting]
        : [Strings.AgentTerminalLine(s), Strings.AgentEventsToday(s.TodayEvents), Strings.AgentServerLine(s)];

    private void ShowStatus()
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }
        _window = new StatusWindow();
        _window.Closed += (_, _) => _window = null;
        _window.Show(feed.Current);
        _window.Show();
        _window.Activate();
    }

    private void OpenLogs()
    {
        try
        {
            Directory.CreateDirectory(paths.Logs);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{paths.Logs}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Log papkasini ochib bo'lmadi");
        }
    }

    private void Exit()
    {
        Log.Information("Tray: foydalanuvchi agentni to'xtatdi");
        _window?.Close();
        _lifetime?.Shutdown(); // Program host'ni to'xtatadi
    }

    /// <summary>Ilova ikonasi + pastki o'ng burchakda holat rangidagi nuqta.</summary>
    private WindowIcon IconFor(AgentHealth health)
    {
        if (_icons.TryGetValue(health, out var cached))
            return cached;
        const int size = 64;
        using var logo = new Bitmap(AssetLoader.Open(new Uri("avares://Oylik.Agent/Assets/oylik-256.png")));
        using var target = new RenderTargetBitmap(new PixelSize(size, size));
        using (var ctx = target.CreateDrawingContext())
        {
            ctx.DrawImage(logo, new Rect(logo.Size), new Rect(0, 0, size, size));
            var color = health switch
            {
                AgentHealth.Ok => Color.FromRgb(0x22, 0xA5, 0x4B),
                AgentHealth.Warning => Color.FromRgb(0xF2, 0xA9, 0x00),
                AgentHealth.Error => Color.FromRgb(0xD9, 0x30, 0x25),
                _ => Color.FromRgb(0x8A, 0x8F, 0x98),
            };
            ctx.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, 4), new Point(size - 17, size - 17), 15, 15);
        }
        using var stream = new MemoryStream();
        target.Save(stream);
        stream.Position = 0;
        var icon = new WindowIcon(stream);
        _icons[health] = icon;
        return icon;
    }

    /// <summary>Ikona bosilganda chiqadigan kichik holat oynasi (fokus yo'qolsa yopiladi).</summary>
    private sealed class StatusWindow : Window
    {
        private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, FontSize = 15 };
        private readonly StackPanel _lines = new() { Spacing = 4 };
        private readonly Button _close = new() { HorizontalAlignment = HorizontalAlignment.Right };

        public StatusWindow()
        {
            Title = Strings.AgentTrayTitle;
            Width = 340;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            Topmost = true;
            SystemDecorations = SystemDecorations.BorderOnly;
            WindowStartupLocation = WindowStartupLocation.Manual;
            _close.Click += (_, _) => Close();
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel { Spacing = 10, Children = { _title, _lines, _close } },
            };
            Deactivated += (_, _) => Close();
            Opened += (_, _) => PlaceNearTray(); // o'lcham ma'lum bo'lgach
        }

        public void Show(AgentStatus status)
        {
            _title.Text = Strings.AgentTrayTitle;
            _close.Content = Strings.AgentStatusClose;
            _lines.Children.Clear();
            foreach (var line in StatusLines(status))
                _lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
            foreach (var error in new[] { status.TerminalError, status.SyncError })
                if (error is { Length: > 0 })
                    _lines.Children.Add(new TextBlock
                    {
                        Text = Strings.AgentLastError(error.Length > 200 ? error[..200] + "…" : error),
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.7,
                        FontSize = 12,
                    });
        }

        /// <summary>Ekranning ish maydoni pastki o'ng burchagiga (tray yoniga).</summary>
        public void PlaceNearTray()
        {
            if (Screens.Primary is not { } screen)
                return;
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            var width = (int)(Width * scale);
            var height = (int)(ClientSize.Height * scale);
            Position = new PixelPoint(area.Right - width - (int)(12 * scale), area.Bottom - height - (int)(12 * scale));
        }
    }
}
