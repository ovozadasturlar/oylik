using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Contracts;
using Oylik.Core.Domain;
using Oylik.Desktop.Resources;
using Oylik.Desktop.Services;
using Oylik.Data.Sync;
using Oylik.Desktop.Views;
using Serilog;

namespace Oylik.Desktop.ViewModels;

public sealed class DeviceItem(Device device, Func<Device, Task> deactivate)
{
    public Device Device { get; } = device;
    public string Name => Device.Name;
    public string PairedText => Strings.DevicePaired(Device.PairedAt.ToLocalTime());
    public string LastSeenText => Device.LastSeenAt is { } at ? Strings.DeviceLastSeen(at.ToLocalTime()) : "";
    public bool IsActive => Device.IsActive;
    public string StateText => Device.IsActive ? Strings.DeviceActive : Strings.DeviceInactive;
    public IAsyncRelayCommand DeactivateCommand { get; } = new AsyncRelayCommand(() => deactivate(device));
}

/// <summary>5.6 (2-bosqich): Server URL, [Serverga ulash], [QR ko'rsatish], bog'langan qurilmalar.</summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty] private string _localServerUrl = "";
    [ObservableProperty] private string _globalServerUrl = "";
    [ObservableProperty] private string _registrationKey = "";
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private string? _serverMessage;
    [ObservableProperty] private bool _isServerConfigured;

    public ObservableCollection<DeviceItem> Devices { get; } = [];
    public SyncService Sync => Main.Sync;

    private void LoadServer()
    {
        LocalServerUrl = Main.Service.Settings.LocalServerUrl ?? "";
        GlobalServerUrl = Main.Service.Settings.ServerUrl ?? "";
        RefreshServer();
    }

    public void RefreshServer()
    {
        IsServerConfigured = Main.Sync.IsConfigured;
        Devices.Clear();
        foreach (var d in Main.Service.Devices.OrderByDescending(d => d.IsActive).ThenBy(d => d.PairedAt))
            Devices.Add(new DeviceItem(d, DeactivateAsync));
    }

    /// <summary>
    /// Kalit berilgan bo'lsa — ro'yxatdan o'tish (o'z InstallationId bilan). Kalit bo'sh va API kalit bor bo'lsa —
    /// faqat yangi URL tekshirilib saqlanadi (server ko'chirilganda).
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync()
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
        local = local is null ? null : ServerClient.NormalizeUrl(local);
        global = global is null ? null : ServerClient.NormalizeUrl(global);
        var settings = Main.Service.Settings;
        IsConnecting = true;
        try
        {
            if (string.IsNullOrWhiteSpace(RegistrationKey))
            {
                if (Main.Sync.ApiKey is not { } existing)
                {
                    ServerMessage = Strings.RegistrationKeyRequired;
                    return;
                }
                // Manzil o'zgardi — server o'sha, kalit o'sha: tekshirib saqlaymiz (qayta ro'yxatdan o'tish shart emas)
                await new ServerConnection(local, global, existing).RunAsync(c => c.GetDevicesAsync());
                await Main.RunAsync(() => Main.Service.SaveServerAddressesAsync(local, global));
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(settings.CompanyName) ? Environment.MachineName : settings.CompanyName;
                var key = RegistrationKey.Trim();
                var res = await new ServerConnection(local, global, null).RunAsync(c => c.RegisterAsync(name, key, settings.InstallationId));
                await Main.RunAsync(() => Main.Service.SaveServerConnectionAsync(local, global, SecretProtector.Protect(res.ApiKey)));
                RegistrationKey = "";
            }
            LocalServerUrl = local ?? "";
            GlobalServerUrl = global ?? "";
            Main.Sync.Reset();
            await Main.Sync.SyncNowAsync();
            ServerMessage = Main.Sync.IsConnected ? Strings.ServerConnected : Main.Sync.LastError;
        }
        catch (ServerException ex) when (ex.Status == HttpStatusCode.Conflict)
        {
            ServerMessage = Strings.InstallationAlreadyRegistered;
        }
        catch (ServerException ex) when (ex.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            ServerMessage = Strings.RegistrationKeyWrong;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ServerException or JsonException)
        {
            Log.Warning("Serverga ulanish: {Error}", ex.Message);
            ServerMessage = Strings.ServerUnreachable(ex.Message);
        }
        finally
        {
            IsConnecting = false;
            RefreshServer();
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await Main.Sync.SyncNowAsync();
        ServerMessage = Main.Sync.IsConnected ? null : Main.Sync.LastError;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (!await Main.Dialogs.ConfirmAsync(Strings.ConfirmDisconnectServer))
            return;
        await Main.RunAsync(() => Main.Service.SaveServerConnectionAsync(null, null, null));
        LocalServerUrl = "";
        GlobalServerUrl = "";
        Main.Sync.Reset();
        RefreshServer();
    }

    /// <summary>6-bo'lim QR payload: {"server","installationId","code"} — 10 daqiqalik kod.</summary>
    [RelayCommand]
    private async Task ShowQrAsync()
    {
        ServerMessage = null;
        var connection = Main.Sync.Connection;
        if (connection is null)
        {
            ServerMessage = Strings.ServerNotConfigured;
            return;
        }
        try
        {
            var settings = Main.Service.Settings;
            var code = await connection.RunAsync(c => c.CreatePairingCodeAsync(settings.InstallationId));
            // Server — global (faqat lokal bo'lsa — lokal); LocalServer — ikkalasi bo'lsa lokal (eski telefon faqat Server'ni o'qiydi)
            var both = connection.LocalUrl is not null && connection.GlobalUrl is not null;
            var payload = new PairingQrPayload(connection.GlobalUrl ?? connection.LocalUrl!, settings.InstallationId, code.Code,
                both ? connection.LocalUrl : null);
            var json = JsonSerializer.Serialize(payload, Api.Json);
            await Main.Dialogs.ShowDialogAsync(new PairingQrWindow(json, payload, code.ExpiresAt));
            await Main.Sync.SyncNowAsync(); // yangi qurilma ro'yxatda ko'rinsin
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ServerException or JsonException)
        {
            ServerMessage = Strings.ServerUnreachable(ex.Message);
        }
    }

    private async Task DeactivateAsync(Device device)
    {
        if (!await Main.Dialogs.ConfirmAsync(Strings.ConfirmDeactivateDevice(device.Name)))
            return;
        var connection = Main.Sync.Connection;
        if (connection is null)
            return;
        try
        {
            await connection.RunAsync(c => c.DeactivateDeviceAsync(device.Id));
            await Main.Sync.SyncNowAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ServerException)
        {
            ServerMessage = Strings.ServerUnreachable(ex.Message);
        }
    }
}
