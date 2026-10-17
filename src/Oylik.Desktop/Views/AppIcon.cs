using Avalonia.Controls;
using Avalonia.Platform;

namespace Oylik.Desktop.Views;

/// <summary>Dastur ikonkasi — koddan yaratiladigan oynalar uchun ham (taskbar/Alt+Tab'da default ikonka chiqmasin).</summary>
public static class AppIcon
{
    private static WindowIcon? _icon;

    public static WindowIcon Get() => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://Oylik/Assets/oylik.ico")));
}
