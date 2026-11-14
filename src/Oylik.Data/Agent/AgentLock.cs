using System.Security.Cryptography;
using System.Text;

namespace Oylik.Data.Agent;

/// <summary>
/// Bosqich 2: terminalni faqat BITTA jarayon so'raydi. Fon agenti ishlayotganda nomli mutex ushlab turadi; desktop har
/// tick oldidan tekshiradi — mutex bor bo'lsa terminalni o'zi so'ramaydi (agent birlamchi), yo'q bo'lsa hozirgidek so'raydi.
/// Mutex nomi baza yo'liga bog'langan: boshqa OYLIK_HOME (sinov, demo) haqiqiy o'rnatmani to'smaydi.
/// Mutex egalik uchun emas, "jarayon tirikmi" belgisi sifatida ishlatiladi: jarayon tugasa (hatto yiqilsa) OS uni yo'qotadi.
/// </summary>
public sealed class AgentLock : IDisposable
{
    private readonly Mutex _mutex;

    private AgentLock(Mutex mutex, string name)
    {
        _mutex = mutex;
        Name = name;
    }

    /// <summary>Ushlab turilgan mutex nomi (log uchun).</summary>
    public string Name { get; }

    /// <summary>Baza yo'li bo'yicha nom asosi (prefikssiz): <c>Oylik.TerminalPoller.{xesh}</c>.</summary>
    public static string BaseName(AppPaths paths)
    {
        var full = Path.GetFullPath(paths.Database).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16];
        return $"Oylik.TerminalPoller.{hash}";
    }

    /// <summary>Avval <c>Global\</c> (service — session 0 va foydalanuvchi sessiyasi bir-birini ko'radi), ruxsat bo'lmasa <c>Local\</c>.</summary>
    private static IEnumerable<string> Names(AppPaths paths)
    {
        var name = BaseName(paths);
        yield return @"Global\" + name;
        yield return @"Local\" + name;
    }

    /// <summary>Agent uchun: mutexni yaratadi. Boshqa agent allaqachon ishlayotgan bo'lsa — null.</summary>
    public static AgentLock? TryAcquire(AppPaths paths)
    {
        foreach (var name in Names(paths))
        {
            Mutex mutex;
            bool createdNew;
            try
            {
                mutex = new Mutex(initiallyOwned: false, name, out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                // Global nomda yaratishga ruxsat yo'q yoki boshqa foydalanuvchining agenti ushlab turibdi
                if (IsHeldBy(name))
                    return null;
                continue;
            }
            if (!createdNew)
            {
                mutex.Dispose();
                return null; // boshqa agent ishlayapti
            }
            return new AgentLock(mutex, name);
        }
        return null;
    }

    /// <summary>Desktop uchun: shu bazaning agenti ishlayaptimi.</summary>
    public static bool IsHeld(AppPaths paths) => Names(paths).Any(IsHeldBy);

    private static bool IsHeldBy(string name)
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var existing))
                return false;
            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // bor, lekin boshqa hisob (masalan, service) yaratgan
        }
    }

    public void Dispose() => _mutex.Dispose();
}
