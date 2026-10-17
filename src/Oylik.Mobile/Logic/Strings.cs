namespace Oylik.Mobile.Logic;

/// <summary>Qorovul ilovasining barcha UI matnlari (o'zbek, lotin).</summary>
public static class Strings
{
    public const string AppTitle = "Oylik · Qorovul";

    // Tablar
    public const string TabEmployees = "Xodimlar";
    public const string TabToday = "Bugun";
    public const string TabSync = "Sinxron";
    public const string TabSettings = "Sozlamalar";

    // Bog'lash
    public const string PairTitle = "Telefonni bog'lash";
    public const string PairHint = "Kompyuterda Sozlamalar → «QR ko'rsatish» ni bosing va kodni skanerlang. Kod 10 daqiqa amal qiladi.";
    public const string PairScan = "QR skanerlash";
    public const string PairOrManual = "yoki qo'lda";
    public const string PairServer = "Server manzili";
    public const string PairCode = "Bog'lash kodi";
    public const string PairCodeHint = "6 xonali kod";
    public const string PairDeviceName = "Qurilma nomi";
    public const string PairButton = "Bog'lash";
    public const string PairBusy = "Bog'lanmoqda…";
    public const string PairDemoNote = "Sinov uchun server manziliga «demo» yozing.";
    public const string DefaultDeviceName = "Qorovul-1";
    public const string ScanTitle = "QR kodni skanerlang";
    public const string ScanCameraDenied = "Kameraga ruxsat berilmadi. Kodni qo'lda kiriting.";
    public const string ErrBadQr = "Bu QR kod Oylik bog'lash kodi emas.";
    public const string ErrBadServer = "Server manzili noto'g'ri (masalan http://192.168.1.10:5000).";
    public const string ErrTooManyAttempts = "Ko'p urinish — 1 daqiqadan keyin qayta urinib ko'ring.";
    public const string ErrCodeRejected = "Kod noto'g'ri yoki muddati o'tgan. Kompyuterda yangi kod oling.";
    public const string ErrBadCode = "Kod 6 ta raqamdan iborat bo'lishi kerak.";
    public const string ErrBadDeviceName = "Qurilma nomini kiriting.";
    public const string DeviceRevoked = "Qurilma uzilgan, qayta bog'lang.";
    public const string PairLocalServer = "Lokal manzil (Wi-Fi), ixtiyoriy";
    public const string PairLocalHint = "Ofisda Wi-Fi orqali tezroq ishlash uchun (masalan http://192.168.1.10:5000). Javob bermasa — yuqoridagi manzil ishlatiladi.";
    public const string ErrBadLocalServer = "Lokal manzil noto'g'ri (masalan http://192.168.1.10:5000) yoki bo'sh qoldiring.";
    public const string ErrPairInProgress = "Bog'lanmoqda — biroz kuting.";
    public const string ErrCodeUsed = "Bu kod allaqachon ishlatilgan. Kompyuterda yangi kod oling.";
    public const string ErrCodeExpired = "Kod muddati o'tgan. Kompyuterda yangi kod oling.";

    // Xodimlar
    public const string Search = "Ism bo'yicha qidirish";
    public const string In = "KELDI";
    public const string Out = "KETDI";
    public const string Inside = "Ichkarida";
    public const string Left = "Ketdi";
    public const string Dash = "—";
    public const string Undo = "BEKOR";
    public const string Came = "keldi";
    public const string Went = "ketdi";
    public const string AlreadyMarked = "Allaqachon belgilangan";
    public const string Undone = "Bekor qilindi";
    public const string UndoReason = "Qorovul bekor qildi";
    public const string NoEmployees = "Xodimlar hali yuklanmagan. Sinxronlashni kuting.";
    public const string NothingFound = "Hech kim topilmadi";
    public const string ClockWrong = "Telefon soati noto'g'ri";

    // Holat belgisi
    public const string PillSynced = "Sinxron";
    public const string PillPending = "Jo'natilmagan";
    public const string PillOffline = "Oflayn";

    // Bugun
    public const string Date = "Sana";
    public const string ColName = "Ism";
    public const string ColIn = "Keldi";
    public const string ColOut = "Ketdi";
    public const string ColHours = "Soat";
    public const string CountInside = "Ichkarida";
    public const string CountLeft = "Ketdi";
    public const string CountAbsent = "Kelmadi";
    public const string Absent = "kelmadi";
    public const string LiveNote = "• ichkarida — soat jonli hisoblanadi";

    // Sinxronizatsiya
    public const string SyncTitle = "Sinxronizatsiya";
    public const string SyncPending = "Jo'natilmagan";
    public const string SyncLastOk = "Oxirgi muvaffaqiyat";
    public const string SyncServer = "Server";
    public const string SyncNow = "Hozir sinxronlash";
    public const string SyncBusy = "Sinxronlanmoqda…";
    public const string SyncNever = "hali yo'q";
    public const string SyncConnected = "ulangan";
    public const string ServerLocal = "lokal";
    public const string ServerInternet = "internet orqali";
    public const string ServerNotConnected = "ulanmagan";
    public const string SyncError = "Xato";
    public const string SyncNextTry = "Keyingi urinish";
    public const string SyncOfflineSafe = "Internet yo'q bo'lsa ham hech narsa yo'qolmaydi — voqealar telefonda saqlanadi.";
    public const string ErrNoNetwork = "Server bilan aloqa yo'q (internet yoki server o'chiq).";
    public const string ErrServer = "Server xatosi";
    public const string ErrRejected = "Server voqeani qabul qilmadi";
    public const string SyncFailed = "Server rad etgan voqealar";
    public const string SyncFailedHint = "Bu voqealar qayta yuborilmaydi — kompyuterda qo'lda kiriting.";

    // Sozlamalar
    public const string SettingsTitle = "Sozlamalar";
    public const string SettingsDevice = "Qurilma nomi";
    public const string SettingsServer = "Server";
    public const string SettingsInstallation = "Kompyuter ID";
    public const string SettingsKeepScreenOn = "Ekran o'chmasin";
    public const string SettingsVersion = "Versiya";
    public const string SettingsEditAddresses = "Manzillarni o'zgartirish";
    public const string AddrServer = "Server manzili";
    public const string AddrLocal = "Lokal manzil (Wi-Fi)";
    public const string AddrHint = "Saqlashdan oldin har bir manzil tekshiriladi. Qayta bog'lash shart emas.";
    public const string AddrSave = "Tekshirish va saqlash";
    public const string AddrChecking = "Tekshirilmoqda…";
    public const string AddrSaved = "Manzillar saqlandi.";
    public const string AddrCancel = "Bekor";
    public const string ErrAddrUnreachable = "javob bermadi — saqlanmadi.";
    public const string ErrAddrOtherServer = "bu qurilmani tanimadi (boshqa server) — saqlanmadi.";
    public const string Disconnect = "Uzish";
    public const string DisconnectTitle = "Telefonni uzish";
    public const string DisconnectAsk = "Telefon serverdan uziladi. Qayta ishlatish uchun yangi kod bilan bog'lash kerak bo'ladi.";
    public const string DisconnectPendingWarn = "Jo'natilmagan voqealar telefonda saqlanib qoladi va qayta bog'langach jo'natiladi.";
    public const string Yes = "Uzish";
    public const string No = "Yo'q";
    public const string Demo = "demo (sinov)";
}
