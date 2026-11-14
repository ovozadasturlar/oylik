using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;
using Oylik.Desktop.ViewModels;
using Oylik.Desktop.Views;

namespace Oylik.Desktop.Tests;

/// <summary>Bosqich 1: rollar — ruxsat matritsasi, kirish (PIN), bo'lim cheklovi, read-only va maoshsiz rejim.</summary>
public sealed class RolesTests : IAsyncLifetime
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "oylik-roles-tests-" + Guid.NewGuid().ToString("N"));
    private AppPaths _paths = null!;
    private PayrollService _service = null!;
    private MainWindowViewModel _main = null!;
    private Window? _window;

    public async Task InitializeAsync()
    {
        _paths = new AppPaths(_home);
        _paths.EnsureCreated();
        await OpenAsync();
    }

    private async Task OpenAsync()
    {
        _service = new PayrollService(_paths, TimeProvider.System);
        _main = new MainWindowViewModel(_service, _paths) { NoSync = true };
        await _main.InitializeAsync();
    }

    /// <summary>Dasturni "qayta ishga tushirish" — o'sha baza bilan yangi sessiya.</summary>
    private async Task RestartAsync()
    {
        _main.Dispose();
        _service.Dispose();
        SqliteConnection.ClearAllPools();
        await OpenAsync();
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

    // ---------- Matritsa ----------

    [Theory]
    //          rol                  Oylik  Xodim  Davomat Oylar  Audit  Sozl.
    [InlineData(UserRole.Admin,      true,  true,  true,   true,  true,  true)]
    [InlineData(UserRole.Accountant, true,  true,  true,   true,  true,  false)]
    [InlineData(UserRole.Manager,    true,  true,  true,   true,  true,  false)]
    [InlineData(UserRole.Guard,      false, true,  true,   false, false, false)]
    public void CanOpen_matrix(UserRole role, bool payroll, bool employees, bool attendance, bool periods, bool audit, bool settings)
    {
        Permissions.CanOpen(role, Page.Payroll).Should().Be(payroll);
        Permissions.CanOpen(role, Page.Employees).Should().Be(employees);
        Permissions.CanOpen(role, Page.Attendance).Should().Be(attendance);
        Permissions.CanOpen(role, Page.Periods).Should().Be(periods);
        Permissions.CanOpen(role, Page.Audit).Should().Be(audit);
        Permissions.CanOpen(role, Page.Settings).Should().Be(settings);
    }

    [Theory]
    //          rol                  Edit   Maosh  Davomat Yuz
    [InlineData(UserRole.Admin,      true,  true,  true,   true)]
    [InlineData(UserRole.Accountant, true,  true,  true,   true)]
    [InlineData(UserRole.Manager,    false, true,  false,  false)]
    [InlineData(UserRole.Guard,      false, false, true,   true)]
    public void Edit_salary_attendance_faces_matrix(UserRole role, bool edit, bool salary, bool attendance, bool faces)
    {
        Permissions.CanEdit(role).Should().Be(edit);
        Permissions.CanSeeSalary(role).Should().Be(salary);
        Permissions.CanEditAttendance(role).Should().Be(attendance);
        Permissions.CanManageFaces(role).Should().Be(faces);
        Permissions.Allows(role, Access.View).Should().BeTrue();
        Permissions.Allows(role, Access.Edit).Should().Be(edit);
        Permissions.Allows(role, Access.Settings).Should().Be(role == UserRole.Admin);
    }

    [Fact]
    public void First_page_is_allowed_for_every_role()
    {
        foreach (var role in Enum.GetValues<UserRole>())
            Permissions.CanOpen(role, Permissions.FirstPage(role)).Should().BeTrue();
        Permissions.FirstPage(UserRole.Guard).Should().Be(Page.Employees);
    }

    // ---------- Kirish ----------

    [AvaloniaFact]
    public void Without_pins_app_opens_as_admin_without_login()
    {
        _main.IsLocked.Should().BeFalse();
        _main.LoginRequired.Should().BeFalse();
        _main.CurrentRole.Should().Be(UserRole.Admin);
        _main.CanOpenSettings.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Login_with_pin_opens_only_allowed_pages()
    {
        await _service.SetPinAsync(UserRole.Admin, "4321");
        await _service.SetPinAsync(UserRole.Guard, "1111");
        await RestartAsync();

        _main.IsLocked.Should().BeTrue();
        _main.Login.Roles.Select(r => r.Role).Should().Equal(UserRole.Admin, UserRole.Guard); // PINsiz rol ro'yxatda yo'q

        _main.Login.SelectedRole = _main.Login.Roles.Single(r => r.Role == UserRole.Guard);
        _main.Login.Pin = "2222";
        await _main.Login.SignInAsync();
        _main.IsLocked.Should().BeTrue();
        _main.Login.Error.Should().Be(Strings.LoginWrongPin);
        _main.Login.Pin.Should().BeEmpty();

        _main.Login.Pin = "1111";
        await _main.Login.SignInAsync();
        _main.IsLocked.Should().BeFalse();
        _main.CurrentRole.Should().Be(UserRole.Guard);
        _main.Page.Should().Be(Page.Employees); // Oylik ochilmaydi — birinchi ruxsat etilgan bo'lim
        _main.CanOpenPayroll.Should().BeFalse();
        _main.CanSeeSalary.Should().BeFalse();

        _main.Navigate(Page.Payroll);
        _main.Page.Should().Be(Page.Employees);
        _main.Navigate(Page.Settings);
        _main.Page.Should().Be(Page.Employees);
        _main.Navigate(Page.Attendance);
        _main.Page.Should().Be(Page.Attendance);

        _main.SwitchRole();
        _main.IsLocked.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Too_many_wrong_pins_lock_out_for_a_while()
    {
        await _service.SetPinAsync(UserRole.Admin, "4321");
        await RestartAsync();
        _main.Login.SelectedRole = _main.Login.Roles.Single();
        for (var i = 0; i < LoginViewModel.MaxAttempts; i++)
        {
            _main.Login.Pin = "0000";
            await _main.Login.SignInAsync();
        }
        _main.Login.Pin = "4321"; // to'g'ri PIN ham kutish vaqtida qabul qilinmaydi
        await _main.Login.SignInAsync();
        _main.IsLocked.Should().BeTrue();
        _main.Login.Error.Should().StartWith(Strings.LoginLockedOut(30)[..10]);
    }

    // ---------- Rahbar: faqat ko'rish ----------

    [AvaloniaFact]
    public async Task Manager_cannot_save_but_can_change_view_preferences()
    {
        _main.SignIn(UserRole.Manager);
        var ran = false;
        (await _main.RunAsync(() => { ran = true; return Task.CompletedTask; })).Should().BeFalse();
        ran.Should().BeFalse();
        _main.StatusError.Should().Be(Strings.NoPermission);

        (await _main.RunAsync(() => { ran = true; return Task.CompletedTask; }, Access.View)).Should().BeTrue();
        ran.Should().BeTrue();
        (await _main.RunAsync(() => Task.CompletedTask, Access.Attendance)).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Manager_payroll_grid_is_read_only()
    {
        await _service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        await _service.CreatePeriodAsync(new MonthKey(2026, 10));
        _main.SignIn(UserRole.Manager);
        _main.Payroll.Load();
        _main.Payroll.IsOpen.Should().BeTrue();
        _main.Payroll.IsGridReadOnly.Should().BeTrue();

        _main.SignIn(UserRole.Accountant);
        _main.Payroll.IsGridReadOnly.Should().BeFalse();
    }

    // ---------- Qorovul: maoshsiz ----------

    [AvaloniaFact]
    public async Task Guard_sees_employees_without_salary_columns_and_cannot_edit()
    {
        await _service.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        _main.SignIn(UserRole.Guard);
        _main.Navigate(Page.Employees);

        var view = new EmployeesView { DataContext = _main.Employees };
        _window = new Window { Content = view, Width = 1280, Height = 700 };
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = view.GetVisualDescendants().OfType<DataGrid>().First();

        grid.Columns.Where(c => Equals(c.Tag, "salary")).Should().HaveCount(3).And.OnlyContain(c => !c.IsVisible);
        grid.IsReadOnly.Should().BeTrue();
        view.GetVisualDescendants().OfType<TextBlock>().Should().NotContain(t => t.Text == "6 000 000");

        _main.SignIn(UserRole.Admin);
        Dispatcher.UIThread.RunJobs();
        grid.Columns.Where(c => Equals(c.Tag, "salary")).Should().OnlyContain(c => c.IsVisible);
        grid.IsReadOnly.Should().BeFalse();
    }
}
