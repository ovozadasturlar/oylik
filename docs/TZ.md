# Oylik — Texnik topshiriq (TZ)

Versiya 1.0 · 2026-10-09 · Holat: tasdiqlashga
Loyiha nomi: **Oylik** (taklif, o'zgartirish mumkin)

---

## 1. Maqsad va chegaralar

**Nima quriladi.** Excel'dagi oylik ish haqi jadvalini almashtiradigan Windows desktop dastur, qorovul uchun Android ilova (Keldi/Ketdi) va ular orasidagi sinxronizatsiya serveri.

**Kim ishlatadi.**
- Buxgalter / rahbar — desktop: oylik, xodimlar, davomat, sozlamalar.
- Qorovul — Android: faqat Keldi/Ketdi. Telefonda va serverda **hech qanday pul ma'lumoti yo'q**.

**Hajm.** 60–150 xodim, bitta korxona. Server bir nechta o'rnatmaga (korxonaga) xizmat qila oladigan qilib yoziladi.

**Hozircha chegaradan tashqarida.** Soliq/hisobotlar, bank, biometrika, filiallar, ish haqining davomatdan avtomatik hisoblanishi (Kun qo'lda kiritiladi).

**Atamalar (Excel ↔ kod).**

| Excel ustuni | Kodda | Ma'nosi |
|---|---|---|
| Оклад | `MonthlySalary` | oylik maosh, so'mda (6 → 6 000 000) |
| Ишчилар | `FullName` | xodim ismi, erkin matn |
| Сумма | `DailyRate` | kunlik stavka |
| Кун | `DaysWorked` | oyda ishlagan kunlar (qo'lda) |
| Жами | `Total` | Kun × Kunlik |
| Аванс | `AdvanceTotal` | avanslar yig'indisi |
| Колдик | `Remainder` | Jami − Avans |

---

## 2. Texnologiyalar va tuzilma

| Qism | Tanlov |
|---|---|
| Platforma | .NET 10 (LTS), C# 14, `Nullable` yoqiq, file-scoped namespace |
| Desktop | Avalonia UI 11.x, CommunityToolkit.Mvvm, Avalonia.Controls.DataGrid, tema: Semi.Avalonia |
| Ma'lumotlar | EF Core 10 + SQLite, migratsiyalar, WAL rejimi |
| Excel | ClosedXML |
| PDF / chop etish | QuestPDF (Community) — 3-bosqich |
| Log | Serilog → fayl, 14 kun |
| Server | ASP.NET Core 10 minimal API, EF Core + SQLite (PostgreSQL'ga o'tishga tayyor), Docker |
| Android | .NET MAUI 10 (faqat Android, minSdk 26), sqlite-net-pcl, CommunityToolkit.Mvvm, ZXing.Net.Maui (faqat bog'lash QR'i uchun) |
| Test | xUnit + FluentAssertions; Avalonia.Headless (ixtiyoriy) |

```
Oylik.sln
├─ src/Oylik.Core          domen, hisob qoidalari, interfeyslar — tashqi bog'liqliksiz
├─ src/Oylik.Data          EF Core DbContext, migratsiyalar, backup xizmati
├─ src/Oylik.Desktop       Avalonia (Windows)
├─ src/Oylik.Contracts     Server ↔ Desktop ↔ Mobile DTO'lari        (2-bosqich)
├─ src/Oylik.Server        minimal API                                (2-bosqich)
├─ src/Oylik.Mobile        MAUI Android                               (2-bosqich)
├─ tests/Oylik.Core.Tests  qoidalar testlari
└─ docs/                   shu TZ, sintetik namuna Excel `sample.xlsx` (import testi uchun)
```

**Konventsiyalar.**
- Identifikatorlar ingliz tilida. UI matnlari bitta joyda: `Oylik.Desktop/Resources/Strings.cs` (keyin kirill/rus qo'shish oson).
- Pul — `long`, so'mda, tiyinsiz. Kun — `decimal(5,2)`. Vaqt — `DateTimeOffset` (UTC saqlanadi, mahalliy ko'rsatiladi).
- Yaxlitlash — so'mgacha, `MidpointRounding.AwayFromZero`.
- Raqam ko'rinishi: `6 000 000` (bo'sh joy ming ajratgich), kun `26` yoki `26,5`.

---

## 3. Domen modeli

### 3.1 `Employee` — Xodim

| Maydon | Tur | Izoh |
|---|---|---|
| Id | Guid | |
| FullName | string(100) | `Олим ака (тест)` — Excel'dagidek, qavsdagi izoh bilan birga |
| Note | string(100)? | bo'lim/izoh, ixtiyoriy |
| MonthlySalary | long | Oklad: 6 000 000 |
| DailyRate | long | Kunlik; default = round(MonthlySalary ÷ 30) |
| DailyRateIsManual | bool | true → Oklad o'zgarganda qayta hisoblanmaydi |
| IsActive | bool | false → yangi oyga ko'chmaydi, eski oylarda qoladi |
| BranchId | Guid? | Tarmoq (3.9); null = belgilanmagan |
| CreatedAt, UpdatedAt | DateTimeOffset | |

### 3.2 `PayrollPeriod` — Oy

| Maydon | Tur | Izoh |
|---|---|---|
| Id | Guid | |
| Year, Month | int | unique (Year, Month) |
| Status | enum | `Open` / `Closed` |
| ClosedAt | DateTimeOffset? | |

### 3.3 `PayrollLine` — Jadval qatori

| Maydon | Tur | Izoh |
|---|---|---|
| Id | Guid | |
| PeriodId, EmployeeId | Guid | unique juftlik |
| MonthlySalary | long | oy ochilganda xodim kartasidan nusxa |
| DailyRate | long | nusxa; ochiq oyda tahrirlanadi |
| DaysWorked | decimal(5,2) | Kun, qo'lda, default 0, 0–31, 0,5 qadam |
| BranchId | Guid? | qator yaratilganda xodimdan nusxa; ochiq oyda xodim tarmog'i o'zgarsa yangilanadi, yopiq oyda o'zgarmaydi (R9 kabi) |
| Note | string? | |
| *Total, AdvanceTotal, Remainder* | *hisoblanadi* | DB'da saqlanmaydi (domen metodlari) |

### 3.4 `Advance` — Avans

| Maydon | Tur | Izoh |
|---|---|---|
| Id | Guid | |
| PayrollLineId | Guid | |
| Date | DateOnly | |
| Amount | long | > 0 |
| Kind | enum | `Cash` (naqd) / `CarryOverDebt` (o'tgan oydan qarz) |
| Note | string? | `Sentyabr qarzi` |
| SourcePeriodId | Guid? | faqat CarryOverDebt: qaysi oydan |
| CreatedAt | DateTimeOffset | |

`CarryOverDebt` yozuvini qo'lda tahrirlab/o'chirib bo'lmaydi — faqat manba oyni qayta ochish orqali.

### 3.5 `AuditEntry`

`At, Entity, EntityId, Field, OldValue, NewValue, Comment`. Yoziladigan hodisalar: DaysWorked, DailyRate, MonthlySalary, FullName o'zgarishi; avans qo'shish/o'chirish; oy ochish/yopish/qayta ochish; xodim yaratish/nofaol qilish; davomat tuzatishlari.

### 3.6 `AppSettings` (bitta qator)

`CompanyName, DaysInMonth = 30, HoursPerDay = 8, ShortInputEnabled = true, BackupFolder?, InstallationId (Guid, birinchi ishga tushganda yaratiladi), ServerUrl? (global), LocalServerUrl? (LAN), ApiKey?, Language, Theme`

### 3.7 `AttendanceEvent` — Davomat voqeasi (2-bosqich)

| Maydon | Tur | Izoh |
|---|---|---|
| Id | Guid | **telefonda yaratiladi** — idempotentlik kaliti |
| EmployeeId | Guid | |
| Type | enum | `In` (Keldi) / `Out` (Ketdi) |
| OccurredAt | DateTimeOffset | telefon vaqti, offset bilan |
| DeviceId | Guid? | null = desktopdan qo'lda kiritilgan |
| ReceivedAt | DateTimeOffset? | serverga yetgan vaqt |
| ServerSeq | long? | server tartib raqami — sinxronizatsiya kursori |
| IsVoided, VoidedAt, VoidReason | | tuzatish: voqea **o'chirilmaydi**, bekor qilinadi |

### 3.8 `Device` — Telefon (2-bosqich)

`Id, Name, PairedAt, LastSeenAt, IsActive`

### 3.9 `Branch` — Tarmoq (filial)

`Id, Name(100), SortOrder, IsActive`. Korxona bitta, tarmoq bir nechta (boshida 2 ta: `1-tarmoq`, `2-tarmoq`; nomi Sozlamalarda o'zgartiriladi). Xodimi bor tarmoq o'chirilmaydi, faqat nofaol qilinadi.
**Filtr:** Oylik, Xodimlar, Davomat ekranlari tepasida `Barchasi` (default) · har tarmoq · `Belgilanmagan` (faqat shunday xodim bo'lsa). Tanlov ekranlar o'rtasida umumiy va eslab qolinadi. Filtrda jadval, xulosa qatori va JAMI faqat shu tarmoq bo'yicha; Excel eksport joriy filtr bo'yicha (shablon ustunlari o'zgarmaydi). Hisob qoidalari (4-bo'lim) tarmoqqa bog'liq emas. Server va telefonga tarmoq uzatilmaydi.

---

## 4. Hisob qoidalari (Oylik.Core, har biri testlanadi)

### 4.1 Oylik

- **R1.** `DailyRate = round(MonthlySalary ÷ DaysInMonth)`, DaysInMonth = 30. Oklad o'zgarsa va `DailyRateIsManual = false` → qayta hisoblanadi. Kunlik qo'lda kiritilsa → `DailyRateIsManual = true`.
- **R2.** `Total = round(DaysWorked × DailyRate)`.
- **R3.** `AdvanceTotal = Σ Advance.Amount` (Cash + CarryOverDebt).
- **R4.** `Remainder = Total − AdvanceTotal`. Manfiy bo'lishi mumkin (qarz).
- **R5.** Yangi oy: eng oxirgi oydan keyingi (yoki tanlangan) yil/oy. Barcha `IsActive` xodimlar uchun qator; MonthlySalary/DailyRate xodim kartasidan nusxa; DaysWorked = 0; avanslar bo'sh (oldingi oy yopilganda tushgan CarryOverDebt bundan mustasno).
- **R6.** Oyni yopish (`Close`):
  1. Status = Closed, ClosedAt = hozir. Yopishdan oldin avtomatik backup.
  2. Har qator uchun `Remainder < 0` bo'lsa: keyingi oy yo'q bo'lsa yaratiladi (R5); keyingi oyda shu xodim qatori yo'q bo'lsa (nofaol bo'lsa ham) yaratiladi; `Advance { Kind = CarryOverDebt, Amount = −Remainder, Date = keyingi oyning 1-sanasi, Note = "<Oy nomi> qarzi", SourcePeriodId = yopilayotgan oy }` qo'shiladi.
  3. **Idempotent:** shu `SourcePeriodId` + xodim uchun yozuv bor bo'lsa — summasi yangilanadi, ikkinchisi yaratilmaydi.
  4. `Remainder ≥ 0` → hech narsa (to'langan).
- **R7.** Yopiq oy — faqat o'qish: DaysWorked, DailyRate, MonthlySalary, avanslar o'zgarmaydi.
- **R8.** Qayta ochish (`Reopen`): faqat keyingi oy `Closed` bo'lmasa. Shu oy manba bo'lgan CarryOverDebt yozuvlari keyingi oydan o'chiriladi; Status = Open. Qayta yopilganda R6 qaytadan ishlaydi. Audit yoziladi.
- **R9.** Ochiq oyda qatorda Oklad/Kunlik o'zgartirilsa → shu oy nusxasi **va** xodim kartasidagi joriy qiymat ikkalasi yangilanadi (keyingi oylar uchun). Yopiq oylar o'zgarmaydi.
- **R10.** Qatorni oydan olib tashlash: faqat `DaysWorked = 0` va avans yo'q bo'lsa; aks holda taqiqlanadi.
- **R11.** Qisqa kiritish (`ShortInputEnabled`, sozlamada o'chiriladi): Oklad katagida qiymat < 1 000 → × 1 000 000 (`6` → 6 000 000, `6,5` → 6 500 000); Kunlik va Avans katagida qiymat < 10 000 → × 1 000 (`200` → 200 000, `1500` → 1 500 000). Kiritilgach to'liq raqam ko'rsatiladi.

### 4.2 Davomat (2-bosqich)

- **A1.** Xodim voqealari vaqt bo'yicha saralanadi; `IsVoided = true` bo'lganlar hisobga olinmaydi.
- **A2.** Juftlash: `In` → undan keyingi birinchi `Out`. Juftlik davomiyligi ≤ `MaxShiftHours = 16`. Oshsa Out juftlanmaydi, In **ochiq** (`OpenNoOut`) deb belgilanadi.
- **A3.** Out'dan oldingi 16 soat ichida ochiq In bo'lmasa → **yetim Out** (`OrphanOut`) — tasdiqlash ro'yxatiga.
- **A4.** In ketma-ket ikki marta (orasida Out yo'q) → birinchisi `OpenNoOut`, ikkinchisi yangi juftlik.
- **A5.** `WorkDay` sanasi = In'ning mahalliy sanasi. Tungi smena: Out ertasi kun bo'lsa ham In kuniga yoziladi.
- **A6.** Bir kunda bir nechta juftlik → daqiqalar yig'iladi.
- **A7.** `Hours = Minutes ÷ 60` (2 xona); `DayEquivalent = Hours ÷ HoursPerDay` (8).
- **A8.** Oy xulosasi (xodim bo'yicha): `AttendedDays` = kamida bitta tugallangan juftligi bor kunlar soni; `TotalHours = Σ Hours`; `DayEquivalent = TotalHours ÷ 8`.
- **A9.** Oylik jadvalida Kun katagi yonida maslahat: `Davomat: 22 kun · 171 s`. **[Ko'chirish]** tugmasi `AttendedDays` ni `DaysWorked` ga yozadi (faqat ochiq oyda, audit bilan). **Avtomatik yozilmaydi.**
- **A10.** Takror bosish himoyasi: bir xodim uchun bir xil turdagi voqea 2 daqiqa ichida ikkinchi marta → telefon qabul qilmaydi (`Allaqachon belgilangan: 08:02`).
- **A11.** Qo'lda tuzatish (desktop): voqea qo'shish (sana, vaqt, tur, izoh) yoki bekor qilish (sabab majburiy). Audit.
- **A12.** Soat farqi: onlayn jo'natilganda `ReceivedAt − OccurredAt > 10 daqiqa` → ogohlantirish belgisi, rad etilmaydi.

### 4.3 Ish jadvali va soatbay hisob (2.2-bosqich)

Har xodimda **hisob usuli** (`PayMode`): `Daily` — kunbay (4.1, Kun qo'lda; hozirgi xulq o'zgarmaydi) yoki `Schedule` — jadval bo'yicha (soatbay, quyidagi qoidalar). Usul ochiq oyda o'zgartiriladi (R9 kabi: karta + ochiq oy qatori), yopiq oyda o'zgarmaydi.

- **H1. Jadval shabloni** (`WorkSchedule`): `Name`, `Kind` = `Weekly` (hafta kunlari tanlanadi: Du…Ya) yoki `Cycle` (`WorkDays` ish / `OffDays` dam, takrorlanadi: 1/1 — kun ora, 2/2, …); `StartTime` (mahalliy), `Duration` (00:30–24:00), `BreakMinutes` (to'lanmaydigan tanaffus, default 0). Namunalar: `Kunduzgi 08:00–17:00, Du–Sh`; `12 soat 08:00–20:00`; `Sutkalik 08:00–08:00, kun ora` (Cycle 1/1, 24:00).
- **H2. Xodimga biriktirish:** `ScheduleId`, `CycleAnchor` (Cycle uchun: birinchi ish kuni sanasi). Kunlik istisno (`ScheduleOverride`: sana → `Off` yoki boshqa vaqt/davomiylik) — almashuv, bayram. Audit.
- **H3. Reja:** oyning har kuni uchun smena oynasi `[Start, End)`; End ertasi kunga o'tishi mumkin — smena sanasi = boshlanish sanasi (A5 bilan mos). `PlannedMinutes(oy) = Σ (Duration − BreakMinutes)`.
- **H4. Ishlangan vaqt — faqat smena ichida:** har davomat juftligi (A2) smena oynasi bilan kesishadi: `[max(In, Start), min(Out, End))`. Smenadan oldin kelgan vaqt sanalmaydi. Tanaffus ish vaqtidan ayiriladi (smena ichida ishlangan bo'lsa, `BreakMinutes` gacha). Daqiqagacha aniq (soniyalar pastga).
- **H5. Kechikish:** `LateGraceMinutes` (Sozlamalar, 0–180, buyurtmachi o'zi belgilaydi). `In ≤ Start + Grace` → vaqtida, ishlangan vaqt `Start` dan sanaladi. `In > Start + Grace` → `LateMinutes = In − Start` (to'liq), ishlangan vaqt `In` dan. Erta ketish: `EarlyLeaveMinutes = End − Out` (grace yo'q).
- **H6. Asosiy haq:** `MinuteRate = MonthlySalary ÷ PlannedMinutes(oy)` (decimal, oraliqda yaxlitlanmaydi). `BasePay = round(MinuteRate × min(WorkedMinutes, PlannedMinutes))`. Reja to'liq ishlansa `BasePay = MonthlySalary` aniq. Ko'rsatish uchun `HourlyRate = MinuteRate × 60`.
- **H7. Ortiqcha ish** (`OvertimeMinutes` = smena tugagandan keyin qolgan vaqt + reja bo'lmagan kunda ishlangan vaqt; smenadan oldin kelgan vaqt kirmaydi). Siyosat — Sozlamalarda default, xodimda alohida bo'lishi mumkin: `None` (to'lanmaydi, faqat ko'rsatiladi) · `Multiplier k` (`k × HourlyRate`, masalan 1,0 yoki 1,5) · `FixedPerHour` (so'm/soat). `OvertimePay = round(OvertimeMinutes × stavka ÷ 60)`.
- **H8. Sababli kunlar:** desktopda xodim kuniga belgi: `To'lanadigan` (ta'til, kasallik — reja daqiqalari ishlangan deb hisoblanadi) yoki `To'lanmaydigan` (ishlangan 0, kechikish yozilmaydi). Izoh majburiy, audit.
- **H9. Oylik:** Schedule usulida `Total = BasePay + OvertimePay`; R3, R4, R6 (qarz ko'chishi) o'zgarmaydi. Jadvalda `Summa` = soatlik stavka, `Kun` = ishlangan soat (`s:dd`), `Jami` = Total.
- **H10. Ochiq oy** davomat o'zgarishi bilan jonli qayta hisoblanadi. **Yopilganda** qatorda muzlatiladi: `PlannedMinutes, WorkedMinutes, LateMinutes, EarlyLeaveMinutes, OvertimeMinutes, BasePay, OvertimePay`, siyosat va grace qiymati. Yopiq oy hech qachon qayta hisoblanmaydi (Grace yoki jadval keyin o'zgarsa ham). Qayta ochilganda (R8) — joriy sozlamalar bilan qayta hisoblanadi.
- **H11. Yopishdan oldin tekshiruv:** oyda tasdiqlanmagan voqealar (OpenNoOut/OrphanOut) bo'lsa — `N ta tasdiqlanmagan voqea: oylikka ta'sir qiladi` ogohlantirishi, ro'yxatga o'tish yoki baribir yopish.
- **H12. Holat ranglari** (Davomat — Bugun va jadval kataklari):
  - smena boshlanmagan — kulrang; dam kuni — bo'sh;
  - smena boshlandi, kelmagan — **sariq** (`Kechikmoqda · 12 daq`); `Start + AbsentAfterMinutes` (Sozlamalar, default 60) o'tdi yoki smena tugadi — **qizil** (`Kelmadi`);
  - keldi — **yashil** (kech kelgan bo'lsa yashil + `kech 25 daq` belgisi);
  - kun tugagach: `Worked ÷ Planned ≥ GreenPercent` (default 95%) — yashil; `≥ YellowPercent` (default 75%) — sariq; kam — qizil. Foizlar Sozlamalarda.
  - H12 faqat Schedule xodimlariga; Daily xodimlarda hozirgi soat ranglari qoladi.
- **H13. Aniqliklar:**
  1. Bir smenada bir nechta juftlik: grace va kechikish — smenaning **birinchi** In'iga, erta ketish — **oxirgi** Out'iga. Oradagi chiqib-kirish oralig'i sanalmaydi, lekin kechikish/erta ketish deb yozilmaydi.
  2. Juftlik smenaga **vaqt kesishmasi** bo'yicha bog'lanadi (In sanasi bo'yicha emas): 20:00–08:00 smenada 00:30 da kelgan — o'sha smenaga. Smenaga kirmagan qism: smenadan keyin → ortiqcha; smenadan oldin → sanalmaydi; hech qaysi smenaga tegmasa va o'sha kunda reja yo'q → to'liq ortiqcha.
  3. Override `Boshqa vaqt` — ixtiyoriy o'z `BreakMinutes`i (bo'sh — shablonniki). Override dam kunini ish kuniga aylantira oladi (navbatchilik) — bu reja hisoblanadi.
  4. `PlannedMinutes(oy) = 0` → `BasePay = 0`; Multiplier ortiqcha ish = 0, FixedPerHour ishlaydi; qatorda ogohlantirish.
  5. Jadval biriktirish **tarixi bilan**: `EmployeeScheduleAssignment(EmployeeId, ScheduleId, CycleAnchor, EffectiveFrom)`. Oy ichida jadval almashsa — har kun o'sha sanada amalda bo'lgan jadval bilan; PlannedMinutes ham shunday. Hisob usuli (`PayMode`) oy bo'yicha (qator darajasida) — oy o'rtasida almashmaydi.
  6. `To'lanadigan` sababli kunda xodim ishlagan bo'lsa ham — reja daqiqalari **almashtiradi** (qo'shilmaydi); kechikish/erta ketish/ortiqcha yozilmaydi.
  7. Ortiqcha ish siyosati xodimda: `OvertimePolicy?` + qiymat (null — Sozlamalardagi default).
  8. Oylik ekrani xulosasi: `Kun Σ` — faqat Daily qatorlar; Schedule qatorlari bo'lsa yonida `Soat Σ 1 234:30`. JAMI qatorida Kun — Daily kunlar yig'indisi.
  9. Excel eksport: Schedule qatorida `Сумма` (D) va `Кун` (E) **bo'sh**, `Жами` (F) — qiymat (formula emas) va katakka izoh (comment): `Soatbay: 172:30 × 33 333,33 so'm/soat`; `Колдик` (H) formula avvalgidek. JAMI qatoridagi `SUM` formulalari o'zgarmaydi (E — faqat Daily kunlar, F — hammasi).
  10. Hujjat raqami: `OY-YYYY-MM-<Tabel №>`; Xodimda doimiy `TabelNo` (avtomatik, yaratilish tartibida; migratsiyada mavjudlarga CreatedAt bo'yicha 1..n). Xodimlar jadvalida va PDF'da ko'rinadi.
  11. Bu yerda yozilmagan holat — `oy` ga savol.
- **H14. Juftlik chegarasi (A2 kengaytmasi):** Schedule xodimida juftlik chegarasi = o'sha kungi smena `Duration + 4 soat` (sutkalik 24:00 → 28 soat); Daily xodim va jadvalsiz kun — 16 soat (A2 o'zgarmaydi). Telefon "Ichkarida" holatini (TZ 7) ham shu chegara bilan aniqlaydi: desktop `EmployeeDto.MaxShiftMinutes` = xodim jadvalidagi eng uzun smena + 4 soat (yo'q — null → 16 soat) yuboradi, server saqlab telefonga uzatadi.

---

## 5. Desktop (Oylik.Desktop)

### 5.1 Umumiy

- Windows 10/11 x64; self-contained single-file publish; o'rnatuvchisiz ishlaydi.
- Baza: `%LOCALAPPDATA%\Oylik\oylik.db`; loglar `%LOCALAPPDATA%\Oylik\logs\`.
- **Backup:** har kuni birinchi ishga tushganda va har oy yopishdan oldin → `%LOCALAPPDATA%\Oylik\backups\oylik-YYYYMMDD-HHmm.db`, oxirgi 90 ta saqlanadi. Sozlamada qo'shimcha papka (masalan Google Drive) berilsa o'sha yerga ham nusxa.
- **Saqlash tugmasi yo'q** — har katak tasdiqlanganda (Enter / Tab / fokus ketganda) darhol DB'ga yoziladi; status qatorida `Saqlandi 10:42:13`.
- Bitta oyna, chapda navigatsiya: **Oylik · Xodimlar · Davomat (2-bosqich) · Oylar · Sozlamalar**.
- Minimal oyna 1100×700; shrift Segoe UI; raqamlar o'ngga tekis.

### 5.2 Oylik ekrani (asosiy) — Excel bilan bir xil

**Yuqori panel:** `◀ 2026 · Oktabr ▶` (ochiq/yopiq belgisi) · Qidirish · **[Yangi oy] [Excel'dan import] [Excel'ga eksport] [Oyni yopish / Qayta ochish]**

**Xulosa qatori:** `Xodimlar: 62 · Kun Σ: 1 540 · Jami: 312 400 000 · Avans: 95 000 000 · Qoldiq: 217 400 000`

**Jadval — ustunlar tartibi va nomlari aynan:**

| № | Оклад | Ишчилар | Сумма | Кун | Жами | Аванс | Колдик |
|---|---|---|---|---|---|---|---|

| Ustun | Manba | Tahrir | Izoh |
|---|---|---|---|
| № | tartib raqami | — | saralashdan keyin 1..n |
| Оклад | `MonthlySalary` | ha | R11 qisqa kiritish; o'zgarsa Kunlik R1 bo'yicha yangilanadi (manual bo'lmasa); R9 |
| Ишчилар | `FullName` | ha | xodim kartasiga yoziladi |
| Сумма | `DailyRate` | ha | → `DailyRateIsManual`; R9 |
| Кун | `DaysWorked` | ha | 0–31, 0,5 qadam |
| Жами | `Total` | — | kulrang fon |
| Аванс | `AdvanceTotal` | bosilsa / F2 | **Avanslar oynasi** ochiladi (5.3) |
| Колдик | `Remainder` | — | manfiy → qizil matn |

- **JAMI qatori** pastda yopishtirilgan: Kun, Jami, Avans, Qoldiq yig'indisi.
- **Saralash:** ism bo'yicha, madaniy (`CultureInfo("ru-RU")`, katta-kichik harf farqsiz) — kirill/lotin aralash to'g'ri saralanadi.
- **Klaviatura:** `Enter` → pastdagi katak (Excel'dek), `Tab` → o'ngdagi, `Esc` → bekor, `F2` → tahrir, `Ctrl+F` → qidirish, `Ctrl+N` → yangi xodim, `Delete` → qatorni oydan olib tashlash (R10, tasdiq bilan).
- **Qator qo'shish:** jadval ostida `+ Yangi xodim` — ism, Oklad, Kunlik; xodim yaratiladi va ochiq oyga qator qo'shiladi.
- **Yopiq oy:** barcha kataklar read-only, sarlavhada qulf belgisi va `Yopilgan: 01.10.2026 18:20`.
- **Ranglar:** tahrirlanadigan — oq; hisoblanadigan — `#F3F4F6`; manfiy Qoldiq — qizil; qo'lda o'zgartirilgan Kunlik — ko'k kursiv; CarryOverDebt bor qator — Avans katagida kichik `qarz` belgisi.
- (2-bosqich) Kun katagi yonida A9 maslahati va **[Ko'chirish]**.

### 5.3 Avanslar oynasi (modal)

Sarlavha: `Олим ака — Oktabr 2026 avanslari`
Jadval: `Sana · Summa · Turi (Naqd / Qarz) · Izoh · [o'chirish]`
Pastda yangi qator: sana (default bugun), summa (R11), izoh, **[Qo'shish]**. Jami.
CarryOverDebt qatorlari kulrang, o'chirib bo'lmaydi; tooltip: `Sentyabr oyi yopilganda avtomatik o'tgan`.

### 5.4 Xodimlar ekrani

Jadval (faol/nofaol filtri, qidirish, tarmoq filtri): `№ · Ism · Tarmoq · Izoh · Oklad · Kunlik · Holat`.
**Tahrir jadvalning o'zida** — Oylik ekrani bilan bir xil xulq (bitta bosish/F2/yozish → tahrir, matn to'liq belgilanadi, Enter/Tab saqlaydi, Esc bekor). Oklad/Kunlik — R1, R9, R11; qo'lda o'zgartirilgan Kunlik — ko'k kursiv. Tarmoq — ro'yxatdan tanlanadi. Yangi xodim — jadval ostida `+ Yangi xodim` (filtrdagi tarmoq default).
Nofaol qilish — tarixdan o'chirmaydi. O'chirish — faqat birorta oyda qatori bo'lmasa.

### 5.5 Oylar ekrani

`Oy · Holat · Xodimlar · Jami · Avans · Qoldiq · Yopilgan sana` — **[Yangi oy] [Ochish] [Yopish] [Qayta ochish]**.

### 5.6 Sozlamalar

Korxona nomi · DaysInMonth (30) va HoursPerDay (8) — o'zgartirish mumkin, ogohlantirish bilan · Qisqa kiritish · Backup papkasi · Ko'rinish: mavzu (Yorug'/Qorong'i/Tizim) va til (lotin/kirill/rus) — darhol, qayta ishga tushirmasdan · **Tarmoqlar** (3.9) · **(2-bosqich)** Server: **Lokal manzil** (Wi-Fi/LAN) va **Global manzil** (domen) — kamida bittasi (6.1), **Kompyuter ID** (`InstallationId`), [Serverga ulash]; Telefon bog'lash: [QR ko'rsatish] (10 daqiqalik kod); bog'langan qurilmalar ro'yxati.

### 5.7 Excel'dan import

Manba: mavjud fayl (`Лист1`, A1:H…). Ustun xaritasi:

| Excel | → | Qoida |
|---|---|---|
| B | MonthlySalary | qiymat < 1 000 → × 1 000 000 |
| C | FullName | trim; bo'sh → qator o'tkaziladi |
| D | DailyRate | bo'sh → R1 bo'yicha; B ham bo'sh → 0 |
| E | DaysWorked | bo'sh → 0 |
| G | Avans | bitta Cash avans, sana = oyning 1-si, izoh `Excel'dan` |
| F, H | — | e'tiborsiz (hisoblanadi) |

Rejimlar: **Faqat xodimlar** (ro'yxatni to'ldirish) yoki **Oy sifatida** (tanlangan yil/oyga qatorlar bilan). Dublikat ism → mavjud xodimga bog'lanadi (so'raladi). Oldindan ko'rish: nechta xodim, nechtasi yangi, xatolar → **[Import]**.

### 5.8 Excel'ga eksport

Tanlangan oy → `Oylik_2026-10.xlsx`, `Лист1`, aynan 8 ustun, sarlavha qalin, raqam formati `#,##0`, **real formulalar**: F = `=E{r}*D{r}`, H = `=F{r}-G{r}`, oxirida JAMI (`=SUM`). Buxgalter Excel'da davom eta oladi.

### 5.9 Audit ekrani

`Sana · Nima · Eski → Yangi · Izoh`. Filtr: xodim, oy.

### 5.10 Ish jadvallari (2.2-bosqich)

Sozlamalar → **Ish jadvallari**: shablonlar ro'yxati (H1) — qo'shish, tahrirlash, nusxa, o'chirish (xodimga biriktirilmagan bo'lsa). Oldindan 3 ta namuna. Hisob siyosati: `Kechikishga ruxsat (daq)`, `Kelmadi deb hisoblash (daq)`, `Ortiqcha ish` (H7), rang chegaralari (H12).
Xodimlar jadvalida ustunlar: `Hisob usuli` (Kunbay / Jadval) va `Jadval` (shablon + Cycle uchun langar sana). Davomat ekranida xodim-kun katagi: reja vaqti, keldi/ketdi, kechikish, rang (H12); kontekst menyuda — `Dam`/`Boshqa vaqt` (H2) va `Sababli` (H8).

### 5.11 Hisob varaqasi (vedomost) — PDF

Har xodim uchun oy bo'yicha **A4 PDF** (QuestPDF), savolga o'rin qoldirmaydigan:
1. **Sarlavha:** korxona, tarmoq, xodim, oy, holat (Ochiq — `Dastlabki`, suv belgisi bilan / Yopiq — `Yakuniy`), hujjat raqami (`OY-2026-10-0042`), tuzilgan sana va vaqt.
2. **Shartlar:** hisob usuli; oklad; jadval nomi va vaqti; oydagi reja (kunlar, soatlar); soatlik (yoki kunlik) stavka va formulasi; kechikish ruxsati; ortiqcha ish siyosati.
3. **Kunlik jadval:** sana · hafta kuni · reja (08:00–20:00 yoki `dam`) · keldi · ketdi · ishlangan (`s:dd`) · kechikish · erta ketish · ortiqcha · hisoblangan summa · izoh. Qo'lda kiritilgan/tuzatilgan voqea — `*` belgi va sababi (audit). Sababli kunlar belgisi bilan.
4. **Jami:** reja va amaldagi soat, kechikish va erta ketish jami, asosiy haq, ortiqcha ish haqi, **hisoblangan jami**; avanslar ro'yxati (sana, summa, turi), o'tgan oy qarzi; **to'lanadigan qoldiq** (yoki qarz).
5. **Izoh:** hisoblash formulasi oddiy tilda (`6 000 000 ÷ 180 s = 33 333,33 so'm/soat × 172:30 = 5 750 000`).
6. **Imzolar:** `Hisobchi ______` · `Tanishdim: xodim ______  sana ____`.
Kunbay xodim uchun — Kun, Kunlik, Jami va davomat ma'lumoti (bo'lsa).
Tilda: interfeys tili (lotin/kirill/rus). Oylik ekranida qator menyusi/tugma: **[Hisob varaqasi]** → oldindan ko'rish oynasi: **[Chop etish] [Saqlash] [Telegram'ga yuborish]**. Barcha xodimlar uchun bitta fayl (har biri alohida sahifada) va **umumiy vedomost** (imzo ustuni bilan) — Oylik ekranidan, joriy tarmoq filtri bo'yicha.
**Telegram'ga yuborish:** PDF `Hujjatlar\Oylik\Vedomostlar\2026-10\` ga saqlanadi → fayl buferga **fayl sifatida** nusxalanadi (Explorer'dagi Ctrl+C kabi) → Telegram Desktop ochiladi → `Telegram'da chatni tanlang va Ctrl+V bosing` xabari. Telegram topilmasa — papka ochiladi.
**Chop etish:** Windows chop etish oynasi (printer tanlash).

---

## 6. Server (Oylik.Server) — 2-bosqich

**Vazifa:** telefon(lar) va desktop orasida voqealar va xodimlar ro'yxatini tashish. Pul ma'lumoti saqlamaydi.

**Joylashtirish — bitta kod, uch rejim (ochiq qaror):**
- **VPS + Docker (tavsiya):** HTTPS Caddy yoki Cloudflare orqali.
- **Ofis kompyuteri + Cloudflare Tunnel:** server Windows service, tunnel orqali chiqadi; kompyuter o'chiq bo'lsa telefon outbox'da kutadi.
- **Faqat LAN:** server ofis kompyuterida, telefon Wi-Fi orqali `http://192.168.x.x:5000`.

**Ma'lumotlar:**
`Installation(Id, Name, ApiKeyHash, CreatedAt)` · `Device(Id, InstallationId, Name, TokenHash, PairedAt, LastSeenAt, IsActive)` · `PairingCode(Code, InstallationId, ExpiresAt, UsedAt)` · `EmployeeSnapshot(InstallationId, EmployeeId, FullName, Note, IsActive, UpdatedAt)` · `AttendanceEvent(Seq PK autoincrement, Id unique, InstallationId, EmployeeId, Type, OccurredAt, DeviceId, ReceivedAt, IsVoided, VoidedAt, VoidReason)`

**Autentifikatsiya:** desktop — `X-Api-Key` (o'rnatma kaliti); telefon — `Authorization: Bearer <deviceToken>`. Ro'yxatdan o'tish uchun serverda `RegistrationKey` (appsettings) — begona o'rnatmalar qo'shilmasin.

**Endpointlar:**

| Metod | Yo'l | Kim | Vazifa |
|---|---|---|---|
| POST | `/api/installations` `{name, registrationKey}` → `{installationId, apiKey}` | desktop | bir marta ro'yxat |
| POST | `/api/installations/{id}/pairing-codes` → `{code, expiresAt}` | desktop | 6 xonali kod, 10 daqiqa |
| POST | `/api/devices/pair` `{installationId, code, deviceName}` → `{deviceId, deviceToken}` | telefon | bog'lash |
| PUT | `/api/employees` `[{id, fullName, note, isActive, updatedAt}]` | desktop | to'liq ro'yxat, upsert |
| GET | `/api/employees?updatedSince=` | telefon | ro'yxat yangilash |
| POST | `/api/attendance/events` `[{id, employeeId, type, occurredAt, deviceId}]` → `{accepted[], duplicate[]}` | telefon | batch, Id bo'yicha idempotent |
| POST | `/api/attendance/events/{id}/void` `{reason}` | telefon, desktop | bekor qilish |
| GET | `/api/attendance/events?sinceSeq=0&limit=500` → `{events[], lastSeq}` | desktop (60 s), telefon | kursor bo'yicha tortish |
| GET | `/api/health` | — | |

**QR payload (desktop ko'rsatadi):** `{"server":"https://…","installationId":"…","code":"482913","localServer":"http://192.168.1.19:5000"}` — `localServer` faqat ikkala manzil sozlanganda; eski ilova faqat `server` ni o'qiydi.

### 6.1 Ikki manzil (lokal + global)

Bitta server (bitta baza, bitta ApiKey) ikki manzil orqali: **lokal** (Wi-Fi/LAN, `http://192.168.x.x:5000`) va **global** (domen, `https://…`). Desktop ham, telefon ham:
- avval lokalga urinadi (`/api/health`, `Api.LocalServerProbeTimeout` = 2 s), javob bo'lmasa globalga o'tadi;
- globalda ishlayotganda lokalni har `Api.LocalServerRecheckInterval` (5 daqiqa) qayta tekshiradi; telefon Wi-Fi'ga ulanganda — darhol;
- faol yo'l eslab qolinadi (har so'rovda kutilmaydi); backoff faqat ikkala manzil ham ishlamasa o'sadi;
- holat: `Ulangan · lokal` / `Ulangan · internet orqali` / `Ulanmagan`.
Faqat bittasi sozlangan bo'lsa — o'sha ishlatiladi.

**Qoidalar:** voqea `Id` unique → takror jo'natish zararsiz; `OccurredAt` ISO 8601 offset bilan; server hech narsani o'chirmaydi; 90 kundan eski voqealar arxivlanishi mumkin (desktop allaqachon tortib olgan).

**Desktop sinxronizatsiya:** fon taymeri 60 s: `GET events?sinceSeq` → lokal bazaga upsert; xodim o'zgarsa 5 s debounce bilan `PUT employees`; status qatorida `Server: ulangan · oxirgi 10:41 · Qorovul-1 (10:40)`.

---

## 7. Android ilova (Oylik.Mobile) — 2-bosqich

**Umumiy:** Android 8.0+ (API 26), portret, katta tugmalar (≥ 56 dp), o'zbek (lotin), pul ma'lumoti yo'q, "ekran o'chmasin" sozlamasi.

**Lokal baza (sqlite-net-pcl):**
`Employee(Id, FullName, Note, IsActive, UpdatedAt)` · `Event(Id, EmployeeId, Type, OccurredAt, DeviceId, IsVoided, SyncState: Pending/Sent/Failed, Attempts, LastError)` · `Settings(ServerUrl, LocalServerUrl?, InstallationId, DeviceId, DeviceToken, DeviceName, LastSeq, LastSyncAt)`

**Ekranlar:**

1. **Bog'lash** (birinchi ishga tushganda): **[QR skanerlash]** yoki qo'lda: server URL + kod. Muvaffaqiyat → xodimlar yuklanadi.
2. **Xodimlar (asosiy):** qidirish; ro'yxat ism bo'yicha; har qator: `Ism · holat · bitta tugma`.
   - Holat: `Ichkarida · 08:02` (yashil) / `Ketdi · 17:30` (kulrang) / `—`.
   - Tugma: **KELDI** (yashil) agar ichkarida bo'lmasa; **KETDI** (to'q sariq) agar ichkarida bo'lsa.
   - Bosish → darhol yoziladi (vibratsiya) + snackbar `Олим ака — keldi 08:02 [BEKOR]` 60 soniya. **Tasdiq oynasi yo'q** — tezlik muhim.
   - "Ichkarida" aniqlash: oxirgi bekor qilinmagan voqea `In` va u 16 soat ichida bo'lsa.
   - A10 takror himoyasi.
3. **Bugun:** `Ism · Keldi · Ketdi · Soat` (ichkarida bo'lsa jonli hisob); yuqorida `Ichkarida N · Ketdi N · Kelmadi N`. Oxirgi 31 kunni ko'rish (sana tanlash).
4. **Sinxronizatsiya:** jo'natilmagan N · oxirgi muvaffaqiyat · server holati · **[Hozir sinxronlash]** · xato matni.
5. **Sozlamalar:** qurilma nomi, server, **[Uzish]** (tasdiq bilan), versiya.

**Sinxronizatsiya (outbox):** har voqea avval lokal SQLite'ga (`Pending`) → darhol jo'natishga urinish → muvaffaqiyatsiz bo'lsa fon taymeri 60 s (ilova ochiq bo'lganda va resume'da), eksponensial kutish 1 → 2 → 5 → 15 daqiqa. Har sinxronizatsiyada: `POST pending events` → `GET employees?updatedSince` → `GET events?sinceSeq` (boshqa qurilmalar). **Internet yo'q — hech narsa yo'qolmaydi, ilova to'liq ishlaydi.**

**Vaqt:** qurilmaning mahalliy vaqti offset bilan; server bilan farq 10 daqiqadan oshsa ekranda `Telefon soati noto'g'ri` ogohlantirishi.

**Xavfsizlik:** token `SecureStorage`da; uzilganda o'chadi; ilova PIN'siz (qorovul telefoni), lekin **[Uzish]** tasdiq bilan.

**Tarqatish:** APK fayl (Play Market'siz), "noma'lum manbalar" ruxsati; yangi versiya — qo'lda (3-bosqichda ilova ichidan yangilash tekshiruvi).

---

## 8. Desktop — Davomat ekrani (2-bosqich)

- Oy tanlash. Jadval: qatorlar — xodimlar; ustunlar — 1..31 kun; katak — soat (`9,5`).
  Rang: ≥ 8 yashil · 4–8 sariq · < 4 qizil · ochiq juftlik `?` to'q sariq · bo'sh — kelmagan.
  O'ngda: `Kelgan kunlar · Σ soat · Kun ekv. (soat ÷ 8)`.
- Katakka bosish → kun tafsiloti: voqealar (vaqt, tur, qurilma), **[Voqea qo'shish] [Bekor qilish (sabab)]** (A11).
- **Bugun** paneli: Ichkarida / Ketdi / Kelmagan ro'yxatlari, 60 s yangilanadi.
- **Tasdiqlash kerak:** 16 soatdan oshgan ochiq In'lar va yetim Out'lar — bir bosishda Out vaqti kiritiladi yoki bekor qilinadi.
- Oylik ekranida Kun katagi yonida A9 maslahati va **[Ko'chirish]**.

---

## 9. Bosqichlar va qabul mezonlari

### 1-bosqich — Desktop oylik (Excel o'rnini bosadi)

Qamrov: 3.1–3.6, 4.1, 5.1–5.9.

- [ ] `docs/sample.xlsx` (sintetik, mijoz Excel tuzilmasida) import qilinadi; `6` → 6 000 000, kunlik 200 000.
- [ ] Jadval Excel bilan bir xil 8 ustun; Kun va Avans kiritilganda Jami/Qoldiq darhol yangilanadi; JAMI qatori to'g'ri.
- [ ] Enter pastga, Tab o'ngga; har o'zgarish darhol saqlanadi; dastur qayta ochilganda hammasi joyida.
- [ ] Bir xodimga bir oyda bir nechta avans; yig'indi Аванс ustunida.
- [ ] Sentyabr `Qoldiq = −1 200 000` bilan yopilsa → Oktabrda `Sentyabr qarzi — 1 200 000` avansi; qayta ochilsa yo'qoladi; qayta yopilsa **bitta** (ikkita emas).
- [ ] Yopiq oy tahrirlanmaydi.
- [ ] Eksport qilingan Excel'da formulalar ishlaydi (Excel'da Kun o'zgartirilsa Jami o'zgaradi).
- [ ] Backup fayli yaratiladi.
- [ ] R1–R11 uchun xUnit testlar yashil.

### 2-bosqich — Server + APK + Davomat

Qamrov: 3.7–3.8, 4.2, 6, 7, 8.

- [ ] Desktop serverga ulanadi, QR ko'rsatadi; telefon QR'ni skanerlab bog'lanadi; xodimlar telefonda ko'rinadi.
- [ ] Telefonda internet o'chiq holda 10 ta Keldi/Ketdi → internet yoqilgach 2 daqiqa ichida desktopda, takrorsiz.
- [ ] Bugun ekranida soat to'g'ri (08:02 → 17:32 = 9,5 s).
- [ ] Tungi smena: 22:00 Keldi, ertasi 06:00 Ketdi → 8 s, oldingi kunga.
- [ ] Ketdi bosilmagan → desktopda "Tasdiqlash kerak"; qo'lda yopiladi.
- [ ] Davomat jadvali oy bo'yicha to'g'ri; Oylik ekranida `Davomat: N kun · M s` maslahati va **[Ko'chirish]** ishlaydi.
- [ ] A1–A12 testlar yashil.

### 2.1-bosqich — Tarmoqlar, ikki manzil, qulayliklar

- [ ] Interfeys 3 tilda (lotin/kirill/rus) va Yorug'/Qorong'i mavzu — darhol almashadi, qayta ishga tushirmasdan.
- [ ] Tahrir katagi katakni to'liq egallaydi, matn belgilangan, bitta qatorda; Enter/Tab saqlaydi, Esc bekor qiladi.
- [ ] Xodimlar jadvalda tahrirlanadi (3.1, 5.4).
- [ ] Tarmoq (3.9): filtr `Barchasi`/1/2; xulosa va JAMI filtr bo'yicha; yopiq oyda tarmoq o'zgarmaydi.
- [ ] Ikki manzil (6.1): Wi-Fi'da lokal, tashqarida global; Wi-Fi'ga qaytilganda lokalga o'tadi.
- [ ] Taskbar, Start menyu va `Oylik.exe` da dastur ikonkasi.

### 2.2-bosqich — Ish jadvali, soatbay hisob, hisob varaqasi

- [ ] Jadval shablonlari (Weekly, Cycle 1/1 sutkalik, 12 soat) va xodimga biriktirish; Kunbay xodimlar o'zgarmaydi.
- [ ] H4–H7: smena ichida sanash, grace, kechikish/erta ketish daqiqagacha, ortiqcha ish 3 siyosati; reja to'liq ishlansa Jami = Oklad aniq.
- [ ] H10: yopiq oy muzlatiladi, keyingi sozlama o'zgarishi ta'sir qilmaydi.
- [ ] H12: Bugun ekranida sariq/qizil/yashil jonli; kun yakunida foiz bo'yicha.
- [ ] Hisob varaqasi PDF (5.11) — 3 tilda; chop etish; Telegram'ga yuborish (fayl buferda + Telegram ochiladi).

### 3-bosqich — Qulayliklar

Admin parol (oy yopish/qayta ochish) · Velopack avto-yangilanish · APK ichidan yangilash · ikki telefon · Google Drive'ga backup · telefonda jadval bo'yicha holat (kechikmoqda/kelmadi).

---

## 10. Claude Code uchun ish tartibi

1. `Oylik.sln` skeleti — barcha loyihalar bo'sh holda build bo'lsin.
2. **Core:** entity'lar + 4.1 qoidalari + testlar — **UI'dan oldin.**
3. **Data:** DbContext, migratsiya, SQLite WAL, backup xizmati.
4. **Desktop:** Oylik ekrani (5.2) → Avanslar (5.3) → Xodimlar (5.4) → Oylar (5.5) → Import/Eksport (5.7–5.8) → Sozlamalar/Audit.
5. Har qadamda `dotnet build` va `dotnet test` yashil; UI matnlari `Strings.cs`da.
6. **2-bosqich:** Contracts → Server (+ Dockerfile) → Desktop sinxronizatsiya → Mobile.

`CLAUDE.md` ga: *"Avval `docs/TZ.md` ni o'qi. Qoidalar (4-bo'lim) o'zgartirilmaydi — savol bo'lsa so'ra."*

---

## 11. Ochiq qarorlar

| Savol | Variantlar | Default |
|---|---|---|
| Server joylashuvi | VPS + Docker / Ofis PC + Cloudflare Tunnel / faqat LAN | VPS + Docker |
| Tema | Semi.Avalonia / Fluent | Semi.Avalonia |
| Interfeys tili | lotin / kirill / rus | lotin (uchala til 2.1-bosqichda qo'shildi) |
