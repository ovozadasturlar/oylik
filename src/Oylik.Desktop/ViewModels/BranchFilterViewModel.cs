using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;

namespace Oylik.Desktop.ViewModels;

/// <summary>Filtr tanlovi: Barchasi · tarmoq · Belgilanmagan. Nomi joriy tilda.</summary>
public sealed class BranchOption(BranchFilter filter, Branch? branch) : ObservableObject
{
    public BranchFilter Filter { get; } = filter;
    public Branch? Branch { get; } = branch;
    public string Label => Filter.IsAll ? Strings.BranchAll : Strings.BranchName(Branch);
    public void RefreshTexts() => OnPropertyChanged(nameof(Label));
    public override string ToString() => Label;
}

/// <summary>
/// 3.9: tarmoq filtri ekranlar o'rtasida umumiy (Oylik, Xodimlar, Davomat) va sozlamada eslab qolinadi.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private bool _loadingBranches;

    public ObservableCollection<BranchOption> BranchOptions { get; } = [];

    [ObservableProperty] private BranchOption? _selectedBranchOption;

    public BranchFilter BranchFilter => SelectedBranchOption?.Filter ?? BranchFilter.All;

    /// <summary>Barchasi + kamida 2 tanlov bo'lsa ko'rinadi.</summary>
    public bool ShowBranchFilter => BranchOptions.Count > 2;

    /// <summary>Filtrdagi tarmoq — yangi xodim uchun default.</summary>
    public Guid? DefaultBranchId => BranchFilter.IsAll ? null : BranchFilter.BranchId;

    /// <summary>Tarmoqlar ro'yxati yoki «Belgilanmagan» holati o'zgarganda — tanlov saqlanadi.</summary>
    public void RefreshBranchOptions()
    {
        var current = SelectedBranchOption?.Filter ?? BranchFilter.Parse(Service.Settings.BranchFilter);
        _loadingBranches = true;
        BranchOptions.Clear();
        BranchOptions.Add(new BranchOption(BranchFilter.All, null));
        foreach (var branch in Service.Branches.Where(b => b.IsActive || b.Id == current.BranchId))
            BranchOptions.Add(new BranchOption(BranchFilter.Of(branch.Id), branch));
        if (Service.HasUnassigned || current.IsUnassigned)
            BranchOptions.Add(new BranchOption(BranchFilter.Unassigned, null));
        SelectedBranchOption = BranchOptions.FirstOrDefault(o => o.Filter == current) ?? BranchOptions[0];
        _loadingBranches = false;
        OnPropertyChanged(nameof(ShowBranchFilter));
    }

    /// <summary>Ro'yxatdagi branch choices (Xodimlar jadvali katagi uchun): Belgilanmagan + faol tarmoqlar.</summary>
    public IReadOnlyList<BranchOption> BranchChoices(Guid? keep) =>
        [new BranchOption(BranchFilter.Unassigned, null),
         .. Service.Branches.Where(b => b.IsActive || b.Id == keep).Select(b => new BranchOption(BranchFilter.Of(b.Id), b))];

    partial void OnSelectedBranchOptionChanged(BranchOption? value)
    {
        OnPropertyChanged(nameof(BranchFilter));
        OnPropertyChanged(nameof(DefaultBranchId));
        if (_loadingBranches || value is null || !IsReady)
            return;
        var setting = value.Filter.ToSetting();
        if (Service.Settings.BranchFilter != setting)
            _ = RunAsync(() => Service.SaveSettingsAsync(s => s.BranchFilter = setting));
        Payroll.OnBranchFilterChanged();
        Employees.Load();
        Attendance.OnBranchFilterChanged();
    }

    private void RefreshBranchTexts()
    {
        foreach (var option in BranchOptions)
            option.RefreshTexts();
    }
}
