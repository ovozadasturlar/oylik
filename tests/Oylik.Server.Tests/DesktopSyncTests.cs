using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Oylik.Contracts;
using Oylik.Data;
using Oylik.Data.Sync;
using Domain = Oylik.Core.Domain;

namespace Oylik.Server.Tests;

/// <summary>Desktop sinxronizatsiyasi (Oylik.Data.Sync) haqiqiy server bilan (WebApplicationFactory).</summary>
public sealed class DesktopSyncTests : IAsyncLifetime
{
    private static readonly TimeSpan Uz = TimeSpan.FromHours(5);
    private readonly TestServer _server = new();
    private readonly string _home = Path.Combine(Path.GetTempPath(), "oylik-sync-tests-" + Guid.NewGuid().ToString("N"));
    private PayrollService _desktop = null!;
    private ServerSync _sync = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var paths = new AppPaths(_home);
        paths.EnsureCreated();
        _desktop = new PayrollService(paths, _server.Clock);
        await _desktop.InitializeAsync(dailyBackup: false);
        _sync = new ServerSync(_desktop);
        _http = _server.CreateClient();
    }

    public Task DisposeAsync()
    {
        _desktop.Dispose();
        _server.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_home, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private ServerClient Client() => new(_desktop.Settings.ServerUrl!, _desktop.Settings.ApiKey, _http);

    /// <summary>Desktop o'z InstallationId'si bilan ro'yxatdan o'tadi va ulanishni saqlaydi.</summary>
    private async Task ConnectAsync()
    {
        var url = _http.BaseAddress!.ToString();
        var res = await new ServerClient(url, null, _http).RegisterAsync("Korxona", TestServer.RegistrationKey, _desktop.Settings.InstallationId);
        res.InstallationId.Should().Be(_desktop.Settings.InstallationId);
        await _desktop.SaveServerConnectionAsync(null, url, res.ApiKey);
    }

    private async Task<(HttpClient Phone, Guid DeviceId)> PairPhoneAsync(string name = "Qorovul-1")
    {
        var code = await Client().CreatePairingCodeAsync(_desktop.Settings.InstallationId);
        var payload = new PairingQrPayload(_desktop.Settings.ServerUrl!, _desktop.Settings.InstallationId, code.Code);
        var res = await _server.CreateClient().PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(payload.InstallationId, payload.Code, name), Api.Json);
        var body = (await res.Content.ReadFromJsonAsync<PairDeviceResponse>(Api.Json))!;
        return (_server.Bearer(body.DeviceToken), body.DeviceId);
    }

    private static AttendanceEventDto Tap(Guid employeeId, AttendanceType type, int day, int hour, int minute = 0) =>
        new(Guid.NewGuid(), employeeId, type, new DateTimeOffset(2026, 10, day, hour, minute, 0, Uz), null);

    [Fact]
    public async Task Employees_reach_phone_and_offline_taps_arrive_once()
    {
        var olim = await _desktop.CreateEmployeeAsync("Олим ака (тест)", 6_000_000, null, null);
        var sardor = await _desktop.CreateEmployeeAsync("Сардор", 7_000_000, null, null);
        await ConnectAsync();
        await _sync.RunAsync(Client());

        var (phone, deviceId) = await PairPhoneAsync();
        var employees = (await phone.GetFromJsonAsync<List<EmployeeDto>>(Api.Routes.Employees, Api.Json))!;
        employees.Select(e => e.FullName).Should().BeEquivalentTo("Олим ака (тест)", "Сардор");
        employees.Select(e => e.TabelNo).Should().BeEquivalentTo([olim.TabelNo.ToString(), sardor.TabelNo.ToString()], "Bosqich 3: tabel raqami ham ko'chadi");

        // 10 ta Keldi/Ketdi oflayn yig'ilgan, internet qaytganda — ikki marta jo'natiladi (outbox takrori)
        var taps = new List<AttendanceEventDto>();
        for (int day = 5; day <= 9; day++)
        {
            taps.Add(Tap(olim.Id, AttendanceType.In, day, 8, 2));
            taps.Add(Tap(olim.Id, AttendanceType.Out, day, 17, 32));
        }
        (await phone.PostAsJsonAsync(Api.Routes.Events, taps, Api.Json)).EnsureSuccessStatusCode();
        (await phone.PostAsJsonAsync(Api.Routes.Events, taps, Api.Json)).EnsureSuccessStatusCode();

        await _sync.RunAsync(Client());
        await _sync.RunAsync(Client());

        _desktop.AttendanceEvents.Should().HaveCount(10);
        _desktop.AttendanceEvents.Should().OnlyContain(e => e.DeviceId == deviceId && e.ServerSeq != null && !e.PendingPush);
        var summary = await _desktop.GetMonthSummariesAsync(new Domain.MonthKey(2026, 10));
        summary[olim.Id].Should().Be(new Domain.AttendanceMonthSummary(5, 47.5m, 5.94m));
        summary.Should().NotContainKey(sardor.Id);

        _desktop.Devices.Should().ContainSingle().Which.Name.Should().Be("Qorovul-1");
        _desktop.Settings.LastEventSeq.Should().Be(_desktop.AttendanceEvents.Max(e => e.ServerSeq!.Value));
    }

    [Fact]
    public async Task Night_shift_from_phone_counts_for_the_day_of_in()
    {
        var emp = await _desktop.CreateEmployeeAsync("Qorovul", 4_000_000, null, null);
        await ConnectAsync();
        await _sync.RunAsync(Client());
        var (phone, _) = await PairPhoneAsync();

        await phone.PostAsJsonAsync(Api.Routes.Events, new[] { Tap(emp.Id, AttendanceType.In, 12, 22), Tap(emp.Id, AttendanceType.Out, 13, 6) }, Api.Json);
        await _sync.RunAsync(Client());

        var day = _desktop.AnalyzeAttendance()[emp.Id].Day(new DateOnly(2026, 10, 12))!;
        day.Hours.Should().Be(8m);
    }

    [Fact]
    public async Task Manual_desktop_event_and_void_reach_server_and_phone()
    {
        var emp = await _desktop.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        await ConnectAsync();
        await _sync.RunAsync(Client());
        var (phone, _) = await PairPhoneAsync();

        var outEvent = await _desktop.AddManualEventAsync(emp, Domain.AttendanceEventType.Out, new DateTimeOffset(2026, 10, 12, 18, 0, 0, Uz), "Ketdi bosilmagan");
        var wrong = await _desktop.AddManualEventAsync(emp, Domain.AttendanceEventType.In, new DateTimeOffset(2026, 10, 12, 9, 0, 0, Uz), null);
        await _desktop.VoidEventAsync(wrong, "Xato");
        await _sync.RunAsync(Client());

        _desktop.PendingPushEvents.Should().BeEmpty();
        var page = (await phone.GetFromJsonAsync<EventsPageResponse>($"{Api.Routes.Events}?sinceSeq=0", Api.Json))!;
        page.Events.Single(e => e.Id == outEvent.Id).DeviceId.Should().BeNull();
        page.Events.Single(e => e.Id == wrong.Id).Should().Match<ServerEventDto>(e => e.IsVoided && e.VoidReason == "Xato");
        _desktop.AttendanceEvents.Should().OnlyContain(e => e.ServerSeq != null);
    }

    [Fact]
    public async Task Phone_void_is_pulled_to_desktop()
    {
        var emp = await _desktop.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        await ConnectAsync();
        await _sync.RunAsync(Client());
        var (phone, _) = await PairPhoneAsync();
        var tap = Tap(emp.Id, AttendanceType.In, 12, 8);
        await phone.PostAsJsonAsync(Api.Routes.Events, new[] { tap }, Api.Json);
        await _sync.RunAsync(Client());

        await phone.PostAsJsonAsync(Api.Routes.VoidEvent(tap.Id), new VoidEventRequest("Noto'g'ri xodim"), Api.Json);
        await _sync.RunAsync(Client());

        _desktop.AttendanceEvents.Single().Should().Match<Domain.AttendanceEvent>(e => e.IsVoided && e.VoidReason == "Noto'g'ri xodim");
    }

    [Fact]
    public async Task Offline_desktop_keeps_pending_and_sends_later()
    {
        var emp = await _desktop.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        await ConnectAsync();
        await _sync.RunAsync(Client());
        var manual = await _desktop.AddManualEventAsync(emp, Domain.AttendanceEventType.In, new DateTimeOffset(2026, 10, 12, 8, 0, 0, Uz), null);

        var offline = new ServerClient("http://127.0.0.1:9", _desktop.Settings.ApiKey);
        var act = () => _sync.RunAsync(offline);
        await act.Should().ThrowAsync<HttpRequestException>();
        _desktop.PendingPushEvents.Should().Equal(manual);

        await _sync.RunAsync(Client());
        _desktop.PendingPushEvents.Should().BeEmpty();
        manual.ServerSeq.Should().NotBeNull();
    }

    [Fact]
    public async Task Renamed_or_deactivated_employee_is_updated_on_server()
    {
        var emp = await _desktop.CreateEmployeeAsync("Олим ака", 6_000_000, null, null);
        await ConnectAsync();
        await _sync.RunAsync(Client());
        _sync.EmployeesDirty.Should().BeFalse();

        await _desktop.UpdateEmployeeAsync(emp, "Олим ака (тест)", "Sex", 6_000_000, 200_000, false, false);
        _sync.EmployeesDirty = true; // SyncService buni EmployeesChanged hodisasida qiladi
        await _sync.RunAsync(Client());

        var (phone, _) = await PairPhoneAsync();
        var list = (await phone.GetFromJsonAsync<List<EmployeeDto>>(Api.Routes.Employees, Api.Json))!;
        list.Single().Should().Match<EmployeeDto>(e => e.FullName == "Олим ака (тест)" && e.Note == "Sex" && !e.IsActive);
    }

    [Fact]
    public async Task Schedule_employee_sends_max_shift_to_phone_h14()
    {
        var guard = await _desktop.CreateEmployeeAsync("Qorovul", 4_000_000, null, null);
        var office = await _desktop.CreateEmployeeAsync("Hisobchi", 5_000_000, null, null);
        await _desktop.SetPayModeAsync(guard, Domain.PayMode.Schedule);
        await _desktop.AssignScheduleAsync(guard, _desktop.FindSchedule(Domain.WorkSchedule.SampleDailyId), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));
        await ConnectAsync();
        await _sync.RunAsync(Client());

        var (phone, _) = await PairPhoneAsync();
        var list = (await phone.GetFromJsonAsync<List<EmployeeDto>>(Api.Routes.Employees, Api.Json))!;
        list.Single(e => e.Id == guard.Id).MaxShiftMinutes.Should().Be(28 * 60, "sutkalik 24 s + 4 s");
        list.Single(e => e.Id == office.Id).MaxShiftMinutes.Should().BeNull("Daily — 16 soat");

        // shablon o'zgarsa — xodim qayta yuboriladi
        var daily = _desktop.FindSchedule(Domain.WorkSchedule.SampleDailyId)!;
        await _desktop.UpdateScheduleAsync(daily, new Domain.WorkSchedule
        {
            Name = daily.Name, Kind = daily.Kind, CycleWorkDays = 1, CycleOffDays = 1, StartTime = daily.StartTime, DurationMinutes = 20 * 60,
        });
        _sync.EmployeesDirty.Should().BeFalse("SyncService buni EmployeesChanged hodisasida qiladi");
        _sync.EmployeesDirty = true;
        await _sync.RunAsync(Client());
        (await phone.GetFromJsonAsync<List<EmployeeDto>>(Api.Routes.Employees, Api.Json))!.Single(e => e.Id == guard.Id)
            .MaxShiftMinutes.Should().Be(24 * 60);
    }
}
