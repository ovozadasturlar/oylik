using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Oylik.Desktop.Resources;

namespace Oylik.Desktop.Services;

public interface IDialogs
{
    Task<bool> ConfirmAsync(string message, string? yes = null, string? no = null);
    Task MessageAsync(string title, string message);

    /// <summary>Bir nechta tugmali savol; tanlangan tugma indeksi, yopilsa — -1. Birinchi tugma asosiy.</summary>
    Task<int> ChooseAsync(string title, string message, params string[] options);
    Task<string?> PickOpenExcelAsync();
    Task<string?> PickSaveExcelAsync(string suggestedName);
    Task<string?> PickFolderAsync();
    Task ShowDialogAsync(Window dialog);

    /// <summary>Bir nechta matn maydonli so'rov (masalan Boshqa vaqt: boshlanish, davomiylik, tanaffus, izoh). Bekor — null.</summary>
    Task<string[]?> PromptFieldsAsync(string title, string message, params (string Label, string Value, string? Watermark)[] fields);

    /// <summary>Bitta matn maydonli so'rov (masalan, bekor qilish sababi). Bekor qilinsa — null.</summary>
    Task<string?> PromptAsync(string title, string message, string? watermark = null);
}

public sealed class Dialogs(Func<Window?> owner) : IDialogs
{
    private Window Owner => owner() ?? throw new InvalidOperationException("Asosiy oyna yo'q");

    public async Task<bool> ConfirmAsync(string message, string? yes = null, string? no = null)
    {
        yes ??= Strings.Yes;
        no ??= Strings.No;
        var result = false;
        var dialog = BuildDialog(Strings.AppName, message, out var buttons);
        var yesButton = new Button { Content = yes, Classes = { "Primary" }, MinWidth = 96 };
        yesButton.Theme = TryTheme("SolidButton");
        var noButton = new Button { Content = no, MinWidth = 96 };
        yesButton.Click += (_, _) => { result = true; dialog.Close(); };
        noButton.Click += (_, _) => dialog.Close();
        buttons.Children.Add(noButton);
        buttons.Children.Add(yesButton);
        dialog.Opened += (_, _) => yesButton.Focus();
        await ShowFitted(dialog);
        return result;
    }

    public async Task MessageAsync(string title, string message)
    {
        var dialog = BuildDialog(title, message, out var buttons);
        var ok = new Button { Content = Strings.Ok, MinWidth = 96 };
        ok.Click += (_, _) => dialog.Close();
        buttons.Children.Add(ok);
        await ShowFitted(dialog);
    }

    public Task ShowDialogAsync(Window dialog) => ShowFitted(dialog);

    /// <summary>Har bir dialog — joriy monitorning ish maydoni ichida (ScreenFit).</summary>
    private Task ShowFitted(Window dialog)
    {
        Views.ScreenFit.Apply(dialog, Owner);
        return dialog.ShowDialog(Owner);
    }

    public async Task<int> ChooseAsync(string title, string message, params string[] options)
    {
        var result = -1;
        var dialog = BuildDialog(title, message, out var buttons);
        for (var i = options.Length - 1; i >= 0; i--)
        {
            var index = i;
            var button = new Button { Content = options[i], MinWidth = 96 };
            if (i == 0)
            {
                button.Classes.Add("Primary");
                button.Theme = TryTheme("SolidButton");
            }
            button.Click += (_, _) => { result = index; dialog.Close(); };
            buttons.Children.Add(button);
        }
        await ShowFitted(dialog);
        return result;
    }

    public async Task<string?> PromptAsync(string title, string message, string? watermark = null)
    {
        string? result = null;
        var dialog = BuildDialog(title, message, out var buttons);
        var box = new TextBox { Watermark = watermark, MaxLength = 200 };
        ((StackPanel)dialog.Content!).Children.Insert(1, box);
        var ok = new Button { Content = Strings.Ok, Classes = { "Primary" }, MinWidth = 96, IsEnabled = false };
        ok.Theme = TryTheme("SolidButton");
        var cancel = new Button { Content = Strings.Cancel, MinWidth = 96 };
        box.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        void Accept()
        {
            if (string.IsNullOrWhiteSpace(box.Text))
                return;
            result = box.Text.Trim();
            dialog.Close();
        }
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => dialog.Close();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) Accept();
            else if (e.Key == Avalonia.Input.Key.Escape) dialog.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        dialog.Opened += (_, _) => box.Focus();
        await ShowFitted(dialog);
        return result;
    }

    public async Task<string[]?> PromptFieldsAsync(string title, string message, params (string Label, string Value, string? Watermark)[] fields)
    {
        string[]? result = null;
        var dialog = BuildDialog(title, message, out var buttons);
        var panel = (StackPanel)dialog.Content!;
        var boxes = new List<TextBox>();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", fields.Select(_ => "*"))), ColumnSpacing = 10 };
        for (var i = 0; i < fields.Length; i++)
        {
            var box = new TextBox { Text = fields[i].Value, Watermark = fields[i].Watermark, MaxLength = 200 };
            boxes.Add(box);
            var cell = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = fields[i].Label, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, box } };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        panel.Children.Insert(1, grid);
        var ok = new Button { Content = Strings.Ok, Classes = { "Primary" }, MinWidth = 96 };
        ok.Theme = TryTheme("SolidButton");
        var cancel = new Button { Content = Strings.Cancel, MinWidth = 96 };
        ok.Click += (_, _) => { result = boxes.Select(b => b.Text?.Trim() ?? "").ToArray(); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        dialog.Width = Math.Max(460, 150 * fields.Length + 48);
        dialog.Opened += (_, _) => boxes.FirstOrDefault()?.Focus();
        await ShowFitted(dialog);
        return result;
    }

    public async Task<string?> PickOpenExcelAsync()
    {
        var files = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ImportTitle,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Excel") { Patterns = ["*.xlsx", "*.xlsm"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveExcelAsync(string suggestedName)
    {
        var file = await Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.ExportExcel,
            SuggestedFileName = suggestedName,
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel") { Patterns = ["*.xlsx"] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static Avalonia.Styling.ControlTheme? TryTheme(string key) =>
        Avalonia.Application.Current?.TryGetResource(key, null, out var theme) == true ? theme as Avalonia.Styling.ControlTheme : null;

    private static Window BuildDialog(string title, string message, out StackPanel buttons)
    {
        buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        return new Window
        {
            Title = title,
            Icon = Views.AppIcon.Get(),
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 20,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 21 },
                    buttons,
                },
            },
        };
    }
}
