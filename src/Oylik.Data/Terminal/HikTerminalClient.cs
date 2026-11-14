using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Oylik.Data.Terminal;

/// <summary>Terminal ma'lumoti (ISAPI /System/deviceInfo).</summary>
public sealed record TerminalInfo(string Model, string SerialNumber, string Firmware, string DeviceName);

/// <summary>
/// Yuz tanish voqeasi (ISAPI AcsEvent, major=5 minor=75). <see cref="SerialNo"/> — qurilmada o'suvchi noyob raqam
/// (idempotentlik kaliti/kursor). <see cref="EmployeeNo"/> — terminaldagi "Xodim ID" (bizda <c>Employee.TabelNo</c>).
/// <see cref="AttendanceStatus"/> — qo'lda rejimda xodim tanlagan status ("checkIn"/"checkOut"); avto rejimda <c>null</c>
/// (toggle bilan hal qilinadi).
/// </summary>
public sealed record TerminalEvent(
    long SerialNo, string EmployeeNo, string Name, DateTimeOffset Time, string? PictureUrl, string? AttendanceStatus = null);

/// <summary>Terminalga ulanib bo'lmaganda (tarmoq yoki parol xatosi).</summary>
public sealed class TerminalException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Hikvision yuz terminali (DS-K1Txxx) bilan ISAPI orqali gaplashuvchi mijoz. Digest autentifikatsiya.
/// Pul ma'lumoti yo'q; faqat qurilma holati, voqealar va (keyin) xodim/yuz boshqaruvi.
/// </summary>
public sealed class HikTerminalClient : IDisposable
{
    /// <summary>Yuz orqali muvaffaqiyatli autentifikatsiya voqeasi.</summary>
    public const int MajorFace = 5;
    public const int MinorFaceAuth = 75;

    /// <summary>Bitta sahifada ko'pi bilan (ISAPI chegarasi).</summary>
    private const int PageSize = 30;

    private readonly HttpClient _http;
    private readonly Uri _base;
    private readonly bool _ownsHttp;

    /// <param name="http">Testlar uchun; null — digest kredensiali bilan yangi HttpClient yaratiladi.</param>
    public HikTerminalClient(string baseUrl, string username, string password, HttpClient? http = null)
    {
        _base = new Uri(NormalizeUrl(baseUrl) + "/");
        if (http is not null)
        {
            _http = http;
            _ownsHttp = false;
        }
        else
        {
            var cred = new CredentialCache { { _base, "Digest", new NetworkCredential(username, password) } };
            _http = new HttpClient(new SocketsHttpHandler { Credentials = cred, ConnectTimeout = TimeSpan.FromSeconds(5) })
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _http.DefaultRequestHeaders.ExpectContinue = false; // Hikvision "Expect: 100-continue" ni yomon ko'radi (401)
            _ownsHttp = true;
        }
    }

    public static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "http://" + url;
        return url;
    }

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(NormalizeUrl(url), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Lokal tarmoq(lar)dan <paramref name="serial"/> seriya raqamli Hikvision qurilmasini topadi (IP DHCP'da o'zgarsa ham).
    /// Har hostda <c>/ISAPI/System/deviceInfo</c> so'raladi; serialNumber mos kelsa — <c>http://{ip}</c> qaytadi. Topilmasa — null.
    /// </summary>
    public static async Task<string?> DiscoverBySerialAsync(string serial, string username, string password, CancellationToken ct = default)
    {
        serial = serial.Trim();
        if (serial.Length == 0)
            return null;
        var hosts = LocalSubnetHosts().ToList();
        if (hosts.Count == 0)
            return null;

        // Bitta umumiy handler (254 ta HttpClient/handler yaratib-yo'q qilish o'rniga). Kredensial faqat Digest sxemasiga
        // va faqat skanlanayotgan hostlarga beriladi (Basic orqali parol ochiq yuborilmaydi).
        var cred = new CredentialCache();
        var nc = new NetworkCredential(username, password);
        foreach (var ip in hosts)
            cred.Add(new Uri($"http://{ip}/"), "Digest", nc);
        using var http = new HttpClient(new SocketsHttpHandler
        {
            Credentials = cred,
            ConnectTimeout = TimeSpan.FromMilliseconds(600),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(2),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan, // har so'rov o'z tokeni bilan cheklanadi (MatchesSerialAsync)
        };
        http.DefaultRequestHeaders.ExpectContinue = false;

        string? result = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = cts.Token },
                async (ip, token) =>
                {
                    if (await MatchesSerialAsync(http, ip, serial, token))
                    {
                        result = $"http://{ip}";
                        cts.Cancel(); // birinchi mos qurilmada to'xtaymiz
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // topildi (cts.Cancel) yoki tashqi bekor — result bo'yicha hal qilamiz
        }
        return result;
    }

    private static async Task<bool> MatchesSerialAsync(HttpClient http, string ip, string serial, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));
            using var resp = await http.GetAsync(new Uri($"http://{ip}/ISAPI/System/deviceInfo"), timeout.Token);
            if (!resp.IsSuccessStatusCode)
                return false;
            var xml = await resp.Content.ReadAsStringAsync(timeout.Token);
            return string.Equals(Tag(xml, "serialNumber"), serial, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // javob bermadi/mos emas
        }
    }

    /// <summary>Lokal IPv4 interfeyslaridan /24 ichidagi barcha hostlar (x.y.z.1–254).</summary>
    private static IEnumerable<string> LocalSubnetHosts()
    {
        var prefixes = new HashSet<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 127 || b[0] == 169)
                    continue;
                prefixes.Add($"{b[0]}.{b[1]}.{b[2]}.");
            }
        }
        foreach (var p in prefixes)
            for (var i = 1; i <= 254; i++)
                yield return p + i;
    }

    /// <summary>Qurilma ma'lumoti (ulanish + parolni tekshirish uchun ham).</summary>
    public async Task<TerminalInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var xml = await GetStringAsync("ISAPI/System/deviceInfo", ct);
        return new TerminalInfo(
            Tag(xml, "model"), Tag(xml, "serialNumber"), Tag(xml, "firmwareVersion"), Tag(xml, "deviceName"));
    }

    /// <summary>
    /// <paramref name="from"/>–<paramref name="to"/> oralig'idagi yuz voqealari, <paramref name="afterSerial"/> dan katta
    /// SerialNo'lilar (vaqt bo'yicha o'sish tartibida). Sahifalab to'liq o'qiydi.
    /// </summary>
    public async Task<IReadOnlyList<TerminalEvent>> FetchFaceEventsAsync(
        DateTimeOffset from, DateTimeOffset to, long afterSerial, CancellationToken ct = default)
    {
        var result = new List<TerminalEvent>();
        var searchId = Guid.NewGuid().ToString("N");
        var position = 0;
        while (true)
        {
            var body = JsonSerializer.Serialize(new
            {
                AcsEventCond = new
                {
                    searchID = searchId,
                    searchResultPosition = position,
                    maxResults = PageSize,
                    major = MajorFace,
                    minor = MinorFaceAuth,
                    startTime = from.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    endTime = to.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                },
            });
            using var doc = await PostJsonAsync("ISAPI/AccessControl/AcsEvent?format=json", body, ct);
            var acs = doc.RootElement.GetProperty("AcsEvent");
            var count = acs.TryGetProperty("numOfMatches", out var n) ? n.GetInt32() : 0;
            if (count == 0 || !acs.TryGetProperty("InfoList", out var list))
                break;

            foreach (var e in list.EnumerateArray())
            {
                var serial = e.TryGetProperty("serialNo", out var s) ? s.GetInt64() : 0;
                if (serial <= afterSerial)
                    continue;
                var empNo = e.TryGetProperty("employeeNoString", out var en) ? en.GetString() : null;
                if (string.IsNullOrEmpty(empNo))
                    continue; // tanilmagan yuz — xodimsiz voqea
                var time = e.GetProperty("time").GetDateTimeOffset();
                var name = e.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                var pic = e.TryGetProperty("pictureURL", out var pu) ? pu.GetString() : null;
                var attStatus = e.TryGetProperty("attendanceStatus", out var at) ? at.GetString() : null;
                result.Add(new TerminalEvent(serial, empNo, name, time, pic, attStatus));
            }

            position += count;
            var status = acs.TryGetProperty("responseStatusStrg", out var rs) ? rs.GetString() : "OK";
            if (status != "MORE")
                break;
        }
        result.Sort((a, b) => a.SerialNo.CompareTo(b.SerialNo));
        return result;
    }

    /// <summary>
    /// Davomat rejimi: <paramref name="requireConfirm"/> true — "manual" (ekranda Kelish/Ketish tugmalari, xodim tanlaydi);
    /// false — "disable" (terminal faqat taniydi, Keldi/Ketdi serverda toggle qilinadi).
    /// </summary>
    public async Task SetAttendanceModeAsync(bool requireConfirm, CancellationToken ct = default)
    {
        var mode = requireConfirm ? "manual" : "disable";
        var body = JsonSerializer.Serialize(new
        {
            AttendanceMode = new { mode, attendanceStatusTime = 20, reqAttendanceStatus = requireConfirm },
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.PutAsync(new Uri(_base, "ISAPI/AccessControl/Configuration/attendanceMode?format=json"), content, ct);
            await EnsureOkAsync(resp, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>
    /// Ekrandagi davomat tugmalarini sozlaydi: <paramref name="requireConfirm"/> true — "Kelish" (checkIn) va "Ketish"
    /// (checkOut) yoqiladi, qolganlari o'chiriladi; false — hammasi o'chiriladi. Har status alohida yoziladi (keyCfg/{id}/attendance).
    /// </summary>
    public async Task SetAttendanceKeysAsync(bool requireConfirm, CancellationToken ct = default)
    {
        // id, status, yorliq, yoqilganmi (requireConfirm bo'lsa faqat checkIn/checkOut).
        (int Id, string Status, string Label, bool Enable)[] keys =
        [
            (1, "checkIn", "Kelish", requireConfirm),
            (2, "checkOut", "Ketish", requireConfirm),
            (3, "breakOut", "Break Out", false),
            (4, "breakIn", "Break In", false),
            (5, "overtimeIn", "Overtime In", false),
            (6, "overtimeOut", "Overtime Out", false),
        ];
        foreach (var k in keys)
        {
            var body = JsonSerializer.Serialize(new
            {
                Attendance = new { id = k.Id, enable = k.Enable, attendanceStatus = k.Status, label = k.Label },
            });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            try
            {
                using var resp = await _http.PutAsync(new Uri(_base, $"ISAPI/AccessControl/keyCfg/{k.Id}/attendance?format=json"), content, ct);
                await EnsureOkAsync(resp, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
            }
        }
    }

    /// <summary>Joriy davomat rejimi "manual"mi (ekranda tugmalar yoqilganmi).</summary>
    public async Task<bool> IsManualModeAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("ISAPI/AccessControl/Configuration/attendanceMode?format=json", ct);
        return doc.RootElement.TryGetProperty("AttendanceMode", out var m)
            && m.TryGetProperty("mode", out var mode) && mode.GetString() == "manual";
    }

    // ─── Xodim (UserInfo) va yuz (FDLib) boshqaruvi — desktop asosiy manba ────────────────────

    /// <summary>Terminalда shu <paramref name="employeeNo"/>'li xodim bormi.</summary>
    public async Task<bool> UserExistsAsync(string employeeNo, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            UserInfoSearchCond = new
            {
                searchID = Guid.NewGuid().ToString("N"),
                searchResultPosition = 0,
                maxResults = 1,
                EmployeeNoList = new[] { new { employeeNo } },
            },
        });
        using var doc = await PostJsonAsync("ISAPI/AccessControl/UserInfo/Search?format=json", body, ct);
        var s = doc.RootElement.GetProperty("UserInfoSearch");
        return s.TryGetProperty("numOfMatches", out var n) && n.GetInt32() > 0;
    }

    /// <summary>Terminalda yuzi (numOfFace&gt;0) bor xodimlarning employeeNo (= TabelNo) to'plami.</summary>
    public async Task<HashSet<int>> ListFacedTabelsAsync(CancellationToken ct = default)
    {
        var result = new HashSet<int>();
        var searchId = Guid.NewGuid().ToString("N");
        var position = 0;
        while (true)
        {
            var body = JsonSerializer.Serialize(new
            {
                UserInfoSearchCond = new { searchID = searchId, searchResultPosition = position, maxResults = PageSize },
            });
            using var doc = await PostJsonAsync("ISAPI/AccessControl/UserInfo/Search?format=json", body, ct);
            var s = doc.RootElement.GetProperty("UserInfoSearch");
            var count = s.TryGetProperty("numOfMatches", out var n) ? n.GetInt32() : 0;
            if (count == 0 || !s.TryGetProperty("UserInfo", out var list))
                break;
            foreach (var u in list.EnumerateArray())
            {
                var face = u.TryGetProperty("numOfFace", out var nf) ? nf.GetInt32() : 0;
                if (face > 0 && u.TryGetProperty("employeeNo", out var en) && int.TryParse(en.GetString(), out var tabel))
                    result.Add(tabel);
            }
            position += count;
            var status = s.TryGetProperty("responseStatusStrg", out var rs) ? rs.GetString() : "OK";
            if (status != "MORE")
                break;
        }
        return result;
    }

    /// <summary>
    /// Xodimni terminalга yozadi/yangilaydi: <paramref name="employeeNo"/> (= <c>Employee.TabelNo</c>), <paramref name="name"/>.
    /// <paramref name="enable"/> false — bloklanadi (Valid o'chadi, yuzi saqlanadi). Yo'q bo'lsa yaratadi (Record), bor bo'lsa yangilaydi (Modify).
    /// </summary>
    public async Task UpsertUserAsync(string employeeNo, string name, bool enable, CancellationToken ct = default)
    {
        var exists = await UserExistsAsync(employeeNo, ct);
        var body = JsonSerializer.Serialize(new
        {
            UserInfo = new
            {
                employeeNo,
                name,
                userType = "normal",
                Valid = new { enable, beginTime = "2020-01-01T00:00:00", endTime = "2037-12-31T23:59:59", timeType = "local" },
                doorRight = "1",
                RightPlan = new[] { new { doorNo = 1, planTemplateNo = "1" } },
            },
        });
        var path = exists ? "ISAPI/AccessControl/UserInfo/Modify?format=json" : "ISAPI/AccessControl/UserInfo/Record?format=json";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var resp = exists
                ? await _http.PutAsync(new Uri(_base, path), content, ct)
                : await _http.PostAsync(new Uri(_base, path), content, ct);
            await EnsureOkAsync(resp, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>Xodimni terminaldan butunlay o'chiradi (yuzi bilan).</summary>
    public async Task DeleteUserAsync(string employeeNo, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            UserInfoDelCond = new { EmployeeNoList = new[] { new { employeeNo } } },
        });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.PutAsync(new Uri(_base, "ISAPI/AccessControl/UserInfo/Delete?format=json"), content, ct);
            await EnsureOkAsync(resp, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>
    /// Xodimга yuz (foto) biriktiradi: JPEG baytlari blackFD (FDID=1) kutubxonasiga FPID=<paramref name="employeeNo"/> bilan yoziladi.
    /// Avval eski yuz o'chiriladi (bo'lsa), keyin yangisi yoziladi. Terminalда xodim (UserInfo) oldin bo'lishi shart.
    /// </summary>
    public async Task UploadFaceAsync(string employeeNo, byte[] jpeg, CancellationToken ct = default)
    {
        await TryDeleteFaceAsync(employeeNo, ct);
        var meta = JsonSerializer.Serialize(new { faceLibType = "blackFD", FDID = "1", FPID = employeeNo });
        var boundary = "OylikFace" + Guid.NewGuid().ToString("N");
        using var form = new MultipartFormDataContent(boundary);
        // JSON qismi CHARSET'siz bo'lishi shart — "application/json; charset=utf-8" bo'lsa Hikvision "badJsonFormat" beradi.
        var metaPart = new ByteArrayContent(Encoding.UTF8.GetBytes(meta));
        metaPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        form.Add(metaPart, "FaceDataRecord");
        var img = new ByteArrayContent(jpeg);
        img.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(img, "img", "face.jpg");
        // Hikvision TIRNOQLI boundary'ni ("boundary=\"...\"") o'qiy olmaydi — tirnoqsiz qilib qo'yamiz, aks holda "badJsonFormat".
        form.Headers.Remove("Content-Type");
        form.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
        try
        {
            using var resp = await _http.PostAsync(new Uri(_base, "ISAPI/Intelligent/FDLib/FaceDataRecord?format=json"), form, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode && text.Contains("\"ok\"", StringComparison.Ordinal))
                return;
            // Qurilма rasmни qabul qildi, lekin yuзни modellay olmadi (sifat past / yuz yo'q / juda katta).
            if (text.Contains("Analysis", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Modeling", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Quality", StringComparison.OrdinalIgnoreCase)
                || text.Contains("saveFacePic", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Face", StringComparison.OrdinalIgnoreCase))
                throw new TerminalException(
                    "Rasmда yuз aniqlanmadi. Aniq, yorug', old tomondан olingan bitta yuз bo'lган surat tanlang "
                    + "(yoki yuзни terminalда oling).");
            await EnsureOkAsync(resp, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>
    /// Terminal kamerasi orqali yuz oladi (masofadan): xodim terminalga qaraб tursa, qurilma yuзни ushlaб, JPEG qaytaradi.
    /// <see cref="CancellationToken"/> yoki ichki muddat (25s) tugaса — xato otiladi. Olingan baytларни <see cref="UploadFaceAsync"/> ga beriladi.
    /// </summary>
    public async Task<byte[]> CaptureFaceAsync(CancellationToken ct = default)
    {
        const string body = "<CaptureFaceDataCond><captureInfrared>false</captureInfrared><dataType>url</dataType></CaptureFaceDataCond>";
        var deadline = DateTime.UtcNow.AddSeconds(40);
        string faceUrl;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string xml;
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/xml");
                using var resp = await _http.PostAsync(new Uri(_base, "ISAPI/AccessControl/CaptureFaceData"), content, ct);
                xml = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode && DateTime.UtcNow >= deadline)
                    throw new TerminalException("Terminal yuzni ololmadi. Xodim terminalga qarasin va qayta urinib ko'ring.");
            }
            catch (HttpRequestException ex)
            {
                throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
            }

            var progress = int.TryParse(Tag(xml, "captureProgress"), out var p) ? p : 0;
            faceUrl = Tag(xml, "faceDataUrl");
            if (progress >= 100 && faceUrl.Length > 0)
                break;
            if (DateTime.UtcNow >= deadline)
                throw new TerminalException("Terminal yuzni ololmadi (vaqt tugadi). Xodim terminal kamerasiga qarasin.");
            await Task.Delay(800, ct);
        }

        try
        {
            using var resp = await _http.GetAsync(new Uri(faceUrl), ct);
            await EnsureOkAsync(resp, ct);
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>FPID bo'yicha eski yuzni o'chiradi; yuz bo'lmasa — e'tiborsiz.</summary>
    private async Task TryDeleteFaceAsync(string employeeNo, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { FPID = new[] { new { value = employeeNo } } });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.PutAsync(
                new Uri(_base, "ISAPI/Intelligent/FDLib/FDSearch/Delete?format=json&FDID=1&faceLibType=blackFD"), content, ct);
            _ = resp.StatusCode; // yuz yo'q bo'lsa xato qaytishi mumkin — e'tiborsiz
        }
        catch (HttpRequestException)
        {
            // tarmoq xatosi keyingi qadamda chiqadi
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(new Uri(_base, path), ct);
            await EnsureOkAsync(resp, ct);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TerminalException("Terminal javob bermadi (vaqt tugadi).");
        }
    }

    private async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(new Uri(_base, path), ct);
            await EnsureOkAsync(resp, ct);
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TerminalException("Terminal javob bermadi (vaqt tugadi).");
        }
    }

    private async Task<JsonDocument> PostJsonAsync(string path, string json, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(new Uri(_base, path), content, ct);
            await EnsureOkAsync(resp, ct);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            throw new TerminalException($"Terminalga ulanib bo'lmadi: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TerminalException("Terminal javob bermadi (vaqt tugadi).");
        }
    }

    private static async Task EnsureOkAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
            return;
        if (resp.StatusCode is HttpStatusCode.Unauthorized)
            throw new TerminalException("Login yoki parol noto'g'ri.", resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync(ct);
        throw new TerminalException($"Terminal xatosi ({(int)resp.StatusCode}): {text}", resp.StatusCode);
    }

    private static string Tag(string xml, string tag)
    {
        var open = $"<{tag}>";
        var i = xml.IndexOf(open, StringComparison.Ordinal);
        if (i < 0)
            return "";
        i += open.Length;
        var j = xml.IndexOf($"</{tag}>", i, StringComparison.Ordinal);
        return j < 0 ? "" : xml[i..j].Trim();
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
