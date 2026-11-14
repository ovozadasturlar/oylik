using SkiaSharp;

namespace Oylik.Desktop.Services;

/// <summary>
/// Terminalга yuklashдан oldin yuz suratини tayyorlaydi: uzun tomonи <see cref="MaxSide"/> px gача kichraytiriladi va
/// JPEG sifatи <see cref="MaxBytes"/> dан oshмайдиган qilib siqiladi (Hikvision yuz kutubxonаси talablari: ~200KB, aniq old-yuz).
/// Telefon fotolari odatда bir necha MB — bu ularни qabul qilинadigan holatга keltiradi.
/// </summary>
public static class FacePhoto
{
    public const int MaxSide = 640;
    public const int MaxBytes = 180 * 1024;

    /// <summary>Har qanday rasm (JPEG/PNG) baytларini qabul qilib, siqилган JPEG baytларини qaytaradi.</summary>
    public static byte[] Prepare(byte[] input)
    {
        using var original = SKBitmap.Decode(input)
            ?? throw new InvalidOperationException("Rasmни o'qib bo'lmadi.");

        var longSide = Math.Max(original.Width, original.Height);
        SKBitmap bmp = original;
        var resized = false;
        if (longSide > MaxSide)
        {
            var scale = (double)MaxSide / longSide;
            var info = new SKImageInfo((int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale));
            var scaledBmp = original.Resize(info, SKFilterQuality.High);
            if (scaledBmp is not null)
            {
                bmp = scaledBmp;
                resized = true;
            }
        }

        try
        {
            using var image = SKImage.FromBitmap(bmp);
            byte[] last = [];
            for (var quality = 92; quality >= 40; quality -= 10)
            {
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
                last = data.ToArray();
                if (last.Length <= MaxBytes)
                    break;
            }
            return last;
        }
        finally
        {
            if (resized)
                bmp.Dispose();
        }
    }
}
