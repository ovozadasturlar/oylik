using Oylik.Contracts;
using Oylik.Mobile.Logic.Data;
using Oylik.Mobile.Logic.Rules;

namespace Oylik.Mobile.Logic;

/// <summary>Bosish natijasi: yozildi yoki A10 bo'yicha rad etildi (<see cref="Duplicate"/> — avvalgi voqea).</summary>
public sealed record TapResult(LocalEvent? Recorded, LocalEvent? Duplicate)
{
    public bool IsRecorded => Recorded is not null;
}

/// <summary>Keldi/Ketdi — avval lokal bazaga (Pending), tasdiq oynasisiz.</summary>
public sealed class AttendanceService(LocalStore store, TimeProvider clock)
{
    private static readonly TimeSpan Lookback = TimeSpan.FromHours(48);

    public TimeZoneInfo TimeZone => clock.LocalTimeZone;

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone);

    public async Task<TapResult> RecordAsync(Guid employeeId, AttendanceType type)
    {
        var s = await store.GetSettingsAsync();
        var now = Now;
        var recent = await store.GetEventsAsync(now - Lookback, now.AddDays(1), employeeId);

        if (PresenceRules.FindDuplicate(recent, type, now) is { } dup)
            return new TapResult(null, dup);

        var e = new LocalEvent
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            InstallationId = s.InstallationId,
            Type = type,
            OccurredAt = now,
            DeviceId = s.DeviceId == Guid.Empty ? null : s.DeviceId,
            SyncState = SyncState.Pending,
        };
        await store.AddEventAsync(e);
        return new TapResult(e, null);
    }

    /// <summary>[BEKOR] — voqea o'chirilmaydi, bekor qilinadi (serverga ham yetkaziladi).</summary>
    public async Task<bool> UndoAsync(Guid eventId)
    {
        var e = await store.FindEventAsync(eventId);
        if (e is null || e.IsVoided) return false;
        e.IsVoided = true;
        e.VoidReason = Strings.UndoReason;
        e.VoidPending = true;
        await store.UpdateEventAsync(e);
        return true;
    }

    /// <summary>Har xodimning hozirgi holati (H14: har biri o'z juftlik chegarasi bilan).</summary>
    public async Task<Dictionary<Guid, Presence>> GetPresenceAsync(IEnumerable<Guid> employeeIds)
    {
        var now = Now;
        var limits = (await store.GetEmployeesAsync(activeOnly: false)).ToDictionary(e => e.Id, e => e.MaxShift);
        var lookback = limits.Values.Append(Lookback).Max();
        var events = (await store.GetEventsAsync(now - lookback, now.AddDays(1))).ToLookup(e => e.EmployeeId);
        return employeeIds.ToDictionary(id => id,
            id => PresenceRules.GetPresence(events[id], now, clock.LocalTimeZone, limits.GetValueOrDefault(id, Api.DefaultMaxShift)));
    }

    public async Task<DayReport> GetDayAsync(DateOnly date)
    {
        var tz = clock.LocalTimeZone;
        var start = PresenceRules.StartOfLocalDay(date, tz);
        // Tungi/sutkalik smena: In shu kuni, Out ertasi kuni — eng uzun chegara qadar qo'shimcha oraliq.
        var employees = await store.GetEmployeesAsync();
        var margin = employees.Select(e => e.MaxShift).DefaultIfEmpty(Api.DefaultMaxShift).Max();
        if (margin < Api.DefaultMaxShift) margin = Api.DefaultMaxShift;
        var events = await store.GetEventsAsync(start - margin, start.AddDays(1) + margin);
        return DayReportBuilder.Build(employees, events, date, Now, tz);
    }
}
