using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;

namespace Oylik.Mobile.Logic;

/// <summary>Bosish natijasi: snackbar matni aynan yozilgan xodim ismi bilan (qorovul xatoni darhol ko'radi).</summary>
public sealed record TapOutcome(Guid EmployeeId, string EmployeeName, LocalEvent? Recorded, string Message)
{
    public bool CanUndo => Recorded is not null;
}

/// <summary>
/// KELDI/KETDI bosishlari. Har bosish navbatga turadi va albatta qayta ishlanadi (hech biri jim tashlanmaydi).
/// Xodim faqat bosilgan qator obyektidan (<see cref="EmployeeRow.Id"/>) olinadi — "tanlangan qator" yoki indeksdan emas.
/// </summary>
public sealed class TapHandler(AttendanceService attendance, TimeProvider clock)
{
    /// <summary>Bir qatorni shu oraliqda qayta bosish — barmoq ikki marta tegib ketgan deb hisoblanadi (qarama-qarshi voqea yozilmaydi).</summary>
    public static readonly TimeSpan RowDebounce = TimeSpan.FromMilliseconds(1200);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TapOutcome> TapAsync(EmployeeRow row)
    {
        // Bosish paytidagi qiymatlar — keyin ro'yxat yangilansa ham o'zgarmaydi.
        var employeeId = row.Id;
        var name = row.Name;

        await _gate.WaitAsync();
        try
        {
            var tz = attendance.TimeZone;
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            if (nowUtc - row.LastTapUtc < RowDebounce && row.Presence.At is { } lastAt)
                return new TapOutcome(employeeId, name, null, $"{Strings.AlreadyMarked}: {Format.Time(lastAt, tz)}");
            row.LastTapUtc = nowUtc;

            var type = row.Presence.NextAction;
            var r = await attendance.RecordAsync(employeeId, type);
            if (!r.IsRecorded)
                return new TapOutcome(employeeId, name, null, $"{Strings.AlreadyMarked}: {Format.Time(r.Duplicate!.OccurredAt, tz)}");

            var e = r.Recorded!;
            row.Apply(new Presence(type == AttendanceType.In ? PresenceKind.Inside : PresenceKind.Left, e.OccurredAt), tz);
            var verb = type == AttendanceType.In ? Strings.Came : Strings.Went;
            return new TapOutcome(employeeId, name, e, $"{name} — {verb} {Format.Time(e.OccurredAt, tz)}");
        }
        finally
        {
            _gate.Release();
        }
    }
}
