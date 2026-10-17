using Oylik.Contracts;
using SQLite;

namespace Oylik.Mobile.Logic.Data;

/// <summary>Telefondagi SQLite. Har voqea avval shu yerga yoziladi (outbox), keyin serverga.</summary>
public sealed class LocalStore
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _init = new(1, 1);
    private bool _ready;

    public LocalStore(string path)
    {
        _db = new SQLiteAsyncConnection(path,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    private async Task<SQLiteAsyncConnection> Db()
    {
        if (_ready) return _db;
        await _init.WaitAsync();
        try
        {
            if (!_ready)
            {
                await _db.EnableWriteAheadLoggingAsync();
                await _db.CreateTableAsync<LocalEmployee>();
                await _db.CreateTableAsync<LocalEvent>();
                await _db.CreateTableAsync<DeviceSettings>();
                _ready = true;
            }
        }
        finally { _init.Release(); }
        return _db;
    }

    public Task CloseAsync() => _db.CloseAsync();

    // ---------- Sozlamalar ----------

    public async Task<DeviceSettings> GetSettingsAsync()
    {
        var db = await Db();
        return await db.FindAsync<DeviceSettings>(1) ?? new DeviceSettings();
    }

    public async Task SaveSettingsAsync(DeviceSettings s)
    {
        s.Id = 1;
        await (await Db()).InsertOrReplaceAsync(s);
    }

    // ---------- Xodimlar ----------

    public async Task<List<LocalEmployee>> GetEmployeesAsync(bool activeOnly = true)
    {
        var db = await Db();
        var q = db.Table<LocalEmployee>();
        if (activeOnly) q = q.Where(e => e.IsActive);
        return await q.ToListAsync();
    }

    public async Task UpsertEmployeesAsync(IEnumerable<EmployeeDto> items)
    {
        var db = await Db();
        await db.RunInTransactionAsync(c =>
        {
            foreach (var e in items)
                c.InsertOrReplace(new LocalEmployee
                {
                    Id = e.Id,
                    FullName = e.FullName,
                    Note = e.Note,
                    IsActive = e.IsActive,
                    UpdatedAt = e.UpdatedAt,
                    MaxShiftMinutes = e.MaxShiftMinutes,
                });
        });
    }

    // ---------- Voqealar ----------

    public async Task AddEventAsync(LocalEvent e) => await (await Db()).InsertAsync(e);

    public async Task<LocalEvent?> FindEventAsync(Guid id) => await (await Db()).FindAsync<LocalEvent>(id);

    public async Task UpdateEventAsync(LocalEvent e) => await (await Db()).UpdateAsync(e);

    public async Task UpdateEventsAsync(IEnumerable<LocalEvent> items) => await (await Db()).UpdateAllAsync(items);

    /// <summary>[from, to) oralig'idagi voqealar (bekor qilinganlar ham).</summary>
    public async Task<List<LocalEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, Guid? employeeId = null)
    {
        var db = await Db();
        long f = from.UtcTicks, t = to.UtcTicks;
        var q = db.Table<LocalEvent>().Where(e => e.OccurredAtUtcTicks >= f && e.OccurredAtUtcTicks < t);
        if (employeeId is { } id) q = q.Where(e => e.EmployeeId == id);
        return await q.ToListAsync();
    }

    /// <summary>Jo'natilishi kerak bo'lganlar (Pending), shu o'rnatma uchun. Failed — server rad etgan, qayta yuborilmaydi.</summary>
    public async Task<List<LocalEvent>> GetOutboxAsync(Guid installationId)
    {
        var db = await Db();
        return await db.Table<LocalEvent>()
            .Where(e => e.InstallationId == installationId && e.SyncState == SyncState.Pending)
            .OrderBy(e => e.OccurredAtUtcTicks)
            .ToListAsync();
    }

    public async Task<List<LocalEvent>> GetPendingVoidsAsync(Guid installationId)
    {
        var db = await Db();
        return await db.Table<LocalEvent>()
            .Where(e => e.InstallationId == installationId && e.VoidPending && e.SyncState == SyncState.Sent)
            .ToListAsync();
    }

    public async Task<int> CountUnsentAsync(Guid installationId)
    {
        var db = await Db();
        return await db.Table<LocalEvent>()
            .Where(e => e.InstallationId == installationId && (e.SyncState == SyncState.Pending || e.VoidPending))
            .CountAsync();
    }

    /// <summary>Server rad etgan voqealar (oxirgi xato matni bilan).</summary>
    public async Task<List<LocalEvent>> GetFailedAsync(Guid installationId)
    {
        var db = await Db();
        return await db.Table<LocalEvent>()
            .Where(e => e.InstallationId == installationId && e.SyncState == SyncState.Failed)
            .OrderByDescending(e => e.OccurredAtUtcTicks)
            .ToListAsync();
    }

    /// <summary>Serverdan kelgan voqealar (boshqa qurilmalar, desktop tuzatishlari, o'zimizning tasdiqlanganlar).</summary>
    public async Task UpsertServerEventsAsync(Guid installationId, IEnumerable<ServerEventDto> items)
    {
        var db = await Db();
        await db.RunInTransactionAsync(c =>
        {
            foreach (var s in items)
            {
                var e = c.Find<LocalEvent>(s.Id);
                if (e is null)
                {
                    c.Insert(new LocalEvent
                    {
                        Id = s.Id,
                        EmployeeId = s.EmployeeId,
                        InstallationId = installationId,
                        Type = s.Type,
                        OccurredAt = s.OccurredAt,
                        DeviceId = s.DeviceId,
                        IsVoided = s.IsVoided,
                        VoidReason = s.VoidReason,
                        SyncState = SyncState.Sent,
                        ServerSeq = s.Seq,
                    });
                }
                else
                {
                    // Lokal bekor qilish hali jo'natilmagan bo'lsa — uni yo'qotmaymiz.
                    e.IsVoided = s.IsVoided || e.IsVoided;
                    e.VoidReason = s.VoidReason ?? e.VoidReason;
                    if (s.IsVoided) e.VoidPending = false;
                    e.SyncState = SyncState.Sent;
                    e.ServerSeq = s.Seq;
                    e.LastError = null;
                    c.Update(e);
                }
            }
        });
    }

    /// <summary>Uzishda: xodimlar ro'yxati va tasdiqlangan voqealar o'chadi; jo'natilmaganlar QOLADI.</summary>
    public async Task ClearForUnpairAsync()
    {
        var db = await Db();
        await db.DeleteAllAsync<LocalEmployee>();
        await db.ExecuteAsync("DELETE FROM Event WHERE SyncState = ? AND VoidPending = 0", (int)SyncState.Sent);
    }
}
