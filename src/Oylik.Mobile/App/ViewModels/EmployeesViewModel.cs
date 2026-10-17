using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oylik.Mobile.Logic;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;
using Oylik.Mobile.Logic.Sync;
using Oylik.Mobile.Services;

namespace Oylik.Mobile.ViewModels;

public sealed partial class EmployeesViewModel : ObservableObject
{
    private static readonly TimeSpan SnackLifetime = TimeSpan.FromSeconds(60);

    private readonly LocalStore _store;
    private readonly AttendanceService _attendance;
    private readonly TapHandler _taps;
    private readonly SyncScheduler _scheduler;
    private readonly EmployeeList _list = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _snackCts;
    private Guid? _snackEventId;

    public EmployeesViewModel(LocalStore store, AttendanceService attendance, TapHandler taps, SyncEngine engine, SyncScheduler scheduler)
    {
        _store = store;
        _attendance = attendance;
        _taps = taps;
        _scheduler = scheduler;
        engine.Completed += _ => MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
    }

    /// <summary>Qator obyektlari doimiy; kolleksiya hech qachon tozalanmaydi (<see cref="EmployeeList"/>).</summary>
    public ObservableCollection<EmployeeRow> Items => _list.Items;

    /// <summary>Ro'yxat birinchi marta yuklanguncha — tugmalar ko'rinmaydi (bosish jim yo'qolmasin).</summary>
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string EmptyText { get; set; } = "";

    [ObservableProperty] public partial bool HasSnack { get; set; }
    [ObservableProperty] public partial string SnackText { get; set; } = "";
    [ObservableProperty] public partial bool SnackCanUndo { get; set; }

    [ObservableProperty] public partial string PillText { get; set; } = "";
    [ObservableProperty] public partial Color PillColor { get; set; } = Colors.Gray;
    [ObservableProperty] public partial Color PillBg { get; set; } = Colors.LightGray;
    [ObservableProperty] public partial bool ClockWrong { get; set; }

    partial void OnSearchTextChanged(string value)
    {
        _list.SetFilter(value);
        UpdateEmptyText();
    }

    /// <summary>Navbat bilan (bir vaqtda ikkitasi kolleksiyani o'zgartirmasin). Faqat UI oqimida chaqiriladi.</summary>
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var employees = await _store.GetEmployeesAsync();
            var presence = await _attendance.GetPresenceAsync(employees.Select(e => e.Id));
            _list.Update(employees, presence, _attendance.TimeZone);
            UpdateEmptyText();
            IsLoading = false;
            await RefreshPillAsync();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void UpdateEmptyText() => EmptyText = _list.TotalCount == 0 ? Strings.NoEmployees : Strings.NothingFound;

    private async Task RefreshPillAsync()
    {
        var s = await _store.GetSettingsAsync();
        var unsent = await _store.CountUnsentAsync(s.InstallationId);
        ClockWrong = s.ClockWrong;

        if (unsent > 0)
        {
            PillText = $"{Strings.PillPending}: {unsent}";
            PillColor = Color.FromArgb("#8A3A00");
            PillBg = Color.FromArgb("#FFF1E0");
        }
        else if (s.LastError is not null || s.LastSyncAt is null)
        {
            PillText = Strings.PillOffline;
            PillColor = Color.FromArgb("#3D434C");
            PillBg = Color.FromArgb("#EEF0F3");
        }
        else
        {
            PillText = $"{Strings.PillSynced} · {Format.Time(s.LastSyncAt.Value, _attendance.TimeZone)}";
            PillColor = Color.FromArgb("#145C2B");
            PillBg = Color.FromArgb("#E3F4E8");
        }
    }

    /// <summary>
    /// Bosish → darhol yoziladi + vibratsiya + snackbar (aynan yozilgan xodim ismi). Tasdiq oynasi YO'Q.
    /// AllowConcurrentExecutions: buyruq bajarilayotganda tugmalar o'chmaydi — keyingi bosish navbatga turadi (TapHandler).
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task TapAsync(EmployeeRow? row)
    {
        if (row is null) return;
        Haptics.Tap();
        var r = await _taps.TapAsync(row);
        ShowSnack(r.Message, r.Recorded?.Id);
        if (r.CanUndo) _scheduler.Kick();
        await RefreshPillAsync();
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_snackEventId is not { } id) return;
        _snackEventId = null;
        await _attendance.UndoAsync(id);
        ShowSnack(Strings.Undone, null, TimeSpan.FromSeconds(3));
        await RefreshAsync();
        _scheduler.Kick();
    }

    private void ShowSnack(string text, Guid? eventId, TimeSpan? lifetime = null)
    {
        _snackCts?.Cancel();
        var cts = _snackCts = new CancellationTokenSource();
        _snackEventId = eventId;
        SnackText = text;
        SnackCanUndo = eventId is not null;
        HasSnack = true;
        _ = HideLaterAsync(lifetime ?? SnackLifetime, cts.Token);
    }

    private async Task HideLaterAsync(TimeSpan after, CancellationToken ct)
    {
        try { await Task.Delay(after, ct); }
        catch (TaskCanceledException) { return; }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            HasSnack = false;
            _snackEventId = null;
        });
    }
}
