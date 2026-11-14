using Oylik.Core.Domain;
using Oylik.Desktop.ViewModels;

namespace Oylik.Desktop.Services;

/// <summary>Saqlash amali qaysi ruxsatni talab qiladi (<see cref="MainWindowViewModel.RunAsync"/>).</summary>
public enum Access
{
    /// <summary>Shaxsiy ko'rinish (til, mavzu, tarmoq filtri) — har rolga.</summary>
    View,
    /// <summary>Ish haqi/xodim/oy ma'lumotini o'zgartirish — Admin, Buxgalter.</summary>
    Edit,
    /// <summary>Davomat voqealari (tasdiqlash, qo'lda kiritish, bekor qilish) — Admin, Buxgalter, Qorovul.</summary>
    Attendance,
    /// <summary>Yuz qo'shish (terminal) — Admin, Buxgalter, Qorovul.</summary>
    Faces,
    /// <summary>Sozlamalar (server, terminal, rollar) — faqat Admin.</summary>
    Settings,
}

/// <summary>
/// Rol → ruxsat matritsasi (bitta joyda; UI va <see cref="MainWindowViewModel.RunAsync"/> shunga qaraydi).
/// docs/reja-agent-rollar-multisite.md, Bosqich 1.
/// </summary>
public static class Permissions
{
    public static bool CanOpen(UserRole role, Page page) => role switch
    {
        UserRole.Admin => true,
        UserRole.Accountant or UserRole.Manager => page != Page.Settings,
        UserRole.Guard => page is Page.Employees or Page.Attendance,
        _ => false,
    };

    /// <summary>Ish haqi/xodim ma'lumotini tahrirlash (Rahbar — faqat ko'rish, Qorovul — maoshsiz).</summary>
    public static bool CanEdit(UserRole role) => role is UserRole.Admin or UserRole.Accountant;

    /// <summary>Maosh/kunlik/jami summalarni ko'rish.</summary>
    public static bool CanSeeSalary(UserRole role) => role is not UserRole.Guard;

    public static bool CanEditAttendance(UserRole role) => role is UserRole.Admin or UserRole.Accountant or UserRole.Guard;

    public static bool CanManageFaces(UserRole role) => role is UserRole.Admin or UserRole.Accountant or UserRole.Guard;

    public static bool Allows(UserRole role, Access access) => access switch
    {
        Access.View => true,
        Access.Edit => CanEdit(role),
        Access.Attendance => CanEditAttendance(role),
        Access.Faces => CanManageFaces(role),
        Access.Settings => CanOpen(role, Page.Settings),
        _ => false,
    };

    /// <summary>Rol ochishi mumkin bo'lgan birinchi bo'lim (kirgandan keyin).</summary>
    public static Page FirstPage(UserRole role) =>
        Enum.GetValues<Page>().First(p => CanOpen(role, p));
}
