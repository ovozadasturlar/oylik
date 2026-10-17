using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;

namespace Oylik.Desktop.ViewModels;

/// <summary>Sozlamalar → Tarmoqlar: bitta qator (nomi, faol, o'chirish).</summary>
public sealed partial class BranchItem(SettingsViewModel parent, Branch branch) : ObservableObject
{
    public Branch Branch { get; } = branch;

    /// <summary>Standart nomlar («1-tarmoq») joriy tilda ko'rsatiladi; o'zgartirilsa — kiritilgan matn saqlanadi.</summary>
    public string NameText
    {
        get => Strings.BranchName(Branch);
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == Strings.BranchName(Branch))
            {
                OnPropertyChanged();
                return;
            }
            _ = parent.SaveBranchAsync(this, s => s.RenameBranchAsync(Branch, value));
        }
    }

    public bool IsActive
    {
        get => Branch.IsActive;
        set
        {
            if (value != Branch.IsActive)
                _ = parent.SaveBranchAsync(this, s => s.SetBranchActiveAsync(Branch, value));
        }
    }

    public bool CanDelete => !parent.Main.Service.IsBranchUsed(Branch);

    [RelayCommand]
    private Task Delete() => parent.DeleteBranchAsync(this);

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed partial class SettingsViewModel
{
    public ObservableCollection<BranchItem> Branches { get; } = [];

    private void LoadBranches()
    {
        Branches.Clear();
        foreach (var branch in main.Service.Branches)
            Branches.Add(new BranchItem(this, branch));
    }

    internal async Task SaveBranchAsync(BranchItem item, Func<Oylik.Data.PayrollService, Task> action)
    {
        await main.RunAsync(() => action(main.Service));
        item.Refresh();
        main.RefreshBranchOptions();
    }

    internal async Task DeleteBranchAsync(BranchItem item)
    {
        if (!await main.Dialogs.ConfirmAsync(Strings.ConfirmDeleteBranch(Strings.BranchName(item.Branch))))
            return;
        if (await main.RunAsync(() => main.Service.DeleteBranchAsync(item.Branch)))
        {
            Branches.Remove(item);
            main.RefreshBranchOptions();
        }
    }

    /// <summary>Yangi tarmoq: bazada `N-tarmoq` (har tilda tarjima qilinadi), nomini shu yerda o'zgartirish mumkin.</summary>
    [RelayCommand]
    private async Task AddBranch()
    {
        var number = main.Service.Branches.Count + 1;
        while (main.Service.Branches.Any(b => b.Name == Branch.DefaultName(number)))
            number++;
        Branch? created = null;
        if (await main.RunAsync(async () => created = await main.Service.CreateBranchAsync(Branch.DefaultName(number))))
        {
            Branches.Add(new BranchItem(this, created!));
            main.RefreshBranchOptions();
        }
    }

    private void RefreshBranchTexts()
    {
        foreach (var item in Branches)
            item.Refresh();
    }
}
