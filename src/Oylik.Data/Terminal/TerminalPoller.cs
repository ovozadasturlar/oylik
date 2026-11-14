using Oylik.Core.Domain;

namespace Oylik.Data.Terminal;

/// <summary>Bitta poll natijasi.</summary>
/// <param name="Connected">Terminal javob berdi.</param>
/// <param name="Added">Yangi yozilgan davomat voqealari.</param>
/// <param name="Info">Qurilma ma'lumoti (ulanganda).</param>
public sealed record TerminalPollResult(bool Connected, int Added, TerminalInfo? Info)
{
    public static TerminalPollResult Offline { get; } = new(false, 0, null);
}

/// <summary>
/// Bosqich 2: terminal → baza bitta "tick" mantig'i (UI'siz) — desktop (<c>FaceTerminalService</c>) ham, fon agenti ham
/// shuni ishlatadi. Ulanish (sozlamadagi IP → bo'lmasa seriya bo'yicha /24 skan, backoff bilan), yangi yuz voqealarini
/// o'qish va <see cref="PayrollService.ApplyTerminalEventsAsync"/> bilan yozish.
/// </summary>
/// <param name="runDb">Bazaga yozuvchi amallar shu orqali (bitta DbContext — ketma-ket). null — to'g'ridan-to'g'ri.</param>
/// <param name="clientFactory">Testlar uchun: (url, parol) → mijoz. null — haqiqiy ISAPI mijozi.</param>
/// <param name="discover">Testlar uchun: (seriya, parol) → topilgan url. null — <see cref="HikTerminalClient.DiscoverBySerialAsync"/>.</param>
/// <param name="unprotect">Shifrlangan parolni ochish. null — <see cref="SecretProtector.Unprotect"/>.</param>
/// <param name="deviceName">Qurilma nomi (desktop — UI tilida). null — <see cref="DefaultDeviceName"/>.</param>
public sealed class TerminalPoller(
    PayrollService service,
    Func<Func<Task>, Task>? runDb = null,
    TimeProvider? clock = null,
    Func<string, string, HikTerminalClient>? clientFactory = null,
    Func<string, string, CancellationToken, Task<string?>>? discover = null,
    Func<string?, string?>? unprotect = null,
    Action<string>? log = null,
    Func<TerminalInfo, string>? deviceName = null)
{
    /// <summary>Har pollda shuncha orqadagi voqealar qayta ko'riladi (kursor bo'yicha filtrlanadi). Offline bo'lib qolsa ham yetadi.</summary>
    public static readonly TimeSpan LookBack = TimeSpan.FromDays(2);

    /// <summary>Tarmoq skanlari orasidagi eng qisqa / eng uzun pauza (terminal topilmasa 2 baravardan oshadi).</summary>
    public static readonly TimeSpan DiscoverMinDelay = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DiscoverMaxDelay = TimeSpan.FromMinutes(2);

    private readonly Func<Func<Task>, Task> _runDb = runDb ?? (a => a());
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Func<string, string, HikTerminalClient> _clientFactory = clientFactory ?? ((url, pass) => new HikTerminalClient(url, "admin", pass));
    private readonly Func<string, string, CancellationToken, Task<string?>> _discover = discover
        ?? ((serial, pass, ct) => HikTerminalClient.DiscoverBySerialAsync(serial, "admin", pass, ct));
    private readonly Func<string?, string?> _unprotect = unprotect ?? SecretProtector.Unprotect;
    private readonly Func<TerminalInfo, string> _deviceName = deviceName ?? DefaultDeviceName;

    private TimeSpan _discoverDelay = DiscoverMinDelay;
    private DateTimeOffset _nextDiscover = DateTimeOffset.MinValue;
    private bool _discovering;

    public PayrollService Service => service;

    /// <summary>Terminal yoqilgan va manzil/parol bor.</summary>
    public bool IsEnabled => service.Settings.TerminalEnabled
        && service.Settings.TerminalUrl is { Length: > 0 }
        && service.Settings.TerminalPassword is { Length: > 0 };

    /// <summary>Keyingi skan qachon ruxsat etiladi (testlar va holat uchun).</summary>
    public DateTimeOffset NextDiscoverAt => _nextDiscover;

    /// <summary>Terminal javob berdi — skan backoff'i normal holatga qaytadi.</summary>
    private void ResetDiscoverBackoff()
    {
        _discoverDelay = DiscoverMinDelay;
        _nextDiscover = DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Ulangan mijozni qaytaradi: sozlamadagi IP javob bersa — o'sha; bermasa va seriya ma'lum bo'lsa (hamda
    /// <paramref name="allowDiscovery"/>) — tarmoqdan seriya bo'yicha qurilmani topib, IP'ni avtomat yangilaydi. Topilmasa — null.
    /// Skan og'ir (/24), shuning uchun backoff bilan: topilmasa keyingi skangacha pauza 20s → 40s → 80s → 2 daq.
    /// gacha uzayadi; terminal javob bersa (to'g'ridan-to'g'ri yoki skan orqali) — darhol 20s ga qaytadi.
    /// To'g'ridan-to'g'ri IP so'rovi har chaqiruvda qoladi (arzon). Muvaffaqiyatda seriyani eslab qoladi.
    /// </summary>
    public async Task<(HikTerminalClient Client, TerminalInfo Info)?> AcquireAsync(bool allowDiscovery = true, CancellationToken ct = default)
    {
        var url = service.Settings.TerminalUrl;
        var pass = _unprotect(service.Settings.TerminalPassword);
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(pass))
            return null;

        var client = _clientFactory(url, pass);
        try
        {
            var info = await client.GetInfoAsync(ct);
            ResetDiscoverBackoff();
            if (!string.IsNullOrWhiteSpace(info.SerialNumber) && service.Settings.TerminalSerial != info.SerialNumber)
                await _runDb(() => service.SaveTerminalSerialAsync(info.SerialNumber));
            return (client, info);
        }
        catch (TerminalException)
        {
            client.Dispose(); // IP javob bermadi — pastda qidiramiz
        }

        var serial = service.Settings.TerminalSerial;
        if (!allowDiscovery || string.IsNullOrEmpty(serial) || _discovering || _clock.GetUtcNow() < _nextDiscover)
            return null;

        string? found;
        _discovering = true;
        try
        {
            found = await _discover(serial, pass, ct);
        }
        finally
        {
            _discovering = false;
        }
        if (found is null)
        {
            _nextDiscover = _clock.GetUtcNow() + _discoverDelay;
            _discoverDelay = TimeSpan.FromTicks(Math.Min(_discoverDelay.Ticks * 2, DiscoverMaxDelay.Ticks));
            return null;
        }
        ResetDiscoverBackoff(); // qurilma tarmoqda — keyingi pollda normal rejim
        if (found == HikTerminalClient.NormalizeUrl(url))
            return null;

        await _runDb(() => service.SaveTerminalUrlAsync(found));
        log?.Invoke($"Terminal yangi IP'da topildi va yangilandi: {found}");
        var moved = _clientFactory(found, pass);
        try
        {
            var info = await moved.GetInfoAsync(ct);
            return (moved, info);
        }
        catch (TerminalException)
        {
            moved.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Bosqich 3: xodimlarni terminalga yozadi/yangilaydi (ID = TabelNo, ism; nofaol — bloklanadi, yuzi saqlanadi).
    /// Boshqa zavoddan pull qilingan xodimni shu zavod terminali tanishi uchun. Best-effort: terminal yo'q/o'chiq — 0;
    /// bitta xodimdagi xato qolganlarini to'xtatmaydi (log — faqat tabel raqami). Yozilganlar sonini qaytaradi.
    /// </summary>
    public async Task<int> PushEmployeesAsync(IEnumerable<Employee> employees, bool allowDiscovery = true, CancellationToken ct = default)
    {
        var list = employees.Where(e => e.TabelNo > 0).ToList();
        if (list.Count == 0 || !IsEnabled)
            return 0;
        var acq = await AcquireAsync(allowDiscovery, ct);
        if (acq is not { } a)
            return 0;
        using var client = a.Client;
        var ok = 0;
        foreach (var e in list)
        {
            try
            {
                await client.UpsertUserAsync(e.TabelNo.ToString(System.Globalization.CultureInfo.InvariantCulture), e.FullName, e.IsActive, ct);
                ok++;
            }
            catch (Exception ex) when (ex is TerminalException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                log?.Invoke($"Terminalga yozib bo'lmadi (tabel {e.TabelNo}): {ex.Message}");
            }
        }
        if (ok > 0)
            log?.Invoke($"Terminalga {ok} xodim yozildi (serverdan)");
        return ok;
    }

    /// <summary>
    /// Bitta tick: ulanish → qurilma yozuvi → yangi yuz voqealari (kursordan keyingilari) → bazaga. Terminal yo'q bo'lsa
    /// <see cref="TerminalPollResult.Offline"/>. <paramref name="afterPoll"/> — o'sha ulanishda qo'shimcha ish (masalan,
    /// desktop yuzli xodimlar ro'yxatini yangilaydi). Tarmoq/parol xatosi — <see cref="TerminalException"/> otiladi.
    /// </summary>
    public async Task<TerminalPollResult> PollAsync(Func<HikTerminalClient, Task>? afterPoll = null, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return TerminalPollResult.Offline;
        var acq = await AcquireAsync(allowDiscovery: true, ct);
        if (acq is not { } a)
            return TerminalPollResult.Offline;
        using var client = a.Client;
        var info = a.Info;
        Device device = null!;
        await _runDb(async () => device = await service.EnsureTerminalDeviceAsync(info.SerialNumber, _deviceName(info)));

        var now = service.Clock.GetUtcNow();
        var events = await client.FetchFaceEventsAsync(now - LookBack, now.AddMinutes(1), service.Settings.TerminalLastSerial, ct);

        int added = 0;
        await _runDb(async () => added = await service.ApplyTerminalEventsAsync(events, device.Id, log));
        if (afterPoll is not null)
            await afterPoll(client);
        if (added > 0)
            log?.Invoke($"Terminaldan {added} yangi davomat voqeasi");
        return new TerminalPollResult(true, added, info);
    }

    /// <summary>Agent uchun qurilma nomi (desktopning standart tili — o'zbekcha lotin).</summary>
    public static string DefaultDeviceName(TerminalInfo info) =>
        string.IsNullOrWhiteSpace(info.Model) ? "Yuz terminali" : $"Yuz terminali ({info.Model})";
}
