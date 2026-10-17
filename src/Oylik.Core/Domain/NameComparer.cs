using System.Globalization;

namespace Oylik.Core.Domain;

/// <summary>Ism bo'yicha madaniy saralash: ru-RU, katta-kichik harf farqsiz (kirill/lotin aralash).</summary>
public static class NameComparer
{
    public static StringComparer Instance { get; } =
        StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true);
}
