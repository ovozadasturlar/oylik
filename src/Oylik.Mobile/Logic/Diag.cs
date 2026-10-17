namespace Oylik.Mobile.Logic;

/// <summary>
/// Diagnostika yozuvi (Android'da — logcat, teg "Oylik"; <c>adb logcat -s Oylik</c>). Token, kod va ismlar yozilmaydi.
/// </summary>
public static class Diag
{
    public static Action<string>? Sink { get; set; }

    public static void Log(string message)
    {
        try { Sink?.Invoke(message); }
        catch { /* diagnostika ishni to'xtatmasin */ }
    }
}
