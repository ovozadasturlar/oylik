using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Data;
using Oylik.Data.Terminal;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;
using Serilog;

namespace Oylik.Desktop.ViewModels;

/// <summary>5.6 (2.3): Yuz terminali (Hikvision) — manzil, parol, [Tekshirish], yoqish, Kelish/Ketish rejimi.</summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty] private string _terminalUrl = "";
    [ObservableProperty] private string _terminalPassword = "";
    [ObservableProperty] private bool _terminalEnabled;
    [ObservableProperty] private bool _terminalRequireConfirm;
    [ObservableProperty] private bool _terminalBusy;
    [ObservableProperty] private string? _terminalMessage;

    public FaceTerminalService FaceTerminal => Main.FaceTerminal;

    private void LoadTerminal()
    {
        var s = Main.Service.Settings;
        TerminalUrl = s.TerminalUrl ?? "";
        TerminalEnabled = s.TerminalEnabled;
        TerminalRequireConfirm = s.TerminalRequireConfirm;
        TerminalPassword = SecretProtector.Unprotect(s.TerminalPassword) is { Length: > 0 } ? "********" : "";
    }

    /// <summary>Parol maydoni o'zgarmagan bo'lsa (yulduzchalar) — eskisini saqlaymiz.</summary>
    private bool PasswordChanged => TerminalPassword != "********";

    [RelayCommand]
    private async Task TestTerminal()
    {
        TerminalMessage = null;
        var url = TerminalUrl.Trim();
        if (url.Length == 0 || !HikTerminalClient.IsValidUrl(url))
        {
            TerminalMessage = Strings.TerminalUrlRequired;
            return;
        }
        var pass = PasswordChanged ? TerminalPassword : SecretProtector.Unprotect(Main.Service.Settings.TerminalPassword);
        if (string.IsNullOrEmpty(pass))
        {
            TerminalMessage = Strings.TerminalPasswordRequired;
            return;
        }
        TerminalBusy = true;
        try
        {
            var info = await FaceTerminalService.TestAsync(url, pass);
            TerminalMessage = Strings.TerminalTestOk(info.Model, info.Firmware);
        }
        catch (Exception ex)
        {
            TerminalMessage = ex.Message;
        }
        finally
        {
            TerminalBusy = false;
        }
    }

    /// <summary>Barcha xodimni terminalга yozadi (ID=TabelNo, ism). Mavjud xodimlar uchun bir martalik.</summary>
    [RelayCommand]
    private async Task SyncAllTerminal()
    {
        TerminalMessage = null;
        if (!FaceTerminal.IsEnabled)
        {
            TerminalMessage = Strings.TerminalDisabled;
            return;
        }
        TerminalBusy = true;
        try
        {
            var (ok, fail) = await FaceTerminal.SyncAllAsync(Main.Service.Employees);
            TerminalMessage = Strings.TerminalSyncResult(ok, fail);
        }
        catch (Exception ex)
        {
            TerminalMessage = ex.Message;
        }
        finally
        {
            TerminalBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveTerminal()
    {
        TerminalMessage = null;
        var url = TerminalUrl.Trim();
        if (url.Length == 0 || !HikTerminalClient.IsValidUrl(url))
        {
            TerminalMessage = Strings.TerminalUrlRequired;
            return;
        }
        var plainPass = PasswordChanged ? TerminalPassword : SecretProtector.Unprotect(Main.Service.Settings.TerminalPassword);
        if (string.IsNullOrEmpty(plainPass))
        {
            TerminalMessage = Strings.TerminalPasswordRequired;
            return;
        }
        var normalized = HikTerminalClient.NormalizeUrl(url);
        var enabled = TerminalEnabled;
        var requireConfirm = TerminalRequireConfirm;
        var protectedPass = SecretProtector.Protect(plainPass);

        // Yoqilganda: terminal rejimini o'rnatish (Kelish/Ketish tugmalari) va birinchi marta kursorni joriy
        // oxirgi SerialNo'ga (eski test voqealari import qilinmasin).
        long? startSerial = null;
        string? serial = null;
        if (enabled)
        {
            try
            {
                using var client = new HikTerminalClient(normalized, "admin", plainPass);
                serial = (await client.GetInfoAsync()).SerialNumber; // IP o'zgarsa qidirish uchun kalit
                await client.SetAttendanceModeAsync(requireConfirm);
                await client.SetAttendanceKeysAsync(requireConfirm);
                if (Main.Service.Settings.TerminalLastSerial == 0)
                {
                    var now = Main.Service.Clock.GetUtcNow();
                    var recent = await client.FetchFaceEventsAsync(now.AddDays(-1), now.AddMinutes(1), 0);
                    startSerial = recent.Count > 0 ? recent[^1].SerialNo : 0;
                }
            }
            catch (Exception ex)
            {
                TerminalMessage = ex.Message;
                Log.Warning(ex, "Terminal rejimini o'rnatishda xato");
                return;
            }
        }

        await Main.RunAsync(() => Main.Service.SaveTerminalSettingsAsync(normalized, protectedPass, enabled, requireConfirm, startSerial, serial));
        TerminalUrl = normalized;
        TerminalMessage = Strings.TerminalSaved;
        FaceTerminal.Start();
        _ = FaceTerminal.PollAsync();
    }
}
