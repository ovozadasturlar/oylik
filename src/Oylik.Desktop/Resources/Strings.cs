using System.Text.RegularExpressions;
using Oylik.Core.Domain;
using Oylik.Data;

namespace Oylik.Desktop.Resources;

/// <summary>
/// Barcha UI matnlari shu yerda, 3 tilda: o'zbek lotin (manba), o'zbek kirill (lotindan transliteratsiya,
/// <see cref="UzCyrillic"/>; zarur bo'lsa uchinchi argument bilan aniq beriladi) va rus.
/// Til <see cref="SetLanguage"/> bilan jonli almashadi: XAML matnlari `{r:T Kalit}` (<see cref="Loc"/>) orqali bog'langan,
/// ViewModel'lar <see cref="LanguageChanged"/> da o'z matnlarini qayta hisoblaydi.
/// </summary>
public static partial class Strings
{
    public static UiLanguage Language { get; private set; }

    /// <summary>Til almashdi — <see cref="Loc"/> (XAML) va ViewModel'lar matnlarini qayta hisoblaydi.</summary>
    public static event Action? LanguageChanged;

    public static void SetLanguage(UiLanguage language)
    {
        if (Language == language)
            return;
        Language = language;
        LanguageChanged?.Invoke();
    }

    /// <summary>Oddiy matn: lotin, rus [, kirill — transliteratsiya to'g'ri chiqmasa].</summary>
    private static string L(string uz, string ru, string? cyr = null) => Language switch
    {
        UiLanguage.UzCyrl => cyr ?? UzCyrillic.FromLatin(uz),
        UiLanguage.Ru => ru,
        _ => uz,
    };

    /// <summary>Parametrli matn: lotin interpolatsiyasining faqat matn qismlari kirillga o'giriladi.</summary>
    private static string F(UzText uz, string ru)
    {
        var text = uz.ToStringAndClear();
        return Language == UiLanguage.Ru ? ru : text;
    }

    public const string AppName = "Oylik";

    // Navigatsiya
    public static string NavPayroll => L("Oylik", "Зарплата", "Ойлик"); // "Oylik" boshqa joyda — dastur nomi
    public static string NavEmployees => L("Xodimlar", "Сотрудники");
    public static string NavAttendance => L("Davomat", "Посещаемость");
    public static string NavPeriods => L("Oylar", "Месяцы");
    public static string NavAudit => L("Audit", "Журнал");
    public static string NavSettings => L("Sozlamalar", "Настройки");

    // Jadval ustunlari — Excel shabloni bilan aynan, har tilda bir xil
    public const string ColNo = "№";
    public const string ColSalary = "Оклад";
    public const string ColName = "Ишчилар";
    public const string ColDaily = "Сумма";
    public const string ColDays = "Кун";
    public const string ColTotal = "Жами";
    public const string ColAdvance = "Аванс";
    public const string ColRemainder = "Колдик";
    public static string Total => L("JAMI", "ИТОГО");

    // Oylar va hafta kunlari
    private static readonly string[] MonthsLatn =
        ["Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun", "Iyul", "Avgust", "Sentyabr", "Oktabr", "Noyabr", "Dekabr"];
    private static readonly string[] MonthsCyrl =
        ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];

    /// <summary>Oy nomi (o'zbek kirill va rus tilida nomlar bir xil).</summary>
    public static string MonthName(int month) => Language == UiLanguage.UzLatn ? MonthsLatn[month - 1] : MonthsCyrl[month - 1];
    /// <summary>"Oktabr 2026".</summary>
    public static string Month(MonthKey key) => $"{MonthName(key.Month)} {key.Year}";

    private static readonly string[] WeekDaysLatn = ["Ya", "Du", "Se", "Ch", "Pa", "Ju", "Sh"];
    private static readonly string[] WeekDaysCyrl = ["Як", "Ду", "Се", "Чо", "Па", "Жу", "Ша"];
    private static readonly string[] WeekDaysRu = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];
    public static string WeekDayShort(DayOfWeek day) => Language switch
    {
        UiLanguage.UzCyrl => WeekDaysCyrl[(int)day],
        UiLanguage.Ru => WeekDaysRu[(int)day],
        _ => WeekDaysLatn[(int)day],
    };

    // Oylik ekrani
    public static string NewPeriod => L("Yangi oy", "Новый месяц");
    public static string ImportExcel => L("Excel'dan import", "Импорт из Excel");
    public static string ExportExcel => L("Excel'ga eksport", "Экспорт в Excel");
    public static string ClosePeriod => L("Oyni yopish", "Закрыть месяц");
    public static string ReopenPeriod => L("Qayta ochish", "Открыть заново");
    public static string Open => L("Ochiq", "Открыт");
    public static string Search => L("Qidirish (Ctrl+F)", "Поиск (Ctrl+F)");
    public static string NewEmployee => L("+ Yangi xodim  (Ctrl+N)", "+ Новый сотрудник  (Ctrl+N)");
    public static string KeyboardHint => L(
        "Enter — pastga · Tab — o'ngga · Esc — bekor · F2 — tahrir/avanslar · Delete — oydan olib tashlash",
        "Enter — вниз · Tab — вправо · Esc — отмена · F2 — правка/авансы · Delete — убрать из месяца");
    public static string NoPeriods => L(
        "Hali oy yo'q. Yangi oy oching yoki Excel'dan import qiling.",
        "Месяцев пока нет. Откройте новый месяц или импортируйте из Excel.");
    public static string DebtBadge => L("qarz", "долг");
    public static string ManualDailyTip => L("Kunlik qo'lda kiritilgan", "Дневная ставка введена вручную");
    public static string Employees => L("Xodimlar", "Сотрудники");
    public static string DaysSum => L("Kun Σ", "Дни Σ");
    public static string TotalSum => L("Jami", "Начислено");
    public static string AdvanceSum => L("Avans", "Аванс");
    public static string RemainderSum => L("Qoldiq", "Остаток");
    public static string PrevMonth => L("Oldingi oy", "Предыдущий месяц");
    public static string NextMonth => L("Keyingi oy", "Следующий месяц");
    /// <summary>Oylik jadvalidagi A9 ustuni (tor — qisqa nom).</summary>
    public static string ColAttendance => L("Davomat", "Явки");
    public static string AdvancesTip => L("Avanslar — bosing (yoki F2)", "Авансы — нажмите (или F2)");

    public static string ClosedAt(DateTimeOffset at) => F($"Yopilgan: {at.ToLocalTime():dd.MM.yyyy HH:mm}", $"Закрыт: {at.ToLocalTime():dd.MM.yyyy HH:mm}");
    public static string SavedAt(DateTimeOffset at) => F($"Saqlandi {at:HH:mm:ss}", $"Сохранено {at:HH:mm:ss}");
    public static string BackupAt(DateTimeOffset? at) => at is null
        ? L("Backup: yo'q", "Резервная копия: нет")
        : F($"Backup: {at.Value.ToLocalTime():dd.MM HH:mm}", $"Резервная копия: {at.Value.ToLocalTime():dd.MM HH:mm}");
    public static string Exported(string file) => F($"Eksport: {file}", $"Экспорт: {file}");
    public static string ExportFailed(string error) => F(
        $"Faylni yozib bo'lmadi (Excel'da ochiq emasmi?): {error}",
        $"Не удалось записать файл (не открыт ли он в Excel?): {error}");

    // Tasdiqlar
    public static string Yes => L("Ha", "Да");
    public static string No => L("Yo'q", "Нет");
    public static string Cancel => L("Bekor qilish", "Отмена");
    public const string Ok = "OK";
    public static string ConfirmClose(string month, int debtors) =>
        F($"{month} yopilsinmi?\n\nYopishdan oldin backup olinadi. Oy faqat o'qish uchun bo'ladi.",
          $"Закрыть {month}?\n\nПеред закрытием создаётся резервная копия. Месяц станет доступен только для чтения.") +
        (debtors > 0
            ? F($"\nQoldig'i manfiy {debtors} ta xodimga keyingi oyda «qarz» avansi yoziladi.",
                $"\nСотрудникам с отрицательным остатком ({debtors}) в следующем месяце будет записан аванс «долг».")
            : "");
    public static string ConfirmReopen(string month) => F(
        $"{month} qayta ochilsinmi?\n\nKeyingi oyga o'tgan «qarz» yozuvlari olib tashlanadi. Qayta yopilganda yangidan hisoblanadi.",
        $"Открыть {month} заново?\n\nЗаписи «долг», перенесённые в следующий месяц, будут удалены. При повторном закрытии они рассчитаются заново.");
    public static string ConfirmRemoveLine(string name, string month) => F(
        $"{name} {month} jadvalidan olib tashlansinmi?",
        $"Убрать {name} из ведомости за {month}?");
    public static string LineNotEmpty => L("Qatorda kun yoki avans bor — oydan olib tashlab bo'lmaydi.", "В строке есть дни или авансы — убрать из месяца нельзя.");
    public static string ConfirmNewPeriod(string month) => F(
        $"{month} ochilsinmi? Barcha faol xodimlar qo'shiladi.",
        $"Открыть {month}? Будут добавлены все активные сотрудники.");
    public static string InvalidNumber => L("Raqam noto'g'ri", "Неверное число");
    public static string InvalidDays => L("Kun 0–31 oralig'ida, 0,5 qadam bilan bo'lishi kerak", "Дни — от 0 до 31 с шагом 0,5");

    // Avanslar
    public static string AdvancesTitle(string name, string month) => F($"{name} — {month} avanslari", $"{name} — авансы за {month}");
    public static string AdvancesTotal(string amount) => F($"Jami: {amount}", $"Итого: {amount}");
    public static string AdvancesSummary(string total, string advance, string remainder) => F(
        $"Jami {total} · Avans {advance} · Qoldiq {remainder}",
        $"Начислено {total} · Аванс {advance} · Остаток {remainder}");
    public static string ConfirmDeleteAdvance(string date, string amount) => F(
        $"{date} — {amount} avansi o'chirilsinmi?",
        $"Удалить аванс {amount} от {date}?");
    public static string AdvDate => L("Sana", "Дата");
    public static string AdvAmount => L("Summa", "Сумма");
    public static string AdvKind => L("Turi", "Тип");
    public static string AdvNote => L("Izoh", "Примечание");
    public static string AdvAdd => L("Qo'shish", "Добавить");
    public static string KindCash => L("Naqd", "Наличные");
    public static string KindDebt => L("Qarz", "Долг");
    public static string DebtTooltip(string month) => F($"{month} oyi yopilganda avtomatik o'tgan", $"Перенесён автоматически при закрытии месяца {month}");
    public static string PreviousMonthFallback => L("O'tgan", "Прошлый");
    public static string ShortInputHint => L("Qisqa kiritish: 200 → 200 000 · 1500 → 1 500 000", "Краткий ввод: 200 → 200 000 · 1500 → 1 500 000");
    public static string Optional => L("ixtiyoriy", "необязательно");
    public static string Note => L("Izoh", "Примечание");

    // Yangi xodim
    public static string NewEmployeeTitle => L("Yangi xodim", "Новый сотрудник");
    public static string FieldName => L("Ism", "Имя");
    public static string FieldNote => L("Izoh / bo'lim", "Примечание / отдел");
    public static string FieldSalary => L("Oklad", "Оклад");
    public static string FieldDaily => L("Kunlik", "Ставка в день");
    public static string FieldDailyHint => L("bo'sh — Oklad ÷ 30", "пусто — Оклад ÷ 30");
    public static string Add => L("Qo'shish", "Добавить");
    public static string SalaryPreview(string salary, string daily) => F($"→ {salary} · kunlik {daily}", $"→ {salary} · в день {daily}");
    public static string DailyPreviewManual(string daily) => F($"→ {daily} (qo'lda)", $"→ {daily} (вручную)");
    public static string EnterName => L("Ismni kiriting", "Введите имя");
    public static string NameRequired => L("Ism bo'sh bo'lmasligi kerak", "Имя не может быть пустым");
    public static string SalaryInvalid => F($"Oklad: {InvalidNumber}", $"Оклад: {InvalidNumber}");
    public static string DailyInvalid => F($"Kunlik: {InvalidNumber}", $"Ставка в день: {InvalidNumber}");
    public static string ConfirmLinkExisting(string name) => F(
        $"«{name}» ismli xodim allaqachon bor. Mavjud xodimni shu oyga qo'shaymi?\n\n«Yo'q» — yangi xodim yaratiladi.",
        $"Сотрудник «{name}» уже есть. Добавить существующего сотрудника в этот месяц?\n\n«Нет» — будет создан новый сотрудник.");

    // Xodimlar ekrani
    public static string FilterActive => L("Faol", "Активные");
    public static string FilterInactive => L("Nofaol", "Неактивные");
    public static string FilterAll => L("Hammasi", "Все");
    public static string SearchNameOrNote => L("Ism yoki izoh", "Имя или примечание");
    public static string ResetToAuto => L("Avtomatik qilish", "Сделать автоматической");
    public static string IsActive => L("Faol", "Активен");
    public static string IsInactive => L("Nofaol", "Неактивен");
    public static string Delete => L("O'chirish", "Удалить");
    public static string DeleteBlocked => L(
        "O'chirib bo'lmaydi — oylarda qatori bor. Nofaol qiling.",
        "Удалить нельзя — есть строки в месяцах. Сделайте неактивным.");
    public static string ConfirmDeleteEmployee(string name) => F($"{name} o'chirilsinmi?", $"Удалить {name}?");

    // Oylar ekrani
    public static string ColMonth => L("Oy", "Месяц");
    public static string ColStatus => L("Holat", "Статус");
    public static string ColClosedAt => L("Yopilgan sana", "Дата закрытия");
    public static string Closed => L("Yopilgan", "Закрыт");
    public static string OpenPeriod => L("Ochish", "Открыть");
    public static string PeriodsHint => L(
        "Oy yopilganda: avtomatik backup olinadi, qoldig'i manfiy xodimlarga keyingi oyda «qarz» avansi yoziladi, oy faqat o'qish uchun bo'ladi. Qayta ochish faqat keyingi oy yopilmagan bo'lsa mumkin — keyingi oyga o'tgan qarz yozuvlari olib tashlanadi.",
        "При закрытии месяца: создаётся резервная копия, сотрудникам с отрицательным остатком в следующем месяце записывается аванс «долг», месяц становится доступен только для чтения. Открыть заново можно, только если следующий месяц не закрыт — перенесённые записи долга удаляются.");

    // Import
    public static string ImportTitle => L("Excel'dan import", "Импорт из Excel");
    public static string ChooseFile => L("Fayl tanlash", "Выбрать файл");
    public static string FileLabel => L("Fayl", "Файл");
    public static string ModeLabel => L("Rejim", "Режим");
    public static string ModeEmployees => L("Faqat xodimlar — ro'yxatni to'ldirish", "Только сотрудники — заполнить список");
    public static string ModePeriod => L("Oy sifatida — qatorlar bilan", "Как месяц — со строками");
    public static string LinkDuplicates => L("Bir xil ismli xodim bo'lsa — mavjudiga bog'lash", "Если есть сотрудник с таким же именем — связать с существующим");
    public static string Import => L("Import", "Импорт");
    public static string ImportBackupNote => L("Import oldidan backup olinadi.", "Перед импортом создаётся резервная копия.");
    public static string ImportWarnings => L("Ogohlantirishlar", "Предупреждения");
    public static string ColRow => L("Qator", "Строка");
    public static string ImportButton(string? month, int count) => month is null
        ? F($"Import — {count} xodim", $"Импорт — сотрудников: {count}")
        : F($"Import — {month}, {count} xodim", $"Импорт — {month}, сотрудников: {count}");
    public static string ImportSummary(int found, int created, int existing, int skipped) => F(
        $"Topildi: {found} · Yangi: {created} · Mavjudga mos: {existing} · O'tkazildi (ism bo'sh): {skipped}",
        $"Найдено: {found} · Новых: {created} · Совпадают с существующими: {existing} · Пропущено (пустое имя): {skipped}");
    public static string ImportDone(int created, int linked, int lines) => F(
        $"Import tugadi: {created} ta yangi xodim, {linked} ta mavjudga bog'landi, {lines} ta qator.",
        $"Импорт завершён: новых сотрудников — {created}, связано с существующими — {linked}, строк — {lines}.");
    public static string ImportBothEmpty(int count) => F(
        $"{count} xodim — Оклад va Сумма bo'sh → 0 (keyin to'ldiriladi)",
        $"Сотрудников: {count} — Оклад и Сумма пустые → 0 (заполните позже)");
    public static string ReadFileFailed(string error) => F($"Faylni o'qib bo'lmadi: {error}", $"Не удалось прочитать файл: {error}");
    public static string ImportPeriodClosed(string month) => F($"{month} yopilgan — import qilib bo'lmaydi.", $"{month} закрыт — импорт невозможен.");

    /// <summary>Import ogohlantirishi/xatosi (Data → ImportIssue).</summary>
    public static string ImportIssueText(ImportIssue issue) => issue.Kind switch
    {
        ImportIssueKind.SalaryEmpty => F(
            $"Оклад bo'sh, Сумма {NumberText.Money(issue.Daily)} → Oklad 0, kunlik qo'lda",
            $"Оклад пуст, Сумма {NumberText.Money(issue.Daily)} → оклад 0, ставка вручную"),
        ImportIssueKind.SalaryAndDailyEmpty => L("Оклад va Сумма bo'sh → 0", "Оклад и Сумма пустые → 0"),
        ImportIssueKind.DailyComputed => F(
            $"Сумма bo'sh → {NumberText.Money(issue.Salary)} ÷ {issue.DaysInMonth} = {NumberText.Money(issue.Daily)}",
            $"Сумма пуста → {NumberText.Money(issue.Salary)} ÷ {issue.DaysInMonth} = {NumberText.Money(issue.Daily)}"),
        ImportIssueKind.InvalidDays => F(
            $"{issue.Row}-qator: Кун noto'g'ri ({issue.Value}) — 0 olindi",
            $"Строка {issue.Row}: неверное значение Кун ({issue.Value}) — взят 0"),
        _ => F(
            $"{issue.Row}-qator: {issue.Column} raqam emas («{issue.Text}») — e'tiborsiz qoldirildi",
            $"Строка {issue.Row}: {issue.Column} — не число («{issue.Text}»), пропущено"),
    };
    public static string ImportError(ImportIssue issue) => F($"Xato: {ImportIssueText(issue)}", $"Ошибка: {ImportIssueText(issue)}");

    // Sozlamalar
    public static string CompanyName => L("Korxona nomi", "Название организации");
    public static string DaysInMonth => L("Oydagi kunlar (Kunlik = Oklad ÷ N)", "Дней в месяце (ставка в день = Оклад ÷ N)");
    public static string HoursPerDay => L("Kunlik soat", "Часов в день");
    public static string DaysWarning => L(
        "Diqqat: o'zgartirish faqat keyin hisoblanadigan avtomatik kunlik summalarga ta'sir qiladi.",
        "Внимание: изменение влияет только на автоматические дневные ставки, рассчитанные после него.");
    public static string ShortInput => L("Qisqa kiritish (6 → 6 000 000, 200 → 200 000)", "Краткий ввод (6 → 6 000 000, 200 → 200 000)");
    public static string BackupSection => L("Backup", "Резервные копии");
    public static string BackupFolder => L("Qo'shimcha backup papkasi (masalan, Google Drive)", "Дополнительная папка для копий (например, Google Drive)");
    public static string Browse => L("Tanlash…", "Выбрать…");
    public static string BackupNow => L("Hozir backup olish", "Создать копию сейчас");
    public static string BackupDone(string path) => F($"Backup olindi:\n{path}", $"Резервная копия создана:\n{path}");
    public static string BackupFailed(string error) => F($"Backup xatosi: {error}", $"Ошибка резервного копирования: {error}");
    public static string OpenDataFolder => L("Ma'lumotlar papkasini ochish", "Открыть папку данных");
    public static string DatabaseOpenFailed(string error, string path) => F(
        $"Ma'lumotlar bazasini ochib bo'lmadi:\n{error}\n\n{path}",
        $"Не удалось открыть базу данных:\n{error}\n\n{path}");

    public static string AppearanceSection => L("Ko'rinish", "Оформление");
    public static string ThemeLabel => L("Mavzu", "Тема");
    public static string ThemeLight => L("Yorug'", "Светлая");
    public static string ThemeDark => L("Qorong'i", "Тёмная");
    public static string ThemeSystem => L("Tizim bo'yicha", "Как в системе");
    public static string LanguageSection => L("Til", "Язык");
    /// <summary>Til nomlari — har tilda o'z yozuvida (UiLanguage tartibida).</summary>
    public static IReadOnlyList<string> LanguageNames { get; } = ["O'zbekcha (lotin)", "Ўзбекча (кирилл)", "Русский"];
    /// <summary>Sidebar'dagi ixcham tanlov uchun (yozuvning o'zi tilni ajratadi).</summary>
    public static IReadOnlyList<string> LanguageShortNames { get; } = ["O'zbekcha", "Ўзбекча", "Русский"];
    public static string ThemeToDark => L("Qorong'i mavzuga o'tish", "Переключить на тёмную тему");
    public static string ThemeToLight => L("Yorug' mavzuga o'tish", "Переключить на светлую тему");

    public static string InstallationId => L("Kompyuter ID", "ID компьютера");
    public static string ServerSection => L("Server va telefon", "Сервер и телефон");
    public static string LocalServerUrl => L("Lokal manzil (Wi-Fi/LAN)", "Локальный адрес (Wi-Fi/LAN)");
    public static string GlobalServerUrl => L("Global manzil (domen)", "Глобальный адрес (домен)");
    public static string ServerAddressesHint => L(
        "Ikkalasi bitta server (bitta kalit). Avval lokal manzil tekshiriladi (2 s), javob bo'lmasa — internet orqali; har 5 daqiqada lokal qayta tekshiriladi. Manzil o'zgarsa qayta ro'yxatdan o'tish shart emas.",
        "Оба адреса — один сервер (один ключ). Сначала проверяется локальный адрес (2 с), если он не отвечает — через интернет; каждые 5 минут локальный проверяется снова. При смене адреса повторная регистрация не нужна.");
    public static string ServerUrlRequired => L("Kamida bitta manzilni kiriting — lokal yoki global.", "Укажите хотя бы один адрес — локальный или глобальный.");
    public static string RegistrationKey => L("Ro'yxatdan o'tish kaliti", "Ключ регистрации");
    public static string RegistrationKeyHint => L("faqat birinchi ulanishda", "только при первом подключении");
    public static string ConnectServer => L("Serverga ulash", "Подключить к серверу");
    public static string SyncNow => L("Hozir sinxronlash", "Синхронизировать сейчас");
    public static string DisconnectServer => L("Ulanishni o'chirish", "Отключить");
    public static string ConfirmDisconnectServer => L(
        "Server bilan ulanish o'chirilsinmi?\n\nLokal davomat ma'lumotlari saqlanib qoladi. Qayta ulash uchun ro'yxatdan o'tish kaliti kerak bo'ladi.",
        "Отключиться от сервера?\n\nЛокальные данные посещаемости сохранятся. Для повторного подключения понадобится ключ регистрации.");
    public static string ServerUrlInvalid => L(
        "Server URL noto'g'ri (masalan: https://oylik.example.uz yoki http://192.168.1.10:5000).",
        "Неверный URL сервера (например: https://oylik.example.uz или http://192.168.1.10:5000).");
    public static string RegistrationKeyRequired => L(
        "Ro'yxatdan o'tish kalitini kiriting (server administratorida).",
        "Введите ключ регистрации (он у администратора сервера).");
    public static string RegistrationKeyWrong => L("Ro'yxatdan o'tish kaliti noto'g'ri.", "Неверный ключ регистрации.");
    public static string InstallationAlreadyRegistered => L(
        "Bu kompyuter serverda allaqachon ro'yxatdan o'tgan — API kalit yo'qolgan bo'lsa server administratoriga murojaat qiling.",
        "Этот компьютер уже зарегистрирован на сервере — если API-ключ утерян, обратитесь к администратору сервера.");
    public static string ServerConnected => L("Serverga ulandi.", "Подключено к серверу.");
    public static string ServerUnreachable(string error) => F($"Serverga ulanib bo'lmadi: {error}", $"Не удалось подключиться к серверу: {error}");
    public static string ServerNotConfigured => L("Server: sozlanmagan", "Сервер: не настроен");

    /// <summary>`Server: ulangan · lokal · oxirgi 10:41 · Qorovul-1 (10:40)`</summary>
    public static string ServerStatus(Oylik.Data.Sync.ServerRoute route, DateTimeOffset? lastSync, string? device, DateTimeOffset? deviceSeen)
    {
        var text = route switch
        {
            Oylik.Data.Sync.ServerRoute.Local => L("Server: ulangan · lokal", "Сервер: подключён · локально"),
            Oylik.Data.Sync.ServerRoute.Global => L("Server: ulangan · internet orqali", "Сервер: подключён · через интернет"),
            _ => L("Server: ulanmagan", "Сервер: нет связи"),
        };
        if (lastSync is { } at)
            text += F($" · oxirgi {at:HH:mm}", $" · последняя {at:HH:mm}");
        if (device is not null && deviceSeen is { } seen)
            text += $" · {device} ({seen:HH:mm})";
        return text;
    }

    public static string PhonesSection => L("Telefonlar", "Телефоны");
    public static string PhonesHint => L(
        "Qorovul telefonida Oylik ilovasini oching va QR'ni skanerlang (kod 10 daqiqa amal qiladi).",
        "Откройте приложение Oylik на телефоне охранника и отсканируйте QR (код действует 10 минут).");
    public static string ShowQr => L("QR ko'rsatish", "Показать QR");
    public static string NoDevices => L("Hali bog'langan telefon yo'q.", "Привязанных телефонов пока нет.");
    public static string DeactivateDevice => L("Uzish", "Отвязать");
    public static string ConfirmDeactivateDevice(string name) => F(
        $"«{name}» uzilsinmi?\n\nTelefon serverga yoza olmaydi; qayta bog'lash uchun yangi QR kerak.",
        $"Отвязать «{name}»?\n\nТелефон больше не сможет отправлять данные; для повторной привязки нужен новый QR.");
    public static string DeviceActive => L("faol", "активен");
    public static string DeviceInactive => L("uzilgan", "отвязан");
    public static string DevicePaired(DateTimeOffset at) => F($"Bog'langan: {at:dd.MM.yyyy}", $"Привязан: {at:dd.MM.yyyy}");
    public static string DeviceLastSeen(DateTimeOffset at) => F($"Oxirgi: {at:dd.MM HH:mm}", $"Последний сеанс: {at:dd.MM HH:mm}");
    public static string PairTitle => L("Telefonni bog'lash", "Привязка телефона");
    public static string PairHint => L(
        "Telefonda: Oylik → [QR skanerlash]. Yoki qo'lda: server manzili va 6 xonali kod.",
        "На телефоне: Oylik → [Сканировать QR]. Или вручную: адрес сервера и 6-значный код.");
    public static string PairExpiresIn(TimeSpan left) => F($"Kod {left:mm\\:ss} dan keyin eskiradi", $"Код истечёт через {left:mm\\:ss}");
    public static string PairExpired => L("Kod eskirdi — oynani yopib, yangisini oling.", "Код истёк — закройте окно и получите новый.");
    public static string PairManual(string server) => F($"Server: {server}", $"Сервер: {server}");
    public static string PairManualLocal(string server) => F($"Lokal: {server}", $"Локально: {server}");
    public static string PairLocalhostWarning => L(
        "Diqqat: server manzili «localhost» — telefon unga ulana olmaydi. Sozlamalarda kompyuterning tarmoq manzilini (masalan http://192.168.1.10:5000) yoki internet manzilini kiriting.",
        "Внимание: адрес сервера «localhost» — телефон не сможет к нему подключиться. Укажите в настройках сетевой адрес компьютера (например http://192.168.1.10:5000) или интернет-адрес.");
    public static string Close => L("Yopish", "Закрыть");

    // Davomat (8-bo'lim)
    public static string AttendanceTitle => L("Davomat", "Посещаемость");
    public static string ColEmployee => L("Xodim", "Сотрудник");
    public static string ColAttendedDays => L("Kelgan", "Явки");
    public static string ColAttendedDaysSub => L("kun", "дн.");
    public static string ColTotalHours => L("Σ soat", "Σ часов");
    public static string ColDayEquivalent => L("Kun ekv.", "Экв. дней");
    public static string ColDayEquivalentDivisor(int hoursPerDay) => $"÷ {hoursPerDay}";
    public static string LegendFull => L("≥ 8 s", "≥ 8 ч");
    public static string LegendPartial => L("4–8 s", "4–8 ч");
    public static string LegendShort => L("< 4 s", "< 4 ч");
    public static string LegendOpen => L("ochiq juftlik", "незакрытая пара");
    public static string LegendAbsent => L("kelmagan", "не пришёл");
    // H12 legendasi (jadval bo'yicha xodimlar)
    public static string LegendSchedule => L("Jadval:", "По графику:");
    public static string LegendLate => L("kechikmoqda", "опаздывает");
    public static string LegendNoShow => L("kelmadi", "не пришёл");
    public static string LegendPresent => L("keldi", "пришёл");
    public static string LegendDayEnd => L("kun oxiri:", "итог дня:");
    public static string LegendDoneAtLeast(int percent) => $"≥ {percent}%";
    public static string LegendDoneBelow(int percent) => $"< {percent}%";
    public static string LegendOff => L("dam kuni", "выходной");
    public static string ColPlanPercent => L("reja %", "план %");
    /// <summary>`141:33 / 384:00 · 37%` — ishlangan / reja (H12).</summary>
    public static string PlanActual(int worked, int planned) => planned > 0
        ? $"{HoursMinutes(worked)} / {HoursMinutes(planned)} · {Math.Round(100m * worked / planned, 0, MidpointRounding.AwayFromZero):0}%"
        : HoursMinutes(worked);
    public static string SearchEmployee => L("Qidirish", "Поиск");
    public static string NoAttendance => L(
        "Bu oyda davomat voqealari yo'q. Telefon bog'langach Keldi/Ketdi shu yerda ko'rinadi (Sozlamalar → Server va telefon).",
        "В этом месяце отметок нет. После привязки телефона «Пришёл/Ушёл» появятся здесь (Настройки → Сервер и телефон).");
    public static string CellNeedsConfirm => L("Tasdiqlash kerak: ochiq juftlik yoki yetim Ketdi", "Требует подтверждения: незакрытая пара или «Ушёл» без «Пришёл»");
    public static string CellInside => L("Hozir ichkarida", "Сейчас на месте");
    public static string HoursTip(decimal hours) => F($"{NumberText.Days(hours)} soat", $"{NumberText.Days(hours)} ч");
    public static string MonthLabel(MonthKey key) => $"{key.Year} · {MonthName(key.Month)}";

    public static string TodayTitle(DateOnly date) => F($"Bugun · {date:dd.MM}", $"Сегодня · {date:dd.MM}");
    public static string TodayRefresh => L("har 60 s yangilanadi", "обновляется каждые 60 с");
    public static string Inside => L("Ichkarida", "На месте");
    public static string Left => L("Ketdi", "Ушли");
    public static string Absent => L("Kelmagan", "Не пришли");

    public static string IssuesTitle(int count) => F($"Tasdiqlash kerak · {count}", $"Требует подтверждения · {count}");
    public static string NoIssues => L("Hammasi joyida — ochiq juftlik yoki yetim Ketdi yo'q.", "Всё в порядке — нет незакрытых пар и «Ушёл» без «Пришёл».");
    public static string IssueOpenNoOut => L("Ketdi yo'q — 16 soatdan oshdi", "Нет «Ушёл» — прошло больше 16 часов");
    public static string IssueOrphanOut => L("Yetim Ketdi — Keldi topilmadi", "«Ушёл» без «Пришёл»");
    public static string Save => L("Saqlash", "Сохранить");
    public static string AddIn => L("Keldi qo'shish", "Добавить «Пришёл»");
    public static string ConfirmedNote => L("Tasdiqlash ro'yxatidan", "Из списка подтверждения");
    public static string InvalidTime => L("Vaqt noto'g'ri (masalan 18:00)", "Неверное время (например 18:00)");
    public static string ShiftTooLong(int hours) => F(
        $"Keldi va Ketdi orasi {hours} soatdan oshmasligi kerak.",
        $"Между «Пришёл» и «Ушёл» должно быть не больше {hours} часов.");

    public static string EventIn => L("Keldi", "Пришёл");
    public static string EventOut => L("Ketdi", "Ушёл");
    public static string EventType(AttendanceEventType type) => type == AttendanceEventType.In ? EventIn : EventOut;
    public static string DayDetailHint => L("Kun tafsiloti — jadvaldagi katakni bosing", "Подробности дня — нажмите на ячейку таблицы");
    public static string DayTitle(string name, DateOnly date, decimal hours) => hours > 0
        ? F($"{name} · {date:dd.MM} · {NumberText.Days(hours)} s", $"{name} · {date:dd.MM} · {NumberText.Days(hours)} ч")
        : $"{name} · {date:dd.MM}";
    public static string NoDayEvents => L("Bu kunda voqea yo'q.", "В этот день отметок нет.");
    public static string ManualSource => L("qo'lda", "вручную");
    public static string UnknownDevice => L("telefon", "телефон");
    public static string ClockSkew => L("soat farqi", "расхождение часов");
    public static string ClockSkewTip => L(
        "Voqea serverga 10 daqiqadan kech yetgan — telefon soati noto'g'ri bo'lishi mumkin",
        "Отметка дошла до сервера позже чем через 10 минут — возможно, часы телефона идут неверно");
    public static string VoidedBecause(string? reason) => F($"bekor qilingan: {reason}", $"отменено: {reason}");
    public static string AddEvent => L("Voqea qo'shish", "Добавить отметку");
    public static string VoidEvent => L("Bekor qilish (sabab)", "Отменить (с причиной)");
    public static string VoidShort => L("Bekor qilish", "Отменить");
    public static string FieldType => L("Tur", "Тип");
    public static string FieldDate => L("Sana", "Дата");
    public static string FieldTime => L("Vaqt", "Время");
    public static string VoidTitle => L("Voqeani bekor qilish", "Отмена отметки");
    public static string VoidPrompt(string what) => F(
        $"{what}\n\nVoqea o'chirilmaydi — bekor qilingan deb belgilanadi. Sabab majburiy:",
        $"{what}\n\nОтметка не удаляется — она помечается как отменённая. Причина обязательна:");
    public static string VoidReasonWatermark => L("Sabab (masalan: xato xodim bosilgan)", "Причина (например: выбран не тот сотрудник)");

    // A9: Oylik jadvalida davomat maslahati
    public static string AttendanceHint(int days, decimal hours) => F(
        $"Davomat: {days} kun · {NumberText.Days(Math.Round(hours, 0, MidpointRounding.AwayFromZero))} s",
        $"Посещаемость: {days} дн. · {NumberText.Days(Math.Round(hours, 0, MidpointRounding.AwayFromZero))} ч");
    public static string AttendanceShort(int days, decimal hours) => F(
        $"{days} k · {NumberText.Days(Math.Round(hours, 0, MidpointRounding.AwayFromZero))} s",
        $"{days} д · {NumberText.Days(Math.Round(hours, 0, MidpointRounding.AwayFromZero))} ч");
    public static string AttendanceChip(int days) => $"⇐ {days}";
    public static string CopyFromAttendance => L("Ko'chirish", "Перенести");
    public static string CopyTip(int days, decimal hours) => AttendanceHint(days, hours) + F(
        $"\nBosing — Kun = {days} (Ko'chirish). Avtomatik yozilmaydi.",
        $"\nНажмите — Кун = {days} (Перенести). Автоматически не записывается.");
    public static string CopyTipClosed(int days, decimal hours) => AttendanceHint(days, hours) + L(
        "\nYopiq oy — ko'chirib bo'lmaydi.", "\nМесяц закрыт — перенести нельзя.");
    public static string CopyTipSame(int days, decimal hours) => AttendanceHint(days, hours) + L(
        "\nKun davomat bilan bir xil.", "\nКун совпадает с посещаемостью.");
    public static string CopyAll => L("Davomatdan ko'chirish", "Перенести из посещаемости");
    public static string ConfirmCopyAll(int count, string month) => F(
        $"{month}: {count} ta xodimning Kun qiymati davomatdagi kelgan kunlar bilan almashtirilsinmi?\n\nHar bir o'zgarish auditga yoziladi.",
        $"{month}: заменить значение Кун у {count} сотрудников на дни явки из посещаемости?\n\nКаждое изменение записывается в журнал.");

    // Tarmoqlar (3.9)
    public static string ColBranch => L("Tarmoq", "Филиал");
    public static string BranchAll => L("Barchasi", "Все");
    public static string BranchUnassigned => L("Belgilanmagan", "Не указан");
    public static string BranchFilterTip => L("Tarmoq bo'yicha filtr — jadval, xulosa, JAMI va Excel eksport", "Фильтр по филиалу — таблица, итоги, ИТОГО и экспорт в Excel");
    public static string BranchesSection => L("Tarmoqlar", "Филиалы");
    public static string BranchesHint => L(
        "Korxona bitta, tarmoqlar bir nechta. Xodimi bor tarmoqni o'chirib bo'lmaydi — nofaol qiling. Hisob qoidalari tarmoqqa bog'liq emas.",
        "Организация одна, филиалов несколько. Филиал с сотрудниками удалить нельзя — сделайте его неактивным. Правила расчёта от филиала не зависят.");
    public static string AddBranch => L("+ Tarmoq qo'shish", "+ Добавить филиал");
    public static string BranchActive => L("Faol", "Активен");
    public static string ConfirmDeleteBranch(string name) => F($"«{name}» tarmog'i o'chirilsinmi?", $"Удалить филиал «{name}»?");

    [GeneratedRegex(@"^(\d+)-tarmoq$")]
    private static partial Regex DefaultBranchName();

    /// <summary>`1-tarmoq` → «1-тармоқ» / «Филиал 1»; foydalanuvchi bergan nom — o'zgarmaydi.</summary>
    public static string BranchName(string name) => DefaultBranchName().Match(name) is { Success: true } m
        ? F($"{m.Groups[1].Value}-tarmoq", $"Филиал {m.Groups[1].Value}")
        : name;

    public static string BranchName(Branch? branch) => branch is null ? BranchUnassigned : BranchName(branch.Name);

    // Xodimlar jadvali (5.4)
    public static string AddEmployee => L("+ Yangi xodim", "+ Новый сотрудник");
    public static string EmployeesKeyboardHint => L(
        "Bosing yoki yozing — tahrir · Enter — pastga · Tab — o'ngga · Esc — bekor · Kunlik qo'lda — ko'k kursiv",
        "Нажмите или начните печатать — правка · Enter — вниз · Tab — вправо · Esc — отмена · ставка вручную — синий курсив");
    public static string DeleteEmployeeTip => L("Faqat birorta oyda qatori bo'lmasa", "Только если у сотрудника нет строк в месяцах");

    // ---------- 2.2: Ish jadvali va soatbay hisob (4.3, 5.10) ----------

    /// <summary>Daqiqa → `172:30`.</summary>
    public static string HoursMinutes(int minutes) =>
        $"{(minutes < 0 ? "−" : "")}{Math.Abs(minutes) / 60}:{Math.Abs(minutes) % 60:00}";

    /// <summary>`33 333,33` — so'm, 2 xona (raqam formati 6 000 000 bilan bir xil ajratgich).</summary>
    public static string Rate(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        var whole = (long)Math.Truncate(rounded);
        var cents = (int)Math.Abs((rounded - whole) * 100);
        return $"{NumberText.Money(whole)},{cents:00}";
    }

    public static string SchedulesSection => L("Ish jadvallari", "Графики работы");
    public static string SchedulesHint => L(
        "Jadval bo'yicha (soatbay) hisoblanadigan xodimlar uchun shablonlar. Xodimga biriktirilgan shablonni o'chirib bo'lmaydi; o'zgartirilsa ochiq oylar qayta hisoblanadi, yopiq oylar o'zgarmaydi.",
        "Шаблоны для сотрудников с расчётом по графику (почасовым). Шаблон, назначенный сотруднику, удалить нельзя; при изменении открытые месяцы пересчитываются, закрытые не меняются.");
    public static string AddSchedule => L("+ Jadval qo'shish", "+ Добавить график");
    public static string Edit => L("Tahrirlash", "Изменить");
    public static string CopyAction => L("Nusxa", "Копия");
    public static string ScheduleCopyName(string name) => F($"{name} (nusxa)", $"{name} (копия)");
    public static string ConfirmDeleteSchedule(string name) => F($"«{name}» jadvali o'chirilsinmi?", $"Удалить график «{name}»?");
    public static string NewScheduleTitle => L("Yangi ish jadvali", "Новый график работы");
    public static string EditScheduleTitle => L("Ish jadvali", "График работы");
    public static string KindWeekly => L("Hafta kunlari", "По дням недели");
    public static string KindCycle => L("Navbatma-navbat (ish / dam)", "Сменный (работа / отдых)");
    public static string FieldScheduleName => L("Nomi", "Название");
    public static string FieldStart => L("Boshlanishi", "Начало");
    public static string FieldDuration => L("Davomiyligi (s:dd)", "Длительность (ч:мм)");
    public static string FieldBreak => L("Tanaffus, daq (to'lanmaydi)", "Перерыв, мин (не оплачивается)");
    public static string FieldWorkDays => L("Ish kunlari", "Рабочих дней");
    public static string FieldOffDays => L("Dam kunlari", "Выходных дней");
    public static string InvalidDuration => L("Davomiylik 0:30–24:00 bo'lishi kerak (masalan 9:00)", "Длительность — от 0:30 до 24:00 (например 9:00)");

    /// <summary>`Du–Sh · 08:00–17:00 · tanaffus 1:00` / `1/1 · 08:00–08:00 (24:00)`.</summary>
    public static string ScheduleSummary(WorkSchedule s)
    {
        var end = s.StartTime.AddMinutes(s.DurationMinutes);
        var time = $"{s.StartTime:HH\\:mm}–{end:HH\\:mm} ({HoursMinutes(s.DurationMinutes)})";
        var pattern = s.Kind == ScheduleKind.Cycle ? $"{s.CycleWorkDays}/{s.CycleOffDays}" : WeekDaysSummary(s);
        var text = $"{pattern} · {time}";
        return s.BreakMinutes > 0 ? text + F($" · tanaffus {HoursMinutes(s.BreakMinutes)}", $" · перерыв {HoursMinutes(s.BreakMinutes)}") : text;
    }

    private static string WeekDaysSummary(WorkSchedule s)
    {
        DayOfWeek[] order = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        var days = order.Where(s.WorksOn).ToList();
        if (days.Count == 7)
            return L("har kuni", "ежедневно");
        // ketma-ket bo'lsa — oraliq (Du–Sh)
        var indexes = days.Select(d => Array.IndexOf(order, d)).ToList();
        if (days.Count > 2 && indexes[^1] - indexes[0] == days.Count - 1)
            return $"{WeekDayShort(days[0])}–{WeekDayShort(days[^1])}";
        return string.Join(", ", days.Select(WeekDayShort));
    }

    /// <summary>3 ta namuna shablon nomi o'zgartirilmaguncha — joriy tilda.</summary>
    public static string ScheduleName(WorkSchedule? s)
    {
        if (s is null)
            return ScheduleNone;
        if (s.Id == WorkSchedule.SampleDaytimeId && s.Name == "Kunduzgi 08:00–17:00")
            return L("Kunduzgi 08:00–17:00", "Дневной 08:00–17:00");
        if (s.Id == WorkSchedule.SampleTwelveHoursId && s.Name == "12 soat 08:00–20:00")
            return L("12 soat 08:00–20:00", "12 часов 08:00–20:00");
        if (s.Id == WorkSchedule.SampleDailyId && s.Name == "Sutkalik 08:00–08:00, kun ora")
            return L("Sutkalik 08:00–08:00, kun ora", "Сутки 08:00–08:00, через день");
        return s.Name;
    }

    public static string PolicySection => L("Hisob siyosati", "Правила расчёта");
    public static string PolicyHint => L(
        "Jadval bo'yicha xodimlarga. Oy yopilganda qiymatlar qatorda muzlatiladi — keyingi o'zgarish yopiq oylarga ta'sir qilmaydi.",
        "Для сотрудников по графику. При закрытии месяца значения фиксируются — последующие изменения не влияют на закрытые месяцы.");
    public static string LateGrace => L("Kechikishga ruxsat, daq", "Допустимое опоздание, мин");
    public static string AbsentAfter => L("«Kelmadi» — smena boshidan, daq", "«Не пришёл» — от начала смены, мин");
    public static string OvertimePolicyLabel => L("Ortiqcha ish", "Сверхурочные");
    public static string OvertimeNone => L("To'lanmaydi (faqat ko'rsatiladi)", "Не оплачиваются (только учёт)");
    public static string OvertimeMultiplier => L("Koeffitsient × soatlik stavka", "Коэффициент × часовая ставка");
    public static string OvertimeFixed => L("Belgilangan, so'm/soat", "Фиксированно, сум/час");
    public static string OvertimeValueLabel => L("Koeffitsient yoki so'm/soat", "Коэффициент или сум/час");
    public static string OvertimeDefault => L("Sozlamalar bo'yicha", "Как в настройках");
    public static string GreenPercent => L("Yashil — bajarilgan, % (≥)", "Зелёный — выполнено, % (≥)");
    public static string YellowPercent => L("Sariq — bajarilgan, % (≥)", "Жёлтый — выполнено, % (≥)");

    /// <summary>`1,5` — kasr vergul bilan (raqam formati bilan bir xil).</summary>
    public static string Factor(decimal value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    public static string OvertimePolicyName(OvertimePolicy policy, decimal value) => policy switch
    {
        OvertimePolicy.Multiplier => F($"× {Factor(value)} soatlik stavka", $"× {Factor(value)} часовой ставки"),
        OvertimePolicy.FixedPerHour => F($"{NumberText.Money((long)value)} so'm/soat", $"{NumberText.Money((long)value)} сум/час"),
        _ => L("to'lanmaydi", "не оплачиваются"),
    };

    // Xodimlar jadvali
    public static string ColTabelNo => L("Tabel №", "Таб. №");
    public static string ColPayMode => L("Hisob usuli", "Способ расчёта");
    public static string ColSchedule => L("Jadval", "График");
    public static string PayModeDaily => L("Kunbay", "Поденно");
    public static string PayModeSchedule => L("Jadval bo'yicha", "По графику");
    public static string PayModeName(PayMode mode) => mode == PayMode.Schedule ? PayModeSchedule : PayModeDaily;
    public static string ScheduleNone => L("Jadvalsiz", "Без графика");
    public static string ScheduleChoose => L("Tanlash…", "Выбрать…");
    public static string ScheduleCell(WorkSchedule? s, DateOnly? anchor) => s is null ? ScheduleChoose
        : anchor is { } a ? F($"{ScheduleName(s)} · {a:dd.MM}", $"{ScheduleName(s)} · {a:dd.MM}") : ScheduleName(s);

    public static string AssignTitle(string name) => F($"{name} — ish jadvali", $"{name} — график работы");
    public static string FieldSchedule => L("Jadval", "График");
    public static string FieldCycleAnchor => L("Birinchi ish kuni (navbatma-navbat uchun)", "Первый рабочий день (для сменного)");
    public static string FieldEffectiveFrom => L("Shu sanadan boshlab", "Действует с");
    public static string Assign => L("Biriktirish", "Назначить");
    public static string HistoryTitle => L("Tarix", "История");
    public static string FromDate(DateOnly date) => F($"{date:dd.MM.yyyy} dan", $"с {date:dd.MM.yyyy}");
    public static string EmployeeOvertimeLabel => L("Ortiqcha ish (shu xodim uchun)", "Сверхурочные (для этого сотрудника)");
    public static string AssignHint => L(
        "Oy ichida jadval almashsa — har kun o'sha sanadagi jadval bilan hisoblanadi. Hisob usuli (Kunbay/Jadval) esa butun oy uchun.",
        "При смене графика в течение месяца каждый день считается по графику, действующему в этот день. Способ расчёта — на весь месяц.");
    public static string InvalidDate => L("Sana noto'g'ri (masalan 01.10.2026)", "Неверная дата (например 01.10.2026)");

    // Davomat — H12 holatlari va kontekst menyu
    public static string StatusNotStarted => L("Smena boshlanmagan", "Смена не началась");
    public static string StatusLate(int minutes) => F($"Kechikmoqda · {minutes} daq", $"Опаздывает · {minutes} мин");
    public static string StatusAbsent => L("Kelmadi", "Не пришёл");
    public static string StatusPresent => L("Keldi", "Пришёл");
    public static string StatusLateMark(int minutes) => F($"kech {minutes} daq", $"опоздание {minutes} мин");
    public static string StatusDone(int percent) => F($"bajarildi {percent}%", $"выполнено {percent}%");
    public static string StatusOff => L("Dam", "Выходной");
    public static string ExcusedName(ExcusedKind kind) => kind == ExcusedKind.Paid
        ? L("Sababli (to'lanadigan)", "Уважительная (оплачиваемая)")
        : L("Sababli (to'lanmaydigan)", "Уважительная (неоплачиваемая)");
    public static string ExcusedMark(ExcusedKind kind) => kind == ExcusedKind.Paid ? L("S", "У") : L("S−", "У−");
    public static string PlanText(DateTimeOffset start, DateTimeOffset end) => F($"Reja: {start:HH:mm}–{end:HH:mm}", $"План: {start:HH:mm}–{end:HH:mm}");
    public static string PlanOff => L("Reja: dam", "План: выходной");
    public static string MinutesShort => L("daq", "мин");
    public static string LateList => L("Kechikmoqda", "Опаздывают");
    /// <summary>Bugun panelidagi tor plitka uchun qisqa nom.</summary>
    public static string LateTile => L("Kechikish", "Опоздание");
    public static string AbsentList => L("Kelmadi", "Не пришли");
    public static string MenuDayOff => L("Dam (istisno)…", "Выходной (исключение)…");
    public static string MenuOtherTime => L("Boshqa vaqt…", "Другое время…");
    public static string MenuExcusedPaid => L("Sababli — to'lanadigan…", "Уважительная — оплачиваемая…");
    public static string MenuExcusedUnpaid => L("Sababli — to'lanmaydigan…", "Уважительная — неоплачиваемая…");
    public static string MenuRemoveOverride => L("Istisnoni olib tashlash…", "Убрать исключение…");
    public static string MenuRemoveExcused => L("Sababli belgisini olib tashlash…", "Снять отметку «уважительная»…");
    public static string NoteRequiredPrompt(string what) => F($"{what}\n\nIzoh majburiy (auditga yoziladi):", $"{what}\n\nКомментарий обязателен (записывается в журнал):");
    public static string NoteWatermark => L("Izoh (masalan: bayram, almashuv, kasallik varaqasi)", "Комментарий (например: праздник, замена, больничный)");
    public static string OtherTimeTitle => L("Boshqa vaqt", "Другое время");
    public static string ScheduleOnlyHint => L("Faqat «Jadval bo'yicha» xodimlar uchun", "Только для сотрудников «по графику»");
    public static string DayLine(string what, DateOnly date) => $"{what} · {date:dd.MM.yyyy}";

    // Oylik ekrani
    public static string HoursSum => L("Soat Σ", "Часы Σ");
    public static string ScheduleRowTip(PayrollLine line, string? schedule) =>
        F($"Jadval bo'yicha: {schedule ?? "—"}\nReja {HoursMinutes(line.PlannedMinutes)} · ishlangan {HoursMinutes(line.WorkedMinutes)}\nKechikish {HoursMinutes(line.LateMinutes)} · erta ketish {HoursMinutes(line.EarlyLeaveMinutes)}\nOrtiqcha {HoursMinutes(line.OvertimeMinutes)} ({OvertimePolicyName(line.OvertimePolicy, line.OvertimeValue)}) → {NumberText.Money(line.OvertimePay)}\nAsosiy haq {NumberText.Money(line.BasePay)} = {NumberText.Money(line.MonthlySalary)} ÷ {HoursMinutes(line.PlannedMinutes)} × {HoursMinutes(Math.Min(line.WorkedMinutes, line.PlannedMinutes))}",
          $"По графику: {schedule ?? "—"}\nПлан {HoursMinutes(line.PlannedMinutes)} · отработано {HoursMinutes(line.WorkedMinutes)}\nОпоздания {HoursMinutes(line.LateMinutes)} · ранний уход {HoursMinutes(line.EarlyLeaveMinutes)}\nСверхурочные {HoursMinutes(line.OvertimeMinutes)} ({OvertimePolicyName(line.OvertimePolicy, line.OvertimeValue)}) → {NumberText.Money(line.OvertimePay)}\nОсновная оплата {NumberText.Money(line.BasePay)} = {NumberText.Money(line.MonthlySalary)} ÷ {HoursMinutes(line.PlannedMinutes)} × {HoursMinutes(Math.Min(line.WorkedMinutes, line.PlannedMinutes))}")
        + (line.PlannedMinutes == 0 ? "\n" + NoPlanWarning : "");
    public static string NoPlanWarning => L("Bu oyda reja yo'q — asosiy haq 0", "В этом месяце нет плана — основная оплата 0");
    public static string HourlyRateTip(decimal rate) => F($"Soatlik stavka: {Rate(rate)} so'm", $"Часовая ставка: {Rate(rate)} сум");
    public static string ExcelScheduleComment(PayrollLine line) =>
        F($"Soatbay: {HoursMinutes(Math.Min(line.WorkedMinutes, line.PlannedMinutes))} × {Rate(line.HourlyRate)} so'm/soat",
          $"Почасово: {HoursMinutes(Math.Min(line.WorkedMinutes, line.PlannedMinutes))} × {Rate(line.HourlyRate)} сум/час")
        + (line.OvertimePay > 0 ? F($" + ortiqcha {NumberText.Money(line.OvertimePay)}", $" + сверхурочные {NumberText.Money(line.OvertimePay)}") : "");
    public static string UnconfirmedTitle => L("Tasdiqlanmagan voqealar", "Неподтверждённые отметки");
    public static string UnconfirmedMessage(int count, string month) => F(
        $"{month}: {count} ta tasdiqlanmagan voqea — oylikka ta'sir qiladi.\n\nAvval Davomat → «Tasdiqlash kerak» ro'yxatini ko'rib chiqing yoki baribir yoping.",
        $"{month}: неподтверждённых отметок — {count}, это влияет на зарплату.\n\nСначала просмотрите Посещаемость → «Требует подтверждения» или всё равно закройте.");
    public static string GoToList => L("Ro'yxatga o'tish", "Перейти к списку");
    public static string CloseAnyway => L("Baribir yopish", "Всё равно закрыть");

    // ---------- 5.11: hisob varaqasi (PDF) ----------

    public static string Payslip => L("Hisob varaqasi", "Расчётный листок");
    public static string Payslips => L("Hisob varaqalari", "Расчётные листки");
    public static string PayslipsTip => L("Barcha xodimlar (joriy tarmoq filtri bo'yicha) — bitta PDF va umumiy vedomost", "Все сотрудники (по текущему фильтру филиала) — один PDF и общая ведомость");
    public static string PayrollSheet => L("Ish haqi vedomosti", "Ведомость по заработной плате");
    public static string PdfDraftWatermark => L("DASTLABKI", "ЧЕРНОВИК");
    public static string PdfDraft => L("Dastlabki (oy ochiq)", "Предварительный (месяц открыт)");
    public static string PdfFinal => L("Yakuniy (oy yopilgan)", "Окончательный (месяц закрыт)");
    public static string PdfCompany => L("Korxona", "Организация");
    public static string PdfBranch => L("Tarmoq", "Филиал");
    public static string PdfEmployee => L("Xodim", "Сотрудник");
    public static string PdfMonth => L("Oy", "Месяц");
    public static string PdfStatus => L("Holat", "Статус");
    public static string PdfNumber => L("Hujjat №", "Документ №");
    public static string PdfGenerated => L("Tuzilgan", "Составлен");
    public static string PdfTerms => L("Shartlar", "Условия");
    public static string PdfPayMode => L("Hisob usuli", "Способ расчёта");
    public static string PdfSalary => L("Oklad", "Оклад");
    public static string PdfSchedule => L("Ish jadvali", "График работы");
    public static string PdfPlan => L("Oydagi reja", "План на месяц");
    public static string PdfPlanValue(int days, int minutes) => F($"{days} kun · {HoursMinutes(minutes)} soat", $"{days} дн. · {HoursMinutes(minutes)} ч");
    public static string PdfHourlyRate => L("Soatlik stavka", "Часовая ставка");
    public static string PdfDailyRate => L("Kunlik stavka", "Дневная ставка");
    /// <summary>PDF formulalari: ming ajratgich — bo'linmas bo'sh joy (raqam qatorga bo'linmaydi).</summary>
    private static string Nb(long value) => NumberText.Money(value).Replace(' ', '\u00A0');
    private static string NbRate(decimal value) => Rate(value).Replace(' ', '\u00A0');

    public static string PdfRateFormula(long salary, int plannedMinutes, decimal rate) => F(
        $"{Nb(salary)} ÷ {HoursMinutes(plannedMinutes)} s = {NbRate(rate)} so'm/soat",
        $"{Nb(salary)} ÷ {HoursMinutes(plannedMinutes)} ч = {NbRate(rate)} сум/час");
    public static string PdfDailyFormula(long salary, int days, long rate) => F(
        $"{Nb(salary)} ÷ {days} = {Nb(rate)} so'm/kun",
        $"{Nb(salary)} ÷ {days} = {Nb(rate)} сум/день");
    public static string PdfGrace => L("Kechikishga ruxsat", "Допустимое опоздание");
    public static string PdfMinutes(int minutes) => F($"{minutes} daq", $"{minutes} мин");
    public static string PdfOvertime => L("Ortiqcha ish", "Сверхурочные");
    public static string PdfDays => L("Kunlik jadval", "Табель по дням");
    public static string PdfColDate => L("Sana", "Дата");
    public static string PdfColWeekDay => L("Kun", "Дн.");
    public static string PdfColPlan => L("Reja", "План");
    public static string PdfColIn => L("Keldi", "Пришёл");
    public static string PdfColOut => L("Ketdi", "Ушёл");
    public static string PdfColWorked => L("Ishlangan", "Отработано");
    public static string PdfColLate => L("Kechikish", "Опоздание");
    public static string PdfColEarly => L("Erta ketish", "Ранний уход");
    public static string PdfColOvertime => L("Ortiqcha", "Сверхур.");
    public static string PdfColPay => L("Summa", "Сумма");
    public static string PdfColNote => L("Izoh", "Примечание");
    public static string PdfDayOff => L("dam", "вых.");
    public static string PdfNoAttendance => L("Davomat ma'lumoti yo'q.", "Данных посещаемости нет.");
    public static string PdfCorrectedLegend => L("* — qo'lda kiritilgan yoki tuzatilgan voqea (sababi izohda)", "* — отметка введена вручную или исправлена (причина в примечании)");
    public static string PdfTotals => L("Jami", "Итоги");
    public static string PdfPlanActual => L("Reja / amalda", "План / факт");
    public static string PdfLateEarly => L("Kechikish / erta ketish", "Опоздания / ранний уход");
    public static string PdfBasePay => L("Asosiy haq", "Основная оплата");
    public static string PdfOvertimePay => L("Ortiqcha ish haqi", "Оплата сверхурочных");
    public static string PdfDaysWorked => L("Ishlangan kunlar", "Отработано дней");
    public static string PdfAccrued => L("Hisoblangan jami", "Начислено всего");
    public static string PdfAdvances => L("Avanslar", "Авансы");
    public static string PdfCarryDebt => L("shu jumladan o'tgan oy qarzi", "в т. ч. долг прошлого месяца");
    public static string PdfToPay => L("To'lanadigan qoldiq", "К выплате");
    public static string PdfDebt => L("Qarz (keyingi oyga o'tadi)", "Долг (переходит на следующий месяц)");
    public static string PdfNoAdvances => L("Avans yo'q.", "Авансов нет.");
    public static string PdfExplanation => L("Hisoblash", "Расчёт");
    public static string PdfScheduleExplanation(PayslipLike m) => F(
        $"{Nb(m.Salary)} ÷ {HoursMinutes(m.Planned)} s = {NbRate(m.Rate)} so'm/soat × {HoursMinutes(Math.Min(m.Worked, m.Planned))} = {Nb(m.BasePay)}",
        $"{Nb(m.Salary)} ÷ {HoursMinutes(m.Planned)} ч = {NbRate(m.Rate)} сум/час × {HoursMinutes(Math.Min(m.Worked, m.Planned))} = {Nb(m.BasePay)}");
    public static string PdfOvertimeExplanation(int minutes, string policy, long pay) => F(
        $"Ortiqcha ish: {HoursMinutes(minutes)} ({policy}) = {Nb(pay)}",
        $"Сверхурочные: {HoursMinutes(minutes)} ({policy}) = {Nb(pay)}");
    public static string PdfDailyExplanation(long rate, decimal days, long total) => F(
        $"{Nb(rate)} × {NumberText.Days(days)} kun = {Nb(total)}",
        $"{Nb(rate)} × {NumberText.Days(days)} дн. = {Nb(total)}");
    public static string PdfRemainderExplanation(long total, long advances, long remainder) => F(
        $"{Nb(total)} − avanslar {Nb(advances)} = {Nb(remainder)}",
        $"{Nb(total)} − авансы {Nb(advances)} = {Nb(remainder)}");
    public static string PdfFrozenNote => L(
        "Oy yopilgan: jami summalar yopilgan paytdagi qiymatlar (keyingi sozlama o'zgarishi ta'sir qilmaydi).",
        "Месяц закрыт: итоговые суммы — значения на момент закрытия (последующие изменения настроек не влияют).");
    public static string PdfSignAccountant => L("Hisobchi", "Бухгалтер");
    public static string PdfSignEmployee => L("Tanishdim: xodim", "Ознакомлен: сотрудник");
    public static string PdfSignDate => L("sana", "дата");
    public static string PdfSignature => L("Imzo", "Подпись");
    public static string PdfColName => L("F.I.Sh.", "Ф.И.О.");
    public static string PdfColMeasure => L("Kun / soat", "Дни / часы");
    public static string PdfColAccrued => L("Hisoblangan", "Начислено");
    public static string PdfColRemainder => L("Qoldiq", "Остаток");
    public static string PdfPage(int page, int total) => F($"{page} / {total}", $"{page} / {total}");
    public static string PdfSheetMeasure(PayrollLine line) => line.IsSchedule ? F($"{HoursMinutes(line.WorkedMinutes)} s", $"{HoursMinutes(line.WorkedMinutes)} ч") : F($"{NumberText.Days(line.DaysWorked)} kun", $"{NumberText.Days(line.DaysWorked)} дн.");
    public static string PdfFileName(MonthKey month, string name, int tabelNo) => $"OY-{month.Year:0000}-{month.Month:00}-{tabelNo:0000} {name}.pdf";
    public static string PdfSheetFileName(MonthKey month, string? branch) => branch is null
        ? F($"Vedomost {month.Year:0000}-{month.Month:00}.pdf", $"Ведомость {month.Year:0000}-{month.Month:00}.pdf")
        : F($"Vedomost {month.Year:0000}-{month.Month:00} - {branch}.pdf", $"Ведомость {month.Year:0000}-{month.Month:00} - {branch}.pdf");

    // Oldindan ko'rish oynasi
    public static string PrintAction => L("Chop etish", "Печать");
    public static string SavePdf => L("Saqlash", "Сохранить");
    public static string SendTelegram => L("Telegram'ga yuborish", "Отправить в Telegram");
    public static string TelegramHint => L("Telegram'da chatni tanlang va Ctrl+V bosing — fayl buferda.", "Выберите чат в Telegram и нажмите Ctrl+V — файл в буфере обмена.");
    public static string TelegramNotFound(string folder) => F($"Telegram Desktop topilmadi. Fayl saqlandi va papka ochildi:\n{folder}", $"Telegram Desktop не найден. Файл сохранён, папка открыта:\n{folder}");
    public static string PdfSaved(string path) => F($"Saqlandi: {path}", $"Сохранено: {path}");
    public static string PrintFailed(string error) => F($"Chop etib bo'lmadi: {error}", $"Не удалось напечатать: {error}");
    public static string PdfFailed(string error) => F($"PDF yaratib bo'lmadi: {error}", $"Не удалось создать PDF: {error}");

    // Audit
    public static string AuditAt => L("Sana", "Дата");
    public static string AuditWhat => L("Nima", "Что");
    public static string AuditChange => L("Eski → Yangi", "Было → Стало");
    public static string AuditComment => L("Izoh", "Комментарий");
    public static string AllEmployees => L("Barcha xodimlar", "Все сотрудники");
    public static string AllPeriods => L("Barcha oylar", "Все месяцы");

    /// <summary>Audit jurnalidagi maydon nomi (bazadagi kalit → UI matni); noma'lum kalit — o'zi.</summary>
    public static string AuditField(string field) => field switch
    {
        "DaysWorked" => L("Kun", "Дни"),
        "DailyRate" => L("Kunlik", "Ставка в день"),
        "MonthlySalary" => L("Oklad", "Оклад"),
        "FullName" => L("Ism", "Имя"),
        "Amount" => L("Avans", "Аванс"),
        "Status" => L("Oy holati", "Статус месяца"),
        "IsActive" => L("Holat", "Статус"),
        "Created" => L("Xodim yaratildi", "Сотрудник создан"),
        "Added" => L("Qator qo'shildi", "Строка добавлена"),
        "Removed" => L("Qator olib tashlandi", "Строка удалена"),
        "Deleted" => L("Xodim o'chirildi", "Сотрудник удалён"),
        "Note" => L("Izoh", "Примечание"),
        "DailyRateIsManual" => L("Kunlik rejimi", "Режим ставки"),
        "Branch" => L("Tarmoq", "Филиал"),
        "PayMode" => L("Hisob usuli", "Способ расчёта"),
        "Schedule" => L("Ish jadvali", "График работы"),
        "OvertimePolicy" => L("Ortiqcha ish siyosati", "Правило сверхурочных"),
        "Updated" => L("O'zgartirildi", "Изменено"),
        "Name" => L("Nomi", "Название"),
        _ => field,
    };

    public static string Error => L("Xato", "Ошибка");

    /// <summary>Core xatolari (DomainError + Args) — UI tilida.</summary>
    public static string Domain(DomainException ex)
    {
        string MonthArg(int i) => ex.Args.Count > i && ex.Args[i] is MonthKey key ? Month(key) : "—";
        string TextArg(int i) => ex.Args.Count > i ? ex.Args[i]?.ToString() ?? "" : "";
        return ex.Error switch
        {
            DomainError.PeriodClosed => F($"{MonthArg(0)} yopilgan — o'zgartirib bo'lmaydi.", $"{MonthArg(0)} закрыт — изменения невозможны."),
            DomainError.PeriodExists => F($"{MonthArg(0)} allaqachon mavjud.", $"{MonthArg(0)} уже существует."),
            DomainError.NextPeriodClosed when ex.Args.Count > 1 => F(
                $"{MonthArg(0)} yopilgan — {MonthArg(1)}ni qayta ochib bo'lmaydi.",
                $"{MonthArg(0)} закрыт — {MonthArg(1)} открыть заново нельзя."),
            DomainError.NextPeriodClosed => F($"{MonthArg(0)} yopilgan — avval uni qayta oching.", $"{MonthArg(0)} закрыт — сначала откройте его заново."),
            DomainError.InvalidDaysWorked => InvalidDays + ".",
            DomainError.InvalidAmount => L("Summa noto'g'ri — 0 dan katta bo'lishi kerak.", "Неверная сумма — должна быть больше 0."),
            DomainError.LineNotEmpty => LineNotEmpty,
            DomainError.CarryOverReadOnly => L(
                "O'tgan oydan qarz yozuvini o'chirib bo'lmaydi — manba oyni qayta oching.",
                "Запись долга из прошлого месяца удалить нельзя — откройте заново исходный месяц."),
            DomainError.LineExists => F($"{TextArg(0)} bu oyda allaqachon bor.", $"{TextArg(0)} уже есть в этом месяце."),
            DomainError.EmployeeHasHistory => DeleteBlocked,
            DomainError.EmptyName => NameRequired + ".",
            DomainError.ReasonRequired => L("Bekor qilish sababi majburiy.", "Причина отмены обязательна."),
            DomainError.AlreadyVoided => L("Voqea allaqachon bekor qilingan.", "Отметка уже отменена."),
            DomainError.InvalidEvent => L("Voqea noto'g'ri.", "Неверная отметка."),
            DomainError.InvalidSchedule => TextArg(0) switch
            {
                "Name" => L("Jadval nomi bo'sh bo'lmasligi kerak.", "Название графика не может быть пустым."),
                "Duration" => InvalidDuration + ".",
                "Break" => L("Tanaffus smenadan qisqa bo'lishi kerak.", "Перерыв должен быть короче смены."),
                "WeekDays" => L("Kamida bitta hafta kunini tanlang.", "Выберите хотя бы один день недели."),
                _ => L("Ish/dam kunlari noto'g'ri (ish ≥ 1, jami ≤ 31).", "Неверные дни работы/отдыха (работа ≥ 1, всего ≤ 31)."),
            },
            DomainError.ScheduleInUse => F($"«{TextArg(0)}» xodimga biriktirilgan — o'chirib bo'lmaydi.", $"График «{TextArg(0)}» назначен сотруднику — удалить нельзя."),
            DomainError.CycleAnchorRequired => L("Navbatma-navbat jadval uchun birinchi ish kuni sanasini kiriting.", "Для сменного графика укажите дату первого рабочего дня."),
            DomainError.BranchInUse => F(
                $"«{BranchName(TextArg(0))}» tarmog'ida xodimlar bor — o'chirib bo'lmaydi, nofaol qiling.",
                $"В филиале «{BranchName(TextArg(0))}» есть сотрудники — удалить нельзя, сделайте его неактивным."),
            _ => ex.Message,
        };
    }
}

/// <summary>PDF izohidagi formula uchun qiymatlar (Strings.PdfScheduleExplanation).</summary>
public readonly record struct PayslipLike(long Salary, int Planned, int Worked, decimal Rate, long BasePay);
