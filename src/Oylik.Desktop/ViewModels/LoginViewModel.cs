using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;

namespace Oylik.Desktop.ViewModels;

public sealed partial class RoleOption(UserRole role) : ObservableObject
{
    public UserRole Role { get; } = role;
    public string Name => Strings.RoleName(Role);
    public void RefreshTexts() => OnPropertyChanged(nameof(Name));
}

/// <summary>
/// Kirish ekrani (Bosqich 1): rol tanlab PIN kiritiladi. Faqat PINi o'rnatilgan rollar ro'yxatda.
/// Ketma-ket 5 ta xato PIN — 30 soniya kutish (taxmin qilishni sekinlashtirish).
/// </summary>
public sealed partial class LoginViewModel(MainWindowViewModel main) : ObservableObject
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromSeconds(30);

    private int _failures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    public ObservableCollection<RoleOption> Roles { get; } = [];

    [ObservableProperty] private RoleOption? _selectedRole;
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Ro'yxatni yangilaydi (PINi borlar), PIN va xatoni tozalaydi. Oldingi tanlangan rol saqlanadi.</summary>
    public void Reset()
    {
        var keep = SelectedRole?.Role;
        Roles.Clear();
        foreach (var role in main.Service.RolesWithPin)
            Roles.Add(new RoleOption(role));
        SelectedRole = Roles.FirstOrDefault(r => r.Role == keep) ?? Roles.FirstOrDefault();
        Pin = "";
        Error = null;
    }

    public void RefreshTexts()
    {
        foreach (var r in Roles)
            r.RefreshTexts();
    }

    [RelayCommand]
    public async Task SignInAsync()
    {
        if (IsBusy || SelectedRole is not { } option)
            return;
        var now = DateTimeOffset.UtcNow;
        if (now < _lockedUntil)
        {
            Error = Strings.LoginLockedOut((int)Math.Ceiling((_lockedUntil - now).TotalSeconds));
            return;
        }
        var pin = Pin;
        IsBusy = true;
        bool ok;
        try
        {
            ok = await main.Service.VerifyPinAsync(option.Role, pin); // PBKDF2 — UI oqimidan tashqarida
        }
        finally
        {
            IsBusy = false;
        }
        Pin = "";
        if (!ok)
        {
            _failures++;
            if (_failures >= MaxAttempts)
            {
                _failures = 0;
                _lockedUntil = DateTimeOffset.UtcNow + LockoutTime;
                Error = Strings.LoginLockedOut((int)LockoutTime.TotalSeconds);
            }
            else
                Error = Strings.LoginWrongPin;
            return;
        }
        _failures = 0;
        Error = null;
        main.SignIn(option.Role);
    }
}
