# Oylik — Reja: Background agent, Rollar, Ko'p-zavod sinxroni

> Holat: **taklif (tasdiq kutilmoqda)**. Egasi: `oy` (nazorat, Contracts/DTO), ijro: `oy-exe` (Data/Server/Desktop/Agent).
> Repo public — parol, mijoz ismlari, kalitlar kodga/hujjatga/commitga kirmaydi.

## 1. Maqsad va arxitektura (foydalanuvchi tasdiqlagan)

Yuz terminali davomatini **desktop ochiq bo'lishidan qat'i nazar** doim yig'ib, asosiy serverga yetkazish; desktopda rollar bilan ruxsatlarni cheklash; ikki (keyin ko'p) zavodni bitta asosiy serverga ulash.

```
1-ZAVOD (asosiy kompyuter)
  Terminal ──(LAN/Wi-Fi, lokal)── Agent ──► Server (localhost, doim yoniq)
                                   Desktop (kerakda ochiladi) ──► Server
2-ZAVOD (uzoq)
  Terminal ──(LAN/Wi-Fi)── Agent ──(internet)──► 1-zavoddagi Server
                           Desktop (kerakda: xodim/rasm) ──► Server
```

- **Agent** = UI'siz, kompyuter yonganda avtoyuklanadigan kichik dastur. Terminaldan **faqat yangi** (serial kursor, idempotent) keldi-ketdini oladi, serverga jo'natadi. Doim ishlaydi.
- **Desktop** = ko'rish + ish haqi + xodim/rasm boshqaruvi. Kerakda ochiladi, keyin yopiladi. Terminalni o'zi so'ramaydi (agent birlamchi).
- Ma'lumot yo'qolmaydi: terminal 150k voqea saqlaydi + kursor → oddiy o'chib-yonishda yangi voqealar baribir olinadi.

## 2. Ish bo'laklari (tartib bo'yicha)

### Bosqich 1 — Rollar va ruxsatlar (Desktop)
Rollar: **Admin, Rahbar, Buxgalter, Qorovul**. Kirishda rol + parol/PIN. Rol → ruxsat etilgan bo'limlar; qolgani yashiriladi/bloklanadi.

Bo'limlar (Page): Oylik, Xodimlar, Davomat, Oylar, Audit, Sozlamalar.

| Rol | Oylik (maosh) | Xodimlar | Davomat | Oylar/Audit | Sozlamalar | Tahrir |
|-----|:---:|:---:|:---:|:---:|:---:|:---:|
| Admin | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Buxgalter | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Rahbar | ✅ | ✅ | ✅ | ✅ | ❌ | **faqat ko'rish** |
| Qorovul | ❌ | ro'yxat + yuz (maoshsiz) | ✅ | ❌ | ❌ | faqat yuz/davomat |

**Qarorlar (tasdiqlangan):**
- **Qorovul** maoshni umuman ko'rmaydi (Oylik/daily/total ustunlar yashirin); 2-zavod qorovul kompyuteri shu rolda.
- **Rahbar** hamma hisobni ko'radi, lekin o'zgartira olmaydi (read-only).
- **Kirish: PIN** (raqamli), har rol uchun; **Admin** o'rnatadi va tiklaydi. PIN hash bilan saqlanadi (salt+SHA-256, ochiq matnda emas, repoga kirmaydi).
- Birinchi ishga tushganda PIN yo'q → **Admin** sifatida ochiladi; Admin Sozlamalar → Rollar da PINlarni o'rnatadi.
- Rol — joriy sessiya tanlovi (xotirada); lokal, hozircha sinxronlanmaydi. Egasi: oy-exe (Contracts/DTO kerak emas).

### Bosqich 2 — Background agent (+ terminal-poll refaktori)
- `Oylik.Agent` — yangi UI'siz loyiha (Windows service yoki avtoyuklanish). `Oylik.Data` + `Oylik.Contracts` ni ishlatadi.
- Terminal o'qish mantig'ini (`FaceTerminalService` dagi AcquireAsync/poll/ApplyTerminalEvents) **UI'dan ajratib** `Oylik.Data` ga ko'chiramiz — agent ham, desktop ham shuni ishlatadi (takrorsiz).
- Agent sikli: terminalni so'ra → yangi keldi-ketdi → bazaga yoz → ServerSync (serverga jo'nat).
- **Desktop–agent muvofiqligi:** terminalni faqat BITTA jarayon so'raydi. Tavsiya: agent birlamchi; desktop ochilganda agent ishlayotganini (nomli mutex) sezsa, o'zi terminalni so'ramaydi — faqat ko'rsatadi.
- **Avtoyuklanish:** Windows service (eng barqaror) yoki Startup. Tavsiya: service.

### Bosqich 3 — Ko'p-zavod sinxroni (Contracts + Server + Data + Desktop)

**Holat: ✅ BAJARILDI va tasdiqlandi (oy kod ko'rigi + 311 test yashil: Core 161, Server 56, Desktop 94). Contracts — `oy`; Server/Data/Desktop — `oy-exe`.**

**Kelajak ishlari (bloker emas, qamrovda emas edi):** (1) JoinInstallation'da JoinCount WriteGate'siz — bir vaqtdagi ikki join uchun ApiKeys(InstallationId,SiteIndex) unique indeks; (2) join'dan oldingi ServerSeq'li voqealar yangi o'rnatmaga ko'chmaydi; (3) ulangan mashina kalitini bekor qilish UI'si (hozir server bazasidan). Server deploy (HTTPS/public URL/VPN) — foydalanuvchi bajaradi.

Kodda tasdiqlangan 4 muammo va yechimi:

**1. TabelNo yo'q** — `EmployeeDto`, `EmployeeSnapshot` da yo'q; 2-zavod terminali xodimni tanimaydi.
- ✅ Contracts (oy): `EmployeeDto` ga `string? TabelNo` (oxirida, default null — eski mos).
- Server (oy-exe): `EmployeeSnapshot.TabelNo` (migratsiya); `PutEmployees` qabul qiladi, `GetEmployees` qaytaradi.
- Data (oy-exe): push'da TabelNo yuboriladi; pull'da lokal xodimga yoziladi.

**2. `PutEmployees` ro'yxatda yo'q xodimni nofaol qiladi** (hozir `Endpoints.cs:204`) — ikki zavod bir-birining xodimini o'chiradi.
- Server (oy-exe): **upsert-only** — "yo'q bo'lsa nofaol qil" sikli olib tashlanadi. Nofaol qilish faqat `IsActive=false` ni push qilish orqali (desktop xodimni o'chirganda/nofaollashtirganda shunday qiladi; `_service.Employees` nofaollarni ham o'z ichiga oladi — tekshir). Qattiq-o'chirish (tarixsiz xodim) serverda eski snapshot qoldiradi — zararsiz (faqat telefon ko'rsatish + voqea tekshiruvi uchun).

**3. Xodim pull yo'q** — `ServerSync` xodimni faqat push qiladi (voqealar allaqachon ikki tomonlama).
- Data (oy-exe): `ServerSync` ga qadam qo'shiladi — `GET /api/employees?updatedSince={EmployeesSyncedAt}` → lokal roster upsert (Id bo'yicha yaratish/yangilash: FullName/Note/IsActive/MaxShiftMinutes/TabelNo), kursor `Settings.EmployeesSyncedAt` (server `UpdatedAt` bo'yicha, `LastEventSeq` kabi). Pull'da yangi yaratilgan xodim: ochiq davrlarga kiritiladi (`EnsureActiveEmployeesInOpenPeriodsAsync`) VA **lokal terminalga push** qilinadi (shu zavod terminali uni tanishi uchun). Push/pull halqa bo'lmasin — pull faqat o'zgargani kelsin, push imzosi (EmployeesSignature) o'sha qiymatga qaytmasin.

**4. Join yo'q** — `RegisterInstallation` mavjud Id'ga 409 beradi; 2-chi desktop ApiKey ololmaydi.
- ✅ Contracts (oy): `JoinCodeResponse`, `JoinInstallationRequest`; route'lar `JoinCodes(id)` = `POST /api/installations/{id}/join-codes`, `JoinInstallation` = `POST /api/installations/join`.
- Server (oy-exe): join-code — telefon pairing kodidan **alohida** (telefon kodi desktop ApiKey ololmasin; `PairingCode` ga `Kind`/`IsDesktop` yoki alohida jadval). `join-codes`: Desktop caller, 6 xona, 10 daqiqa, bir martalik. `join`: kalitsiz, kod bo'yicha o'rnatmani topadi, **yangi alohida ApiKey** yaratadi (`Installation` ga ko'p ApiKeyHash kerak bo'ladi — hozir bitta; ApiKey'larni alohida `ApiKey` jadvaliga ko'chirish yoki installationga 1:ko'p qilish). Urinishlar `pair` kabi cheklanadi.
- Desktop (oy-exe): 1-zavod (Admin, Sozlamalar→Server) "ulanish kodi yarat"; 2-zavod "mavjud o'rnatmaga ulanish" (server URL + InstallationId + kod → ApiKey, Settings ga yoziladi).

**Qarorlar (oy):**
- Join: har mashina **o'z ApiKey'ini** oladi (bitta kalit ulashilmaydi — birini o'chirish ikkinchisiga ta'sir qilmaydi). Kod admin tomonidan yaratiladi, 10 daqiqa, bir martalik.
- Nofaol: `IsActive=false` push orqali, absence bilan emas.
- **TabelNo to'qnashuvi** (kodda tasdiqlangan xavf: lokal `NextTabelNo = max+1`, UNIQUE emas; ikki zavod bir xil raqam beradi → pull'dan keyin `ToDictionary(TabelNo)` qulaydi, terminal polli to'xtaydi): **zavod diapazoni**, bazani server join'da avtomatik beradi. `JoinInstallationResponse(InstallationId, ApiKey, int TabelBase)`. Server: Installation'da monoton `JoinCount`, asosiy=siteIndex 0 (base 1), har join siteIndex=++JoinCount → `TabelBase = siteIndex*1_000_000+1`. Data: `Settings.TabelBase`; `NextTabelNo = max(TabelBase, o'z diapazonidagi lokal max)+1`. Qo'shimcha to'r: `PayrollService.Terminal.cs` dagi `ToDictionary(TabelNo)` dublikatga chidamli (log + o'tkazib yuborish, otmaydi). ✅ (b) qadamda bajarildi.
- **Hard-delete propagatsiyasi** (upsert-only yon ta'siri: tarixsiz xodim lokal o'chirilsa server snapshot FAOL qoladi → pull boshqa zavodda qayta tiriltiradi): **tombstone** — lokal `EmployeeTombstones` (Id, FullName, TabelNo); o'chirishda yoziladi, ServerSync push'da roster bilan birga `IsActive=false` jo'natiladi, server tasdiqlagach tozalanadi. **Pull tirilmaslik qoidasi:** `IsActive=false` kelgan xodim lokalda YO'Q bo'lsa — o'tkazib yuboriladi (yaratilmaydi); lokalda bor bo'lsa — nofaol qilinadi (terminaldan olinadi). `IsActive=true` — upsert (yangi bo'lsa ochiq davr + lokal terminal push).
- **Server internetdan ochiq** (public URL / VPN) — HTTPS, kuchli ro'yxat kaliti. Bu **tarqatish (deployment)** qadami, kod emas — foydalanuvchi bajaradi. Kalit hech qayerga yozilmaydi.

Ketma-ketlik (oy-exe): (a) TabelNo server+data+push/pull → (b) upsert-only → (c) join (server+desktop). Har qadamdan keyin build+test yashil.

## 3. Muhim qarorlar
1. Avtoyuklanish: agent **host-agnostik** (.NET Generic Host + `UseWindowsService()`) — service sifatida ham, Startup ilova sifatida ham ishlaydi. Ishlab chiqarishda **Windows service** tavsiya. (TASDIQLANDI)
2. **Bitta baza:** agent + desktop o'sha lokal SQLite (OYLIK_HOME). Agent davomatni (append-only, deterministik Id) + ServerSync; desktop xodim/ish haqi. SQLite WAL + busy_timeout. Terminalni faqat BITTA jarayon so'raydi (nomli mutex: agent birlamchi, desktop ochiq bo'lsa chekinadi). (TASDIQLANDI)
3. Rol PINi unutilsa tiklash — **keyinroq** (foydalanuvchi tasdiqladi). Hozircha bazadan RolePins o'chirish bilan.
4. 2-zavodni asosiy o'rnatmaga ulash usuli (join kodi) — Bosqich 3 da.
5. Server qayerda (doim yoniq kompyuter/bulut) — Bosqich 3 da.

### Bosqich 2 — holati: ✅ BAJARILDI va tasdiqlandi (oy ko'rib chiqdi)
- `Oylik.Data/Terminal/TerminalPoller.cs` (UI'siz refaktor), `Oylik.Agent` (worker, UseWindowsService), `Agent/AgentLock.cs` (mutex), `Agent/AgentRunner.cs` (sikl, attendanceOnly), `PayrollService.Store.cs` (RefreshFromStore), `SecretProtector` Data'ga ko'chdi. `docs/agent.md` — o'rnatish. 295 test yashil.
- **Muhim:** agent desktop foydalanuvchisi nomidan ishlashi shart (DPAPI/LOCALAPPDATA). LocalSystem emas.
- Jonli sinov (haqiqiy terminal+server, desktopsiz) hali qilinmagan — foydalanuvchi sinaydi.

### Bosqich 1 — holati: ✅ BAJARILDI va tasdiqlandi (oy ko'rib chiqdi)
- `Permissions.cs`, `Roles.cs` (PBKDF2 PIN), `PayrollService.Roles.cs`, migratsiya `RolePins`, Login + Sozlamalar→Rollar. 285 test yashil.
- Tasdiq: Davomat "dam/boshqa vaqt/sababli" faqat Admin/Buxgalter (maoshga ta'sir); Qorovul oddiy keldi-ketdi/tasdiq qiladi. (foydalanuvchi tasdiqladi)

## 4. Egalik va ketma-ketlik
- `oy` (men): arxitektura, Contracts/DTO (TabelNo), reja, har bosqichni **tekshirish**.
- `oy-exe`: Desktop (rollar), `Oylik.Agent`, poll refaktori, Server/Sync (pull, TabelNo).
- Tartib: **1) Rollar → 2) Agent → 3) Ko'p-zavod**. Har bosqich alohida topshiriladi, build+test yashil, commit faqat `oy` orqali (so'ralganda).
