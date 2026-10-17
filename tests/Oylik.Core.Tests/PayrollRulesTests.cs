using Oylik.Core.Domain;

namespace Oylik.Core.Tests;

public class PayrollRulesTests
{
    private readonly AppSettings _settings = new();
    private readonly TestClock _clock = TestClock.Default();
    private PayrollEngine NewEngine() => new(_settings, _clock);

    private static PayrollPeriod Period(int year, int month, PeriodStatus status = PeriodStatus.Open) =>
        new() { Year = year, Month = month, Status = status };

    private (PayrollEngine engine, Employee employee, PayrollPeriod period, PayrollLine line) Setup(long salary = 6_000_000)
    {
        var engine = NewEngine();
        var employee = engine.CreateEmployee("Олим ака (тест)", salary);
        var period = engine.CreatePeriod(new MonthKey(2026, 10), [employee], []);
        return (engine, employee, period, period.Lines.Single());
    }

    // ----- R1 -----

    [Fact]
    public void R1_DailyRate_is_salary_divided_by_30_rounded()
    {
        PayrollMath.DailyRate(6_000_000, 30).Should().Be(200_000);
        PayrollMath.DailyRate(7_000_000, 30).Should().Be(233_333);
        PayrollMath.DailyRate(5_000_000, 30).Should().Be(166_667);
        PayrollMath.DailyRate(45, 30).Should().Be(2); // 1.5 → 2 (AwayFromZero)
    }

    [Fact]
    public void R1_salary_change_recalculates_auto_daily_rate()
    {
        var employee = new Employee();
        employee.SetMonthlySalary(6_000_000, 30);
        employee.DailyRate.Should().Be(200_000);
        employee.SetMonthlySalary(7_500_000, 30);
        employee.DailyRate.Should().Be(250_000);
    }

    [Fact]
    public void R1_manual_daily_rate_is_kept_when_salary_changes()
    {
        var employee = new Employee();
        employee.SetMonthlySalary(6_000_000, 30);
        employee.SetDailyRateManual(180_000);
        employee.DailyRateIsManual.Should().BeTrue();

        employee.SetMonthlySalary(9_000_000, 30);
        employee.DailyRate.Should().Be(180_000);
    }

    // ----- R2–R4 -----

    [Fact]
    public void R2_R3_R4_total_advances_and_remainder()
    {
        var (engine, _, period, line) = Setup();
        engine.SetDaysWorked(period, line, 26);
        engine.AddAdvance(period, line, new DateOnly(2026, 10, 5), 500_000, null);
        engine.AddAdvance(period, line, new DateOnly(2026, 10, 15), 1_000_000, null);

        line.Total.Should().Be(5_200_000);
        line.AdvanceTotal.Should().Be(1_500_000);
        line.Remainder.Should().Be(3_700_000);
    }

    [Fact]
    public void R2_half_days_are_rounded_to_sum()
    {
        PayrollMath.Total(20.5m, 166_667).Should().Be(3_416_674); // 3 416 673,5 → 3 416 674
    }

    [Fact]
    public void R4_remainder_can_be_negative()
    {
        var (engine, _, period, line) = Setup();
        engine.SetDaysWorked(period, line, 5);
        engine.AddAdvance(period, line, new DateOnly(2026, 10, 1), 2_200_000, null);
        line.Remainder.Should().Be(-1_200_000);
    }

    [Fact]
    public void R3_carry_over_debt_is_part_of_advance_total()
    {
        var line = new PayrollLine { DailyRate = 200_000, DaysWorked = 26 };
        line.Advances.Add(new Advance { Amount = 1_200_000, Kind = AdvanceKind.CarryOverDebt });
        line.Advances.Add(new Advance { Amount = 500_000, Kind = AdvanceKind.Cash });
        line.AdvanceTotal.Should().Be(1_700_000);
    }

    // ----- R5 -----

    [Fact]
    public void R5_new_period_copies_active_employees_only()
    {
        var engine = NewEngine();
        var a = engine.CreateEmployee("Бахтиёр", 6_000_000);
        var b = engine.CreateEmployee("Сардор", 7_000_000);
        b.IsActive = false;

        var period = engine.CreatePeriod(new MonthKey(2026, 10), [a, b], []);

        period.Lines.Should().ContainSingle();
        var line = period.Lines[0];
        line.EmployeeId.Should().Be(a.Id);
        line.MonthlySalary.Should().Be(6_000_000);
        line.DailyRate.Should().Be(200_000);
        line.DaysWorked.Should().Be(0);
        line.Advances.Should().BeEmpty();
    }

    [Fact]
    public void R5_next_key_follows_latest_period_or_current_month()
    {
        var engine = NewEngine();
        engine.NextPeriodKey([]).Should().Be(new MonthKey(2026, 10));
        engine.NextPeriodKey([Period(2026, 9), Period(2026, 12)]).Should().Be(new MonthKey(2027, 1));
    }

    [Fact]
    public void R5_duplicate_period_is_rejected()
    {
        var engine = NewEngine();
        var act = () => engine.CreatePeriod(new MonthKey(2026, 10), [], [Period(2026, 10)]);
        act.Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.PeriodExists);
    }

    // ----- R6 -----

    private (PayrollEngine engine, Employee employee, PayrollPeriod sep, PayrollLine line) SeptemberWithDebt()
    {
        var engine = NewEngine();
        var employee = engine.CreateEmployee("Олим ака (тест)", 6_000_000);
        var sep = engine.CreatePeriod(new MonthKey(2026, 9), [employee], []);
        var line = sep.Lines.Single();
        engine.SetDaysWorked(sep, line, 5); // 1 000 000
        engine.AddAdvance(sep, line, new DateOnly(2026, 9, 10), 2_200_000, null);
        line.Remainder.Should().Be(-1_200_000);
        return (engine, employee, sep, line);
    }

    [Fact]
    public void R6_close_creates_next_period_and_carry_over_debt()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();

        var result = engine.Close(sep, null, [employee]);

        sep.Status.Should().Be(PeriodStatus.Closed);
        sep.ClosedAt.Should().Be(_clock.GetUtcNow());
        result.NextPeriodCreated.Should().BeTrue();
        var oct = result.NextPeriod!;
        oct.Key.Should().Be(new MonthKey(2026, 10));
        var debt = oct.FindLine(employee.Id)!.Advances.Single();
        debt.Kind.Should().Be(AdvanceKind.CarryOverDebt);
        debt.Amount.Should().Be(1_200_000);
        debt.Date.Should().Be(new DateOnly(2026, 10, 1));
        debt.Note.Should().Be("Sentyabr qarzi");
        debt.SourcePeriodId.Should().Be(sep.Id);
    }

    [Fact]
    public void R6_close_adds_line_for_inactive_debtor_in_existing_next_period()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();
        employee.IsActive = false;
        var oct = engine.CreatePeriod(new MonthKey(2026, 10), [employee], [sep]);
        oct.Lines.Should().BeEmpty();

        engine.Close(sep, oct, [employee]);

        oct.FindLine(employee.Id)!.AdvanceTotal.Should().Be(1_200_000);
    }

    [Fact]
    public void R6_close_without_debt_does_nothing_for_paid_lines()
    {
        var engine = NewEngine();
        var employee = engine.CreateEmployee("Бахтиёр", 6_000_000);
        var sep = engine.CreatePeriod(new MonthKey(2026, 9), [employee], []);
        engine.SetDaysWorked(sep, sep.Lines[0], 26);

        var result = engine.Close(sep, null, [employee]);

        result.NextPeriod.Should().BeNull();
        result.CarriedDebts.Should().BeEmpty();
    }

    [Fact]
    public void R6_R8_close_reopen_close_leaves_exactly_one_debt()
    {
        var (engine, employee, sep, line) = SeptemberWithDebt();
        var oct = engine.Close(sep, null, [employee]).NextPeriod!;
        var octLine = oct.FindLine(employee.Id)!;

        var removed = engine.Reopen(sep, oct);
        removed.Should().ContainSingle();
        octLine.Advances.Should().BeEmpty();
        sep.Status.Should().Be(PeriodStatus.Open);
        sep.ClosedAt.Should().BeNull();

        engine.AddAdvance(sep, line, new DateOnly(2026, 9, 20), 300_000, null);
        engine.Close(sep, oct, [employee]);

        octLine.Advances.Should().ContainSingle().Which.Amount.Should().Be(1_500_000);
    }

    [Fact]
    public void R6_close_is_idempotent_for_same_source()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();
        var oct = engine.Close(sep, null, [employee]).NextPeriod!;
        // Holatni sun'iy ravishda qayta ochamiz, lekin qarz yozuvini qoldiramiz.
        sep.Status = PeriodStatus.Open;

        engine.Close(sep, oct, [employee]);

        oct.FindLine(employee.Id)!.Advances.Should().ContainSingle().Which.Amount.Should().Be(1_200_000);
    }

    [Fact]
    public void R6_cannot_close_twice_or_into_closed_next()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();
        var oct = Period(2026, 10, PeriodStatus.Closed);

        engine.Invoking(e => e.Close(sep, oct, [employee]))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.NextPeriodClosed);

        engine.Close(sep, null, [employee]);
        engine.Invoking(e => e.Close(sep, null, [employee]))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.PeriodClosed);
    }

    // ----- R7 -----

    [Fact]
    public void R7_closed_period_is_read_only()
    {
        var (engine, employee, period, line) = Setup();
        engine.Close(period, null, [employee]);

        engine.Invoking(e => e.SetDaysWorked(period, line, 10)).Should().Throw<DomainException>();
        engine.Invoking(e => e.SetMonthlySalary(period, line, employee, 1)).Should().Throw<DomainException>();
        engine.Invoking(e => e.SetDailyRate(period, line, employee, 1)).Should().Throw<DomainException>();
        engine.Invoking(e => e.AddAdvance(period, line, new DateOnly(2026, 10, 1), 1, null)).Should().Throw<DomainException>();
        engine.Invoking(e => e.RemoveLine(period, line)).Should().Throw<DomainException>();
    }

    // ----- R8 -----

    [Fact]
    public void R8_reopen_is_blocked_when_next_period_is_closed()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();
        var oct = engine.Close(sep, null, [employee]).NextPeriod!;
        engine.Close(oct, null, [employee]);

        engine.Invoking(e => e.Reopen(sep, oct))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.NextPeriodClosed);
    }

    [Fact]
    public void R8_reopen_writes_audit()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();
        var oct = engine.Close(sep, null, [employee]).NextPeriod!;
        engine.Audit.Clear();

        engine.Reopen(sep, oct);

        engine.Audit.Should().Contain(a => a.EntityId == sep.Id && a.NewValue == "Open");
    }

    // ----- R9 -----

    [Fact]
    public void R9_salary_change_in_open_period_updates_line_and_card()
    {
        var (engine, employee, period, line) = Setup();

        engine.SetMonthlySalary(period, line, employee, 7_500_000);

        line.MonthlySalary.Should().Be(7_500_000);
        line.DailyRate.Should().Be(250_000);
        employee.MonthlySalary.Should().Be(7_500_000);
        employee.DailyRate.Should().Be(250_000);
    }

    [Fact]
    public void R9_daily_rate_change_marks_manual_and_updates_card()
    {
        var (engine, employee, period, line) = Setup();

        engine.SetDailyRate(period, line, employee, 180_000);
        engine.SetMonthlySalary(period, line, employee, 9_000_000);

        line.DailyRate.Should().Be(180_000);
        employee.DailyRate.Should().Be(180_000);
        employee.DailyRateIsManual.Should().BeTrue();
    }

    [Fact]
    public void R9_closed_periods_are_not_touched_by_card_changes()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();
        var oct = engine.Close(sep, null, [employee]).NextPeriod!;
        var sepLine = sep.FindLine(employee.Id)!;

        engine.SetMonthlySalary(oct, oct.FindLine(employee.Id)!, employee, 9_000_000);

        sepLine.MonthlySalary.Should().Be(6_000_000);
        sepLine.DailyRate.Should().Be(200_000);
    }

    // ----- R10 -----

    [Fact]
    public void R10_line_can_be_removed_only_when_empty()
    {
        var (engine, _, period, line) = Setup();
        engine.SetDaysWorked(period, line, 1);
        engine.Invoking(e => e.RemoveLine(period, line))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.LineNotEmpty);

        engine.SetDaysWorked(period, line, 0);
        var advance = engine.AddAdvance(period, line, new DateOnly(2026, 10, 1), 100_000, null);
        engine.Invoking(e => e.RemoveLine(period, line)).Should().Throw<DomainException>();

        engine.RemoveAdvance(period, line, advance);
        engine.RemoveLine(period, line);
        period.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Carry_over_debt_cannot_be_removed_by_hand()
    {
        var (engine, employee, sep, _) = SeptemberWithDebt();
        var oct = engine.Close(sep, null, [employee]).NextPeriod!;
        var line = oct.FindLine(employee.Id)!;

        engine.Invoking(e => e.RemoveAdvance(oct, line, line.Advances[0]))
            .Should().Throw<DomainException>().Which.Error.Should().Be(DomainError.CarryOverReadOnly);
    }

    // ----- R11 -----

    [Theory]
    [InlineData("6", AmountField.MonthlySalary, 6_000_000)]
    [InlineData("6,5", AmountField.MonthlySalary, 6_500_000)]
    [InlineData("6.5", AmountField.MonthlySalary, 6_500_000)]
    [InlineData("6 000 000", AmountField.MonthlySalary, 6_000_000)]
    [InlineData("999", AmountField.MonthlySalary, 999_000_000)]
    [InlineData("1000", AmountField.MonthlySalary, 1_000)]
    [InlineData("200", AmountField.DailyRate, 200_000)]
    [InlineData("1500", AmountField.Advance, 1_500_000)]
    [InlineData("9999", AmountField.Advance, 9_999_000)]
    [InlineData("10000", AmountField.Advance, 10_000)]
    [InlineData("200 000", AmountField.DailyRate, 200_000)]
    [InlineData("0", AmountField.DailyRate, 0)]
    public void R11_short_input_expands(string text, AmountField field, long expected)
    {
        ShortInput.TryParse(text, field, enabled: true, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void R11_short_input_can_be_disabled()
    {
        ShortInput.TryParse("6", AmountField.MonthlySalary, enabled: false, out var salary).Should().BeTrue();
        salary.Should().Be(6);
        ShortInput.TryParse("200", AmountField.Advance, enabled: false, out var advance).Should().BeTrue();
        advance.Should().Be(200);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void R11_invalid_input_is_rejected(string text)
    {
        ShortInput.TryParse(text, AmountField.Advance, true, out _).Should().BeFalse();
    }

    // ----- Formatlash va kunlar -----

    [Fact]
    public void Money_and_days_are_formatted_like_spec()
    {
        NumberText.Money(6_000_000).Should().Be("6 000 000");
        NumberText.Money(-1_200_000).Should().Be("−1 200 000");
        NumberText.Money(0).Should().Be("0");
        NumberText.Days(26m).Should().Be("26");
        NumberText.Days(26.5m).Should().Be("26,5");
    }

    [Theory]
    [InlineData("26", true)]
    [InlineData("26,5", true)]
    [InlineData("31", true)]
    [InlineData("31,5", false)]
    [InlineData("26,3", false)]
    [InlineData("-1", false)]
    public void Days_are_0_to_31_with_half_steps(string text, bool valid)
    {
        NumberText.TryParseDays(text, out _).Should().Be(valid);
    }

    [Fact]
    public void Names_sort_with_russian_culture_ignoring_case()
    {
        string[] names = ["Шахноза", "алишер", "Анвар ака", "Бахтиёр", "Ёқубжон", "Akmal"];
        var sorted = names.Order(NameComparer.Instance).ToArray();
        // ru-RU: kirill avval, lotin keyin.
        sorted.Should().Equal("алишер", "Анвар ака", "Бахтиёр", "Ёқубжон", "Шахноза", "Akmal");
    }

    [Fact]
    public void Summary_sums_all_lines()
    {
        var (engine, _, period, line) = Setup();
        engine.SetDaysWorked(period, line, 26);
        engine.AddAdvance(period, line, new DateOnly(2026, 10, 1), 1_000_000, null);

        var s = PeriodSummary.Of(period.Lines);

        s.Should().Be(new PeriodSummary(1, 26, 5_200_000, 1_000_000, 4_200_000));
    }
}
