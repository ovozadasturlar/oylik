using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;
using Serilog;

namespace Oylik.Desktop.ViewModels;

public enum Page { Payroll, Employees, Attendance, Periods, Audit, Settings }

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MainWindowViewModel(PayrollService service, AppPaths paths, IDialogs? dialogs = null)
    {
        Service = service;
        Paths = paths;
        Dialogs = dialogs ?? new Dialogs(() =>
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        Payroll = new PayrollViewModel(this);
        Employees = new EmployeesViewModel(this);
        Periods = new PeriodsViewModel(this);
        Audit = new AuditViewModel(this);
        Sync = new SyncService(service, RunDbAsync);
        Attendance = new AttendanceViewModel(this);
        Settings = new SettingsViewModel(this);
        _currentPage = Payroll;
        Sync.Synced += (_, _) => OnSynced();
        Strings.LanguageChanged += OnLanguageChanged;
    }

    public PayrollService Service { get; }
    public AppPaths Paths { get; }
    public IDialogs Dialogs { get; }

    public PayrollViewModel Payroll { get; }
    public EmployeesViewModel Employees { get; }
    public PeriodsViewModel Periods { get; }
    public AuditViewModel Audit { get; }
    public SettingsViewModel Settings { get; }
    public AttendanceViewModel Attendance { get; }
    public SyncService Sync { get; }

    [ObservableProperty] private object _currentPage;
    [ObservableProperty] private Page _page = Page.Payroll;
    [ObservableProperty] private string _savedText = "";
    [ObservableProperty] private string _backupText = "";
    [ObservableProperty] private string? _statusError;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _companyName = "";

    public bool IsPayroll => Page == Page.Payroll;
    public bool IsEmployees => Page == Page.Employees;
    public bool IsAttendance => Page == Page.Attendance;
    public bool IsPeriods => Page == Page.Periods;
    public bool IsAudit => Page == Page.Audit;
    public bool IsSettings => Page == Page.Settings;

    partial void OnPageChanged(Page value)
    {
        OnPropertyChanged(nameof(IsPayroll));
        OnPropertyChanged(nameof(IsEmployees));
        OnPropertyChanged(nameof(IsAttendance));
        OnPropertyChanged(nameof(IsPeriods));
        OnPropertyChanged(nameof(IsAudit));
        OnPropertyChanged(nameof(IsSettings));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await Service.InitializeAsync();
            CompanyName = Service.Settings.CompanyName;
            BackupText = Strings.BackupAt(Service.Backup.LastBackupAt());
            RefreshBranchOptions();
            IsReady = true;
            Payroll.Load();
            if (!NoSync)
                Sync.Start();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Baza ochilmadi");
            await Dialogs.MessageAsync(Strings.Error, Strings.DatabaseOpenFailed(ex.Message, Paths.Database));
        }
    }

    [RelayCommand]
    public void Navigate(Page page)
    {
        Page = page;
        StatusError = null;
        CurrentPage = page switch
        {
            Page.Employees => Employees,
            Page.Attendance => Attendance,
            Page.Periods => Periods,
            Page.Audit => Audit,
            Page.Settings => Settings,
            _ => Payroll,
        };
        switch (page)
        {
            case Page.Payroll: Payroll.Load(); break;
            case Page.Employees: Employees.Load(); break;
            case Page.Attendance: _ = Attendance.LoadAsync(); break;
            case Page.Periods: Periods.Load(); break;
            case Page.Audit: _ = Audit.LoadAsync(); break;
            case Page.Settings: Settings.Load(); break;
        }
    }

    public void OpenPeriod(PayrollPeriod period)
    {
        Payroll.SelectedKey = period.Key;
        Navigate(Page.Payroll);
    }

    /// <summary>
    /// Barcha saqlash amallari shu yerdan o'tadi: ketma-ket (bitta DbContext), xatolar status qatoriga.
    /// </summary>
    public async Task<bool> RunAsync(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await action();
            StatusError = null;
            if (Service.LastSavedAt is { } at)
                SavedText = Strings.SavedAt(at);
            return true;
        }
        catch (DomainException ex)
        {
            StatusError = Strings.Domain(ex);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saqlashda xato");
            StatusError = ex.Message;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Fon ishlari uchun (sinxronizatsiya): bazaga ketma-ket kirish, xatolar status qatoriga chiqmaydi.</summary>
    public async Task RunDbAsync(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Mavzuni darhol qo'llash (App beradi).</summary>
    public Action<UiTheme>? ApplyTheme { get; set; }

    /// <summary>`--snapshot` rejimida server bilan sinxronlanmaydi.</summary>
    public bool NoSync { get; set; }

    private void OnSynced()
    {
        if (!IsReady)
            return;
        if (Page == Page.Attendance)
            _ = Attendance.LoadAsync();
        else if (Page == Page.Payroll)
        {
            _ = Payroll.RefreshAttendanceAsync();
            Payroll.RefreshTotals(); // H10: Schedule qatorlari davomat bilan jonli
        }
        Settings.RefreshServer();
    }

    public void ShowError(string message) => StatusError = message;

    /// <summary>
    /// H11: yopishdan oldin — oyda tasdiqlanmagan voqealar bo'lsa ogohlantirish: [Ro'yxatga o'tish] (Davomat, shu oy)
    /// [Baribir yopish] [Bekor qilish]. true — yopishda davom etiladi.
    /// </summary>
    public async Task<bool> ConfirmUnconfirmedEventsAsync(Oylik.Core.Domain.PayrollPeriod period)
    {
        var count = Service.UnconfirmedEventsCount(period);
        if (count == 0)
            return true;
        var choice = await Dialogs.ChooseAsync(Strings.UnconfirmedTitle, Strings.UnconfirmedMessage(count, Strings.Month(period.Key)),
            Strings.GoToList, Strings.CloseAnyway, Strings.Cancel);
        if (choice == 0)
        {
            Attendance.Month = period.Key;
            Navigate(Page.Attendance);
        }
        return choice == 1;
    }

    /// <summary>Strings.LanguageChanged statik — obunani bo'shatish (dastur yopilganda / testlarda).</summary>
    public void Dispose()
    {
        Strings.LanguageChanged -= OnLanguageChanged;
        Sync.Stop();
    }

    // ---------- Ko'rinish: mavzu va til (darhol, qayta ishga tushirishsiz) ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightTheme), nameof(IsDarkTheme), nameof(IsSystemTheme))]
    private UiTheme _theme = UiTheme.Light;

    /// <summary>Hozir ko'rinayotgan variant (Tizim bo'yicha bo'lsa — Windows sozlamasi); App yangilab turadi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeToggleTip))]
    private bool _isDarkActual;

    public bool IsLightTheme { get => Theme == UiTheme.Light; set { if (value) SetTheme(UiTheme.Light); } }
    public bool IsDarkTheme { get => Theme == UiTheme.Dark; set { if (value) SetTheme(UiTheme.Dark); } }
    public bool IsSystemTheme { get => Theme == UiTheme.System; set { if (value) SetTheme(UiTheme.System); } }
    public string ThemeToggleTip => IsDarkActual ? Strings.ThemeToLight : Strings.ThemeToDark;

    /// <summary>Til tanlovi — sidebar tepasi va Sozlamalar → Ko'rinish shu bitta manbaga bog'langan (<see cref="UiLanguage"/> tartibida).</summary>
    public int LanguageIndex
    {
        get => (int)Strings.Language;
        set
        {
            if (value >= 0)
                SetLanguage((UiLanguage)value);
        }
    }

    public IReadOnlyList<string> LanguageOptions => Strings.LanguageNames;
    public IReadOnlyList<string> LanguageShortOptions => Strings.LanguageShortNames;

    /// <summary>Sidebar'dagi quyosh/oy tugmasi: Yorug' ↔ Qorong'i.</summary>
    [RelayCommand]
    private void ToggleTheme() => SetTheme(IsDarkActual ? UiTheme.Light : UiTheme.Dark);

    public void SetTheme(UiTheme theme, bool save = true)
    {
        if (Theme == theme)
            return;
        Theme = theme;
        ApplyTheme?.Invoke(theme);
        if (save && IsReady)
            _ = RunAsync(() => Service.SaveSettingsAsync(s => s.Theme = theme));
    }

    public void SetLanguage(UiLanguage language, bool save = true)
    {
        if (Strings.Language == language)
            return;
        Strings.SetLanguage(language); // → OnLanguageChanged
        var code = UiLanguages.Code(language);
        if (save && IsReady)
            _ = RunAsync(() => Service.SaveSettingsAsync(s => s.Language = code));
    }

    /// <summary>XAML matnlari Loc orqali o'zi yangilanadi; bu yerda ViewModel'lardagi hisoblangan matnlar.</summary>
    private void OnLanguageChanged()
    {
        if (Service.LastSavedAt is { } at && SavedText.Length > 0)
            SavedText = Strings.SavedAt(at);
        RefreshBackupText();
        Sync.UpdateStatus();
        OnPropertyChanged(nameof(ThemeToggleTip));
        OnPropertyChanged(nameof(LanguageIndex));
        RefreshBranchTexts();
        if (!IsReady)
            return;
        Payroll.RefreshTexts();
        Employees.RefreshTexts();
        Periods.Load();
        Attendance.RefreshTexts();
        _ = Audit.RefreshTextsAsync();
        Settings.RefreshTexts();
    }

    public void RefreshBackupText() => BackupText = Strings.BackupAt(Service.Backup.LastBackupAt());
}
