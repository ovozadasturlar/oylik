using System.Security.Cryptography;
using System.Text;

namespace Oylik.Core.Domain;

/// <summary>Desktop foydalanuvchi roli (sessiya tanlovi; lokal, sinxronlanmaydi).</summary>
public enum UserRole
{
    Admin = 0,
    Accountant = 1, // Buxgalter
    Manager = 2,    // Rahbar — faqat ko'rish
    Guard = 3,      // Qorovul — maoshsiz: xodimlar ro'yxati, yuz, davomat
}

/// <summary>Rol PINi — faqat xesh (PBKDF2-HMAC-SHA256 + tasodifiy salt). Ochiq PIN hech qayerda saqlanmaydi.</summary>
public sealed class RolePin
{
    public UserRole Role { get; set; }
    public byte[] Salt { get; set; } = [];
    public byte[] Hash { get; set; } = [];
    public int Iterations { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>PIN qoidasi va xeshlash. PIN — 4–8 ta raqam.</summary>
public static class PinHasher
{
    public const int MinLength = 4;
    public const int MaxLength = 8;
    public const int DefaultIterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static bool IsValid(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    /// <summary>Yangi salt bilan xesh. PIN qoidaga mos bo'lmasa — <see cref="ArgumentException"/>.</summary>
    public static (byte[] Salt, byte[] Hash, int Iterations) Create(string pin, int iterations = DefaultIterations)
    {
        if (!IsValid(pin))
            throw new ArgumentException("PIN 4–8 ta raqamdan iborat bo'lishi kerak.", nameof(pin));
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return (salt, Derive(pin, salt, iterations), iterations);
    }

    public static bool Verify(string? pin, RolePin stored)
    {
        if (!IsValid(pin) || stored.Salt.Length == 0 || stored.Hash.Length == 0 || stored.Iterations <= 0)
            return false;
        var actual = Derive(pin!, stored.Salt, stored.Iterations);
        return CryptographicOperations.FixedTimeEquals(actual, stored.Hash);
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
