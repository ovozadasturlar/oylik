using System.Net;
using Oylik.Contracts;

namespace Oylik.Mobile.Logic.Sync;

/// <summary>
/// Xotiradagi soxta server — haqiqiy server tayyor bo'lguncha UI'ni sinash uchun va testlarda.
/// Ilovada server manzili <see cref="DemoServer"/> ("demo") bo'lsa ishlatiladi.
/// </summary>
public sealed class FakeOylikApi(TimeProvider clock) : IOylikApi
{
    public const string DemoServer = "demo";
    public static readonly Guid DemoInstallationId = Guid.Parse("00000000-0000-0000-0000-00000000d3e0");

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, EmployeeDto> _employees = [];
    private readonly List<ServerEventDto> _events = [];
    private readonly Dictionary<string, Guid> _tokens = [];
    private long _seq;

    /// <summary>true — har so'rov "aloqa yo'q" bilan tugaydi (oflayn sinov).</summary>
    public bool Offline { get; set; }

    /// <summary>Shu manzillar "aloqa yo'q" (lokal/global almashinuvini sinash). Bir server — bir nechta manzil.</summary>
    public HashSet<string> Unreachable { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shu manzillar javob bermay osilib qoladi (timeout'ni sinash) — faqat bekor qilinganda tugaydi.</summary>
    public HashSet<string> Hanging { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shu manzillar boshqa server — token tanilmaydi (401).</summary>
    public HashSet<string> Foreign { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Haqiqiy server kabi: kod bir martalik (ikkinchi marta — 400). Eski testlar uchun default o'chiq.</summary>
    public bool SingleUseCodes { get; set; }

    /// <summary>Shu manzil so'rovlari shu vazifa tugaguncha kutadi (poyga holatlarini sinash).</summary>
    public Dictionary<string, TaskCompletionSource> Holds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Manzilga so'rov kelganda (ushlab turilishidan oldin) bajariladi — testlar aniq tartibni kutadi.</summary>
    public Dictionary<string, TaskCompletionSource> Arrived { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Manzilni ushlab turish: (so'rov keldi, qo'yib yuborish).</summary>
    public (Task Arrived, Action Release) Hold(string server)
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            Holds[server] = hold;
            Arrived[server] = arrived;
        }
        return (arrived.Task, () => hold.TrySetResult());
    }

    /// <summary>Berilsa — health shu xatoni tashlaydi (kutilmagan xato sinovi).</summary>
    public Exception? HealthThrows { get; set; }

    private readonly HashSet<string> _usedCodes = [];

    /// <summary>Qaysi manzilga qaysi so'rov ketgan: "server route".</summary>
    public List<string> Log { get; } = [];

    /// <summary>Server soati telefondan shuncha farq qiladi (soat noto'g'ri ogohlantirishini sinash).</summary>
    public TimeSpan ServerClockShift { get; set; }

    public static bool IsDemo(string? server) =>
        string.Equals(server?.Trim(), DemoServer, StringComparison.OrdinalIgnoreCase);

    public FakeOylikApi SeedDemo()
    {
        string[] names =
        [
            "Олим ака (бригадир)", "Сардор", "Нилуфар опа", "Анвар", "Олим 2 (омбор)", "Тимур",
            "Рустам ака", "Фаррух", "Гулнора опа", "Бобур", "Бахтиёр", "Санжар ака",
        ];
        var at = clock.GetUtcNow().AddDays(-1);
        for (var i = 0; i < names.Length; i++)
        {
            var id = new Guid(i + 1, 0, 0, new byte[8]);
            SetEmployee(new EmployeeDto(id, names[i], null, true, at));
        }
        return this;
    }

    public void SetEmployee(EmployeeDto e)
    {
        lock (_gate) _employees[e.Id] = e;
    }

    /// <summary>Boshqa qurilma yoki desktop qo'shgan voqea.</summary>
    public ServerEventDto AddServerEvent(Guid employeeId, AttendanceType type, DateTimeOffset at, Guid? deviceId = null)
    {
        lock (_gate)
        {
            var e = new ServerEventDto(++_seq, Guid.NewGuid(), employeeId, type, at, deviceId, clock.GetUtcNow(), false, null, null);
            _events.Add(e);
            return e;
        }
    }

    public IReadOnlyList<ServerEventDto> Events
    {
        get { lock (_gate) return _events.ToList(); }
    }

    /// <summary>Desktopdan "uzish" — keyingi so'rovlar 401.</summary>
    public void RevokeAll()
    {
        lock (_gate) _tokens.Clear();
    }

    public int Calls { get; private set; }

    private async Task CheckAsync(string server, string route, CancellationToken ct)
    {
        lock (_gate)
        {
            Calls++;
            Log.Add($"{server} {route}");
        }
        if (Offline || Unreachable.Contains(server)) throw new ApiException(null, "Oflayn (soxta server)");
        if (Hanging.Contains(server)) await Task.Delay(Timeout.Infinite, ct);
        TaskCompletionSource? hold, arrived;
        lock (_gate)
        {
            Holds.TryGetValue(server, out hold);
            Arrived.TryGetValue(server, out arrived);
        }
        arrived?.TrySetResult();
        if (hold is not null) await hold.Task.WaitAsync(ct);
    }

    private async Task<Guid> AuthAsync(ApiSession s, string route, CancellationToken ct)
    {
        await CheckAsync(s.Server, route, ct);
        if (Foreign.Contains(s.Server)) throw new ApiException(HttpStatusCode.Unauthorized, "401 Unauthorized");
        lock (_gate)
            return _tokens.TryGetValue(s.Token, out var d) ? d : throw new ApiException(HttpStatusCode.Unauthorized, "401 Unauthorized");
    }

    public async Task<HealthResponse> HealthAsync(string server, CancellationToken ct = default)
    {
        await CheckAsync(server, "health", ct);
        if (HealthThrows is { } boom) throw boom;
        return new HealthResponse("ok", clock.GetUtcNow() + ServerClockShift, "demo");
    }

    public async Task<PairDeviceResponse> PairAsync(string server, PairDeviceRequest request, CancellationToken ct = default)
    {
        await CheckAsync(server, "pair", ct);
        if (request.Code.Length != Api.PairingCodeLength || !request.Code.All(char.IsAsciiDigit) || request.Code == "000000")
            throw new ApiException(HttpStatusCode.BadRequest, "400 Kod noto'g'ri");
        if (request.Code == "429429") throw new ApiException(HttpStatusCode.TooManyRequests, "429");
        if (request.Code == "409409") throw new ApiException(HttpStatusCode.Conflict, "409 Kod ishlatilgan");
        if (request.Code == "410410") throw new ApiException(HttpStatusCode.Gone, "410 Kod muddati o'tgan");
        lock (_gate)
            if (SingleUseCodes && !_usedCodes.Add(request.Code))
                throw new ApiException(HttpStatusCode.BadRequest, "400 Kod noto'g'ri yoki muddati o'tgan");
        var deviceId = Guid.NewGuid();
        var token = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        lock (_gate) _tokens[token] = deviceId;
        return new PairDeviceResponse(deviceId, token, request.InstallationId ?? DemoInstallationId);
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(ApiSession s, DateTimeOffset? updatedSince, CancellationToken ct = default)
    {
        await AuthAsync(s, "employees", ct);
        lock (_gate)
            return _employees.Values.Where(e => updatedSince is null || e.UpdatedAt >= updatedSince).ToList(); // inklyuziv, server kabi
    }

    public async Task<PostEventsResponse> PostEventsAsync(ApiSession s, IReadOnlyList<AttendanceEventDto> events, CancellationToken ct = default)
    {
        var device = await AuthAsync(s, "events", ct);
        var accepted = new List<Guid>();
        var duplicate = new List<Guid>();
        lock (_gate)
        {
            // Haqiqiy server kabi: noma'lum xodim bo'lsa butun partiya 400.
            if (events.Any(e => !_employees.ContainsKey(e.EmployeeId)))
                throw new ApiException(HttpStatusCode.BadRequest, "400 Noma'lum xodim");
            foreach (var e in events)
            {
                if (_events.Any(x => x.Id == e.Id)) { duplicate.Add(e.Id); continue; }
                // Server body'dagi DeviceId'ni emas, tokendagi qurilmani yozadi.
                _events.Add(new ServerEventDto(++_seq, e.Id, e.EmployeeId, e.Type, e.OccurredAt, device, clock.GetUtcNow(), false, null, null));
                accepted.Add(e.Id);
            }
        }
        return new PostEventsResponse(accepted, duplicate);
    }

    public async Task VoidEventAsync(ApiSession s, Guid eventId, VoidEventRequest request, CancellationToken ct = default)
    {
        await AuthAsync(s, "void", ct);
        lock (_gate)
        {
            var i = _events.FindIndex(x => x.Id == eventId);
            if (i < 0) throw new ApiException(HttpStatusCode.NotFound, "404");
            // Bekor qilish ham yangi Seq oladi — boshqa qurilmalar kursor orqali ko'radi.
            _events[i] = _events[i] with { Seq = ++_seq, IsVoided = true, VoidedAt = clock.GetUtcNow(), VoidReason = request.Reason };
        }
    }

    public async Task<EventsPageResponse> GetEventsAsync(ApiSession s, long sinceSeq, int limit, CancellationToken ct = default)
    {
        await AuthAsync(s, "pull", ct);
        lock (_gate)
        {
            var page = _events.Where(e => e.Seq > sinceSeq).OrderBy(e => e.Seq).Take(limit).ToList();
            return new EventsPageResponse(page, page.Count > 0 ? page[^1].Seq : sinceSeq);
        }
    }
}

/// <summary>Server manzili "demo" bo'lsa — soxta, aks holda haqiqiy HTTP.</summary>
public sealed class ApiRouter(HttpOylikApi http, FakeOylikApi demo) : IOylikApi
{
    private IOylikApi Pick(string server) => FakeOylikApi.IsDemo(server) ? demo : http;

    public Task<HealthResponse> HealthAsync(string server, CancellationToken ct = default) => Pick(server).HealthAsync(server, ct);
    public Task<PairDeviceResponse> PairAsync(string server, PairDeviceRequest request, CancellationToken ct = default) => Pick(server).PairAsync(server, request, ct);
    public Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(ApiSession s, DateTimeOffset? updatedSince, CancellationToken ct = default) => Pick(s.Server).GetEmployeesAsync(s, updatedSince, ct);
    public Task<PostEventsResponse> PostEventsAsync(ApiSession s, IReadOnlyList<AttendanceEventDto> events, CancellationToken ct = default) => Pick(s.Server).PostEventsAsync(s, events, ct);
    public Task VoidEventAsync(ApiSession s, Guid eventId, VoidEventRequest request, CancellationToken ct = default) => Pick(s.Server).VoidEventAsync(s, eventId, request, ct);
    public Task<EventsPageResponse> GetEventsAsync(ApiSession s, long sinceSeq, int limit, CancellationToken ct = default) => Pick(s.Server).GetEventsAsync(s, sinceSeq, limit, ct);
}
