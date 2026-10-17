using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.ViewModels;
using Oylik.Desktop.Views;

[assembly: AvaloniaTestApplication(typeof(Oylik.Desktop.Tests.TestAppBuilder))]

namespace Oylik.Desktop.Tests;

/// <summary>Headless Avalonia: haqiqiy uslublar (Semi + AppStyles), App.axaml.cs ishlatilmaydi (u haqiqiy bazani ochadi).</summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Semi.Avalonia.SemiTheme());
        Styles.Add(new Semi.Avalonia.DataGrid.DataGridSemiTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Oylik/")) { Source = new Uri("avares://Oylik/Styles/AppStyles.axaml") });
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>Oylik jadvali: tahrirga o'tganda katakdagi matn to'liq belgilanadi (yozilgan qiymat eskisini almashtiradi).</summary>
public sealed class PayrollEditingTests : IAsyncLifetime
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "oylik-edit-tests-" + Guid.NewGuid().ToString("N"));
    private PayrollService _service = null!;
    private MainWindowViewModel _main = null!;
    private Window _window = null!;
    private DataGrid _grid = null!;

    public async Task InitializeAsync()
    {
        var paths = new AppPaths(_home);
        paths.EnsureCreated();
        _service = new PayrollService(paths, TimeProvider.System);
        _main = new MainWindowViewModel(_service, paths) { NoSync = true };
        await _main.InitializeAsync();
        await _service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        await _service.CreateEmployeeAsync("Санжар", 5_500_000, null, null);
        await _service.CreatePeriodAsync(new MonthKey(2026, 10));
        _main.Payroll.Load();
    }

    public Task DisposeAsync()
    {
        _window?.Close();
        _main.Dispose();
        _service.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_home, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private void Show()
    {
        var view = new PayrollView { DataContext = _main.Payroll };
        _window = new Window { Content = view, Width = 1280, Height = 700 };
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        _grid = view.GetVisualDescendants().OfType<DataGrid>().First();
    }

    private void SelectCell(int row, int column)
    {
        _grid.SelectedIndex = row;
        _grid.CurrentColumn = _grid.Columns[column];
        _grid.Focus();
        Dispatcher.UIThread.RunJobs();
        var focused = TopLevel.GetTopLevel(_grid)!.FocusManager!.GetFocusedElement();
        (focused as Visual)?.FindAncestorOfType<DataGrid>(includeSelf: true).Should().BeSameAs(_grid, $"focus: {focused}");
    }

    private TextBox EditingBox()
    {
        Dispatcher.UIThread.RunJobs();
        return _grid.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible && t.FindAncestorOfType<DataGridCell>() is not null);
    }

    private static void ShouldBeFullySelected(TextBox box)
    {
        box.Text.Should().NotBeNullOrEmpty();
        box.SelectedText.Should().Be(box.Text);
    }

    [AvaloniaTheory]
    [InlineData(1)] // Оклад
    [InlineData(2)] // Ишчилар
    [InlineData(3)] // Сумма
    [InlineData(4)] // Кун
    public void F2_selects_all_text(int column)
    {
        Show();
        SelectCell(0, column);
        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        ShouldBeFullySelected(EditingBox());
    }

    [AvaloniaTheory]
    [InlineData(1)] // Оклад (o'ngga tekislangan katak)
    [InlineData(2)] // Ишчилар
    public void Editor_fills_the_whole_cell_on_one_line(int column)
    {
        Show();
        SelectCell(0, column);
        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        var box = EditingBox();
        _window.UpdateLayout();
        var cell = box.FindAncestorOfType<DataGridCell>()!;
        box.Bounds.Width.Should().BeGreaterThan(cell.Bounds.Width - 4);
        box.Bounds.Height.Should().BeGreaterThan(cell.Bounds.Height - 4);
        box.TextWrapping.Should().Be(Avalonia.Media.TextWrapping.NoWrap);
        box.AcceptsReturn.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Mouse_click_on_current_cell_selects_all_text()
    {
        Show();
        SelectCell(0, 1);
        var cell = _grid.GetVisualDescendants().OfType<DataGridCell>()
            .First(c => c.DataContext == _main.Payroll.Rows[0] && c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "6 000 000"));
        var center = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), _window)!.Value;
        _window.MouseDown(center, MouseButton.Left);
        _window.MouseUp(center, MouseButton.Left);
        ShouldBeFullySelected(EditingBox());
    }

    [AvaloniaFact]
    public void Value_is_saved_on_enter_not_on_each_keystroke_and_escape_cancels()
    {
        Show();
        SelectCell(0, 1);
        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        var box = EditingBox();
        _window.KeyTextInput("8");
        _window.KeyTextInput("5");
        Dispatcher.UIThread.RunJobs();
        box.Text.Should().Be("85");
        _main.Payroll.Rows[0].Line.MonthlySalary.Should().Be(6_000_000);
        _window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        _main.Payroll.Rows[0].Line.MonthlySalary.Should().Be(6_000_000, "Esc — bekor");

        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        EditingBox();
        _window.KeyTextInput("8");
        _window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        _main.Payroll.Rows.Single(r => r.Employee.FullName == "Олим ака").Line.MonthlySalary.Should().Be(8_000_000);
    }

    [AvaloniaFact]
    public void Clicking_another_row_saves_the_edited_value()
    {
        Show();
        SelectCell(0, 4);
        _window.KeyTextInput("5");
        EditingBox().Text.Should().Be("5");
        var other = _grid.GetVisualDescendants().OfType<DataGridCell>()
            .First(c => c.DataContext == _main.Payroll.Rows[1] && c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Санжар"));
        var point = other.TranslatePoint(new Point(other.Bounds.Width / 2, other.Bounds.Height / 2), _window)!.Value;
        _window.MouseDown(point, MouseButton.Left);
        _window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        _main.Payroll.Rows.Single(r => r.Employee.FullName == "Олим ака").Line.DaysWorked.Should().Be(5);
    }

    [AvaloniaFact]
    public void Typing_replaces_old_value_and_short_input_still_applies()
    {
        Show();
        SelectCell(0, 1);
        _window.KeyTextInput("7");
        var box = EditingBox();
        box.Text.Should().Be("7");
        _window.KeyTextInput("5");
        Dispatcher.UIThread.RunJobs();
        box.Text.Should().Be("75");
        _main.Payroll.Rows[0].Line.MonthlySalary.Should().Be(6_000_000, "Enter'gacha saqlanmaydi");
        _window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        _main.Payroll.Rows.Single(r => r.Employee.FullName == "Олим ака").Line.MonthlySalary.Should().Be(75_000_000); // R11: 75 → 75 000 000
    }

    [AvaloniaFact]
    public void Tab_from_edited_cell_selects_all_in_next_cell()
    {
        Show();
        SelectCell(0, 3);
        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null); // Сумма tahrirda
        EditingBox();
        _window.KeyTextInput("250");
        _window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); // → Кун, Сумма saqlanadi
        Dispatcher.UIThread.RunJobs();
        ShortInput.TryParse("250", AmountField.DailyRate, true, out var daily).Should().BeTrue();
        _main.Payroll.Rows[0].Line.DailyRate.Should().Be(daily);
        Dispatcher.UIThread.RunJobs();
        _grid.CurrentColumn.Should().Be(_grid.Columns[4]);
        if (!_grid.GetVisualDescendants().OfType<TextBox>().Any(t => t.FindAncestorOfType<DataGridCell>() is not null))
            _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        ShouldBeFullySelected(EditingBox());
    }

    [AvaloniaFact]
    public void Enter_moves_down_and_next_edit_selects_all()
    {
        Show();
        SelectCell(0, 4);
        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        EditingBox();
        _window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // → pastdagi qator
        Dispatcher.UIThread.RunJobs();
        _grid.SelectedIndex.Should().Be(1);
        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        ShouldBeFullySelected(EditingBox());
    }
}
