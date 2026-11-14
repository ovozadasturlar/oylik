# Oylik.Agent — fon xizmati (Bosqich 2)

Desktop ochiq bo'lmasa ham yuz terminali davomatini yig'ib, lokal bazaga yozadi va serverga yetkazadi.
Oynasi yo'q (Startup rejimida faqat tray ikonasi). Desktop bilan **bitta baza** (`%LOCALAPPDATA%\Oylik\oylik.db` yoki `OYLIK_HOME`) ishlatiladi.

- Har 10 soniyada: desktop yozgan o'zgarishlarni bazadan oladi (xodimlar, qo'lda tuzatishlar, sozlamalar), keyin terminalni so'raydi (yangi keldi-ketdi). IP o'zgarsa, terminalni seriya raqami bo'yicha qayta topadi.
- Har 60 soniyada server bilan sinxronlanadi (server sozlangan bo'lsa).
- Oylik qatorlariga tegmaydi. Ish haqi hisobi desktopda qoladi.
- Terminalni faqat bitta jarayon so'raydi. Agent nomli mutex ushlab turadi (`Global\Oylik.TerminalPoller.<baza-xeshi>`). Desktop shu mutexni ko'rsa, terminal va serverga o'zi ulanmaydi, faqat ko'rsatadi: sidebar'da «fon xizmati orqali» yozuvi chiqadi. Agent to'xtasa, desktop darhol o'zi so'rashga qaytadi.
- Bir baza uchun ikkinchi agent ishga tushmaydi (mutex band bo'lsa darhol chiqadi).
- Log: `<baza papkasi>\logs\agent-YYYYMMDD.log`.

## Agent ishlayotganini qanday ko'rish mumkin

| Rejim | Tray ikonasi | Qayerda ko'rinadi |
|---|---|---|
| B — Startup (foydalanuvchi kirganda) | **bor** | soat yonida (tray) Oylik ikonasi + desktop sidebar + log |
| A — Windows service | yo'q (service UI'siz sessiyada ishlaydi) | desktop sidebar'da «fon xizmati orqali» + log |

**Agent ishlayotgani ko'rinib turishi kerak bo'lsa — B (Startup) variantini tanlang.**

Tray ikonasi (B rejimi):

- Ikonaning burchagidagi nuqta holatni ko'rsatadi:
  - yashil — terminal ulangan va server sinxron (yoki server sozlanmagan);
  - sariq — terminal o'chiq yoki server xatosi;
  - qizil — terminal yoqilgan, lekin topilmadi;
  - kulrang — ishga tushmoqda.
- Sichqonchani ikona ustiga olib borilsa, qisqa holat chiqadi: `Terminal: ulangan · oxirgi 10:41`, `Bugun: 12 ta voqea`, `Server: sinxron · oxirgi 10:40`.
- Ikona bosilsa, kichik holat oynasi ochiladi, oxirgi xato matni ham shu yerda ko'rinadi.
- O'ng tugma bosilsa, menyu chiqadi:
  - holat qatorlari;
  - «Holat» — holat oynasini ochadi;
  - «Loglarni ochish» — log papkasini ochadi;
  - «Chiqish» — agentni to'xtatadi. Desktop shundan keyin terminal va serverga o'zi ulanadi.
- Matnlar desktopda tanlangan tilda chiqadi.

Agent oddiy foydalanuvchi sessiyasida ishga tushsa, tray o'zi yoqiladi. Service sifatida ishga tushsa, tray ochilmaydi. `--no-tray` parametri tray'ni foydalanuvchi sessiyasida ham o'chiradi.

## Yig'ish

```
dotnet publish src/Oylik.Agent -c Release -r win-x64 -o publish/agent
```

Natija: bitta `Oylik.Agent.exe` (self-contained). Uni masalan `C:\Oylik\` ga qo'ying (desktop `Oylik.exe` yonida).

Parametrlar:

- `--home <papka>` — baza papkasi. Berilmasa: `OYLIK_HOME`, bo'lmasa `%LOCALAPPDATA%\Oylik`.
- `--no-tray` — foydalanuvchi sessiyasida ham tray ikonasisiz ishlaydi.

## Muhim: qaysi Windows foydalanuvchisi

Agent desktop ishlatadigan **o'sha Windows foydalanuvchisi** nomidan ishlashi kerak. Sabablari:

1. Baza shu foydalanuvchining `%LOCALAPPDATA%\Oylik` papkasida joylashgan.
2. Terminal paroli va server kaliti bazada DPAPI (CurrentUser) bilan shifrlangan. Boshqa hisob, masalan `LocalSystem`, ularni ocha olmaydi. Bunday holda agent ishlaydi, lekin terminal va serverga ulanmaydi.

Service'ni `LocalSystem` nomidan o'rnatmang.

## A variant — Windows service (kompyuter yonishi bilan, kirishdan oldin ham; tray ikonasi yo'q)

PowerShell'ni **administrator** sifatida oching:

```powershell
$cred = Get-Credential "$env:COMPUTERNAME\$env:USERNAME"   # shu foydalanuvchi va uning Windows paroli
New-Service -Name OylikAgent -DisplayName "Oylik Agent" `
  -BinaryPathName '"C:\Oylik\Oylik.Agent.exe"' `
  -StartupType Automatic -Credential $cred `
  -Description "Oylik: yuz terminali davomati -> baza -> server"
sc.exe failure OylikAgent reset= 86400 actions= restart/60000/restart/60000/restart/60000
Start-Service OylikAgent
```

- Foydalanuvchining Windows paroli bo'lishi shart: parolsiz hisob service hisobi bo'la olmaydi. Parol yo'q bo'lsa, B variantdan foydalaning.
- `OYLIK_HOME` kerak bo'lsa: `-BinaryPathName '"C:\Oylik\Oylik.Agent.exe" --home "D:\OylikData"'`.
- «Log on as a service» huquqini `New-Service` odatda o'zi beradi. Bermasa: `secpol.msc` → Local Policies → User Rights Assignment.

Holat va to'xtatish:

```powershell
Get-Service OylikAgent
Stop-Service OylikAgent
sc.exe delete OylikAgent        # o'chirish
```

## B variant — Startup (foydalanuvchi kirganda; tray ikonasi bor — ko'rinuvchanlik uchun tavsiya)

Administrator huquqi shart emas. Agent foydalanuvchi Windows'ga kirgandan keyin ishga tushadi. Oyna ochilmaydi, faqat tray'da ikona turadi.

```powershell
schtasks /Create /TN "Oylik Agent" /TR '"C:\Oylik\Oylik.Agent.exe"' /SC ONLOGON /RL LIMITED /F
schtasks /Run /TN "Oylik Agent"          # hozir ishga tushirish
schtasks /Delete /TN "Oylik Agent" /F    # o'chirish
```

Yoki: `Win+R` → `shell:startup` → `Oylik.Agent.exe` ga yorliq qo'ying.

Farqi: service kompyuter yonishi bilan ishlaydi, lekin ko'rinmaydi. Startup varianti faqat foydalanuvchi kirgandan keyin ishlaydi va tray'da ko'rinib turadi.

Ikkalasini birga o'rnatmang. Ikkinchi nusxa mutex'ni band ko'rib darhol chiqadi, shuning uchun tray ikonasi baribir chiqmaydi.

## Tekshirish

1. `logs\agent-*.log` faylida `Oylik.Agent ishga tushdi: ... rejim: tray` (Startup) yoki `rejim: headless` (service) yozuvi bor. Startup rejimida tray'da Oylik ikonasi ham ko'rinadi.
2. Desktop'ni oching: sidebar'da «Server: fon xizmati orqali», Sozlamalar → Face ID terminal bo'limida «Terminal: fon xizmati orqali · oxirgi HH:mm» chiqadi.
3. Terminalda yuz tanitib ko'ring. Desktop ochiq bo'lsa, 10–20 soniyada Davomat ekranida ko'rinadi.

Agentni qo'lda sinash uchun: `Oylik.Agent.exe --home <scratch papka>`. To'xtatish: tray menyusida «Chiqish» (`--no-tray` bilan ishga tushgan bo'lsa — Task Manager).
