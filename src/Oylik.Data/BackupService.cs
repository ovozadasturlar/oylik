using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Oylik.Data;

/// <summary>
/// 5.1 Backup: har kuni birinchi ishga tushganda va oy yopishdan oldin.
/// `backups\oylik-YYYYMMDD-HHmm.db`, oxirgi 90 tasi saqlanadi; qo'shimcha papka berilsa — u yerga ham.
/// </summary>
public sealed class BackupService(AppPaths paths, TimeProvider clock)
{
    public const int KeepCount = 90;
    private const string Prefix = "oylik-";

    public string Create(string? extraFolder = null)
    {
        Directory.CreateDirectory(paths.Backups);
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var target = UniquePath(Path.Combine(paths.Backups, $"{Prefix}{stamp}.db"));

        using (var source = new SqliteConnection($"Data Source={paths.Database};Pooling=False"))
        using (var destination = new SqliteConnection($"Data Source={target};Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }

        if (!string.IsNullOrWhiteSpace(extraFolder))
        {
            Directory.CreateDirectory(extraFolder);
            File.Copy(target, Path.Combine(extraFolder, Path.GetFileName(target)), overwrite: true);
        }

        Prune();
        return target;
    }

    /// <summary>Bugun hali backup bo'lmagan bo'lsa yaratadi.</summary>
    public string? CreateDailyIfNeeded(string? extraFolder = null)
    {
        var today = clock.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (Directory.Exists(paths.Backups) &&
            Directory.EnumerateFiles(paths.Backups, $"{Prefix}{today}-*.db").Any())
            return null;
        return File.Exists(paths.Database) ? Create(extraFolder) : null;
    }

    public DateTimeOffset? LastBackupAt()
    {
        if (!Directory.Exists(paths.Backups))
            return null;
        var last = new DirectoryInfo(paths.Backups).EnumerateFiles($"{Prefix}*.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        return last is null ? null : new DateTimeOffset(last.LastWriteTimeUtc, TimeSpan.Zero);
    }

    private void Prune()
    {
        var old = new DirectoryInfo(paths.Backups).EnumerateFiles($"{Prefix}*.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(KeepCount);
        foreach (var file in old)
            file.Delete();
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}-{i}.db");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
