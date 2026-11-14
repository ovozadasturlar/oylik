using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Data.Agent;
using Oylik.Data.Terminal;

namespace Oylik.Core.Tests;

/// <summary>Bosqich 2: TerminalPoller (UI'siz tick, backoff), AgentLock (mutex), AgentRunner va agent ↔ desktop bitta baza.</summary>
public sealed class AgentTests : IDisposable
{
    private const string Url = "http://192.0.2.10";
    private const string Serial = "TESTSERIAL01";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oylik-agent-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = TestClock.Default();
    private readonly FakeTerminal _terminal = new();
    private AppPaths Paths => new(_root);

    public AgentTests() => Paths.EnsureCreated();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<PayrollService> OpenAsync(bool attendanceOnly = false)
    {
        var service = new PayrollService(Paths, _clock, attendanceOnly);
        await service.InitializeAsync(dailyBackup: false);
        return service;
    }

    /// <summary>Desktop sozlagan holat: xodim (TabelNo 1) va yoqilgan terminal (sinovda parol shifrlanmagan).</summary>
    private async Task<PayrollService> DesktopWithTerminalAsync()
    {
        var desktop = await OpenAsync();
        await desktop.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        await desktop.SaveTerminalSettingsAsync(Url, "test-pass", enabled: true, requireConfirm: false, startSerial: 0, serial: Serial);
        return desktop;
    }

    private HikTerminalClient Client(string url, string pass) => new(url, "admin", pass, new HttpClient(_terminal));

    private TerminalPoller Poller(PayrollService s, Func<string, string, CancellationToken, Task<string?>>? discover = null) =>
        new(s, clock: _clock, clientFactory: Client, discover: discover ?? ((_, _, _) => Task.FromResult<string?>(null)));

    private DateTimeOffset Ago(int minutes) => _clock.Now.AddMinutes(-minutes);

    // ---------- TerminalPoller ----------

    [Fact]
    public async Task Poller_writes_new_events_once_and_advances_cursor()
    {
        using var s = await DesktopWithTerminalAsync();
        _terminal.Events.Add((1, "1", Ago(60)));
        var poller = Poller(s);

        var r = await poller.PollAsync();
        r.Connected.Should().BeTrue();
        r.Added.Should().Be(1);
        r.Info!.SerialNumber.Should().Be(Serial);
        s.Settings.TerminalLastSerial.Should().Be(1);
        s.TerminalDevice!.LastSeenAt.Should().Be(_clock.GetUtcNow());
        s.AttendanceEvents.Should().ContainSingle(e => e.Type == AttendanceEventType.In && e.PendingPush);

        (await poller.PollAsync()).Added.Should().Be(0); // takror o'qish — idempotent
        _terminal.Events.Add((2, "1", Ago(10)));
        (await poller.PollAsync()).Added.Should().Be(1);
        s.Settings.TerminalLastSerial.Should().Be(2);
    }

    [Fact]
    public async Task Poller_backs_off_network_scan_while_terminal_is_missing_and_resets_when_found()
    {
        using var s = await DesktopWithTerminalAsync();
        _terminal.Online = false;
        var scans = 0;
        var poller = Poller(s, (_, _, _) => { scans++; return Task.FromResult<string?>(null); });

        (await poller.PollAsync()).Should().Be(TerminalPollResult.Offline);
        scans.Should().Be(1);
        await poller.PollAsync();
        scans.Should().Be(1, "keyingi skan 20 s dan keyin");

        _clock.Now += TimeSpan.FromSeconds(20);
        await poller.PollAsync();
        scans.Should().Be(2);
        _clock.Now += TimeSpan.FromSeconds(20);
        await poller.PollAsync();
        scans.Should().Be(2, "pauza 40 s ga uzaydi");
        _clock.Now += TimeSpan.FromSeconds(20);
        await poller.PollAsync();
        scans.Should().Be(3);

        for (var i = 0; i < 10; i++)
        {
            _clock.Now += TerminalPoller.DiscoverMaxDelay;
            await poller.PollAsync();
        }
        (poller.NextDiscoverAt - _clock.GetUtcNow()).Should().BeLessThanOrEqualTo(TerminalPoller.DiscoverMaxDelay, "maksimum 2 daqiqa");

        _terminal.Online = true;
        (await poller.PollAsync()).Connected.Should().BeTrue();
        poller.NextDiscoverAt.Should().Be(DateTimeOffset.MinValue, "terminal javob berdi — backoff tiklandi");
    }

    [Fact]
    public async Task Poller_finds_moved_terminal_by_serial_and_saves_new_address()
    {
        using var s = await DesktopWithTerminalAsync();
        _terminal.OnlineOnlyAt = "192.0.2.77";
        var poller = Poller(s, (serial, _, _) => Task.FromResult<string?>(serial == Serial ? "http://192.0.2.77" : null));

        var r = await poller.PollAsync();
        r.Connected.Should().BeTrue();
        s.Settings.TerminalUrl.Should().Be("http://192.0.2.77");
    }

    [Fact]
    public async Task Disabled_terminal_is_not_contacted()
    {
        using var s = await OpenAsync();
        var poller = Poller(s);
        (await poller.PollAsync()).Should().Be(TerminalPollResult.Offline);
        _terminal.Requests.Should().Be(0);
    }

    [Fact]
    public async Task Poller_writes_employees_pulled_from_other_sites_to_the_local_terminal()
    {
        using var s = await DesktopWithTerminalAsync();
        var olim = s.Employees.Single(); // TabelNo 1
        var sanjar = await s.CreateEmployeeAsync("Санжар", 5_000_000, null, null);
        var gone = await s.CreateEmployeeAsync("Сардор", 5_000_000, null, null);
        await s.UpdateEmployeeAsync(gone, gone.FullName, null, gone.MonthlySalary, gone.DailyRate, false, active: false);
        _terminal.FailUser = "2";
        var log = new List<string>();
        var poller = new TerminalPoller(s, clock: _clock, clientFactory: Client, log: log.Add,
            discover: (_, _, _) => Task.FromResult<string?>(null));

        (await poller.PushEmployeesAsync([olim, sanjar, gone])).Should().Be(2, "bitta xato qolganlarini to'xtatmaydi");
        _terminal.Users["1"].Should().Be(("Олим ака", true));
        _terminal.Users["3"].Should().Be(("Сардор", false), "nofaol — terminalda bloklanadi");
        log.Should().Contain(l => l.StartsWith("Terminalga yozib bo'lmadi (tabel 2)"));
        log.Should().NotContain(l => l.Contains("Санжар"), "logda ism yo'q");

        await s.SaveTerminalSettingsAsync(Url, "test-pass", enabled: false, requireConfirm: false);
        var before = _terminal.Requests;
        (await poller.PushEmployeesAsync([olim])).Should().Be(0);
        _terminal.Requests.Should().Be(before, "terminal o'chiq — murojaat yo'q");
    }

    // ---------- AgentLock ----------

    [Fact]
    public void Agent_lock_is_single_per_database_and_released_on_dispose()
    {
        var other = new AppPaths(Path.Combine(_root, "other"));
        AgentLock.IsHeld(Paths).Should().BeFalse();
        using (var first = AgentLock.TryAcquire(Paths))
        {
            first.Should().NotBeNull();
            AgentLock.IsHeld(Paths).Should().BeTrue();
            AgentLock.TryAcquire(Paths).Should().BeNull("shu baza uchun ikkinchi agent ishga tushmaydi");
            AgentLock.IsHeld(other).Should().BeFalse("boshqa OYLIK_HOME — boshqa mutex");
        }
        AgentLock.IsHeld(Paths).Should().BeFalse();
        AgentLock.BaseName(Paths).Should().StartWith("Oylik.TerminalPoller.");
    }

    // ---------- Agent ↔ desktop: bitta baza ----------

    [Fact]
    public async Task Agent_writes_attendance_and_desktop_picks_it_up_and_vice_versa()
    {
        using var desktop = await DesktopWithTerminalAsync();
        using var agent = new AgentRunner(Paths, _clock, pollerFactory: s => Poller(s));
        await agent.InitializeAsync();
        agent.Service!.AttendanceOnly.Should().BeTrue();

        // 1. Agent terminaldan o'qiydi → desktop bazadan oladi.
        _terminal.Events.Add((1, "1", Ago(60)));
        var tick = await agent.TickAsync();
        tick.Terminal.Added.Should().Be(1);
        tick.Synced.Should().BeFalse("server sozlanmagan — faqat lokal");
        tick.SyncError.Should().BeNull();

        desktop.AttendanceEvents.Should().BeEmpty();
        var changes = await desktop.RefreshFromStoreAsync();
        changes.Events.Should().Be(1);
        desktop.AttendanceEvents.Should().ContainSingle();
        desktop.Settings.TerminalLastSerial.Should().Be(1);
        desktop.TerminalDevice!.LastSeenAt.Should().NotBeNull();
        (await desktop.RefreshFromStoreAsync()).Any.Should().BeFalse("o'zgarish yo'q — qayta hisoblanmaydi");

        // 2. Desktop: qo'lda voqea va yangi xodim → agent keyingi tick'da oladi (serverga jo'natish uchun PendingPush).
        var olim = desktop.Employees.Single();
        var manual = await desktop.AddManualEventAsync(olim, AttendanceEventType.Out, Ago(5), "qo'lda");
        var second = await desktop.CreateEmployeeAsync("Санжар", 5_000_000, null, null);
        second.TabelNo.Should().Be(2);
        _terminal.Events.Add((2, "2", Ago(3)));

        tick = await agent.TickAsync();
        tick.Store.Events.Should().BeGreaterThanOrEqualTo(1);
        tick.Store.Employees.Should().Be(1);
        tick.Terminal.Added.Should().Be(1, "yangi xodim (TabelNo 2) agentga yetib keldi");
        agent.Service.PendingPushEvents.Select(e => e.Id).Should().Contain(manual.Id);

        // 3. Desktop sozlamani o'zgartirsa (terminal o'chirildi) — agent keyingi tick'da so'ramaydi.
        await desktop.SaveTerminalSettingsAsync(Url, "test-pass", enabled: false, requireConfirm: false);
        var before = _terminal.Requests;
        tick = await agent.TickAsync();
        tick.Store.Settings.Should().BeTrue();
        tick.Terminal.Should().Be(TerminalPollResult.Offline);
        _terminal.Requests.Should().Be(before);
    }

    [Fact]
    public async Task Agent_does_not_touch_payroll_lines()
    {
        using (var desktop = await DesktopWithTerminalAsync())
            await desktop.CreatePeriodAsync(new MonthKey(2026, 10));
        SqliteConnection.ClearAllPools();
        var before = await DumpTableAsync("Lines");

        using var agent = new AgentRunner(Paths, _clock, pollerFactory: s => Poller(s));
        _terminal.Events.Add((1, "1", Ago(60)));
        (await agent.TickAsync()).Terminal.Added.Should().Be(1);
        agent.Dispose();
        SqliteConnection.ClearAllPools();

        (await DumpTableAsync("Lines")).Should().Be(before);
    }

    [Fact]
    public async Task Agent_publishes_status_for_tray_after_each_tick()
    {
        using var desktop = await DesktopWithTerminalAsync();
        using var agent = new AgentRunner(Paths, _clock, pollerFactory: s => Poller(s));
        var published = new List<AgentStatus>();
        agent.StatusChanged += published.Add;
        agent.Status.Health.Should().Be(AgentHealth.Starting);

        _terminal.Events.Add((1, "1", Ago(24 * 60))); // kecha — bugungi songa kirmaydi
        _terminal.Events.Add((2, "1", Ago(90)));
        _terminal.Events.Add((3, "1", Ago(30)));
        await agent.TickAsync();

        var ok = agent.Status;
        published.Should().ContainSingle().Which.Should().Be(ok);
        ok.Health.Should().Be(AgentHealth.Ok);
        ok.TerminalEnabled.Should().BeTrue();
        ok.TerminalConnected.Should().BeTrue();
        ok.LastPollAt.Should().Be(_clock.GetUtcNow());
        ok.TodayEvents.Should().Be(2);
        ok.ServerConfigured.Should().BeFalse();
        ok.Language.Should().Be(desktop.Settings.Language);

        // Terminal javob bermayapti — qizil, oxirgi muvaffaqiyatli poll vaqti saqlanadi.
        _terminal.Online = false;
        _clock.Now += TimeSpan.FromMinutes(1);
        await agent.TickAsync();
        agent.Status.Health.Should().Be(AgentHealth.Error);
        agent.Status.LastPollAt.Should().Be(ok.LastPollAt);

        // Terminal o'chirildi — sariq (agent ishlayapti, lekin davomat yig'ilmaydi).
        await desktop.SaveTerminalSettingsAsync(Url, "test-pass", enabled: false, requireConfirm: false);
        await agent.TickAsync();
        agent.Status.Health.Should().Be(AgentHealth.Warning);
        published.Should().HaveCount(3);
    }

    [Fact]
    public async Task Two_processes_can_write_the_same_database_concurrently()
    {
        using var desktop = await DesktopWithTerminalAsync();
        using var agent = await OpenAsync(attendanceOnly: true);
        var olimDesktop = desktop.Employees.Single();
        var olimAgent = agent.Employees.Single();

        // Ikki alohida ulanish (har biri o'z DbContext'i) — bir vaqtda yozadi: busy timeout bilan qulf kutiladi.
        var a = Task.Run(async () =>
        {
            for (var i = 0; i < 40; i++)
                await desktop.AddManualEventAsync(olimDesktop, AttendanceEventType.In, Ago(1000 + i), null);
        });
        var b = Task.Run(async () =>
        {
            for (var i = 0; i < 40; i++)
                await agent.AddManualEventAsync(olimAgent, AttendanceEventType.Out, Ago(2000 + i), null);
        });
        await Task.WhenAll(a, b);

        using var check = await OpenAsync();
        check.AttendanceEvents.Should().HaveCount(80);
    }

    private async Task<string> DumpTableAsync(string table)
    {
        await using var c = new SqliteConnection($"Data Source={Paths.Database}");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {table} ORDER BY Id";
        await using var r = await cmd.ExecuteReaderAsync();
        var sb = new StringBuilder();
        while (await r.ReadAsync())
        {
            for (var i = 0; i < r.FieldCount; i++)
                sb.Append(r.GetValue(i)).Append('|');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Xotiradagi Hikvision ISAPI: deviceInfo (XML) va AcsEvent (JSON). <see cref="Online"/>=false — ulanib bo'lmaydi.</summary>
    private sealed class FakeTerminal : HttpMessageHandler
    {
        public bool Online { get; set; } = true;

        /// <summary>Berilsa — faqat shu host javob beradi (IP o'zgargan terminal).</summary>
        public string? OnlineOnlyAt { get; set; }

        public List<(long Serial, string EmployeeNo, DateTimeOffset Time)> Events { get; } = [];
        public int Requests { get; private set; }

        /// <summary>Terminalga yozilgan xodimlar: employeeNo → (ism, faol).</summary>
        public Dictionary<string, (string Name, bool Enabled)> Users { get; } = [];

        /// <summary>Berilsa — shu employeeNo'ni yozish xato beradi.</summary>
        public string? FailUser { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var host = request.RequestUri!.Host;
            if (!Online || (OnlineOnlyAt is not null && host != OnlineOnlyAt))
                throw new HttpRequestException("unreachable");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/ISAPI/System/deviceInfo", StringComparison.Ordinal))
                return Task.FromResult(Text($"<DeviceInfo><model>DS-TEST</model><serialNumber>{Serial}</serialNumber><firmwareVersion>V1</firmwareVersion><deviceName>T</deviceName></DeviceInfo>", "application/xml"));
            if (path.EndsWith("/ISAPI/AccessControl/AcsEvent", StringComparison.Ordinal))
            {
                var list = Events.Select(e => new { serialNo = e.Serial, employeeNoString = e.EmployeeNo, name = "X", time = e.Time.ToString("yyyy-MM-ddTHH:mm:sszzz") }).ToList();
                var json = JsonSerializer.Serialize(new { AcsEvent = new { numOfMatches = list.Count, responseStatusStrg = "OK", InfoList = list } });
                return Task.FromResult(Text(json, "application/json"));
            }
            if (path.EndsWith("/ISAPI/AccessControl/UserInfo/Search", StringComparison.Ordinal))
            {
                using var q = JsonDocument.Parse(request.Content!.ReadAsStringAsync(ct).Result);
                var no = q.RootElement.GetProperty("UserInfoSearchCond").GetProperty("EmployeeNoList")[0].GetProperty("employeeNo").GetString()!;
                return Task.FromResult(Text(JsonSerializer.Serialize(new { UserInfoSearch = new { numOfMatches = Users.ContainsKey(no) ? 1 : 0 } }), "application/json"));
            }
            if (path.EndsWith("/ISAPI/AccessControl/UserInfo/Record", StringComparison.Ordinal)
                || path.EndsWith("/ISAPI/AccessControl/UserInfo/Modify", StringComparison.Ordinal))
            {
                using var q = JsonDocument.Parse(request.Content!.ReadAsStringAsync(ct).Result);
                var u = q.RootElement.GetProperty("UserInfo");
                var no = u.GetProperty("employeeNo").GetString()!;
                if (no == FailUser)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") });
                Users[no] = (u.GetProperty("name").GetString()!, u.GetProperty("Valid").GetProperty("enable").GetBoolean());
                return Task.FromResult(Text("{}", "application/json"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Text(string body, string type) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type) };
    }
}
