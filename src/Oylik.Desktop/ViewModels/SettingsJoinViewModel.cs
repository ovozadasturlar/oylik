using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Data;
using Oylik.Data.Sync;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;
using Serilog;

namespace Oylik.Desktop.ViewModels;

/// <summary>
/// Bosqich 3 (ko'p-zavod): asosiy mashinada — ikkinchi zavod uchun ulanish kodi; ikkinchi zavodda — mavjud o'rnatmaga
/// ulanish (server manzili + asosiy Kompyuter ID + kod → o'z API kaliti va tabel diapazoni). Sozlamalar faqat Admin'ga ochiq.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty] private string _joinInstallationId = "";
    [ObservableProperty] private string _joinCode = "";
    [ObservableProperty] private string? _joinCodeText;
    [ObservableProperty] private bool _isJoining;

    /// <summary>Asosiy mashina: 10 daqiqalik bir martalik ulanish kodi.</summary>
    [RelayCommand]
    private async Task CreateJoinCodeAsync()
    {
        ServerMessage = null;
        JoinCodeText = null;
        var connection = Main.Sync.Connection;
        if (connection is null)
        {
            ServerMessage = Strings.ServerNotConfigured;
            return;
        }
        try
        {
            var res = await connection.RunAsync(c => c.CreateJoinCodeAsync(Main.Service.Settings.InstallationId));
            JoinCodeText = Strings.JoinCodeCreated(res.Code, res.ExpiresAt.ToLocalTime());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ServerException or JsonException)
        {
            ServerMessage = Strings.ServerUnreachable(ex.Message);
        }
    }

    /// <summary>Ikkinchi zavod: yuqoridagi server manzil(lar)i, asosiy Kompyuter ID va kod bilan ulanadi.</summary>
    [RelayCommand]
    private async Task JoinInstallationAsync()
    {
        ServerMessage = null;
        string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var local = Clean(LocalServerUrl);
        var global = Clean(GlobalServerUrl);
        if (local is null && global is null)
        {
            ServerMessage = Strings.ServerUrlRequired;
            return;
        }
        if ((local is not null && !ServerClient.IsValidUrl(local)) || (global is not null && !ServerClient.IsValidUrl(global)))
        {
            ServerMessage = Strings.ServerUrlInvalid;
            return;
        }
        if (!Guid.TryParse(JoinInstallationId.Trim(), out var installationId) || installationId == Guid.Empty)
        {
            ServerMessage = Strings.JoinInstallationIdInvalid;
            return;
        }
        var code = JoinCode.Trim();
        if (code.Length == 0)
        {
            ServerMessage = Strings.JoinCodeRequired;
            return;
        }
        // Mavjud xodimlarning tabel raqamlari asosiy zavodnikiga to'g'ri kelishi mumkin — ogohlantiramiz.
        var existing = Main.Service.Employees.Count;
        if (existing > 0 && !await Main.Dialogs.ConfirmAsync(Strings.ConfirmJoinWithEmployees(existing)))
            return;

        local = local is null ? null : ServerClient.NormalizeUrl(local);
        global = global is null ? null : ServerClient.NormalizeUrl(global);
        var settings = Main.Service.Settings;
        var name = string.IsNullOrWhiteSpace(settings.CompanyName) ? Environment.MachineName : settings.CompanyName;
        IsJoining = true;
        try
        {
            var res = await new ServerConnection(local, global, null).RunAsync(c => c.JoinAsync(installationId, code, name));
            if (!await Main.RunAsync(() => Main.Service.SaveJoinedInstallationAsync(local, global, res.InstallationId,
                    SecretProtector.Protect(res.ApiKey), res.TabelBase), Access.Settings))
                return;
            JoinCode = "";
            JoinInstallationId = "";
            LocalServerUrl = local ?? "";
            GlobalServerUrl = global ?? "";
            InstallationId = res.InstallationId.ToString();
            Main.Sync.Reset();
            await Main.Sync.SyncNowAsync();
            ServerMessage = Main.Sync.IsConnected ? Strings.JoinedInstallation(res.TabelBase) : Main.Sync.LastError;
        }
        catch (ServerException ex)
        {
            ServerMessage = ex.Status switch
            {
                HttpStatusCode.BadRequest => Strings.JoinCodeWrong,
                HttpStatusCode.Conflict => Strings.JoinCodeUsed,
                HttpStatusCode.Gone => Strings.JoinCodeExpired,
                HttpStatusCode.TooManyRequests => Strings.JoinTooManyAttempts,
                _ => Strings.ServerUnreachable(ex.Message),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Warning("Mavjud o'rnatmaga ulanish: {Error}", ex.Message);
            ServerMessage = Strings.ServerUnreachable(ex.Message);
        }
        finally
        {
            IsJoining = false;
            RefreshServer();
        }
    }
}
