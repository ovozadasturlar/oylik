using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Oylik.Contracts;

namespace Oylik.Server.Tests;

public sealed class ServerApiTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<T>(Api.Json))!;
    }

    private static Task<HttpResponseMessage> PostEvents(HttpClient client, params AttendanceEventDto[] events) =>
        client.PostAsJsonAsync(Api.Routes.Events, events, Api.Json);

    private static async Task<EventsPageResponse> GetEvents(HttpClient client, long sinceSeq, int? limit = null) =>
        await Read<EventsPageResponse>(await client.GetAsync($"{Api.Routes.Events}?sinceSeq={sinceSeq}" + (limit is { } l ? $"&limit={l}" : "")));

    private AttendanceEventDto Event(Guid employeeId, AttendanceType type, int hour, int minute = 0, Guid? deviceId = null) =>
        new(Guid.NewGuid(), employeeId, type, new DateTimeOffset(2026, 10, 12, hour, minute, 0, TimeSpan.FromHours(5)), deviceId);

    // ---------- Health, ro'yxatdan o'tish ----------

    [Fact]
    public async Task Health_is_public()
    {
        var health = await Read<HealthResponse>(await _server.CreateClient().GetAsync(Api.Routes.Health));
        health.Status.Should().Be("ok");
        health.ServerTime.Should().Be(_server.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Register_uses_given_installation_id_and_rejects_duplicates_and_wrong_key()
    {
        var client = _server.CreateClient();
        var id = Guid.NewGuid();

        var ok = await Read<RegisterInstallationResponse>(await client.PostAsJsonAsync(Api.Routes.Installations,
            new RegisterInstallationRequest("Korxona", TestServer.RegistrationKey, id), Api.Json));
        ok.InstallationId.Should().Be(id);
        ok.ApiKey.Should().HaveLength(43);

        var again = await client.PostAsJsonAsync(Api.Routes.Installations, new RegisterInstallationRequest("Korxona", TestServer.RegistrationKey, id), Api.Json);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var wrong = await client.PostAsJsonAsync(Api.Routes.Installations, new RegisterInstallationRequest("Begona", "xato", Guid.NewGuid()), Api.Json);
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var generated = await Read<RegisterInstallationResponse>(await client.PostAsJsonAsync(Api.Routes.Installations,
            new RegisterInstallationRequest("Yangi", TestServer.RegistrationKey), Api.Json));
        generated.InstallationId.Should().NotBe(Guid.Empty).And.NotBe(id);
    }

    [Fact]
    public async Task Api_key_is_stored_hashed()
    {
        var client = _server.CreateClient();
        var res = await Read<RegisterInstallationResponse>(await client.PostAsJsonAsync(Api.Routes.Installations,
            new RegisterInstallationRequest("Korxona", TestServer.RegistrationKey, Guid.NewGuid()), Api.Json));

        using var scope = _server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDb>();
        var key = db.ApiKeys.Single();
        key.KeyHash.Should().NotBe(res.ApiKey).And.Be(Secrets.Hash(res.ApiKey));
        key.InstallationId.Should().Be(res.InstallationId);
        key.SiteIndex.Should().Be(0, "ro'yxatdan o'tgan mashina — asosiy (tabel 1..)");
    }

    // ---------- Bog'lash ----------

    [Fact]
    public async Task Pair_with_qr_installation_id_returns_token_and_installation()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var codeRes = await Read<PairingCodeResponse>(await desktop.PostAsync(Api.Routes.PairingCodes(installationId), null));
        codeRes.Code.Should().MatchRegex("^[0-9]{6}$");
        codeRes.ExpiresAt.Should().Be(_server.Clock.GetUtcNow() + Api.PairingCodeLifetime);

        var pair = await Read<PairDeviceResponse>(await _server.CreateClient().PostAsJsonAsync(Api.Routes.PairDevice,
            new PairDeviceRequest(installationId, codeRes.Code, "Qorovul-1"), Api.Json));
        pair.InstallationId.Should().Be(installationId);

        var devices = await Read<List<DeviceDto>>(await desktop.GetAsync(Api.Routes.Devices));
        devices.Should().ContainSingle().Which.Should().Match<DeviceDto>(d => d.Id == pair.DeviceId && d.Name == "Qorovul-1" && d.IsActive);

        // telefon token bilan ishlay oladi
        (await _server.Bearer(pair.DeviceToken).GetAsync(Api.Routes.Employees)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pair_manually_without_installation_id_finds_installation_by_code()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        await _server.RegisterAsync("Boshqa");
        var code = await _server.PairingCodeAsync(desktop, installationId);

        var pair = await Read<PairDeviceResponse>(await _server.CreateClient().PostAsJsonAsync(Api.Routes.PairDevice,
            new PairDeviceRequest(null, code, "Qorovul-2"), Api.Json));
        pair.InstallationId.Should().Be(installationId);
    }

    [Fact]
    public async Task Pair_code_is_single_use_expires_and_needs_matching_installation()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var client = _server.CreateClient();

        var code = await _server.PairingCodeAsync(desktop, installationId);
        (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(Guid.NewGuid(), code, "X"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(installationId, code, "X"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(installationId, code, "Y"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict); // ishlatilgan → 409

        var expiring = await _server.PairingCodeAsync(desktop, installationId);
        _server.Clock.Advance(Api.PairingCodeLifetime + TimeSpan.FromSeconds(1));
        (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(null, expiring, "Z"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.Gone); // muddati o'tgan → 410
    }

    [Fact]
    public async Task Pair_unknown_code_is_400_and_used_code_entered_manually_is_409()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var client = _server.CreateClient();
        var code = await _server.PairingCodeAsync(desktop, installationId);
        var other = code == "999999" ? "999998" : "999999";

        (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(null, other, "X"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(null, code, "X"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var used = await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(null, code, "Y"), Api.Json);
        used.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await used.Content.ReadAsStringAsync()).Should().Contain("ishlatilgan");
    }

    [Fact]
    public async Task Pair_is_rate_limited_per_ip()
    {
        var client = _server.CreateClient();
        for (int i = 0; i < 5; i++)
        {
            (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(null, "000000", "X"), Api.Json))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        (await client.PostAsJsonAsync(Api.Routes.PairDevice, new PairDeviceRequest(null, "000000", "X"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Pairing_code_for_other_installation_is_forbidden()
    {
        var (desktop, _) = await _server.RegisterAsync();
        var (_, other) = await _server.RegisterAsync("Boshqa");
        (await desktop.PostAsync(Api.Routes.PairingCodes(other), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- Auth ----------

    [Fact]
    public async Task Wrong_or_missing_credentials_are_401()
    {
        var anonymous = _server.CreateClient();
        (await anonymous.GetAsync(Api.Routes.Events)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _server.Bearer("notogri-token").GetAsync(Api.Routes.Events)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var badKey = _server.CreateClient();
        badKey.DefaultRequestHeaders.Add(Api.ApiKeyHeader, "notogri");
        (await badKey.GetAsync(Api.Routes.Devices)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // telefon desktop endpointlariga kira olmaydi
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        (await phone.GetAsync(Api.Routes.Devices)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await phone.PutAsJsonAsync(Api.Routes.Employees, Array.Empty<EmployeeDto>(), Api.Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivated_device_gets_401()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, deviceId) = await _server.PairAsync(desktop, installationId);

        var dto = await Read<DeviceDto>(await desktop.PostAsync(Api.Routes.DeactivateDevice(deviceId), null));
        dto.IsActive.Should().BeFalse();
        (await phone.GetAsync(Api.Routes.Employees)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Device_last_seen_is_updated()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        _server.Clock.Advance(TimeSpan.FromMinutes(30));
        await phone.GetAsync(Api.Routes.Employees);

        var devices = await Read<List<DeviceDto>>(await desktop.GetAsync(Api.Routes.Devices));
        devices.Single().LastSeenAt.Should().Be(_server.Clock.GetUtcNow());
    }

    // ---------- Xodimlar ----------

    [Fact]
    public async Task Employees_put_is_upsert_only_and_get_supports_updated_since()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var a = TestServer.Employee("Олим ака");
        var b = TestServer.Employee("Сардор");
        await TestServer.PutEmployeesAsync(desktop, a, b);

        var all = await Read<List<EmployeeDto>>(await phone.GetAsync(Api.Routes.Employees));
        all.Select(e => e.FullName).Should().BeEquivalentTo("Олим ака", "Сардор");
        var firstSync = all.Max(e => e.UpdatedAt);

        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await TestServer.PutEmployeesAsync(desktop, a with { FullName = "Олим ака (тест)" }); // b ro'yxatda yo'q — o'zgarmaydi

        var since = Uri.EscapeDataString(firstSync.AddTicks(1).ToString("O"));
        var changed = await Read<List<EmployeeDto>>(await phone.GetAsync($"{Api.Routes.Employees}?updatedSince={since}"));
        changed.Should().ContainSingle().Which.FullName.Should().Be("Олим ака (тест)");
        (await Read<List<EmployeeDto>>(await phone.GetAsync(Api.Routes.Employees))).Single(e => e.Id == b.Id).IsActive
            .Should().BeTrue("Bosqich 3: boshqa zavod xodimi ro'yxatda yo'qligi uchun nofaol qilinmaydi");

        // nofaol — faqat IsActive=false push orqali
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await TestServer.PutEmployeesAsync(desktop, b with { IsActive = false });
        (await Read<List<EmployeeDto>>(await phone.GetAsync(Api.Routes.Employees))).Single(e => e.Id == b.Id).IsActive.Should().BeFalse();

        // o'zgarmagan ro'yxat — hech narsa yangilanmaydi
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        var now = Uri.EscapeDataString(_server.Clock.GetUtcNow().ToString("O"));
        await TestServer.PutEmployeesAsync(desktop, a with { FullName = "Олим ака (тест)" }, b with { IsActive = false });
        (await Read<List<EmployeeDto>>(await phone.GetAsync($"{Api.Routes.Employees}?updatedSince={now}"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Employees_carry_tabel_number_and_old_clients_do_not_clear_it()
    {
        var (desktop, _) = await _server.RegisterAsync();
        var a = TestServer.Employee("Олим ака") with { TabelNo = " 7 " };
        await TestServer.PutEmployeesAsync(desktop, a);
        var first = (await Read<List<EmployeeDto>>(await desktop.GetAsync(Api.Routes.Employees))).Single();
        first.TabelNo.Should().Be("7");

        // TabelNo yubormaydigan eski desktop — mavjud raqam saqlanadi, o'zgarish sanalmaydi
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await TestServer.PutEmployeesAsync(desktop, a with { TabelNo = null });
        var same = (await Read<List<EmployeeDto>>(await desktop.GetAsync(Api.Routes.Employees))).Single();
        same.TabelNo.Should().Be("7");
        same.UpdatedAt.Should().Be(first.UpdatedAt);

        // raqam o'zgarsa — UpdatedAt yangilanadi (boshqa zavod pull'da oladi)
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        await TestServer.PutEmployeesAsync(desktop, a with { TabelNo = "8" });
        var moved = (await Read<List<EmployeeDto>>(await desktop.GetAsync(Api.Routes.Employees))).Single();
        moved.TabelNo.Should().Be("8");
        moved.UpdatedAt.Should().BeAfter(first.UpdatedAt);
    }

    [Fact]
    public async Task Employees_are_isolated_per_installation()
    {
        var (desktop1, _) = await _server.RegisterAsync();
        var (desktop2, _) = await _server.RegisterAsync("Boshqa");
        await TestServer.PutEmployeesAsync(desktop1, TestServer.Employee("Олим ака"));

        (await Read<List<EmployeeDto>>(await desktop2.GetAsync(Api.Routes.Employees))).Should().BeEmpty();
    }

    // ---------- Voqealar ----------

    [Fact]
    public async Task Events_are_idempotent_by_id()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var emp = TestServer.Employee("Олим ака");
        await TestServer.PutEmployeesAsync(desktop, emp);

        var inn = Event(emp.Id, AttendanceType.In, 8, 2);
        var outt = Event(emp.Id, AttendanceType.Out, 17, 32);

        var first = await Read<PostEventsResponse>(await PostEvents(phone, inn));
        first.Accepted.Should().Equal(inn.Id);
        first.Duplicate.Should().BeEmpty();

        var second = await Read<PostEventsResponse>(await PostEvents(phone, inn, outt));
        second.Accepted.Should().Equal(outt.Id);
        second.Duplicate.Should().Equal(inn.Id);

        var page = await GetEvents(desktop, 0);
        page.Events.Should().HaveCount(2);
        page.Events.Select(e => e.Id).Should().Equal(inn.Id, outt.Id);
        page.LastSeq.Should().Be(page.Events[^1].Seq);
    }

    [Fact]
    public async Task First_ever_batch_with_many_events_gets_sequential_seqs()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var emp = TestServer.Employee("Олим ака");
        await TestServer.PutEmployeesAsync(desktop, emp);

        var res = await Read<PostEventsResponse>(await PostEvents(phone, Event(emp.Id, AttendanceType.In, 8), Event(emp.Id, AttendanceType.Out, 17), Event(emp.Id, AttendanceType.In, 18)));
        res.Accepted.Should().HaveCount(3);
        (await GetEvents(desktop, 0)).Events.Select(e => e.Seq).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Event_offset_and_received_at_are_preserved()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var emp = TestServer.Employee("Олим ака");
        await TestServer.PutEmployeesAsync(desktop, emp);
        var inn = Event(emp.Id, AttendanceType.In, 8, 2);
        await PostEvents(phone, inn);

        var e = (await GetEvents(desktop, 0)).Events.Single();
        e.OccurredAt.Should().Be(inn.OccurredAt);
        e.OccurredAt.Offset.Should().Be(TimeSpan.FromHours(5));
        e.ReceivedAt.Should().Be(_server.Clock.GetUtcNow());
        e.Type.Should().Be(AttendanceType.In);
    }

    [Fact]
    public async Task Phone_event_device_id_comes_from_token_desktop_event_has_none()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, deviceId) = await _server.PairAsync(desktop, installationId);
        var emp = TestServer.Employee("Олим ака");
        await TestServer.PutEmployeesAsync(desktop, emp);

        var spoofed = Event(emp.Id, AttendanceType.In, 8, deviceId: Guid.NewGuid());
        var manual = Event(emp.Id, AttendanceType.Out, 17, deviceId: deviceId);
        await Read<PostEventsResponse>(await PostEvents(phone, spoofed));
        await Read<PostEventsResponse>(await PostEvents(desktop, manual));

        var events = (await GetEvents(phone, 0)).Events;
        events.Single(e => e.Id == spoofed.Id).DeviceId.Should().Be(deviceId);
        events.Single(e => e.Id == manual.Id).DeviceId.Should().BeNull();
    }

    [Fact]
    public async Task Event_for_unknown_or_foreign_employee_is_rejected()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var (otherDesktop, _) = await _server.RegisterAsync("Boshqa");
        var foreign = TestServer.Employee("Begona");
        await TestServer.PutEmployeesAsync(otherDesktop, foreign);
        var own = TestServer.Employee("Олим ака");
        await TestServer.PutEmployeesAsync(desktop, own);

        (await PostEvents(phone, Event(foreign.Id, AttendanceType.In, 8))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostEvents(desktop, Event(Guid.NewGuid(), AttendanceType.In, 8))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // aralash batch ham butunlay rad etiladi
        (await PostEvents(phone, Event(own.Id, AttendanceType.In, 8), Event(foreign.Id, AttendanceType.In, 8))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetEvents(desktop, 0)).Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Since_seq_cursor_pages_through_events_per_installation()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var (otherDesktop, _) = await _server.RegisterAsync("Boshqa");
        var emp = TestServer.Employee("Олим ака");
        var otherEmp = TestServer.Employee("Begona");
        await TestServer.PutEmployeesAsync(desktop, emp);
        await TestServer.PutEmployeesAsync(otherDesktop, otherEmp);

        await PostEvents(otherDesktop, Event(otherEmp.Id, AttendanceType.In, 7));
        var events = Enumerable.Range(0, 10).Select(i => Event(emp.Id, i % 2 == 0 ? AttendanceType.In : AttendanceType.Out, 8 + i)).ToArray();
        await Read<PostEventsResponse>(await PostEvents(phone, events));

        var page1 = await GetEvents(desktop, 0, limit: 4);
        page1.Events.Should().HaveCount(4);
        var page2 = await GetEvents(desktop, page1.LastSeq, limit: 4);
        var page3 = await GetEvents(desktop, page2.LastSeq, limit: 4);
        page3.Events.Should().HaveCount(2);
        var empty = await GetEvents(desktop, page3.LastSeq);
        empty.Events.Should().BeEmpty();
        empty.LastSeq.Should().Be(page3.LastSeq);

        page1.Events.Concat(page2.Events).Concat(page3.Events).Select(e => e.Id).Should().Equal(events.Select(e => e.Id));
        page1.Events.Concat(page2.Events).Concat(page3.Events).Select(e => e.Seq).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Void_gets_new_seq_so_cursor_sees_it()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var emp = TestServer.Employee("Олим ака");
        await TestServer.PutEmployeesAsync(desktop, emp);
        var inn = Event(emp.Id, AttendanceType.In, 8);
        await PostEvents(phone, inn);
        var firstSeq = (await GetEvents(desktop, 0)).Events.Single().Seq;

        (await phone.PostAsJsonAsync(Api.Routes.VoidEvent(inn.Id), new VoidEventRequest(" "), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        var voided = await Read<ServerEventDto>(await phone.PostAsJsonAsync(Api.Routes.VoidEvent(inn.Id), new VoidEventRequest("Xato bosildi"), Api.Json));
        voided.IsVoided.Should().BeTrue();
        voided.Seq.Should().BeGreaterThan(firstSeq);

        var page = await GetEvents(desktop, firstSeq);
        page.Events.Should().ContainSingle().Which.Should().Match<ServerEventDto>(e =>
            e.Id == inn.Id && e.IsVoided && e.VoidReason == "Xato bosildi" && e.VoidedAt == _server.Clock.GetUtcNow());

        // takror void — zararsiz, Seq o'zgarmaydi
        var again = await Read<ServerEventDto>(await desktop.PostAsJsonAsync(Api.Routes.VoidEvent(inn.Id), new VoidEventRequest("yana"), Api.Json));
        again.Seq.Should().Be(voided.Seq);
        again.VoidReason.Should().Be("Xato bosildi");
    }

    [Fact]
    public async Task Void_of_other_installation_event_is_404()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var (phone, _) = await _server.PairAsync(desktop, installationId);
        var (otherDesktop, _) = await _server.RegisterAsync("Boshqa");
        var emp = TestServer.Employee("Олим ака");
        await TestServer.PutEmployeesAsync(desktop, emp);
        var inn = Event(emp.Id, AttendanceType.In, 8);
        await PostEvents(phone, inn);

        (await otherDesktop.PostAsJsonAsync(Api.Routes.VoidEvent(inn.Id), new VoidEventRequest("x"), Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- H14: MaxShiftMinutes ----------

    [Fact]
    public async Task Employee_max_shift_minutes_round_trips_and_change_bumps_updated_at()
    {
        var (desktop, installationId) = await _server.RegisterAsync();
        var id = Guid.NewGuid();
        await TestServer.PutEmployeesAsync(desktop, new EmployeeDto(id, "Qorovul", null, true, DateTimeOffset.UtcNow, 28 * 60));
        var (phone, _) = await _server.PairAsync(desktop, installationId);

        var first = (await phone.GetFromJsonAsync<List<EmployeeDto>>(Api.Routes.Employees, Api.Json))!.Single();
        first.MaxShiftMinutes.Should().Be(28 * 60);

        _server.Clock.Advance(TimeSpan.FromMinutes(5));
        await TestServer.PutEmployeesAsync(desktop, new EmployeeDto(id, "Qorovul", null, true, DateTimeOffset.UtcNow, null));
        var since = Uri.EscapeDataString(first.UpdatedAt.AddSeconds(1).ToString("O"));
        var changed = (await phone.GetFromJsonAsync<List<EmployeeDto>>($"{Api.Routes.Employees}?updatedSince={since}", Api.Json))!;
        changed.Should().ContainSingle().Which.MaxShiftMinutes.Should().BeNull("faqat chegara o'zgarsa ham telefonga qayta boradi");
    }

    [Fact]
    public async Task Employee_max_shift_minutes_is_validated_and_old_payload_means_null()
    {
        var (desktop, _) = await _server.RegisterAsync();
        (await desktop.PutAsJsonAsync(Api.Routes.Employees, new[] { new EmployeeDto(Guid.NewGuid(), "X", null, true, DateTimeOffset.UtcNow, 0) }, Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await desktop.PutAsJsonAsync(Api.Routes.Employees, new[] { new EmployeeDto(Guid.NewGuid(), "X", null, true, DateTimeOffset.UtcNow, 49 * 60) }, Api.Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // eski desktop: maydon yo'q
        var old = new[] { new { id = Guid.NewGuid(), fullName = "Eski", note = (string?)null, isActive = true, updatedAt = DateTimeOffset.UtcNow } };
        (await desktop.PutAsJsonAsync(Api.Routes.Employees, old, Api.Json)).EnsureSuccessStatusCode();
        (await desktop.GetFromJsonAsync<List<EmployeeDto>>(Api.Routes.Employees, Api.Json))!.Single().MaxShiftMinutes.Should().BeNull();
    }
}
