using System.IO;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;
using Serilog;

namespace Oylik.Desktop.ViewModels;

/// <summary>
/// Yuz biriktirish oqimi (terminalda olish yoki bor rasmni yuklash). Xodimlar va Oylik ekranlaridan,
/// hamda yangi xodim qo'shilgandan keyin chaqiriladi — bitta joyda, takrorsiz.
/// </summary>
internal static class FaceEnroll
{
    /// <summary>Xodimga yuz qo'shish usulini so'raydi va bajaradi. Terminal o'chiq bo'lsa — ogohlantiradi.</summary>
    public static async Task RunAsync(MainWindowViewModel main, Employee emp)
    {
        if (!main.Demand(Access.Faces))
            return;
        if (!main.FaceTerminal.IsEnabled)
        {
            main.ShowError(Strings.TerminalDisabled);
            return;
        }
        var choice = await main.Dialogs.ChooseAsync(
            Strings.TerminalFaceTitle, Strings.TerminalFacePrompt(emp.FullName),
            Strings.TerminalFaceCapture, Strings.TerminalFaceUpload);
        if (choice == 0)
            await CaptureAsync(main, emp);
        else if (choice == 1)
            await UploadAsync(main, emp);
    }

    /// <summary>Terminal kamerasida yuzni oladi (masofadan): xodim kameraga qarab tursin.</summary>
    private static async Task CaptureAsync(MainWindowViewModel main, Employee emp)
    {
        if (!await main.Dialogs.ConfirmAsync(Strings.TerminalFaceLookAtCamera))
            return;
        try
        {
            await main.FaceTerminal.CaptureFaceAsync(emp);
            await main.Dialogs.MessageAsync(Strings.FaceTerminal, Strings.TerminalFaceUploaded);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Terminalda yuz olishda xato (TabelNo {Tabel})", emp.TabelNo);
            main.ShowError(ex.Message);
        }
    }

    /// <summary>Bor rasmdan yuzni yuklaydi (kichraytirib/siqib).</summary>
    private static async Task UploadAsync(MainWindowViewModel main, Employee emp)
    {
        var path = await main.Dialogs.PickImageAsync();
        if (path is null)
            return;
        try
        {
            var raw = await File.ReadAllBytesAsync(path);
            var jpeg = await Task.Run(() => FacePhoto.Prepare(raw)); // kichraytirish/siqish — UI oqimidan tashqarida
            await main.FaceTerminal.UploadFaceAsync(emp, jpeg);
            await main.Dialogs.MessageAsync(Strings.FaceTerminal, Strings.TerminalFaceUploaded);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Terminalga yuz yuklashda xato (TabelNo {Tabel})", emp.TabelNo);
            main.ShowError(ex.Message);
        }
    }
}
