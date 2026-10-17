using System.Security.Cryptography;
using System.Text;

namespace Oylik.Desktop.Services;

/// <summary>API kalit bazada DPAPI (CurrentUser) bilan shifrlangan holda saqlanadi.</summary>
public static class SecretProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = "Oylik.ApiKey.v1"u8.ToArray();

    public static string Protect(string secret)
    {
        if (!OperatingSystem.IsWindows())
            return secret;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>Ochib bo'lmasa (boshqa Windows foydalanuvchisi) — null.</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return null;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal) || !OperatingSystem.IsWindows())
            return stored;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
