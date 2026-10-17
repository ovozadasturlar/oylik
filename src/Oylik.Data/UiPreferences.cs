using Microsoft.Data.Sqlite;
using Oylik.Core.Domain;

namespace Oylik.Data;

/// <summary>
/// Til va mavzu oyna yaratilishidan oldin kerak (XAML matnlari yuklanishda olinadi), baza esa keyin ochiladi —
/// shuning uchun Settings jadvalidan to'g'ridan-to'g'ri o'qiladi. Baza yo'q yoki ustun hali yo'q (eski baza) — default.
/// </summary>
public sealed record UiPreferences(string Language, UiTheme Theme, string? BranchFilter = null)
{
    public static UiPreferences Default { get; } = new(AppSettings.LanguageUzLatn, UiTheme.Light);

    public static UiPreferences Read(AppPaths paths)
    {
        if (!File.Exists(paths.Database))
            return Default;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = paths.Database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Language, Theme FROM Settings LIMIT 1";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return Default;
            var theme = (UiTheme)reader.GetInt32(1);
            return new(reader.GetString(0), Enum.IsDefined(theme) ? theme : UiTheme.Light, ReadBranchFilter(connection));
        }
        catch (SqliteException)
        {
            return Default;
        }
    }

    /// <summary>Eski bazada ustun hali yo'q bo'lishi mumkin (migratsiya oyna ochilgandan keyin).</summary>
    private static string? ReadBranchFilter(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT BranchFilter FROM Settings LIMIT 1";
            return command.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            return null;
        }
    }
}
