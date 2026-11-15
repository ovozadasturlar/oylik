# Oylik server: PostgreSQL bazasi

Prod'da server (`src/Oylik.Server`) **PostgreSQL**'da ishlaydi. Desktop va agent bazasi (`Oylik.Data`) avvalgidek SQLite'da qoladi. Bu hujjat faqat serverga tegishli.

| Sozlama | Qiymat |
|---|---|
| `Oylik:Database` (env `Oylik__Database`) | `postgres` (default) yoki `sqlite` (faqat testlar va lokal sinov uchun) |
| `ConnectionStrings:Oylik` (env `ConnectionStrings__Oylik`) | PostgreSQL ulanish satri. Postgres rejimida berilmasa, server aniq xato bilan to'xtaydi. |

Migratsiyalar (`src/Oylik.Server/Migrations`) PostgreSQL uchun yozilgan. Ular **server ishga tushganda avtomatik** qo'llanadi (`MigrateAsync`). SQLite rejimida sxema modeldan yaratiladi (`EnsureCreated`), migratsiyalar ishlatilmaydi.

## 1. Standart: `docker compose up` (PostgreSQL compose ichida)

Toza kompyuterda server bitta buyruq bilan ishga tushadi. `docker-compose.yml` o'zi ikki servisni ko'taradi:

- `db` — `postgres:16-alpine`. Ma'lumot `oylik-pgdata` volume'ida saqlanadi.
  - `db` host portiga **chiqmaydi**: kompyuterdagi boshqa PostgreSQL bilan (masalan 5432-portda) to'qnashmaydi.
  - Server unga faqat compose ichki tarmog'idan, `db:5432` orqali ulanadi.
- `oylik-server` — `db` sog'lom bo'lishini kutib ishga tushadi (healthcheck) va host'ning 5000-portida ochiladi.

1. `src/Oylik.Server/.env.example` faylidan `src/Oylik.Server/.env` nusxasini oling va qiymatlarni to'ldiring. `.env` fayli `.gitignore` da, repoga tushmaydi.

   ```
   OYLIK_REGISTRATION_KEY=<uzun tasodifiy kalit>
   OYLIK_DB_PASSWORD=<uzun tasodifiy parol>
   ```

   - Ixtiyoriy: `OYLIK_PORT` (default 5000) va `OYLIK_CONTAINER_NAME` (default `oylik-server`).
   - Bo'sh `OYLIK_DB_PASSWORD` bilan `db` ishga tushmaydi, server ham uni kutib qoladi.
   - Parol `db` birinchi marta ishga tushganda o'rnatiladi. Keyin `.env` da o'zgartirish yetmaydi: PostgreSQL ichida `ALTER ROLE oylik PASSWORD ...` ham kerak.

2. Repo ildizidan ishga tushiring:

   ```powershell
   docker compose -f src/Oylik.Server/docker-compose.yml up -d --build
   ```

   Migratsiya server ishga tushganda avtomatik qo'llanadi.

**Zaxira nusxa:**

```powershell
docker compose -f src/Oylik.Server/docker-compose.yml exec db pg_dump -U oylik oylik > oylik-server.sql
```

`down -v` buyrug'i `oylik-pgdata` volume'ini, ya'ni barcha ma'lumotni **o'chiradi**. Prod'da faqat `down` ishlating.

## 2. Muqobil: mavjud tashqi PostgreSQL (`docker-compose.external-pg.yml`)

Bu rejimda `db` servisi o'chiq turadi: u profilda, shuning uchun ishga tushmaydi. Server tashqi PostgreSQL'dagi alohida `oylik` bazasiga ulanadi.

**2.1. Baza va user yaratish.** Tashqi PostgreSQL'da superuser bilan bajaring. Boshqa dasturning bazasiga tegilmaydi. Parolni faqat `.env` ga yozing.

```sql
CREATE ROLE oylik LOGIN PASSWORD '<kuchli-parol>';
CREATE DATABASE oylik OWNER oylik ENCODING 'UTF8' TEMPLATE template0;
REVOKE ALL ON DATABASE oylik FROM PUBLIC;
-- PostgreSQL 15+: public sxemasi egasi — oylik (migratsiyalar jadval yarata olsin)
\c oylik
ALTER SCHEMA public OWNER TO oylik;
```

PostgreSQL docker konteynerda bo'lsa, `psql` ni shu konteyner ichida ishga tushiring: `docker exec -it <pg-konteyner> psql -U <superuser>`.

**2.2. `.env` ga ulanish satri.**

```
OYLIK_REGISTRATION_KEY=<maxfiy kalit>
OYLIK_DB_CONNECTION=Host=host.docker.internal;Port=5432;Database=oylik;Username=oylik;Password=<parol>
```

`Host` qiymati PostgreSQL qayerda turganiga bog'liq:

- **PostgreSQL host'da port ochgan bo'lsa** (masalan `0.0.0.0:5432->5432`): `Host=host.docker.internal`. Override `extra_hosts: host-gateway` qo'shadi, shuning uchun bu Linux Docker'da ham ishlaydi.
- **PostgreSQL boshqa compose'ning network'ida bo'lsa:**
  1. Network nomini aniqlang: `docker inspect <pg-konteyner> --format "{{range $k,$v := .NetworkSettings.Networks}}{{$k}} {{end}}"`.
  2. Ulanish satriga `Host=<pg-konteyner yoki servis nomi>` yozing.
  3. Override faylidagi `networks` bloklarini izohdan chiqaring.
  4. `.env` ga `OYLIK_PG_NETWORK=<network-nomi>` ni yozing.

  Oylik konteyneri o'sha network'ga faqat qo'shiladi: PostgreSQL konteyneri, uning volume'i va sozlamalari o'zgarmaydi.

**2.3. Ishga tushirish.**

```powershell
docker compose -f src/Oylik.Server/docker-compose.yml -f src/Oylik.Server/docker-compose.external-pg.yml up -d --build
```

## 3. Tekshirish

```powershell
docker logs oylik-server --tail 20     # "Baza: Postgres" — migratsiya o'tdi
curl http://localhost:5000/api/health  # {"status":"ok",...}
```

Bazada `__EFMigrationsHistory`, `Installations`, `ApiKeys`, `Employees`, `Events` va boshqa jadvallar paydo bo'ladi.

Eski SQLite `oylik-data` docker volume'i endi ishlatilmaydi. Prod'da ko'chiriladigan ma'lumot bo'lmagani uchun uni `docker volume rm` bilan o'chirish mumkin.

## 4. Dasturchi uchun

Yangi migratsiya qo'shish (`dotnet ef` PostgreSQL migratsiyasi chiqaradi, bunda bazaga ulanilmaydi):

```powershell
dotnet ef migrations add <Name> -p src/Oylik.Server -s src/Oylik.Server
```

`dotnet ef database update` kabi bazaga ulanadigan buyruqlar uchun ulanish satri `OYLIK_DESIGN_PG` env'dan olinadi:

```powershell
$env:OYLIK_DESIGN_PG = "Host=localhost;Port=5432;Database=oylik;Username=oylik;Password=<parol>"
```

Testlar:

- `tests/Oylik.Server.Tests` SQLite'da ishlaydi (`Oylik:Database=sqlite`), Docker kerak emas.
- PostgreSQL integratsiya testi (`PostgresTests`) faqat `OYLIK_TEST_PG` berilganda ishlaydi, aks holda Skipped bo'ladi.
- Bu test har safar vaqtinchalik baza yaratadi va oxirida o'chiradi, shuning uchun `CREATE DATABASE` huquqi kerak.
- Mavjud bazalardan alohida, vaqtinchalik konteyner bilan ishga tushirish mumkin:

```powershell
docker run -d --rm --name oylik-pg-test -p 127.0.0.1:55433:5432 -e POSTGRES_USER=oylik_test -e POSTGRES_PASSWORD=<vaqtinchalik> postgres:latest
$env:OYLIK_TEST_PG = "Host=127.0.0.1;Port=55433;Database=postgres;Username=oylik_test;Password=<vaqtinchalik>"
dotnet test tests/Oylik.Server.Tests --filter FullyQualifiedName~PostgresTests
docker stop oylik-pg-test   # --rm: konteyner va anonim volume o'chadi
```
