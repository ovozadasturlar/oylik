using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Oylik.Desktop.Services.Payslips;

/// <summary>
/// 5.11: PDF matni xavfsizligi — foydalanuvchi ma'lumotida (ism, izoh, sabab, korxona) istalgan Unicode bo'lishi mumkin.
/// Joylangan shriftlarning birortasida bo'lmagan belgi PDF'ga yozilmaydi: mos ekvivalent yoki «?» (PDF hech qachon belgi sababli yiqilmaydi).
/// </summary>
public static class PdfText
{
    private static readonly ConcurrentDictionary<int, bool> Coverage = new();
    private static SKTypeface[] _typefaces = [];

    /// <summary>Shriftda yo'q belgilar uchun yaqin ekvivalent (ekvivalent ham bo'lmasa — «?»).</summary>
    private static readonly Dictionary<int, string> Equivalents = new()
    {
        [0x2715] = "×", [0x2716] = "×", [0x2717] = "×", [0x2718] = "×", [0x274C] = "×",
        [0x2713] = "+", [0x2714] = "+", [0x2705] = "+",
        [0x2192] = "->", [0x2190] = "<-", [0x2265] = ">=", [0x2264] = "<=", [0x21BA] = "",
        [0x2212] = "-", [0x2014] = "-", [0x2013] = "-", [0x2026] = "...", [0x2022] = "·",
        [0x201C] = "\"", [0x201D] = "\"", [0x00AB] = "\"", [0x00BB] = "\"", [0x2018] = "'", [0x2019] = "'",
        [0x02BB] = "'", [0x02BC] = "'", [0x00A0] = " ", [0x202F] = " ", [0x2009] = " ",
    };

    internal static void Register(byte[] font)
    {
        if (SKTypeface.FromData(SKData.CreateCopy(font)) is { } typeface)
            _typefaces = [.. _typefaces, typeface];
        Coverage.Clear();
    }

    public static bool IsCovered(int codepoint) =>
        Coverage.GetOrAdd(codepoint, c => _typefaces.Any(t => t.ContainsGlyph(c)));

    public static bool IsCovered(string text) => text.EnumerateRunes().All(r => IsCovered(r.Value));

    public static string Safe(string text)
    {
        if (string.IsNullOrEmpty(text) || IsCovered(text))
            return text;
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (IsCovered(c))
                sb.Append(rune.ToString());
            else if (c is '\n' or '\r' or '\t')
                sb.Append(c == '\n' ? "\n" : c == '\t' ? " " : "");
            else if (IsInvisible(rune))
                continue; // ZWJ, variation selector, teri rangi, tag — ko'rinmaydi
            else if (Equivalents.TryGetValue(c, out var eq) && IsCovered(eq))
                sb.Append(eq);
            else
                sb.Append('?');
        }
        return sb.ToString();
    }

    public static string? SafeOrNull(string? text) => text is null ? null : Safe(text);

    private static bool IsInvisible(Rune rune) =>
        rune.Value is 0x200D or 0x200C or 0x20E3 or (>= 0xFE00 and <= 0xFE0F) or (>= 0x1F3FB and <= 0x1F3FF) or (>= 0xE0000 and <= 0xE01EF)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
}
