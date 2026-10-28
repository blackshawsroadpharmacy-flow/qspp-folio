namespace QsppManager.Infrastructure.Runtime;

/// <summary>FIX-15: one process per data folder. The <c>.lock</c> file is opened with
/// FileShare.None and held for the process lifetime; a second copy of QSPP Folio pointed at
/// the same data directory fails to acquire and stays closed instead of corrupting the
/// shared database. The OS releases the lock when the process exits, so a leftover empty
/// file never blocks the next start.</summary>
public static class DataDirLock
{
    private static readonly Dictionary<string, FileStream> Held = new(StringComparer.Ordinal);

    /// <summary>True means this process owns <paramref name="dataDir"/> for its lifetime;
    /// false means another process holds it (the app shows the already-open message and
    /// exits). Re-acquiring the same directory in the same process is a no-op success.</summary>
    public static bool TryAcquire(string dataDir)
    {
        var path = Path.Combine(dataDir, ".lock");
        if (Held.ContainsKey(path)) return true;
        try
        {
            Held[path] = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;   // sharing violation: already locked
        }
    }
}

/// <summary>Raised by <c>DatabaseBootstrapper</c> when the data file fails PRAGMA
/// quick_check or is not a readable SQLite file. Carries a user-ready message that names
/// the newest backup file so recovery starts from the right place.</summary>
public sealed class CorruptDatabaseException : Exception
{
    public CorruptDatabaseException(string message) : base(message) { }
}