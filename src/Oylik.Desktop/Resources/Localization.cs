using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Oylik.Core.Domain;

namespace Oylik.Desktop.Resources;

public enum UiLanguage
{
    UzLatn,
    UzCyrl,
    Ru,
}

public static class UiLanguages
{
    public static string Code(UiLanguage language) => language switch
    {
        UiLanguage.UzCyrl => "uz-Cyrl",
        UiLanguage.Ru => "ru",
        _ => AppSettings.LanguageUzLatn,
    };

    /// <summary>"uz-Latn" / "uz-Cyrl" / "ru" (yoki qisqa "latn", "cyrl"); noma'lum — lotin.</summary>
    public static UiLanguage Parse(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "uz-cyrl" or "cyrl" or "uz-cyrillic" => UiLanguage.UzCyrl,
        "ru" or "ru-ru" => UiLanguage.Ru,
        _ => UiLanguage.UzLatn,
    };
}

/// <summary>
/// O'zbek lotin → kirill transliteratsiyasi (1995 alifbosi). Kirill matnlar lotin manbadan olinadi,
/// istisnolar (rus tilidan o'zlashgan so'zlar) — <see cref="Words"/>; qisqartmalar va URL o'zgarmaydi.
/// </summary>
public static partial class UzCyrillic
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>Lotincha qoldiriladigan so'zlar (dastur/klaviatura nomlari).</summary>
    private static readonly HashSet<string> Keep = new(StringComparer.Ordinal)
    {
        "OK", "QR", "URL", "ID", "API", "Excel", "Ctrl", "Enter", "Tab", "Esc", "Delete", "F", "N", "F2",
        "Google", "Drive", "localhost", "Oylik", "Wi", "Fi", "LAN", "PDF", "Telegram", "Desktop", "V",
    };

    /// <summary>Qoidadan tashqari so'zlar (kichik harfda; birinchi harf katta bo'lsa kirillda ham katta).</summary>
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["backup"] = "бэкап",
        ["kompyuter"] = "компьютер",
        ["kompyuterning"] = "компьютернинг",
        ["sentyabr"] = "сентябрь",
        ["oktabr"] = "октябрь",
        ["noyabr"] = "ноябрь",
        ["dekabr"] = "декабрь",
        ["yanvar"] = "январь",
        ["fevral"] = "февраль",
        ["aprel"] = "апрель",
        ["iyun"] = "июнь",
        ["iyul"] = "июль",
    };

    [GeneratedRegex(@"https?://[^\s,)»]+|\d+\.\d+\.\d+\.\d+(:\d+)?|[A-Za-z]+(?=\+)|\+[A-Za-z]\b|\b[A-Za-z]*\d+[A-Za-z]*\b")]
    private static partial Regex Protected();

    [GeneratedRegex(@"[A-Za-z]+(?:['ʻ’`][A-Za-z]+)*['ʻ’`]?")]
    private static partial Regex WordPattern();

    public static string FromLatin(string latin) => Cache.GetOrAdd(latin, Convert);

    private static string Convert(string text)
    {
        var sb = new StringBuilder(text.Length);
        var last = 0;
        foreach (Match m in Protected().Matches(text))
        {
            sb.Append(ConvertWords(text[last..m.Index]));
            sb.Append(m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(ConvertWords(text[last..]));
        return sb.ToString();
    }

    private static string ConvertWords(string text) =>
        WordPattern().Replace(text, m => ConvertWord(m.Value, m.Index > 0 && text[m.Index - 1] == '\''));

    private static string ConvertWord(string word, bool afterApostrophe)
    {
        if (Keep.Contains(word))
            return word;
        // "Excel'dan" — saqlanadigan so'z + qo'shimcha
        var quote = word.IndexOfAny(['\'', 'ʻ', '’', '`']);
        if (quote > 0 && Keep.Contains(word[..quote]))
            return word[..(quote + 1)] + ConvertWord(word[(quote + 1)..], true);

        var lower = word.ToLowerInvariant();
        var allCaps = word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c));
        var result = Words.TryGetValue(lower, out var known) ? known : Transliterate(lower, afterApostrophe);
        if (allCaps)
            return result.ToUpperInvariant();
        if (char.IsUpper(word[0]) && result.Length > 0)
            return char.ToUpperInvariant(result[0]) + result[1..];
        return result;
    }

    private static bool IsQuote(char c) => c is '\'' or 'ʻ' or '’' or '`';
    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static string Transliterate(string w, bool afterApostrophe)
    {
        var sb = new StringBuilder(w.Length);
        for (var i = 0; i < w.Length; i++)
        {
            var c = w[i];
            var next = i + 1 < w.Length ? w[i + 1] : '\0';
            var next2 = i + 2 < w.Length ? w[i + 2] : '\0';
            switch (c)
            {
                case 'o' when IsQuote(next): sb.Append('ў'); i++; continue;
                case 'g' when IsQuote(next): sb.Append('ғ'); i++; continue;
                case 's' when next == 'h': sb.Append('ш'); i++; continue;
                case 'c' when next == 'h': sb.Append('ч'); i++; continue;
                // ts → ц faqat o'zlashma so'zlarda (-tsiya, tsex); kirit+sa → киритса
                case 't' when next == 's' && (i == 0 || w.AsSpan(i + 2).StartsWith("iy")): sb.Append('ц'); i++; continue;
                case 'y' when next == 'o' && !IsQuote(next2): sb.Append('ё'); i++; continue;
                case 'y' when next == 'u': sb.Append('ю'); i++; continue;
                case 'y' when next == 'a': sb.Append('я'); i++; continue;
                case 'y' when next == 'e': sb.Append('е'); i++; continue;
                case 'e':
                    sb.Append((i == 0 && !afterApostrophe) || (i > 0 && IsVowel(w[i - 1])) ? 'э' : 'е');
                    continue;
            }
            if (IsQuote(c))
            {
                // tutuq belgisi (ma'lumot → маълумот); so'z oxirida — o'zgarmaydi
                sb.Append(i + 1 < w.Length ? 'ъ' : c);
                continue;
            }
            sb.Append(c switch
            {
                'a' => 'а', 'b' => 'б', 'd' => 'д', 'f' => 'ф', 'g' => 'г', 'h' => 'ҳ', 'i' => 'и', 'j' => 'ж',
                'k' => 'к', 'l' => 'л', 'm' => 'м', 'n' => 'н', 'o' => 'о', 'p' => 'п', 'q' => 'қ', 'r' => 'р',
                's' => 'с', 't' => 'т', 'u' => 'у', 'v' => 'в', 'x' => 'х', 'y' => 'й', 'z' => 'з', 'c' => 'ц',
                'w' => 'в',
                _ => c,
            });
        }
        return sb.ToString();
    }
}

/// <summary>
/// `Strings.F($"Yopilgan: {at:dd.MM.yyyy}", ...)` — interpolatsiyaning faqat matn qismlari kirillga o'giriladi,
/// qiymatlar (ismlar, sanalar) o'zgarmaydi.
/// </summary>
[InterpolatedStringHandler]
public ref struct UzText
{
    private DefaultInterpolatedStringHandler _inner;
    private readonly bool _cyrillic;

    public UzText(int literalLength, int formattedCount)
    {
        _cyrillic = Strings.Language == UiLanguage.UzCyrl;
        _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
    }

    public void AppendLiteral(string value) => _inner.AppendLiteral(_cyrillic ? UzCyrillic.FromLatin(value) : value);
    public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);
    public void AppendFormatted(string? value) => _inner.AppendFormatted(value);

    public string ToStringAndClear() => _inner.ToStringAndClear();
}
