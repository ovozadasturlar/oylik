namespace Oylik.Data;

public sealed record AppPaths(string Root)
{
    /// <summary>`%LOCALAPPDATA%\Oylik`; sinov uchun `OYLIK_HOME` muhit o'zgaruvchisi bilan almashtiriladi.</summary>
    public static AppPaths Default { get; } = new(
        Environment.GetEnvironmentVariable("OYLIK_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Oylik"));

    public string Database => Path.Combine(Root, "oylik.db");
    public string Logs => Path.Combine(Root, "logs");
    public string Backups => Path.Combine(Root, "backups");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
    }
}
