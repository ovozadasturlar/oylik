using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;

namespace Oylik.Desktop.ViewModels;

/// <summary>Sozlamalar → Rollar: bitta rol qatori (holat, yangi PIN, [O'rnatish] [Tiklash]).</summary>
public sealed partial class RolePinItem(SettingsViewModel owner, UserRole role) : ObservableObject
{
    public UserRole Role { get; } = role;
    public string Name => Strings.RoleName(Role);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _hasPin;

    [ObservableProperty] private string _newPin = "";

    public string StatusText => HasPin ? Strings.PinIsSet : Strings.PinNotSet;

    [RelayCommand]
    private Task Set() => owner.SetPinAsync(this);

    [RelayCommand]
    private Task Clear() => owner.ClearPinAsync(this);

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StatusText));
    }
}

/// <summary>Bosqich 1: rol PINlari (faqat Admin — Sozlamalar faqat Admin'ga ochiq). PIN xotirada ham saqlanmaydi: o'rnatilgach maydon tozalanadi.</summary>
public sealed partial class SettingsViewModel
{
    public ObservableCollection<RolePinItem> RolePins { get; } = [];

    [ObservableProperty] private string? _rolesMessage;

    private void LoadRoles()
    {
        if (RolePins.Count == 0)
            foreach (var role in Enum.GetValues<UserRole>())
                RolePins.Add(new RolePinItem(this, role));
        foreach (var item in RolePins)
        {
            item.HasPin = Main.Service.HasPin(item.Role);
            item.NewPin = "";
        }
        RolesMessage = null;
    }

    private void RefreshRoleTexts()
    {
        foreach (var item in RolePins)
            item.RefreshTexts();
    }

    internal async Task SetPinAsync(RolePinItem item)
    {
        var pin = item.NewPin.Trim();
        if (!PinHasher.IsValid(pin))
        {
            RolesMessage = Strings.PinInvalid;
            return;
        }
        if (await Main.RunAsync(() => Main.Service.SetPinAsync(item.Role, pin), Access.Settings))
        {
            item.NewPin = "";
            item.HasPin = true;
            RolesMessage = Strings.PinSaved(item.Role);
            Main.RefreshLoginRequired();
        }
    }

    internal async Task ClearPinAsync(RolePinItem item)
    {
        if (!item.HasPin)
            return;
        if (item.Role == UserRole.Admin && !await Main.Dialogs.ConfirmAsync(Strings.ClearAdminPinConfirm))
            return;
        if (await Main.RunAsync(() => Main.Service.ClearPinAsync(item.Role), Access.Settings))
        {
            item.NewPin = "";
            item.HasPin = false;
            RolesMessage = Strings.PinCleared(item.Role);
            Main.RefreshLoginRequired();
        }
    }
}
