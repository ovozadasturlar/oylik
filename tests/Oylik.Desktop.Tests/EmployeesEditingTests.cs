using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.ViewModels;
using Oylik.Desktop.Views;

namespace Oylik.Desktop.Tests;

/// <summary>5.4 Xodimlar — Excel kabi jadvalda tahrir; R9 (ochiq oy qatori ham); tarmoq va filtr (3.9).</summary>
public sealed class EmployeesEditingTests : IAsyncLifetime
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "oylik-employees-tests-" + Guid.NewGuid().ToString("N"));
    private PayrollService _service = null!;
    private MainWindowViewModel _main = null!;
    private Window _window = null!;
    private DataGrid _grid = null!;
    private Employee _olim = null!;
    private PayrollPeriod _oct = null!;

    public async Task InitializeAsync()
    {
        var paths = new AppPaths(_home);
        paths.EnsureCreated();
        _service = new PayrollService(paths, TimeProvider.System);
        _main = new MainWindowViewModel(_service, paths) { NoSync = true };
        await _main.InitializeAsync();
        _olim = await _service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null, Branch.DefaultFirstId);
        await _service.CreateEmployeeAsync("Гайрат", 5_500_000, null, null, Branch.DefaultSecondId);
        await _service.CreateEmployeeAsync("Жамшид", 4_000_000, null, null);
        _oct = await _service.CreatePeriodAsync(new MonthKey(2026, 10));
        _main.RefreshBranchOptions();
        _main.Employees.Load();
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
        var view = new EmployeesView { DataContext = _main.Employees };
        _window = new Window { Content = view, Width = 1280, Height = 700 };
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        _grid = view.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "Grid");
    }

    private EmployeeRowViewModel Row(string name) => _main.Employees.Rows.Single(r => r.Employee.FullName == name);

    private void SelectCell(EmployeeRowViewModel row, int column)
    {
        _grid.SelectedItem = row;
        _grid.CurrentColumn = _grid.Columns[column];
        _grid.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private TextBox EditingBox()
    {
        Dispatcher.UIThread.RunJobs();
        return _grid.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsEffectivelyVisible && t.FindAncestorOfType<DataGridCell>() is not null);
    }

    [AvaloniaTheory]
    [InlineData(1)] // Ism
    [InlineData(3)] // Izoh (bo'sh emas bo'lsin)
    [InlineData(4)] // Oklad
    [InlineData(5)] // Kunlik
    public void F2_selects_all_like_payroll(int column)
    {
        _olim.Note = "Sex";
        Show();
        SelectCell(Row("Олим ака"), column);
        _window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        var box = EditingBox();
        box.SelectedText.Should().Be(box.Text);
        box.TextWrapping.Should().Be(Avalonia.Media.TextWrapping.NoWrap);
    }

    [AvaloniaFact]
    public void Typing_salary_updates_card_and_open_month_line_r9_r11()
    {
        Show();
        SelectCell(Row("Олим ака"), 4);
        _window.KeyTextInput("9");
        EditingBox().Text.Should().Be("9");
        _olim.MonthlySalary.Should().Be(6_000_000, "Enter'gacha saqlanmaydi");
        _window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        _olim.MonthlySalary.Should().Be(9_000_000);
        _olim.DailyRate.Should().Be(300_000);
        _oct.FindLine(_olim.Id)!.MonthlySalary.Should().Be(9_000_000);
        _oct.FindLine(_olim.Id)!.DailyRate.Should().Be(300_000);
    }

    [AvaloniaFact]
    public void Manual_daily_is_blue_italic_and_escape_cancels()
    {
        Show();
        var row = Row("Гайрат");
        SelectCell(row, 5);
        _window.KeyTextInput("1");
        _window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        row.IsManualDaily.Should().BeFalse();

        _window.KeyTextInput("190");
        _window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        row.IsManualDaily.Should().BeTrue();
        row.DailyFontStyle.Should().Be(Avalonia.Media.FontStyle.Italic);
        _oct.FindLine(row.Employee.Id)!.DailyRate.Should().Be(row.Employee.DailyRate);

        row.ResetDailyCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        row.IsManualDaily.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Changing_branch_in_the_grid_moves_open_line_and_filter_follows()
    {
        Show();
        var row = Row("Жамшид");
        row.Branch.Should().BeSameAs(row.BranchChoices[0], "belgilanmagan");
        row.Branch = row.BranchChoices.Single(c => c.Filter.BranchId == Branch.DefaultSecondId);
        Dispatcher.UIThread.RunJobs();

        row.Employee.BranchId.Should().Be(Branch.DefaultSecondId);
        _oct.FindLine(row.Employee.Id)!.BranchId.Should().Be(Branch.DefaultSecondId);
        _main.BranchOptions.Should().NotContain(o => o.Filter.IsUnassigned, "belgilanmagan xodim qolmadi");

        _main.SelectedBranchOption = _main.BranchOptions.Single(o => o.Filter.BranchId == Branch.DefaultSecondId);
        Dispatcher.UIThread.RunJobs();
        _main.Employees.Rows.Select(r => r.Employee.FullName).Should().BeEquivalentTo("Гайрат", "Жамшид");
        _main.Payroll.Rows.Select(r => r.Employee.FullName).Should().BeEquivalentTo("Гайрат", "Жамшид");
        _main.Payroll.SumCount.Should().Be("2");
        _service.Settings.BranchFilter.Should().Be(Branch.DefaultSecondId.ToString(), "filtr eslab qolinadi");
    }

    [AvaloniaFact]
    public void New_employee_gets_filter_branch_and_name_cell_opens_for_editing()
    {
        _main.SelectedBranchOption = _main.BranchOptions.Single(o => o.Filter.BranchId == Branch.DefaultFirstId);
        Show();
        _main.Employees.NewEmployeeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var created = _service.Employees.Single(e => e.FullName == "Yangi xodim");
        created.BranchId.Should().Be(Branch.DefaultFirstId);
        var box = EditingBox();
        box.Text.Should().Be("Yangi xodim");
        box.SelectedText.Should().Be("Yangi xodim");
    }
}
