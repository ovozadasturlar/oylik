using System.Collections.Specialized;
using Oylik.Contracts;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;
using static Oylik.Mobile.Tests.TestClock;

namespace Oylik.Mobile.Tests;

public class EmployeeListTests
{
    private static LocalEmployee E(string name) => new() { Id = Guid.NewGuid(), FullName = name, IsActive = true };

    private static Dictionary<Guid, Presence> NoPresence(IEnumerable<LocalEmployee> es) => es.ToDictionary(e => e.Id, _ => Presence.None);

    [Fact]
    public void Refresh_keeps_row_objects_and_never_resets_collection()
    {
        var emps = new[] { E("Сардор"), E("Анвар"), E("Санжар") };
        var list = new EmployeeList();
        list.Update(emps, NoPresence(emps), Tashkent);
        var before = list.Items.ToList();

        var changes = new List<NotifyCollectionChangedAction>();
        list.Items.CollectionChanged += (_, e) => changes.Add(e.Action);

        // Sinxron: holatlar o'zgardi, ro'yxat o'sha.
        var presence = emps.ToDictionary(e => e.Id, _ => new Presence(PresenceKind.Inside, At(2026, 10, 9, 8, 0)));
        list.Update(emps, presence, Tashkent);

        list.Items.Should().Equal(before, (a, b) => ReferenceEquals(a, b));
        changes.Should().BeEmpty("mavjud qatorlar joyida yangilanadi — kolleksiya o'zgarmaydi");
        list.Items.Should().OnlyContain(r => r.IsInside);
    }

    [Fact]
    public void New_and_removed_employees_change_only_their_rows()
    {
        var a = E("Анвар"); var b = E("Гулнора"); var c = E("Сардор");
        var list = new EmployeeList();
        list.Update([a, b, c], NoPresence([a, b, c]), Tashkent);
        var rowA = list.Items[0]; var rowC = list.Items[2];

        var changes = new List<NotifyCollectionChangedAction>();
        list.Items.CollectionChanged += (_, e) => changes.Add(e.Action);

        var n = E("Бобур"); // a va b orasiga tushadi
        list.Update([a, c, n], NoPresence([a, c, n]), Tashkent);

        list.Items.Select(r => r.Name).Should().Equal("Анвар", "Бобур", "Сардор");
        list.Items[0].Should().BeSameAs(rowA);
        list.Items[2].Should().BeSameAs(rowC);
        changes.Should().NotContain(NotifyCollectionChangedAction.Reset);
        changes.Should().BeEquivalentTo([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add]);
    }

    [Fact]
    public void Filter_hides_rows_without_replacing_them()
    {
        var emps = new[] { E("Сардор"), E("Анвар"), E("Санжар") };
        var list = new EmployeeList();
        list.Update(emps, NoPresence(emps), Tashkent);
        var sardor = list.Items.Single(r => r.Name == "Сардор");

        list.SetFilter("са");
        list.Items.Select(r => r.Name).Should().Equal("Санжар", "Сардор");
        list.SetFilter("");
        list.Items.Should().HaveCount(3);
        list.Items.Single(r => r.Name == "Сардор").Should().BeSameAs(sardor);
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2, 3 }, new[] { 3, 2, 1, 0 })]
    [InlineData(new[] { 0, 1, 2 }, new[] { 2, 4, 0 })]
    [InlineData(new int[0], new[] { 1, 2 })]
    [InlineData(new[] { 5, 6 }, new int[0])]
    public void Sync_reaches_desired_order(int[] start, int[] desired)
    {
        var pool = Enumerable.Range(0, 10).Select(i => new object()).ToArray();
        var target = new System.Collections.ObjectModel.ObservableCollection<object>(start.Select(i => pool[i]));
        EmployeeList.Sync(target, desired.Select(i => pool[i]).ToList());
        target.Should().Equal(desired.Select(i => pool[i]), ReferenceEquals);
    }
}

public sealed class TapDuringRefreshTests : IAsyncLifetime
{
    private readonly TempDb _db = new();
    private readonly TestClock _clock = new(At(2026, 10, 9, 8, 0));
    private AttendanceService _att = null!;
    private TapHandler _taps = null!;
    private readonly EmployeeList _list = new();
    private LocalEmployee[] _emps = null!;

    public async Task InitializeAsync()
    {
        _att = new AttendanceService(_db.Store, _clock);
        _taps = new TapHandler(_att, _clock);
        var at = DateTimeOffset.UtcNow;
        await _db.Store.UpsertEmployeesAsync(
            new[] { "Анвар", "Бобур", "Гулнора", "Олим ака", "Сардор", "Тимур" }
                .Select(n => new EmployeeDto(Guid.NewGuid(), n, null, true, at)));
        _emps = (await _db.Store.GetEmployeesAsync()).ToArray();
        await RefreshAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task RefreshAsync()
    {
        var emps = await _db.Store.GetEmployeesAsync();
        _list.Update(emps, await _att.GetPresenceAsync(emps.Select(e => e.Id)), _att.TimeZone);
    }

    [Fact]
    public async Task Tap_records_for_tapped_row_even_if_list_refreshes_and_reorders_meanwhile()
    {
        var tapped = _list.Items[2]; // ekranda 3-qator
        var name = tapped.Name;

        // Bosish va (sinxdan keyingi) ro'yxat yangilanishi bir vaqtda: yangi xodim boshiga qo'shiladi → indekslar siljiydi.
        var tap = _taps.TapAsync(tapped);
        await _db.Store.UpsertEmployeesAsync([new EmployeeDto(Guid.NewGuid(), "Аа Янги", null, true, DateTimeOffset.UtcNow)]);
        await RefreshAsync();
        var r = await tap;

        r.Recorded!.EmployeeId.Should().Be(tapped.Id);
        r.Message.Should().StartWith(name + " — keldi");
        _list.Items[3].Should().BeSameAs(tapped, "qator obyekti almashmadi, faqat bitta pastga siljidi");
        (await _db.Store.FindEventAsync(r.Recorded.Id))!.EmployeeId.Should().Be(tapped.Id);
    }

    [Fact]
    public async Task Rapid_taps_on_different_rows_are_all_recorded_in_order_none_dropped()
    {
        var rows = _list.Items.Take(5).ToList();
        var tasks = rows.Select(r => _taps.TapAsync(r)).ToList(); // bir vaqtda — navbatga turadi
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.CanUndo);
        results.Select(r => r.EmployeeId).Should().Equal(rows.Select(r => r.Id));
        results.Select(r => r.Message).Should().Equal(rows.Select(r => $"{r.Name} — keldi 08:00"));
        var events = await _db.Store.GetEventsAsync(_clock.GetUtcNow().AddHours(-1), _clock.GetUtcNow().AddHours(1));
        events.Select(e => e.EmployeeId).Should().BeEquivalentTo(rows.Select(r => r.Id));
    }

    [Fact]
    public async Task Double_touch_on_same_row_is_not_silent_and_does_not_record_opposite()
    {
        var row = _list.Items[0];
        var first = await _taps.TapAsync(row);
        _clock.Advance(TimeSpan.FromMilliseconds(400));
        var second = await _taps.TapAsync(row);

        first.CanUndo.Should().BeTrue();
        second.CanUndo.Should().BeFalse();
        second.Message.Should().Be($"{Strings.AlreadyMarked}: 08:00");
        (await _db.Store.GetEventsAsync(_clock.GetUtcNow().AddHours(-1), _clock.GetUtcNow().AddHours(1))).Should().ContainSingle();

        // Debounce o'tgach — KETDI yoziladi.
        _clock.Advance(TimeSpan.FromSeconds(2));
        var third = await _taps.TapAsync(row);
        third.Recorded!.Type.Should().Be(AttendanceType.Out);
    }
}
