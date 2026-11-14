using Oylik.Data.Terminal;

namespace Oylik.Desktop;

/// <summary>Dasturchi uchun: `Oylik.exe --terminal-probe http://192.168.1.2 &lt;parol&gt;` — ulanadi, qurilma
/// ma'lumotini va bugungi yuz voqealarini chop etadi (haqiqiy terminalni tekshirish). UI'siz, chiqadi.</summary>
internal static class TerminalProbe
{
    public static async Task<int> Run(string url, string password)
    {
        try
        {
            using var client = new HikTerminalClient(url, "admin", password);
            var info = await client.GetInfoAsync();
            Console.WriteLine($"OK: {info.Model} | SN={info.SerialNumber} | FW={info.Firmware} | ism={info.DeviceName}");

            var now = DateTimeOffset.Now;
            var from = new DateTimeOffset(now.Date, now.Offset);
            var events = await client.FetchFaceEventsAsync(from, now.AddMinutes(1), 0);
            Console.WriteLine($"Bugungi yuz voqealari: {events.Count}");
            foreach (var e in events)
                Console.WriteLine($"  #{e.SerialNo}  {e.Time:HH:mm:ss}  ID={e.EmployeeNo}  {e.Name}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"XATO: {ex.Message}");
            return 1;
        }
    }

    /// <summary>`--discover-probe &lt;serial&gt; &lt;parol&gt;` — tarmoqdan seriya bo'yicha qurilmani topadi.</summary>
    public static async Task<int> Discover(string serial, string password)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var url = await HikTerminalClient.DiscoverBySerialAsync(serial, "admin", password);
        sw.Stop();
        Console.WriteLine(url is null
            ? $"TOPILMADI ({sw.ElapsedMilliseconds} ms)"
            : $"TOPILDI: {url} ({sw.ElapsedMilliseconds} ms)");
        return url is null ? 1 : 0;
    }

    /// <summary>`--face-probe &lt;url&gt; &lt;parol&gt; &lt;empNo&gt; &lt;ism&gt; capture|upload [imgpath]` — yuz oqimini haqiqiy qurilmada sinaydi.</summary>
    public static async Task<int> Face(string url, string password, string empNo, string name, string mode, string? imgPath)
    {
        try
        {
            using var client = new HikTerminalClient(url, "admin", password);
            Console.WriteLine("1) UpsertUser...");
            await client.UpsertUserAsync(empNo, name, true);
            Console.WriteLine("   ok");

            if (mode == "capture")
            {
                Console.WriteLine("2) CaptureFace (kameraga qarab turing)...");
                var jpeg = await client.CaptureFaceAsync();
                Console.WriteLine($"   olindi: {jpeg.Length} bayt");
                Console.WriteLine("3) UploadFace...");
                await client.UploadFaceAsync(empNo, jpeg);
                Console.WriteLine("   ok — yuz biriktirildi");
            }
            else
            {
                var raw = await File.ReadAllBytesAsync(imgPath!);
                var jpeg = Services.FacePhoto.Prepare(raw);
                Console.WriteLine($"2) FacePhoto: {raw.Length} -> {jpeg.Length} bayt");
                Console.WriteLine("3) UploadFace...");
                await client.UploadFaceAsync(empNo, jpeg);
                Console.WriteLine("   ok — yuz biriktirildi");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"XATO [{ex.GetType().Name}]: {ex.Message}");
            return 1;
        }
    }
}
