using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;

namespace Oylik.Desktop.ViewModels;

// ---------- 5.5 Oylar ----------

public sealed class PeriodListItem(PayrollPeriod period)
{
    private readonly PeriodSummary _s = PeriodSummary.Of(period.Lines);
    public PayrollPeriod Period { get; } = period;
    public string Month => Strings.Month(Period.Key);
    public bool IsClosed => Period.IsClosed;
    public bool IsOpen => !Period.IsClosed;
    public string Status => Period.IsClosed ? Strings.Closed : Strings.Open;
    public string Count => _s.EmployeeCount.ToString();
    public string Total => NumberText.Money(_s.Total);
    public string Advance => NumberText.Money(_s.AdvanceTotal);
    public string Remainder => NumberText.Money(_s.Remainder);
    public string ClosedAt => Period.ClosedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
}

public sealed partial class PeriodsViewModel(MainWindowViewModel main) : ObservableObject
{
    public ObservableCollection<PeriodListItem> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(CloseCommand), nameof(ReopenCommand))]
    private PeriodListItem? _selected;

    [ObservableProperty] private string _newPeriodText = Strings.NewPeriod;

    public void Load()
    {
        if (!main.IsReady)
            return;
        var id = Selected?.Period.Id;
        Items.Clear();
        foreach (var p in main.Service.Periods)
            Items.Add(new PeriodListItem(p));
        Selected = Items.FirstOrDefault(i => i.Period.Id == id) ?? Items.FirstOrDefault();
        NewPeriodText = $"{Strings.NewPeriod} — {Strings.Month(main.Service.NextPeriodKey())}";
    }

    public MainWindowViewModel Main => main;

    private bool HasSelected() => Selected is not null;
    private bool CanClose() => Selected is { IsOpen: true };
    private bool CanReopen() => Selected is { IsClosed: true }
        && main.Service.FindPeriod(Selected.Period.Key.Next()) is not { IsClosed: true };

    [RelayCommand]
    private async Task NewPeriod()
    {
        if (!main.Demand(Access.Edit))
            return;
        var key = main.Service.NextPeriodKey();
        if (!await main.Dialogs.ConfirmAsync(Strings.ConfirmNewPeriod(Strings.Month(key))))
            return;
        if (await main.RunAsync(() => main.Service.CreatePeriodAsync(key)))
            Load();
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void Open() => main.OpenPeriod(Selected!.Period);

    [RelayCommand(CanExecute = nameof(CanClose))]
    private async Task Close()
    {
        if (!main.Demand(Access.Edit))
            return;
        var p = Selected!.Period;
        if (!await main.ConfirmUnconfirmedEventsAsync(p))
            return;
        var debtors = p.Lines.Count(l => l.Remainder < 0);
        if (!await main.Dialogs.ConfirmAsync(Strings.ConfirmClose(Strings.Month(p.Key), debtors), Strings.ClosePeriod, Strings.Cancel))
            return;
        if (await main.RunAsync(() => main.Service.ClosePeriodAsync(p)))
        {
            main.RefreshBackupText();
            Load();
        }
    }

    [RelayCommand(CanExecute = nameof(CanReopen))]
    private async Task Reopen()
    {
        if (!main.Demand(Access.Edit))
            return;
        var p = Selected!.Period;
        if (!await main.Dialogs.ConfirmAsync(Strings.ConfirmReopen(Strings.Month(p.Key)), Strings.ReopenPeriod, Strings.Cancel))
            return;
        if (await main.RunAsync(() => main.Service.ReopenPeriodAsync(p)))
            Load();
    }
}

// ---------- 5.9 Audit ----------

public sealed class AuditItem(AuditEntry entry, string what)
{
    public string At => entry.At.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
    public string What => what;
    public string Change => (entry.OldValue, entry.NewValue) switch
    {
        (null, null) => "",
        (null, var n) => "→ " + n,
        (var o, null) => o + " →",
        var (o, n) => $"{o} → {n}",
    };
    public string Comment => entry.Comment ?? "";
}

public sealed partial class AuditViewModel(MainWindowViewModel main) : ObservableObject
{
    public sealed record Option(string Label, object? Value)
    {
        public override string ToString() => Label;
    }

    public ObservableCollection<AuditItem> Items { get; } = [];
    public ObservableCollection<Option> EmployeeOptions { get; } = [];
    public ObservableCollection<Option> PeriodOptions { get; } = [];

    [ObservableProperty] private Option? _employeeFilter;
    [ObservableProperty] private Option? _periodFilter;

    private bool _loading;

    partial void OnEmployeeFilterChanged(Option? value) { if (!_loading) _ = LoadAsync(); }
    partial void OnPeriodFilterChanged(Option? value) { if (!_loading) _ = LoadAsync(); }

    /// <summary>Til almashdi: filtr nomlari va oy nomlari yangilanadi, tanlangan filtrlar saqlanadi.</summary>
    public async Task RefreshTextsAsync()
    {
        if (!main.IsReady || EmployeeOptions.Count == 0)
            return;
        var employee = EmployeeFilter?.Value;
        var period = PeriodFilter?.Value;
        _loading = true;
        RebuildOptions();
        EmployeeFilter = EmployeeOptions.FirstOrDefault(o => Equals(o.Value, employee)) ?? EmployeeOptions[0];
        PeriodFilter = PeriodOptions.FirstOrDefault(o => Equals(o.Value, period)) ?? PeriodOptions[0];
        _loading = false;
        await LoadAsync();
    }

    private void RebuildOptions()
    {
        EmployeeOptions.Clear();
        EmployeeOptions.Add(new Option(Strings.AllEmployees, null));
        foreach (var e in main.Service.Employees)
            EmployeeOptions.Add(new Option(e.FullName, e));
        PeriodOptions.Clear();
        PeriodOptions.Add(new Option(Strings.AllPeriods, null));
        foreach (var p in main.Service.Periods)
            PeriodOptions.Add(new Option(Strings.Month(p.Key), p));
    }

    public async Task LoadAsync()
    {
        if (!main.IsReady)
            return;
        _loading = true;
        if (EmployeeOptions.Count == 0 || EmployeeOptions.Count != main.Service.Employees.Count + 1)
        {
            RebuildOptions();
            EmployeeFilter = EmployeeOptions[0];
            PeriodFilter = PeriodOptions[0];
        }
        _loading = false;

        var employee = EmployeeFilter?.Value as Employee;
        var period = PeriodFilter?.Value as PayrollPeriod;
        var entries = await main.Service.GetAuditAsync(take: 5000);
        if (employee is not null || period is not null)
        {
            var ids = main.Service.RelatedIds(employee, period);
            entries = entries.Where(a => ids.Contains(a.EntityId)).ToList();
        }

        var names = BuildNameLookup();
        Items.Clear();
        foreach (var entry in entries.Take(2000))
            Items.Add(new AuditItem(entry, Describe(entry, names)));
    }

    private Dictionary<Guid, string> BuildNameLookup()
    {
        var map = new Dictionary<Guid, string>();
        foreach (var e in main.Service.Employees)
            map[e.Id] = e.FullName;
        foreach (var p in main.Service.Periods)
        {
            map[p.Id] = Strings.Month(p.Key);
            foreach (var l in p.Lines)
            {
                var name = l.Employee?.FullName ?? "?";
                map[l.Id] = $"{name}, {Strings.MonthName(p.Month)}";
                foreach (var a in l.Advances)
                    map[a.Id] = $"{name}, {Strings.MonthName(p.Month)}";
            }
        }
        return map;
    }

    private static string Describe(AuditEntry e, Dictionary<Guid, string> names)
    {
        var field = Strings.AuditField(e.Field);
        return names.TryGetValue(e.EntityId, out var who) ? $"{field} · {who}" : field;
    }
}

// ---------- 5.6 Sozlamalar ----------

public sealed partial class SettingsViewModel(MainWindowViewModel main) : ObservableObject
{
    [ObservableProperty] private string _companyName = "";
    [ObservableProperty] private decimal? _daysInMonth = 30;
    [ObservableProperty] private decimal? _hoursPerDay = 8;
    [ObservableProperty] private bool _shortInputEnabled = true;
    [ObservableProperty] private string _backupFolder = "";
    [ObservableProperty] private string _installationId = "";
    [ObservableProperty] private string _dataFolder = "";
    [ObservableProperty] private string _backupInfo = "";

    private bool _loading;

    public void Load()
    {
        if (!main.IsReady)
            return;
        _loading = true;
        var s = main.Service.Settings;
        CompanyName = s.CompanyName;
        DaysInMonth = s.DaysInMonth;
        HoursPerDay = s.HoursPerDay;
        ShortInputEnabled = s.ShortInputEnabled;
        BackupFolder = s.BackupFolder ?? "";
        InstallationId = s.InstallationId.ToString();
        DataFolder = main.Paths.Root;
        BackupInfo = Strings.BackupAt(main.Service.Backup.LastBackupAt());
        LoadServer();
        LoadTerminal();
        LoadBranches();
        LoadSchedules();
        LoadRoles();
        _loading = false;
    }

    public void RefreshTexts()
    {
        if (!main.IsReady)
            return;
        BackupInfo = Strings.BackupAt(main.Service.Backup.LastBackupAt());
        RefreshServer();
        RefreshBranchTexts();
        RefreshScheduleTexts();
        RefreshRoleTexts();
    }

    partial void OnShortInputEnabledChanged(bool value) { if (!_loading) _ = Save(); }

    [RelayCommand]
    public async Task Save()
    {
        if (_loading || !main.IsReady)
            return;
        var days = (int)Math.Clamp(DaysInMonth ?? 30, 1, 31);
        var hours = (int)Math.Clamp(HoursPerDay ?? 8, 1, 24);
        var company = CompanyName.Trim();
        var folder = string.IsNullOrWhiteSpace(BackupFolder) ? null : BackupFolder.Trim();
        var shortOn = ShortInputEnabled;
        await main.RunAsync(() => main.Service.SaveSettingsAsync(s =>
        {
            s.CompanyName = company;
            s.DaysInMonth = days;
            s.HoursPerDay = hours;
            s.ShortInputEnabled = shortOn;
            s.BackupFolder = folder;
        }));
        main.CompanyName = company;
    }

    [RelayCommand]
    private async Task Browse()
    {
        var folder = await main.Dialogs.PickFolderAsync();
        if (folder is null)
            return;
        BackupFolder = folder;
        await Save();
    }

    [RelayCommand]
    private async Task BackupNow()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var path = main.Service.Backup.Create(main.Service.Settings.BackupFolder);
            main.RefreshBackupText();
            BackupInfo = Strings.BackupAt(main.Service.Backup.LastBackupAt());
            await main.Dialogs.MessageAsync(Strings.BackupNow, Strings.BackupDone(path));
        }
        catch (Exception ex)
        {
            main.ShowError(Strings.BackupFailed(ex.Message));
        }
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo { FileName = main.Paths.Root, UseShellExecute = true });

    public MainWindowViewModel Main => main;
}
