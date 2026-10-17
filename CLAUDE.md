# Oylik

Avval `docs/TZ.md` ni o'qi. Qoidalar (4-bo'lim: R, A, H) o'zgartirilmaydi — savol bo'lsa so'ra.

- Build: `dotnet build Oylik.sln` · Test: `dotnet test Oylik.sln`
- Migratsiya: `dotnet tool restore` keyin `dotnet ef migrations add <Name> -p src/Oylik.Data -s src/Oylik.Data`
- Pul — `long` (so'm), Kun — `decimal`, vaqt — `DateTimeOffset` (UTC saqlanadi).
- UI matnlari faqat `src/Oylik.Desktop/Resources/Strings.cs` da.
- Namuna Excel: `docs/sample.xlsx` — sintetik (o'ylab topilgan ismlar), mijoz Excel tuzilmasida; `docs/make_sample.py` yasaydi. Haqiqiy mijoz fayli `docs/namuna.xlsx` faqat lokal (.gitignore) — repoga va kodga mijoz xodimlari ismlari kirmasin (repo public).
- Server: `docker compose -f src/Oylik.Server/docker-compose.yml up -d --build` → http://localhost:5000. Ro'yxat kaliti `src/Oylik.Server/.env` da (namuna `.env.example`) — qiymatini hech qayerga yozmang.
- Server migratsiyasi: `dotnet ef migrations add <Name> -p src/Oylik.Server -s src/Oylik.Server`.
- Desktop↔server sinxronizatsiya mantig'i: `src/Oylik.Data/Sync` (UI'siz; `tests/Oylik.Server.Tests` da testlanadi).
- Mobil: `Oylik.Mobile.sln`; APK: `dotnet publish src/Oylik.Mobile/App/Oylik.Mobile.csproj -c Release -f net10.0-android` → `src/Oylik.Mobile/publish/`. Imzo kaliti `src/Oylik.Mobile/App/signing/` — yo'qotilmasin (yangilanishlar shu kalit bilan).
- Publish: `dotnet publish src/Oylik.Desktop -c Release -r win-x64 -o publish` → bitta `Oylik.exe`.
- Sinov ma'lumotlari: `OYLIK_HOME=<papka>` muhit o'zgaruvchisi `%LOCALAPPDATA%\Oylik` o'rniga ishlatiladi.
- UI'ni ko'rish: `Oylik.exe --snapshot <papka> [--seed docs/sample.xlsx] [--lang uz-Latn|uz-Cyrl|ru] [--theme light|dark] [--switch-lang ru] [--switch-theme dark]` — har ekranni PNG ga chizadi va chiqadi (`OYLIK_HOME` bilan ishlating).

## 2-bosqich — ish taqsimoti (3 sessiya)

- `oy` — nazoratchi: vazifa beradi, natijani tekshiradi. `src/Oylik.Contracts` egasi (DTO o'zgarishi faqat `oy` orqali).
- `oy-desktop` — Core davomat qoidalari (A1–A12) + testlar, Data (AttendanceEvent, Device, migratsiya), `src/Oylik.Server` (+Dockerfile), Desktop sinxronizatsiya, Davomat ekrani, QR, A9 maslahat.
- `oy-apk` — faqat `src/Oylik.Mobile` (.NET MAUI Android). Alohida yechim: `Oylik.Mobile.sln` (Contracts + Mobile). `Oylik.sln` ga QO'SHILMAYDI — desktop build/test sekinlashmasin.
- Boshqa sessiyaning papkasidagi fayllarni tahrirlamang; kerak bo'lsa `oy` ga yozing.
- Haqiqiy baza (`%LOCALAPPDATA%\Oylik`) va `demo\data` ga tegmang; sinov uchun `OYLIK_HOME` = scratch papka.
