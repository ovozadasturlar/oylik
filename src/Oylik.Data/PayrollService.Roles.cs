using Microsoft.EntityFrameworkCore;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>
/// Rollar (Bosqich 1): har rol uchun PIN — faqat xesh (<see cref="PinHasher"/>), lokal jadvalda (sinxronlanmaydi).
/// Admin PINi o'rnatilmaguncha kirish so'ralmaydi (birinchi ishga tushirishda lockout yo'q).
/// </summary>
public sealed partial class PayrollService
{
    private Task LoadRolePinsAsync() => _db.RolePins.LoadAsync();

    private RolePin? FindPin(UserRole role) => _db.RolePins.Local.FirstOrDefault(p => p.Role == role);

    public bool HasPin(UserRole role) => FindPin(role) is not null;

    /// <summary>Kirish ekrani kerakmi: faqat Admin PINi o'rnatilgan bo'lsa (aks holda dastur Admin sifatida ochiladi).</summary>
    public bool LoginRequired => HasPin(UserRole.Admin);

    /// <summary>Kirish ekranida tanlash mumkin bo'lgan rollar (PINi borlar), <see cref="UserRole"/> tartibida.</summary>
    public IReadOnlyList<UserRole> RolesWithPin => _db.RolePins.Local.Select(p => p.Role).Distinct().Order().ToList();

    public bool VerifyPin(UserRole role, string? pin) => FindPin(role) is { } stored && PinHasher.Verify(pin, stored);

    /// <summary>Tekshiruv (PBKDF2) fon oqimida; bazadagi yozuv chaqiruvchi oqimda olinadi (DbContext bir oqimli).</summary>
    public async Task<bool> VerifyPinAsync(UserRole role, string? pin)
    {
        if (FindPin(role) is not { } stored)
            return false;
        var copy = new RolePin { Role = stored.Role, Salt = stored.Salt, Hash = stored.Hash, Iterations = stored.Iterations };
        return await Task.Run(() => PinHasher.Verify(pin, copy));
    }

    /// <summary>PIN o'rnatadi/almashtiradi (4–8 raqam). Auditga faqat fakt yoziladi — PIN/xesh emas.</summary>
    public Task SetPinAsync(UserRole role, string pin) => RunAsync(e =>
    {
        var (salt, hash, iterations) = PinHasher.Create(pin);
        var existing = FindPin(role);
        if (existing is null)
        {
            existing = new RolePin { Role = role };
            _db.RolePins.Add(existing);
        }
        existing.Salt = salt;
        existing.Hash = hash;
        existing.Iterations = iterations;
        existing.UpdatedAt = _clock.GetUtcNow();
        e.Log("RolePin", Settings.InstallationId, "RolePinSet", null, role.ToString());
    });

    /// <summary>PINni olib tashlaydi (Admin uchun — kirish ekrani o'chadi).</summary>
    public Task ClearPinAsync(UserRole role) => RunAsync(e =>
    {
        if (FindPin(role) is not { } existing)
            return;
        _db.RolePins.Remove(existing);
        e.Log("RolePin", Settings.InstallationId, "RolePinCleared", role.ToString(), null);
    });
}
