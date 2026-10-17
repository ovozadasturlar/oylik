# Oylik

Excel'dagi oylik ish haqi jadvalini almashtiradigan Windows dastur. To'liq talablar: [docs/TZ.md](docs/TZ.md).

## Holat

**1-bosqich (Desktop oylik)** — ishlaydi:

- Oylik jadvali: Excel bilan bir xil 8 ustun, har katak darhol saqlanadi, JAMI qatori, qisqa kiritish (R11)
- Avanslar oynasi, o'tgan oydan qarz (R6/R8), yopiq oy faqat o'qish uchun
- Xodimlar, Oylar, Audit, Sozlamalar ekranlari
- Excel'dan import (namuna: `docs/sample.xlsx` — sintetik ismlar, `docs/make_sample.py` bilan yasaladi) va formulali eksport
- SQLite (WAL), kunlik va oy yopishdan oldingi backup, Serilog

**2-bosqich (server, Android, davomat)** — desktop va server qismi tayyor:

- `src/Oylik.Server` — ASP.NET Core minimal API (TZ 6): o'rnatma ro'yxati, telefon bog'lash (6 xonali kod, 10 daqiqa), xodimlar ro'yxati, davomat voqealari (`Id` bo'yicha idempotent, `sinceSeq` kursor, bekor qilish). Pul ma'lumoti yo'q.
- Desktop: Sozlamalar → **Server va telefon** (ulash, QR, bog'langan telefonlar), fon sinxronizatsiyasi (60 s), **Davomat** ekrani, Oylik jadvalida davomat maslahati va **[Ko'chirish]** (A9).
- Davomat qoidalari A1–A12 — `src/Oylik.Core/Domain/AttendanceRules.cs`.
- Android ilova — `src/Oylik.Mobile` (alohida yechim: `Oylik.Mobile.sln`).

## Ishga tushirish

```
dotnet test Oylik.sln
dotnet run --project src/Oylik.Desktop
dotnet publish src/Oylik.Desktop -c Release -r win-x64 -o publish
```

Ma'lumotlar: `%LOCALAPPDATA%\Oylik\` (`oylik.db`, `backups\`, `logs\`).

Til va ko'rinish — Sozlamalar → «Ko'rinish» (sahifa tepasida): mavzu (Yorug' / Qorong'i / Tizim bo'yicha) va
til (O'zbekcha lotin / Ўзбекча кирилл / Русский) darhol, qayta ishga tushirishsiz almashadi; sidebar pastidagi
quyosh/oy tugmasi — Yorug' ↔ Qorong'i. Matnlar `src/Oylik.Desktop/Resources/Strings.cs` da (lotin + rus; kirill
lotindan transliteratsiya), XAML'da `{r:T Kalit}` bilan jonli bog'lanadi; ranglar `Styles/AppStyles.axaml` da (Light/Dark).
Snapshot: `--lang uz-Latn|uz-Cyrl|ru --theme light|dark`; jonli almashish isboti: `--switch-lang ru --switch-theme dark`.

## Ish jadvali va hisob varaqasi (2.2-bosqich)

- Xodimlar → «Hisob usuli» (Kunbay / Jadval bo'yicha) va «Jadval» (shablon, amal qilish sanasi, tarix). Sozlamalar → «Ish jadvallari»
  (3 ta namuna) va hisob siyosati (kechikishga ruxsat, «Kelmadi», ortiqcha ish, rang foizlari). Qoidalar — `docs/TZ.md` 4.3 (H1–H14).
- Oylik → «Hisob varaqalari» (barcha xodimlar + umumiy vedomost) yoki qator menyusi → «Hisob varaqasi»: oldindan ko'rish,
  [Chop etish] [Saqlash] [Telegram'ga yuborish] (`Hujjatlar\Oylik\Vedomostlar\YYYY-MM\`, fayl buferga).
- PDF — [QuestPDF](https://www.questpdf.com) **Community** litsenziyasi bilan (`QuestPDF.Settings.License = LicenseType.Community`).
  Korxonaning yillik daromadi **$1 mln dan oshsa — QuestPDF tijorat litsenziyasi** sotib olinishi kerak.
- PDF shrifti — Noto Sans (SIL Open Font License, `src/Oylik.Desktop/Assets/Fonts/OFL.txt`), dastur ichiga joylangan.
- Snapshot: `--now 2026-10-13T08:12:00+05:00` — «hozir»ni simulyatsiya (Davomat holat ranglari); PDF namunalar ham shu papkaga yoziladi.

## Server (2-bosqich)

1. Ro'yxatdan o'tish kalitini tayyorlang: `src/Oylik.Server/.env.example` → `src/Oylik.Server/.env` nusxa oling va `OYLIK_REGISTRATION_KEY=` ga uzun tasodifiy qiymat yozing. `.env` maxfiy — tarqatmang.
2. Ishga tushirish (repo ildizidan):

   ```
   docker compose -f src/Oylik.Server/docker-compose.yml up -d --build
   ```

   Server `http://localhost:5000` da (`/api/health`). Ma'lumotlar `oylikserver_oylik-data` volume'ida (SQLite). Internetga HTTPS orqali chiqarish uchun oldiga Caddy yoki Cloudflare Tunnel qo'yiladi.
   Docker'siz: `dotnet run --project src/Oylik.Server` (kalit: `Oylik__RegistrationKey` muhit o'zgaruvchisi).
3. Desktop: **Sozlamalar → Server va telefon** — Server URL (telefon ham ulana oladigan manzil, masalan `http://192.168.1.10:5000` yoki `https://…`) va ro'yxatdan o'tish kaliti → **[Serverga ulash]**. Kompyuter ID bilan bir marta ro'yxatdan o'tadi; API kalit kompyuterda DPAPI bilan shifrlangan holda saqlanadi.
4. Telefon: **[QR ko'rsatish]** → qorovul telefonida Oylik ilovasi QR'ni skanerlaydi (yoki server manzili va 6 xonali kodni qo'lda kiritadi). Bog'langan telefonlar shu yerda ko'rinadi, **[Uzish]** bilan o'chiriladi.
5. **Davomat** ekrani: xodim × kun jadvali (soat, ranglar), Bugun paneli, «Tasdiqlash kerak» (Ketdi'siz Keldi, yetim Ketdi), kun tafsiloti — voqea qo'shish / bekor qilish (sabab majburiy, audit). Oylik ekranida «Davomat» ustuni (shu oyda davomat bo'lsa) — bosilsa kelgan kunlar **Кун** ga ko'chiriladi; avtomatik yozilmaydi.

Server o'chiq bo'lsa desktop jim ishlaydi (status: `Server: ulanmagan`), qo'lda kiritilgan tuzatishlar server qaytganda yuboriladi.

Testlar: `tests/Oylik.Core.Tests` (oylik va davomat qoidalari), `tests/Oylik.Server.Tests` (API va desktop sinxronizatsiyasi, WebApplicationFactory).
