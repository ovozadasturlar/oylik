using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SkiaSharp;

namespace Oylik.Desktop.Services.Payslips;

/// <summary>5.11: chop etish (Windows printer tanlash oynasi), faylni buferga (Explorer Ctrl+C kabi), Telegram Desktop'ni ochish.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static partial class WindowsShell
{
    // ---------- Chop etish: PrintDlg (printer tanlash) → GDI ----------

    /// <summary>Printer tanlash oynasi; tanlansa — sahifa rasmlari (PNG, yuqori DPI) printerga chiziladi. Bekor — false.</summary>
    public static bool Print(IntPtr owner, string documentName, IReadOnlyList<byte[]> pagesPng)
    {
        var dlg = new PRINTDLGW
        {
            lStructSize = Marshal.SizeOf<PRINTDLGW>(),
            hwndOwner = owner,
            Flags = PD_RETURNDC | PD_NOSELECTION | PD_USEDEVMODECOPIESANDCOLLATE | PD_ALLPAGES,
            nFromPage = 1,
            nToPage = (ushort)pagesPng.Count,
            nMinPage = 1,
            nMaxPage = (ushort)pagesPng.Count,
            nCopies = 1,
        };
        if (!PrintDlgW(ref dlg))
            return false;
        try
        {
            var from = (dlg.Flags & PD_PAGENUMS) != 0 ? Math.Max(1, (int)dlg.nFromPage) : 1;
            var to = (dlg.Flags & PD_PAGENUMS) != 0 ? Math.Min(pagesPng.Count, (int)dlg.nToPage) : pagesPng.Count;
            var info = new DOCINFOW { cbSize = Marshal.SizeOf<DOCINFOW>(), lpszDocName = documentName };
            if (StartDocW(dlg.hDC, ref info) <= 0)
                throw new InvalidOperationException("StartDoc");
            var width = GetDeviceCaps(dlg.hDC, HORZRES);
            var height = GetDeviceCaps(dlg.hDC, VERTRES);
            for (var i = from - 1; i < to; i++)
            {
                using var bitmap = SKBitmap.Decode(pagesPng[i]).Copy(SKColorType.Bgra8888);
                StartPage(dlg.hDC);
                // A4 sahifani printer maydoniga nisbatni saqlab joylash
                var scale = Math.Min((double)width / bitmap.Width, (double)height / bitmap.Height);
                var w = (int)(bitmap.Width * scale);
                var h = (int)(bitmap.Height * scale);
                var header = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = bitmap.Width,
                    biHeight = -bitmap.Height, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                };
                SetStretchBltMode(dlg.hDC, HALFTONE);
                StretchDIBits(dlg.hDC, (width - w) / 2, (height - h) / 2, w, h, 0, 0, bitmap.Width, bitmap.Height,
                    bitmap.GetPixels(), ref header, DIB_RGB_COLORS, SRCCOPY);
                EndPage(dlg.hDC);
            }
            EndDoc(dlg.hDC);
            return true;
        }
        finally
        {
            DeleteDC(dlg.hDC);
            if (dlg.hDevMode != IntPtr.Zero) GlobalFree(dlg.hDevMode);
            if (dlg.hDevNames != IntPtr.Zero) GlobalFree(dlg.hDevNames);
        }
    }

    // ---------- Fayl buferga (CF_HDROP) ----------

    /// <summary>Faylni buferga fayl sifatida (Explorer'dagi Ctrl+C kabi) — Telegram'da Ctrl+V bilan yuboriladi.</summary>
    public static void CopyFileToClipboard(string path)
    {
        var full = Path.GetFullPath(path);
        var chars = (full + "\0\0").ToCharArray();
        var headerSize = Marshal.SizeOf<DROPFILES>();
        var size = headerSize + chars.Length * 2;
        var handle = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)size);
        var ptr = GlobalLock(handle);
        Marshal.StructureToPtr(new DROPFILES { pFiles = headerSize, fWide = 1 }, ptr, false);
        Marshal.Copy(chars, 0, ptr + headerSize, chars.Length);
        GlobalUnlock(handle);

        var effect = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, 4);
        Marshal.WriteInt32(GlobalLock(effect), DROPEFFECT_COPY);
        GlobalUnlock(effect);

        for (var attempt = 0; attempt < 10 && !OpenClipboard(IntPtr.Zero); attempt++)
            Thread.Sleep(30);
        try
        {
            EmptyClipboard();
            SetClipboardData(CF_HDROP, handle);
            SetClipboardData(RegisterClipboardFormatW("Preferred DropEffect"), effect);
        }
        finally
        {
            CloseClipboard();
        }
    }

    // ---------- Telegram Desktop ----------

    /// <summary>Telegram Desktop'ni ochadi (exe yoki tg:// protokoli). Topilmasa — false.</summary>
    public static bool OpenTelegram()
    {
        var exe = FindTelegramExe();
        if (exe is not null)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return true;
        }
        if (Registry.CurrentUser.OpenSubKey(@"Software\Classes\tg") is not null || Registry.ClassesRoot.OpenSubKey("tg") is not null)
        {
            Process.Start(new ProcessStartInfo("tg://") { UseShellExecute = true });
            return true;
        }
        return false;
    }

    private static string? FindTelegramExe()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Telegram Desktop", "Telegram.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Telegram Desktop", "Telegram.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Telegram Desktop", "Telegram.exe"),
        ];
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null)
            return found;
        var command = Registry.CurrentUser.OpenSubKey(@"Software\Classes\tg\shell\open\command")?.GetValue(null) as string
                      ?? Registry.ClassesRoot.OpenSubKey(@"tg\shell\open\command")?.GetValue(null) as string;
        if (command is null)
            return null;
        var exe = command.StartsWith('"') ? command[1..command.IndexOf('"', 1)] : command.Split(' ')[0];
        return File.Exists(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : null;
    }

    /// <summary>Explorer'da papkani ochib, faylni belgilaydi.</summary>
    public static void ShowInFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    /// <summary>`Hujjatlar\Oylik\Vedomostlar\2026-10\`.</summary>
    public static string PayslipFolder(Core.Domain.MonthKey month) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Oylik", "Vedomostlar", $"{month.Year:0000}-{month.Month:00}");

    // ---------- Win32 ----------

    private const int PD_ALLPAGES = 0x0, PD_PAGENUMS = 0x2, PD_NOSELECTION = 0x4, PD_RETURNDC = 0x100, PD_USEDEVMODECOPIESANDCOLLATE = 0x40000;
    private const int HORZRES = 8, VERTRES = 10, DIB_RGB_COLORS = 0, SRCCOPY = 0x00CC0020, HALFTONE = 4;
    private const uint CF_HDROP = 15, GMEM_MOVEABLE = 0x2, GMEM_ZEROINIT = 0x40;
    private const int DROPEFFECT_COPY = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTDLGW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hDevMode;
        public IntPtr hDevNames;
        public IntPtr hDC;
        public int Flags;
        public ushort nFromPage;
        public ushort nToPage;
        public ushort nMinPage;
        public ushort nMaxPage;
        public ushort nCopies;
        public IntPtr hInstance;
        public IntPtr lCustData;
        public IntPtr lpfnPrintHook;
        public IntPtr lpfnSetupHook;
        public IntPtr lpPrintTemplateName;
        public IntPtr lpSetupTemplateName;
        public IntPtr hPrintTemplate;
        public IntPtr hSetupTemplate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOCINFOW
    {
        public int cbSize;
        public string lpszDocName;
        public string? lpszOutput;
        public string? lpszDatatype;
        public int fwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public int pFiles;
        public int ptX;
        public int ptY;
        public int fNC;
        public int fWide;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PrintDlgW(ref PRINTDLGW lppd);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int StartDocW(IntPtr hdc, ref DOCINFOW lpdi);

    [DllImport("gdi32.dll")] private static extern int StartPage(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int EndPage(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int EndDoc(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr hdc, int index);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int wDest, int hDest, int xSrc, int ySrc, int wSrc, int hSrc,
        IntPtr bits, ref BITMAPINFOHEADER bmi, int usage, int rop);

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string format);
}
