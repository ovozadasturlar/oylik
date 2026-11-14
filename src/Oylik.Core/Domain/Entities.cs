namespace Oylik.Core.Domain;

public sealed class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = "";
    public string? Note { get; set; }
    public long MonthlySalary { get; set; }
    public long DailyRate { get; set; }
    public bool DailyRateIsManual { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Tarmoq (3.9); null — belgilanmagan.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>H13.10: doimiy tabel raqami (yaratilish tartibida) — hujjat raqami OY-YYYY-MM-&lt;TabelNo&gt;.</summary>
    public int TabelNo { get; set; }

    /// <summary>
    /// Bosqich 3: server bilan oxirgi kelishilgan holat xeshi (ism/izoh/faollik/tabel/smena chegarasi). Joriy xesh farq qilsa —
    /// lokal o'zgarish hali serverga yetmagan: faqat shular push qilinadi va pull ularni bosib ketmaydi. null — hech qachon.
    /// </summary>
    public string? SyncHash { get; set; }

    /// <summary>4.3: hisob usuli; ochiq oy qatori bilan birga o'zgaradi (R9 kabi).</summary>
    public PayMode PayMode { get; set; } = PayMode.Daily;

    /// <summary>H7, H13.7: null — Sozlamalardagi default siyosat.</summary>
    public OvertimePolicy? OvertimePolicy { get; set; }

    /// <summary>Multiplier uchun k, FixedPerHour uchun so'm/soat.</summary>
    public decimal? OvertimeValue { get; set; }

    /// <summary>R1: Oklad o'zgarsa, Kunlik qo'lda bo'lmasa qayta hisoblanadi.</summary>
    public void SetMonthlySalary(long value, int daysInMonth)
    {
        MonthlySalary = value;
        if (!DailyRateIsManual)
            DailyRate = PayrollMath.DailyRate(value, daysInMonth);
    }

    /// <summary>R1: Kunlik qo'lda kiritildi.</summary>
    public void SetDailyRateManual(long value)
    {
        DailyRate = value;
        DailyRateIsManual = true;
    }

    public void ResetDailyRateToAuto(int daysInMonth)
    {
        DailyRateIsManual = false;
        DailyRate = PayrollMath.DailyRate(MonthlySalary, daysInMonth);
    }
}

public enum PeriodStatus
{
    Open = 0,
    Closed = 1,
}

public sealed class PayrollPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Year { get; set; }
    public int Month { get; set; }
    public PeriodStatus Status { get; set; } = PeriodStatus.Open;
    public DateTimeOffset? ClosedAt { get; set; }
    public List<PayrollLine> Lines { get; set; } = [];

    public bool IsClosed => Status == PeriodStatus.Closed;
    public MonthKey Key => new(Year, Month);

    public PayrollLine? FindLine(Guid employeeId) => Lines.FirstOrDefault(l => l.EmployeeId == employeeId);

    /// <summary>R7: yopiq oy — faqat o'qish.</summary>
    public void EnsureOpen()
    {
        if (IsClosed)
            throw new DomainException(DomainError.PeriodClosed, $"{Key.DisplayName} yopilgan — o'zgartirib bo'lmaydi.", Key);
    }
}

public sealed class PayrollLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PeriodId { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public long MonthlySalary { get; set; }
    public long DailyRate { get; set; }
    public decimal DaysWorked { get; set; }
    public string? Note { get; set; }

    /// <summary>Qator yaratilganda xodimdan nusxa; ochiq oyda xodim tarmog'i o'zgarsa yangilanadi, yopiq oyda o'zgarmaydi.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>H13.5: hisob usuli oy bo'yicha (qator darajasida).</summary>
    public PayMode PayMode { get; set; } = PayMode.Daily;

    // H9/H10: Schedule natijasi — ochiq oyda jonli qayta hisoblanadi, yopilganda muzlaydi (qayta hisoblanmaydi).
    public int PlannedMinutes { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public long BasePay { get; set; }
    public long OvertimePay { get; set; }

    /// <summary>Hisoblangan paytdagi siyosat (H10: muzlatiladi).</summary>
    public int GraceMinutes { get; set; }
    public OvertimePolicy OvertimePolicy { get; set; }
    public decimal OvertimeValue { get; set; }

    public List<Advance> Advances { get; set; } = [];

    public bool IsSchedule => PayMode == PayMode.Schedule;

    /// <summary>R2 (Daily) yoki H9 (Schedule: BasePay + OvertimePay).</summary>
    public long Total => IsSchedule ? BasePay + OvertimePay : PayrollMath.Total(DaysWorked, DailyRate);

    /// <summary>H6: Schedule qatori uchun ko'rsatiladigan soatlik stavka.</summary>
    public decimal HourlyRate => PlannedMinutes > 0 ? (decimal)MonthlySalary / PlannedMinutes * 60 : 0;

    /// <summary>R3</summary>
    public long AdvanceTotal => Advances.Sum(a => a.Amount);

    /// <summary>R4: manfiy bo'lishi mumkin (qarz).</summary>
    public long Remainder => Total - AdvanceTotal;

    public bool HasCarryOverDebt => Advances.Any(a => a.Kind == AdvanceKind.CarryOverDebt);
}

public enum AdvanceKind
{
    Cash = 0,
    CarryOverDebt = 1,
}

public sealed class Advance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PayrollLineId { get; set; }
    public DateOnly Date { get; set; }
    public long Amount { get; set; }
    public AdvanceKind Kind { get; set; }
    public string? Note { get; set; }
    public Guid? SourcePeriodId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Entity { get; set; } = "";
    public Guid EntityId { get; set; }
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Comment { get; set; }
}

/// <summary>Tarmoq (filial, 3.9). Korxona bitta, tarmoq bir nechta; hisob qoidalari tarmoqqa bog'liq emas.</summary>
public sealed class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Migratsiyada yaratiladigan 2 ta tarmoq (nomi UI'da tarjima qilinadi, o'zgartirilmaguncha).</summary>
    public static readonly Guid DefaultFirstId = new("8f3c1a52-6b1e-4c43-9a5e-2f0d7c1b0a01");
    public static readonly Guid DefaultSecondId = new("8f3c1a52-6b1e-4c43-9a5e-2f0d7c1b0a02");

    /// <summary>Standart nom bazada lotincha saqlanadi (`3-tarmoq`) — UI'da har tilda tarjima qilinadi.</summary>
    public static string DefaultName(int number) => $"{number}-tarmoq";
}

/// <summary>Desktop ko'rinishi: Yorug' (default), Qorong'i yoki Windows sozlamasi bo'yicha.</summary>
public enum UiTheme
{
    Light,
    Dark,
    System,
}

public sealed class AppSettings
{
    public int Id { get; set; } = 1;
    public string CompanyName { get; set; } = "";
    public int DaysInMonth { get; set; } = 30;
    public int HoursPerDay { get; set; } = 8;
    public bool ShortInputEnabled { get; set; } = true;
    public string? BackupFolder { get; set; }
    public Guid InstallationId { get; set; } = Guid.NewGuid();
    /// <summary>Global manzil (domen), masalan https://oylik.example.uz. 6.1: kamida bittasi (lokal yoki global).</summary>
    public string? ServerUrl { get; set; }

    /// <summary>Lokal manzil (Wi-Fi/LAN), masalan http://192.168.1.19:5000 — avval shunga urinadi. Ikkalasi bitta server (bitta ApiKey).</summary>
    public string? LocalServerUrl { get; set; }
    public string? ApiKey { get; set; }

    /// <summary>2-bosqich: serverdan tortilgan oxirgi voqea Seq'i (kursor).</summary>
    public long LastEventSeq { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }

    /// <summary>
    /// Bosqich 3: xodimlar pull kursori — serverdan olingan oxirgi xodim UpdatedAt (server vaqti). null — hammasi.
    /// </summary>
    public DateTimeOffset? EmployeesSyncedAt { get; set; }

    /// <summary>
    /// Bosqich 3: shu mashinaning tabel diapazoni boshi (join paytida server beradi: 1000001, 2000001, ...).
    /// 0 — asosiy mashina / ulanmagan: tabel 1 dan. Yangi xodim tabeli shu diapazondan beriladi (zavodlar to'qnashmasin).
    /// </summary>
    public int TabelBase { get; set; }

    // ---------- Yuz terminali (Hikvision DS-K1Txxx, 2.3) ----------

    /// <summary>Terminal manzili, masalan http://192.168.1.2. null — terminal ishlatilmaydi (telefon ilovasi davomat manbai).</summary>
    public string? TerminalUrl { get; set; }

    /// <summary>Terminal admin paroli (DPAPI bilan shifrlangan, ApiKey kabi). Login doim "admin".</summary>
    public string? TerminalPassword { get; set; }

    /// <summary>Terminaldan davomat o'qish yoqilganmi.</summary>
    public bool TerminalEnabled { get; set; }

    /// <summary>Terminal seriya raqami — IP (DHCP) o'zgarsa, tarmoqdan shu seriya bo'yicha qurilmani topib, manzilni avtomat yangilash uchun.</summary>
    public string? TerminalSerial { get; set; }

    /// <summary>Terminaldan o'qilgan oxirgi voqea SerialNo'si (kursor — idempotent o'qish).</summary>
    public long TerminalLastSerial { get; set; }

    /// <summary>
    /// true — xodim terminal ekranida «Kelish»/«Ketish»ni o'zi tanlaydi (manual rejim); false (default) — server o'zi
    /// hal qiladi: kunning 1-tanishi Kelish, keyingisi Ketish (toggle).
    /// </summary>
    public bool TerminalRequireConfirm { get; set; }

    /// <summary>H5: kechikishga ruxsat, daqiqa (0–180).</summary>
    public int LateGraceMinutes { get; set; } = 5;

    /// <summary>H12: Start + shuncha daqiqa o'tsa — «Kelmadi».</summary>
    public int AbsentAfterMinutes { get; set; } = 60;

    /// <summary>H12: kun yakuni ranglari, %.</summary>
    public int GreenPercent { get; set; } = 95;
    public int YellowPercent { get; set; } = 75;

    /// <summary>H7: default ortiqcha ish siyosati.</summary>
    public OvertimePolicy OvertimePolicy { get; set; } = OvertimePolicy.None;
    public decimal OvertimeValue { get; set; } = 1m;

    /// <summary>Tarmoq filtri (ekranlar o'rtasida umumiy): null — Barchasi, "none" — Belgilanmagan, aks holda tarmoq Id.</summary>
    public string? BranchFilter { get; set; }

    /// <summary>UI tili: <see cref="LanguageUzLatn"/>, "uz-Cyrl" yoki "ru".</summary>
    public string Language { get; set; } = LanguageUzLatn;
    public UiTheme Theme { get; set; } = UiTheme.Light;

    public const string LanguageUzLatn = "uz-Latn";
}
