using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Oylik.Core.Domain;
using Oylik.Data;
using Oylik.Desktop.Resources;
using Oylik.Desktop.ViewModels;
using Oylik.Desktop.Views;
using Serilog;

namespace Oylik.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Til va mavzu oyna yaratilishidan oldin — birinchi kadr to'g'ri tilda/mavzuda chiqadi (keyin jonli almashadi).
            // --lang/--theme — snapshot uchun, saqlanmaydi.
            var prefs = UiPreferences.Read(AppPaths.Default);
            Strings.SetLanguage(UiLanguages.Parse(ArgValue(desktop.Args, "--lang") ?? prefs.Language));
            var theme = ParseTheme(ArgValue(desktop.Args, "--theme")) ?? prefs.Theme;
            ApplyTheme(theme);

            // --now 2026-10-13T08:12 — snapshot uchun «hozir»ni simulyatsiya (H12 jonli ranglar); oddiy ishga tushishda — tizim vaqti
            TimeProvider clock = ArgValue(desktop.Args, "--now") is { } nowArg && DateTimeOffset.TryParse(nowArg, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var simulated)
                ? new SimulatedClock(simulated)
                : TimeProvider.System;
            var service = new PayrollService(AppPaths.Default, clock);
            var main = new MainWindowViewModel(service, AppPaths.Default);
            var window = new MainWindow { DataContext = main };
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => service.Dispose();

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Log.Error(e.Exception, "UI exception");
                main.ShowError(e.Exception.Message);
                e.Handled = true;
            };

            main.NoSync = SnapshotArgs(desktop.Args) is not null;
            desktop.Exit += (_, _) => main.Dispose();
            main.Theme = theme;
            main.ApplyTheme = ApplyTheme;
            main.IsDarkActual = ActualThemeVariant == ThemeVariant.Dark;
            ActualThemeVariantChanged += (_, _) =>
            {
                Palette.Refresh();
                main.IsDarkActual = ActualThemeVariant == ThemeVariant.Dark;
            };
            window.Opened += async (_, _) =>
            {
                await main.InitializeAsync();
                Log.Information("Baza ochildi: {Path}", AppPaths.Default.Database);
                if (desktop.Args is { } args && Array.IndexOf(args, "--seed") is var si and >= 0 && si + 1 < args.Length)
                {
                    await SeedAsync(main, args[si + 1]);
                    await SeedSchedulesAsync(main);
                    await SeedAttendanceAsync(main);
                }
                if (SnapshotArgs(desktop.Args) is { } snapshot)
                    await TakeSnapshotsAsync(desktop, window, main, snapshot);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ApplyTheme(UiTheme theme) => RequestedThemeVariant = theme switch
    {
        UiTheme.Dark => ThemeVariant.Dark,
        UiTheme.System => ThemeVariant.Default,
        _ => ThemeVariant.Light,
    };

    private static UiTheme? ParseTheme(string? value) => value?.ToLowerInvariant() switch
    {
        "dark" => UiTheme.Dark,
        "light" => UiTheme.Light,
        "system" => UiTheme.System,
        _ => null,
    };

    private static string? ArgValue(string[]? args, string name)
    {
        if (args is null) return null;
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ---------- Dasturchi uchun: `Oylik.exe --snapshot <papka> [--lang uz-Latn|uz-Cyrl|ru] [--theme light|dark]
    //            [--switch-lang ru] [--switch-theme dark]` — har ekranni PNG ga chizadi va chiqadi.
    //            --switch-*: oxirida o'sha jarayonda (qayta ishga tushirishsiz) til/mavzu almashtirilib, Switch-*.png chiziladi. ----------

    private static string? SnapshotArgs(string[]? args) => ArgValue(args, "--snapshot");

    private static async Task TakeSnapshotsAsync(IClassicDesktopStyleApplicationLifetime desktop, Window window, MainWindowViewModel main, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var page in Enum.GetValues<Page>())
        {
            main.Navigate(page);
            await Task.Delay(700);
            if (page == Page.Payroll && main.Payroll.PrevMonthCommand.CanExecute(null))
            {
                // Davomatsiz oy (oldingi) — "Davomat" ustuni yashirin holati
                Render(window, Path.Combine(folder, $"{page}.png"));
                main.Payroll.PrevMonthCommand.Execute(null);
                await Task.Delay(700);
                Render(window, Path.Combine(folder, $"{page}-prev.png"));
                main.Payroll.NextMonthCommand.Execute(null);
                await Task.Delay(700);
            }
            if (page == Page.Payroll && window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault() is { } grid && main.Payroll.Rows.Count > 2)
            {
                // Tahrir holati: Оклад katagi, matn to'liq belgilangan
                grid.SelectedIndex = 2;
                grid.CurrentColumn = grid.Columns[1];
                grid.Focus();
                grid.BeginEdit();
                await Task.Delay(500);
                Render(window, Path.Combine(folder, $"{page}-edit.png"));
                grid.CancelEdit();
                await Task.Delay(300);

                // 3.9: filtr — 1-tarmoq (jadval, xulosa, JAMI shu tarmoq bo'yicha), keyin yana Barchasi
                if (main.BranchOptions.Count > 1)
                {
                    main.SelectedBranchOption = main.BranchOptions[1];
                    await Task.Delay(700);
                    Render(window, Path.Combine(folder, $"{page}-branch.png"));
                    main.SelectedBranchOption = main.BranchOptions[0];
                    await Task.Delay(500);
                }
            }
            if (page == Page.Employees && window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault() is { } employeesGrid && main.Employees.Rows.Count > 3)
            {
                // Tahrir holati: Oklad katagi (jadvalning o'zida)
                employeesGrid.SelectedIndex = 3;
                employeesGrid.CurrentColumn = employeesGrid.Columns[4];
                employeesGrid.Focus();
                employeesGrid.BeginEdit();
                await Task.Delay(500);
                Render(window, Path.Combine(folder, $"{page}-edit.png"));
                employeesGrid.CancelEdit();
                await Task.Delay(300);
            }
            if (page == Page.Attendance && main.Attendance.Issues.FirstOrDefault() is { } issue)
            {
                await main.Attendance.ShowIssueAsync(issue);
                await Task.Delay(300);
            }
            Render(window, Path.Combine(folder, $"{page}.png"));
            if (page == Page.Settings && window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Content is Control content)
            {
                // Sozlamalar uzun — butun sahifa (Ko'rinish, Tarmoqlar, Server — ikki manzil) bitta rasmda
                if (content is Panel panel)
                    panel.Background = window.Background;
                Render(content, Path.Combine(folder, $"{page}-full.png"));
                if (content is Panel restore)
                    restore.ClearValue(Panel.BackgroundProperty);
            }
        }
        main.Navigate(Page.Payroll);
        await Task.Delay(500);
        await PayslipSamplesAsync(main, folder);
        await PreviewSnapshotAsync(window, main, folder);
        var switchLang = ArgValue(desktop.Args, "--switch-lang");
        var switchTheme = ParseTheme(ArgValue(desktop.Args, "--switch-theme"));
        if (switchLang is not null || switchTheme is not null)
            await SwitchSnapshotsAsync(window, main, folder, switchLang, switchTheme);
        desktop.Shutdown();
    }

    /// <summary>5.11 namunalar: jadval bo'yicha xodim (Гайрат — sutkalik, kechikish, ortiqcha, avans, qarz), kunbay xodim va umumiy vedomost.</summary>
    private static async Task PayslipSamplesAsync(MainWindowViewModel main, string folder)
    {
        if (main.Payroll.Period is not { } period)
            return;
        var lines = period.Lines.OrderBy(l => l.Employee?.FullName, NameComparer.Instance).ToList();
        var schedule = lines.FirstOrDefault(l => l.Employee?.FullName == "Гайрат" && l.IsSchedule) ?? lines.FirstOrDefault(l => l.IsSchedule);
        var daily = lines.FirstOrDefault(l => !l.IsSchedule && l.Employee?.FullName == "Бекзод") ?? lines.FirstOrDefault(l => !l.IsSchedule);
        await Task.Run(() =>
        {
            void Save(string name, IReadOnlyList<Oylik.Desktop.Services.Payslips.PayslipModel> slips, Oylik.Desktop.Services.Payslips.PayrollSheetModel? sheet)
            {
                File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), Oylik.Desktop.Services.Payslips.PayslipPdf.Pdf(slips, sheet));
                var pages = Oylik.Desktop.Services.Payslips.PayslipPdf.Images(slips, sheet, 110);
                for (var i = 0; i < Math.Min(2, pages.Count); i++)
                    File.WriteAllBytes(Path.Combine(folder, $"{name}-p{i + 1}.png"), pages[i]);
            }
            if (schedule is not null)
                Save("Payslip-schedule", [main.Payroll.BuildPayslip(period, schedule)], null);
            if (daily is not null)
                Save("Payslip-daily", [main.Payroll.BuildPayslip(period, daily)], null);
            Save("Vedomost", [], Oylik.Desktop.Services.Payslips.PayslipBuilder.Sheet(main.Service, period, lines, null, Strings.PdfSheetMeasure));
        });
    }

    /// <summary>Hisob varaqasi oldindan ko'rish — kichik noutbuk ekrani (ish maydoni 1280×800) simulyatsiyasi bilan (ScreenFit).</summary>
    private static async Task PreviewSnapshotAsync(Window owner, MainWindowViewModel main, string folder)
    {
        if (main.Payroll.Period is not { } period
            || (period.Lines.FirstOrDefault(l => l.Employee?.FullName == "Гайрат") ?? period.Lines.FirstOrDefault()) is not { } line)
            return;
        var model = main.Payroll.BuildPayslip(period, line);
        var preview = new PayslipPreviewWindow($"{Strings.Payslip} · {model.EmployeeName}", [model], null, period.Key, "snapshot");
        ScreenFit.Apply(preview, new Rect(0, 0, 1280, 800));
        preview.Show(owner);
        await Task.Delay(3000);
        Render(preview, Path.Combine(folder, "Payslip-preview-1280x800.png"));
        preview.Close();
    }

    /// <summary>
    /// Jonli almashish isboti: Payroll'da qator tanlanadi va jadval pastga suriladi → chiziladi → til/mavzu almashadi
    /// (oyna va ekranlar qayta yaratilmaydi) → yana chiziladi. Tanlangan qator, oy va scroll joyida qolishi kerak.
    /// </summary>
    private static async Task SwitchSnapshotsAsync(Window window, MainWindowViewModel main, string folder, string? language, UiTheme? theme)
    {
        if (main.Payroll.Rows.Count > 20)
        {
            main.Payroll.SelectedRow = main.Payroll.Rows[20];
            if (window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault() is { } grid)
                grid.ScrollIntoView(main.Payroll.SelectedRow, null);
        }
        await Task.Delay(500);
        Render(window, Path.Combine(folder, "Switch-1-before.png"));
        if (language is not null)
            main.SetLanguage(UiLanguages.Parse(language), save: false);
        if (theme is { } t)
            main.SetTheme(t, save: false);
        await Task.Delay(700);
        Render(window, Path.Combine(folder, "Switch-2-after.png"));
        main.Navigate(Page.Settings);
        await Task.Delay(700);
        Render(window, Path.Combine(folder, "Switch-3-settings.png"));
        main.Navigate(Page.Attendance);
        await Task.Delay(700);
        Render(window, Path.Combine(folder, "Switch-4-attendance.png"));
    }

    /// <summary>
    /// Demo: shablondan 57 xodim. Iyul, Avgust, Sentyabr to'liq to'ldirilib yopiladi (qarzdorlar keyingi oyga o'tadi),
    /// Oktabr ochiq — 9-sanagacha. Faqat baza bo'sh bo'lsa ishlaydi.
    /// </summary>
    private static async Task SeedAsync(MainWindowViewModel main, string xlsx)
    {
        var service = main.Service;
        if (service.Periods.Count > 0)
            return;

        var rnd = new Random(2026);
        var julKey = new Oylik.Core.Domain.MonthKey(2026, 7);
        var preview = ExcelImporter.Read(xlsx, service.Settings.DaysInMonth);
        await service.ImportAsync(preview, new ImportOptions(julKey));
        await service.SaveSettingsAsync(s => s.CompanyName = "Demo korxona");
        main.CompanyName = "Demo korxona";

        // 3.9: demo tarmoqlar — har 3-xodim 2-tarmoqda, ikkitasi belgilanmagan, qolgani 1-tarmoqda.
        var demoEmployees = service.Employees.ToList();
        for (int i = 0; i < demoEmployees.Count - 2; i++)
            await service.SetEmployeeBranchAsync(demoEmployees[i], i % 3 == 2 ? Branch.DefaultSecondId : Branch.DefaultFirstId);

        // Shablonda Oklad bo'sh xodimlarga Oklad beramiz (R9 — kartaga ham yoziladi, keyingi oylarga ko'chadi).
        var jul = service.FindPeriod(julKey)!;
        foreach (var line in jul.Lines.Where(l => l.MonthlySalary == 0))
            await service.SetMonthlySalaryAsync(jul, line, rnd.Next(8, 17) * 500_000);

        // Iyul — 27 ish kuni, Avgust — 26, Sentyabr — 26 (yakshanbalarsiz).
        var months = new (int Month, decimal Full)[] { (7, 27), (8, 26), (9, 26) };
        var period = jul;
        foreach (var (month, full) in months)
        {
            var key = new Oylik.Core.Domain.MonthKey(2026, month);
            period = service.FindPeriod(key) ?? await service.CreatePeriodAsync(key);

            foreach (var line in period.Lines)
            {
                var name = line.Employee!.FullName;
                decimal days = name == "Ошпаз" ? Math.Round(full / 2)
                    : rnd.Next(10) switch { 0 => full - 4, 1 => full - 2, 2 => full - 1.5m, 3 => full - 1, _ => full };
                await service.SetDaysWorkedAsync(period, line, days);
                await service.AddAdvanceAsync(period, line, new DateOnly(2026, month, 10), rnd.Next(1, 4) * 500_000, null);
                if (rnd.Next(3) == 0)
                    await service.AddAdvanceAsync(period, line, new DateOnly(2026, month, 25), rnd.Next(1, 5) * 250_000, "Oy oxiri");
            }

            // Har oyda bitta-ikkita qarzdor.
            if (month == 7)
                await MakeDebtor(service, period, "Жамшид", 6, 2_000_000, "Ta'mirlash uchun");
            if (month == 8)
            {
                await MakeDebtor(service, period, "Чори 120", 4, 1_500_000, "To'y");
                // Avgustda Сардор'ning maoshi oshdi.
                var sardor = period.Lines.First(l => l.Employee!.FullName == "Сардор");
                await service.SetMonthlySalaryAsync(period, sardor, 7_500_000);
            }
            if (month == 9)
            {
                // TZ'dagi misol: Sentyabr −1 200 000 qoldiq bilan yopiladi.
                var olim = period.Lines.First(l => l.Employee!.FullName == "Олим ака (тест)");
                await service.SetDaysWorkedAsync(period, olim, 5);
                foreach (var a in olim.Advances.ToList())
                    await service.RemoveAdvanceAsync(period, olim, a);
                await service.AddAdvanceAsync(period, olim, new DateOnly(2026, 9, 3), 2_200_000, "Uy uchun");
                await MakeDebtor(service, period, "Гайрат", 9, 2_500_000, "Davolanish");
            }

            await service.ClosePeriodAsync(period);
        }

        // Oktabr: 9-oktabrgacha 8 ish kuni.
        var octKey = new Oylik.Core.Domain.MonthKey(2026, 10);
        var oct = service.FindPeriod(octKey) ?? await service.CreatePeriodAsync(octKey);
        decimal[] octDays = [8, 8, 8, 7, 8, 6, 8, 7.5m, 8, 0];
        foreach (var line in oct.Lines)
        {
            await service.SetDaysWorkedAsync(oct, line, octDays[rnd.Next(octDays.Length)]);
            if (rnd.Next(4) == 0)
                await service.AddAdvanceAsync(oct, line, new DateOnly(2026, 10, 5), rnd.Next(1, 3) * 500_000, null);
        }
        var farrux = oct.Lines.First(l => l.Employee!.FullName == "Фаррух");
        await service.SetDailyRateAsync(oct, farrux, 180_000);

        main.Payroll.SelectedKey = null;
        main.RefreshBranchOptions();
        main.Navigate(Page.Payroll);
    }

    /// <summary>
    /// 2.2 demo: bir nechta xodim «Jadval bo'yicha» (joriy oy boshidan). Гайрат — sutkalik 1/1 (ortiqcha × 1,5; Sentyabr qarzi bor),
    /// Фаррух, Сардор, Бахтиёр — kunduzgi, Ботир — 12 soat 2/2. Sababli kun, Dam istisnosi va bugungi «Boshqa vaqt».
    /// </summary>
    private static async Task SeedSchedulesAsync(MainWindowViewModel main)
    {
        var service = main.Service;
        if (service.Employees.Any(e => e.PayMode == PayMode.Schedule))
            return;
        var today = service.Today;
        var first = new DateOnly(today.Year, today.Month, 1);
        Employee? Find(string name) => service.Employees.FirstOrDefault(e => e.FullName == name);

        async Task Assign(string name, Guid scheduleId, OvertimePolicy? overtime = null, decimal? value = null)
        {
            if (Find(name) is not { } e)
                return;
            await service.SetPayModeAsync(e, PayMode.Schedule);
            await service.AssignScheduleAsync(e, service.FindSchedule(scheduleId), first, first);
            if (overtime is not null)
                await service.SetEmployeeOvertimeAsync(e, overtime, value);
        }

        await Assign("Гайрат", WorkSchedule.SampleDailyId, OvertimePolicy.Multiplier, 1.5m);
        await Assign("Фаррух", WorkSchedule.SampleDaytimeId);
        await Assign("Сардор", WorkSchedule.SampleDaytimeId);
        await Assign("Бахтиёр", WorkSchedule.SampleDaytimeId);
        await Assign("Ботир", WorkSchedule.SampleTwelveHoursId);

        if (Find("Сардор") is { } sardor && first.AddDays(6) < today)
            await service.SetExcusedAsync(sardor, first.AddDays(6), ExcusedKind.Paid, "Kasallik varaqasi");
        if (Find("Бахтиёр") is { } bahtiyor && first.AddDays(8) < today)
            await service.SetOverrideAsync(bahtiyor, first.AddDays(8), true, null, null, null, "Navbat almashuvi");
        if (Find("Фаррух") is { } farrux)
            await service.SetOverrideAsync(farrux, today, false, new TimeOnly(7, 0), 9 * 60, 60, "Erta smena");
    }

    /// <summary>
    /// Demo davomat: joriy oyning 1-sanasidan «hozir»gacha (TimeProvider — --now bilan simulyatsiya), "Qorovul-1" telefonidan.
    /// Kunbay xodimlar — yakshanbasiz; jadval bo'yicha xodimlar — o'z smenasi bo'yicha (ba'zan kech kelish va ortiqcha ish).
    /// Bugun: kimdir kechikmoqda, kimdir kelmadi. Bitta ochiq juftlik va bitta yetim Ketdi (Tasdiqlash kerak). Faqat voqealar bo'lmasa.
    /// </summary>
    private static async Task SeedAttendanceAsync(MainWindowViewModel main)
    {
        var service = main.Service;
        if (service.AttendanceEvents.Any())
            return;
        var rnd = new Random(1009);
        var zone = service.LocalZone;
        var now = service.Clock.GetLocalNow();
        var today = service.Today;
        var device = new Device { Id = Guid.NewGuid(), Name = "Qorovul-1", PairedAt = now.AddDays(-12), LastSeenAt = now.AddMinutes(-1) };
        var events = new List<AttendanceEvent>();

        DateTimeOffset At(DateOnly d, int minutes)
        {
            var local = d.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        void Add(Employee e, AttendanceEventType type, DateTimeOffset at)
        {
            if (at <= now)
                events.Add(new() { EmployeeId = e.Id, Type = type, OccurredAt = at, DeviceId = device.Id, ReceivedAt = at.AddSeconds(rnd.Next(2, 40)) });
        }

        var employees = service.Employees.Where(e => e.IsActive).ToList();
        var nightGuard = employees.FirstOrDefault(e => e.FullName.StartsWith("Жамшид", StringComparison.Ordinal));
        var first = new DateOnly(today.Year, today.Month, 1);

        // Jadval bo'yicha xodimlar: smena bo'yicha keldi/ketdi
        foreach (var e in employees.Where(e => e.PayMode == PayMode.Schedule))
        {
            var ctx = service.ScheduleContextFor(e.Id);
            for (var d = first; d <= today; d = d.AddDays(1))
            {
                if (ctx.Excused.ContainsKey(d) || ctx.ShiftFor(d) is not { } shift)
                    continue;
                if (d == today && e.FullName == "Бахтиёр")
                    continue; // bugun hali kelmagan — «Kechikmoqda»
                var lateRoll = rnd.Next(7);
                var arrive = lateRoll == 0 ? rnd.Next(9, 36) : rnd.Next(-12, 4);
                if (d == today && e.FullName == "Сардор")
                    arrive = 9; // keldi, kech 9 daq (grace 5 dan oshgan)
                if (e.FullName == "Гайрат" && d == first.AddDays(4))
                    arrive = 27; // hisob varaqasi namunasi: kechikish
                if (d == today && e.FullName == "Фаррух")
                    continue; // 07:00 dagi smena — kelmadi
                var stay = rnd.Next(8) switch { 0 => -rnd.Next(30, 90), 1 or 2 => rnd.Next(20, 75), _ => rnd.Next(-3, 12) };
                var inAt = shift.Start.AddMinutes(arrive).AddSeconds(rnd.Next(0, 50));
                Add(e, AttendanceEventType.In, inAt);
                Add(e, AttendanceEventType.Out, shift.End.AddMinutes(stay).AddSeconds(rnd.Next(0, 50)));
            }
        }

        // Kunbay xodimlar (avvalgidek)
        foreach (var d in Enumerable.Range(0, today.Day).Select(i => first.AddDays(i)))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday && d != today)
                continue;
            foreach (var e in employees.Where(e => e.PayMode == PayMode.Daily))
            {
                var isToday = d == today;
                if (rnd.Next(14) == 0)
                    continue; // kelmagan
                if (e == nightGuard)
                {
                    if (isToday) continue;
                    Add(e, AttendanceEventType.In, At(d, 20 * 60 + rnd.Next(-10, 10)));
                    Add(e, AttendanceEventType.Out, At(d.AddDays(1), 8 * 60 + rnd.Next(-10, 10)));
                    continue;
                }
                var arrive = 7 * 60 + 45 + rnd.Next(0, 45);
                Add(e, AttendanceEventType.In, At(d, arrive));
                if (isToday && rnd.Next(10) > 0)
                    continue; // hali ichkarida
                var leave = rnd.Next(10) switch { 0 => arrive + 180, 1 => arrive + 360, _ => 17 * 60 + rnd.Next(0, 90) };
                Add(e, AttendanceEventType.Out, At(d, leave));
            }
        }

        // Tasdiqlash kerak: Ketdi bosilmagan va Keldi'siz Ketdi (2 kun oldin) — kunbay xodimlarda
        var past = today.AddDays(-2).DayOfWeek == DayOfWeek.Sunday ? today.AddDays(-3) : today.AddDays(-2);
        if (employees.FirstOrDefault(e => e.FullName == "Анвар") is { PayMode: PayMode.Daily } anvar)
        {
            events.RemoveAll(x => x.EmployeeId == anvar.Id && AttendanceRules.LocalDate(x.OccurredAt, zone) == past);
            Add(anvar, AttendanceEventType.In, At(past, 8 * 60 + 5));
        }
        if (employees.FirstOrDefault(e => e.FullName == "Дилмурод") is { PayMode: PayMode.Daily } dilmurod)
        {
            events.RemoveAll(x => x.EmployeeId == dilmurod.Id && AttendanceRules.LocalDate(x.OccurredAt, zone) == past);
            Add(dilmurod, AttendanceEventType.Out, At(past, 18 * 60 + 12));
        }

        await service.ApplyServerEventsAsync(events, 0);
        await service.ReplaceDevicesAsync([device]);

        // Bitta qo'lda tuzatish (hisob varaqasida «*» bilan) va Гайрат'ga oktabr avansi
        if (employees.FirstOrDefault(e => e.FullName == "Гайрат") is { } gayrat
            && service.FindPeriod(new MonthKey(today.Year, today.Month)) is { } period && period.FindLine(gayrat.Id) is { } line)
        {
            var mistaken = events.Where(x => x.EmployeeId == gayrat.Id && x.Type == AttendanceEventType.Out).OrderBy(x => x.OccurredAt).FirstOrDefault();
            if (mistaken is not null)
            {
                await service.VoidEventAsync(mistaken, "Noto'g'ri xodim bosilgan");
                await service.AddManualEventAsync(gayrat, AttendanceEventType.Out, mistaken.OccurredAt.AddMinutes(14), "Qorovul aytdi: 08:20 da ketgan");
            }
            if (period.IsClosed is false && line.Advances.All(a => a.Kind != AdvanceKind.Cash))
                await service.AddAdvanceAsync(period, line, first.AddDays(4), 1_000_000, "Bayram oldidan");
        }
    }

    private static async Task MakeDebtor(PayrollService service, Oylik.Core.Domain.PayrollPeriod period, string name, decimal days, long advance, string note)
    {
        var line = period.Lines.First(l => l.Employee!.FullName == name);
        await service.SetDaysWorkedAsync(period, line, days);
        await service.AddAdvanceAsync(period, line, new DateOnly(period.Year, period.Month, 20), advance, note);
    }

    public static void Render(Control control, string path)
    {
        var size = new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height);
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(control);
        bitmap.Save(path);
    }
}

/// <summary>Snapshot uchun: berilgan paytdan boshlab yuruvchi soat (H12 holatlarini ko'rsatish).</summary>
internal sealed class SimulatedClock(DateTimeOffset start) : TimeProvider
{
    private readonly long _origin = global::System.Diagnostics.Stopwatch.GetTimestamp();

    public override DateTimeOffset GetUtcNow() => start.ToUniversalTime() + global::System.Diagnostics.Stopwatch.GetElapsedTime(_origin);
}
