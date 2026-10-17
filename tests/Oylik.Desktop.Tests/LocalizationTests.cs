using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Oylik.Desktop.Tests;

/// <summary>3 til: hech bir matn bo'sh emas, kirill/rus matnlarda tarjima qilinmagan lotin so'z qolmagan.</summary>
public sealed partial class LocalizationTests : IDisposable
{
    public static TheoryData<UiLanguage> Languages => new() { UiLanguage.UzLatn, UiLanguage.UzCyrl, UiLanguage.Ru };

    public void Dispose() => Strings.SetLanguage(UiLanguage.UzLatn);

    /// <summary>Kirill/rus matnda ruxsat etilgan lotin: qisqartmalar, klaviatura, URL, dastur nomi.</summary>
    [GeneratedRegex(@"https?://\S+|oylik\.example\.uz|\b(OK|QR|URL|ID|API|Excel|Ctrl|Enter|Tab|Esc|Delete|F2|F|N|Google|Drive|localhost|Oylik|Wi|Fi|LAN|PDF|Telegram|Desktop|V|OY|pdf)\b")]
    private static partial Regex AllowedLatin();

    /// <summary>PDF: dasturning o'z matnlaridagi har bir belgi joylangan shriftlarda bor («?» ga almashmaydi).</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_text_is_covered_by_embedded_pdf_fonts(UiLanguage language)
    {
        Oylik.Desktop.Services.Payslips.PayslipPdf.EnsureInitialized();
        Strings.SetLanguage(language);
        var missing = AllTexts()
            .Where(t => t.Text is not null && !Oylik.Desktop.Services.Payslips.PdfText.IsCovered(t.Text.Replace("\n", "").Replace("\r", "").Replace("\t", "")))
            .Select(t => $"{t.Key}: {t.Text}")
            .ToList();
        missing.Should().BeEmpty();
    }

    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex AnyLatin();

    private static readonly MonthKey Oct = new(2026, 10);
    private static readonly DateTimeOffset At = new(2026, 10, 12, 9, 41, 0, TimeSpan.FromHours(5));

    /// <summary>Strings'ning har bir public matni (property va metodlar — namunaviy argumentlar bilan).</summary>
    private static IEnumerable<(string Key, string? Text)> AllTexts()
    {
        var type = typeof(Strings);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string)))
            yield return (p.Name, (string?)p.GetValue(null));
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(string)))
            yield return (f.Name, (string?)f.GetValue(null));
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName))
        {
            if (m.Name is nameof(Strings.Domain) or nameof(Strings.ImportIssueText) or nameof(Strings.ImportError) or nameof(Strings.AuditField))
                continue; // quyida alohida tekshiriladi
            var args = m.GetParameters().Select(p => Sample(p.ParameterType)).ToArray();
            var key = m.GetParameters().Length == 0 ? m.Name : $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})";
            yield return (key, (string?)m.Invoke(null, args));
        }
        foreach (var error in Enum.GetValues<DomainError>())
        {
            // Args — DomainError izohidagi tartibda: oy(lar) yoki xodim ismi
            object[] args = error switch
            {
                DomainError.LineExists => ["Олим"],
                DomainError.BranchInUse => ["1-tarmoq"],
                DomainError.ScheduleInUse => ["Қоровул"],
                DomainError.InvalidSchedule => ["Duration"],
                _ => [Oct, Oct.Previous()],
            };
            yield return ($"Domain.{error}", Strings.Domain(new DomainException(error, "", args)));
            yield return ($"Domain.{error}.1", Strings.Domain(new DomainException(error, "", args[0])));
        }
        foreach (var kind in Enum.GetValues<ImportIssueKind>())
            yield return ($"ImportIssue.{kind}", Strings.ImportError(new ImportIssue(kind, 7, "Оклад", "абв", 32, 6_000_000, 200_000, 30)));
        foreach (var field in new[] { "DaysWorked", "DailyRate", "MonthlySalary", "FullName", "Amount", "Status", "IsActive", "Created", "Added", "Removed", "Deleted", "Note", "DailyRateIsManual" })
            yield return ($"AuditField.{field}", Strings.AuditField(field));
        foreach (var route in Enum.GetValues<Oylik.Data.Sync.ServerRoute>())
            yield return ($"ServerStatus.{route}", Strings.ServerStatus(route, At, "Қоровул-1", At));
        // 2.2: barcha holatlar va namuna shablonlar
        var weekly = new WorkSchedule { Id = WorkSchedule.SampleDaytimeId, Name = "Kunduzgi 08:00–17:00", WeekDays = WorkSchedule.MondayToSaturday, DurationMinutes = 540, BreakMinutes = 60 };
        yield return ("ScheduleSummary.Weekly", Strings.ScheduleSummary(weekly));
        yield return ("ScheduleName.Daytime", Strings.ScheduleName(weekly));
        yield return ("ScheduleName.Twelve", Strings.ScheduleName(new WorkSchedule { Id = WorkSchedule.SampleTwelveHoursId, Name = "12 soat 08:00–20:00" }));
        yield return ("ScheduleName.Null", Strings.ScheduleName(null));
        foreach (var policy in Enum.GetValues<OvertimePolicy>())
            yield return ($"OvertimePolicyName.{policy}", Strings.OvertimePolicyName(policy, 30_000));
        foreach (var kind in Enum.GetValues<ExcusedKind>())
            yield return ($"ExcusedName.{kind}", Strings.ExcusedName(kind));
        foreach (var mode in Enum.GetValues<PayMode>())
            yield return ($"PayModeName.{mode}", Strings.PayModeName(mode));
        for (var month = 1; month <= 12; month++)
            yield return ($"MonthName.{month}", Strings.MonthName(month));
        foreach (var day in Enum.GetValues<DayOfWeek>())
            yield return ($"WeekDay.{day}", Strings.WeekDayShort(day));
    }

    private static object? Sample(Type type) =>
        type == typeof(string) ? "Олим" :
        type == typeof(int) ? 3 :
        type == typeof(decimal) ? 47.5m :
        type == typeof(DateTimeOffset) ? At :
        type == typeof(DateTimeOffset?) ? At :
        type == typeof(DateOnly) ? new DateOnly(2026, 10, 12) :
        type == typeof(TimeSpan) ? TimeSpan.FromSeconds(421) :
        type == typeof(bool) ? true :
        type == typeof(MonthKey) ? Oct :
        type == typeof(DayOfWeek) ? DayOfWeek.Monday :
        type == typeof(UiLanguage) ? UiLanguage.Ru :
        type == typeof(AttendanceEventType) ? AttendanceEventType.In :
        type == typeof(Branch) ? new Branch { Name = "1-tarmoq" } :
        type == typeof(Oylik.Data.Sync.ServerRoute) ? Oylik.Data.Sync.ServerRoute.Local :
        type == typeof(WorkSchedule) ? new WorkSchedule { Id = WorkSchedule.SampleDailyId, Name = "Sutkalik 08:00–08:00, kun ora", Kind = ScheduleKind.Cycle, DurationMinutes = 1440 } :
        type == typeof(PayMode) ? PayMode.Schedule :
        type == typeof(long) ? 6_000_000L :
        type == typeof(PayslipLike) ? new PayslipLike(6_000_000, 10_800, 10_350, 33_333.33m, 5_750_000) :
        type == typeof(OvertimePolicy) ? OvertimePolicy.Multiplier :
        type == typeof(ExcusedKind) ? ExcusedKind.Paid :
        type == typeof(DateOnly?) ? new DateOnly(2026, 10, 1) :
        type == typeof(PayrollLine) ? new PayrollLine { PayMode = PayMode.Schedule, MonthlySalary = 6_000_000, PlannedMinutes = 10_800, WorkedMinutes = 10_350, OvertimeMinutes = 90, OvertimePay = 75_000, BasePay = 5_750_000, OvertimePolicy = OvertimePolicy.Multiplier, OvertimeValue = 1.5m } :
        throw new NotSupportedException($"Strings testiga namunaviy {type} qo'shing");

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_text_exists_in_every_language(UiLanguage language)
    {
        Strings.SetLanguage(language);
        var texts = AllTexts().ToList();

        texts.Should().HaveCountGreaterThan(250);
        texts.Where(t => string.IsNullOrWhiteSpace(t.Text)).Select(t => t.Key).Should().BeEmpty();
        Strings.LanguageNames.Should().HaveCount(Enum.GetValues<UiLanguage>().Length);
    }

    [Theory]
    [InlineData(UiLanguage.UzCyrl)]
    [InlineData(UiLanguage.Ru)]
    public void Cyrillic_and_russian_texts_have_no_untranslated_latin(UiLanguage language)
    {
        Strings.SetLanguage(language);
        var leftovers = AllTexts()
            .Select(t => (t.Key, Remaining: AllowedLatin().Replace(t.Text ?? "", "")))
            .Where(t => AnyLatin().IsMatch(t.Remaining))
            .Select(t => $"{t.Key}: {t.Remaining}");
        leftovers.Should().BeEmpty();
    }

    [Fact]
    public void Russian_differs_from_uzbek_and_domain_errors_are_translated()
    {
        Strings.SetLanguage(UiLanguage.UzLatn);
        var latn = AllTexts().ToDictionary(t => t.Key, t => t.Text);
        Strings.SetLanguage(UiLanguage.Ru);
        var ru = AllTexts().ToDictionary(t => t.Key, t => t.Text);

        ru.Keys.Should().BeEquivalentTo(latn.Keys);
        Strings.NavAttendance.Should().Be("Посещаемость");
        Strings.RemainderSum.Should().Be("Остаток");
        Strings.Domain(new DomainException(DomainError.PeriodClosed, "Oktabr 2026 yopilgan", Oct)).Should().Be("Октябрь 2026 закрыт — изменения невозможны.");
        foreach (var error in Enum.GetValues<DomainError>())
            Strings.Domain(new DomainException(error, "LOG", Oct)).Should().NotBe("LOG", $"{error} tarjimasi yo'q");
    }

    [Fact]
    public void Schedule_texts_hours_and_rates()
    {
        Strings.SetLanguage(UiLanguage.UzLatn);
        Strings.HoursMinutes(10_350).Should().Be("172:30");
        Strings.HoursMinutes(5).Should().Be("0:05");
        Strings.Rate(6_000_000m / 180).Should().Be("33 333,33");
        var weekly = new WorkSchedule { Name = "x", WeekDays = WorkSchedule.MondayToSaturday, StartTime = new(8, 0), DurationMinutes = 540, BreakMinutes = 60 };
        Strings.ScheduleSummary(weekly).Should().Be("Du–Sh · 08:00–17:00 (9:00) · tanaffus 1:00");
        var cycle = new WorkSchedule { Name = "x", Kind = ScheduleKind.Cycle, CycleWorkDays = 1, CycleOffDays = 1, StartTime = new(8, 0), DurationMinutes = 1440 };
        Strings.ScheduleSummary(cycle).Should().Be("1/1 · 08:00–08:00 (24:00)");
        Strings.SetLanguage(UiLanguage.Ru);
        Strings.ScheduleName(new WorkSchedule { Id = WorkSchedule.SampleDailyId, Name = "Sutkalik 08:00–08:00, kun ora" }).Should().Be("Сутки 08:00–08:00, через день");
        Strings.ScheduleName(new WorkSchedule { Id = WorkSchedule.SampleDailyId, Name = "Qorovullar" }).Should().Be("Qorovullar", "nomi o'zgartirilgan — tarjima qilinmaydi");
        Strings.ScheduleSummary(weekly).Should().Be("Пн–Сб · 08:00–17:00 (9:00) · перерыв 1:00");
    }

    [Fact]
    public void Excel_column_headers_are_the_same_in_every_language()
    {
        // Oylik jadvali ustunlari namuna Excel shabloni bilan aynan — til almashganda o'zgarmaydi.
        string[] Headers() => [Strings.ColNo, Strings.ColSalary, Strings.ColName, Strings.ColDaily, Strings.ColDays, Strings.ColTotal, Strings.ColAdvance, Strings.ColRemainder];
        Strings.SetLanguage(UiLanguage.UzLatn);
        var latn = Headers();
        Strings.SetLanguage(UiLanguage.Ru);
        Headers().Should().Equal(latn);
    }

    [Theory]
    [InlineData("O'chirish", "Ўчириш")]
    [InlineData("Ro'yxatdan o'tish kaliti", "Рўйхатдан ўтиш калити")]
    [InlineData("Qorong'i", "Қоронғи")]
    [InlineData("Yorug'", "Ёруғ")]
    [InlineData("Ma'lumotlar papkasini ochish", "Маълумотлар папкасини очиш")]
    [InlineData("Yetim Ketdi — Keldi topilmadi", "Етим Кетди — Келди топилмади")]
    [InlineData("Excel'ga eksport", "Excel'га экспорт")]
    [InlineData("Hozir sinxronlash", "Ҳозир синхронлаш")]
    [InlineData("sinxronizatsiya", "синхронизация")]
    [InlineData("Kunlikni qo'lda kiritsangiz", "Кунликни қўлда киритсангиз")]
    [InlineData("Oylik ilovasini oching", "Oylik иловасини очинг")]
    [InlineData("Hali bog'langan telefon yo'q.", "Ҳали боғланган телефон йўқ.")]
    [InlineData("Kompyuter ID", "Компьютер ID")]
    [InlineData("Qidirish (Ctrl+F)", "Қидириш (Ctrl+F)")]
    [InlineData("JAMI", "ЖАМИ")]
    [InlineData("Yangi xodim", "Янги ходим")]
    [InlineData("Shu yerda", "Шу ерда")]
    [InlineData("Hozir backup olish", "Ҳозир бэкап олиш")]
    [InlineData("masalan http://192.168.1.10:5000", "масалан http://192.168.1.10:5000")]
    [InlineData("Voqea", "Воқеа")]
    [InlineData("ta'sir", "таъсир")]
    [InlineData("Lokal manzil (Wi-Fi/LAN)", "Локал манзил (Wi-Fi/LAN)")]
    public void Latin_to_cyrillic_transliteration(string latin, string cyrillic) =>
        UzCyrillic.FromLatin(latin).Should().Be(cyrillic);

    [Fact]
    public void Interpolated_values_are_not_transliterated()
    {
        Strings.SetLanguage(UiLanguage.UzCyrl);
        Strings.ConfirmDeactivateDevice("Qorovul-1").Should().StartWith("«Qorovul-1» узилсинми?");
        Strings.Month(Oct).Should().Be("Октябрь 2026");
        Strings.SetLanguage(UiLanguage.UzLatn);
        Strings.Month(Oct).Should().Be("Oktabr 2026");
    }

    [Fact]
    public void Loc_switches_live_and_raises_indexer_change()
    {
        Strings.SetLanguage(UiLanguage.UzLatn);
        var loc = Loc.Instance;
        var changed = new List<string?>();
        loc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var languageEvents = 0;
        void OnChanged() => languageEvents++;
        Strings.LanguageChanged += OnChanged;
        try
        {
            loc["NavAttendance"].Should().Be("Davomat");
            loc["ColSalary"].Should().Be("Оклад");

            Strings.SetLanguage(UiLanguage.Ru);
            changed.Should().Contain("Item[]");
            loc["NavAttendance"].Should().Be("Посещаемость");
            loc["ThemeDark"].Should().Be("Тёмная");

            Strings.SetLanguage(UiLanguage.UzCyrl);
            loc["NavAttendance"].Should().Be("Давомат");

            var before = languageEvents;
            Strings.SetLanguage(UiLanguage.UzCyrl); // o'zgarmadi — hodisa yo'q
            languageEvents.Should().Be(before);
            languageEvents.Should().Be(2);
            loc["NoSuchKey"].Should().Be("#NoSuchKey#");
        }
        finally
        {
            Strings.LanguageChanged -= OnChanged;
        }
    }

    [GeneratedRegex(@"\{r:T (\w+)\}")]
    private static partial Regex XamlKey();

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void Every_xaml_text_key_exists()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(SourceFile())!); // build chiqishi boshqa joyda bo'lishi mumkin
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Oylik.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var views = Path.Combine(dir!.FullName, "src", "Oylik.Desktop", "Views");
        var keys = Directory.GetFiles(views, "*.axaml")
            .SelectMany(f => XamlKey().Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();

        keys.Should().HaveCountGreaterThan(100);
        keys.Where(k => !Loc.Has(k.Key)).Select(k => $"{k.File}: {k.Key}").Should().BeEmpty();
        Directory.GetFiles(views, "*.axaml").Where(f => File.ReadAllText(f).Contains("x:Static r:Strings.") &&
                File.ReadAllText(f).Split("x:Static r:Strings.").Skip(1).Any(part => !part.StartsWith("Col") && !part.StartsWith("AppName")))
            .Should().BeEmpty("tarjima qilinadigan matnlar jonli bo'lishi uchun {r:T ...} bilan bog'lanadi");
    }

    [Theory]
    [InlineData("uz-Latn", UiLanguage.UzLatn)]
    [InlineData("uz-Cyrl", UiLanguage.UzCyrl)]
    [InlineData("ru", UiLanguage.Ru)]
    [InlineData(null, UiLanguage.UzLatn)]
    [InlineData("xx", UiLanguage.UzLatn)]
    public void Language_codes_round_trip(string? code, UiLanguage expected)
    {
        UiLanguages.Parse(code).Should().Be(expected);
        UiLanguages.Parse(UiLanguages.Code(expected)).Should().Be(expected);
    }

    [Fact]
    public async Task Ui_preferences_are_read_before_the_database_is_opened()
    {
        var home = Path.Combine(Path.GetTempPath(), "oylik-ui-tests-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(home);
        paths.EnsureCreated();
        try
        {
            UiPreferences.Read(paths).Should().Be(UiPreferences.Default); // baza hali yo'q

            using (var service = new PayrollService(paths, TimeProvider.System))
            {
                await service.InitializeAsync(dailyBackup: false);
                service.Settings.Language.Should().Be("uz-Latn");
                service.Settings.Theme.Should().Be(UiTheme.Light);
                await service.SaveSettingsAsync(s =>
                {
                    s.Language = UiLanguages.Code(UiLanguage.Ru);
                    s.Theme = UiTheme.Dark;
                });
            }
            SqliteConnection.ClearAllPools();

            UiPreferences.Read(paths).Should().Be(new UiPreferences("ru", UiTheme.Dark));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(home, recursive: true); } catch (IOException) { }
        }
    }
}
