using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Runtime;

namespace QsppManager.Infrastructure.Persistence;

/// <summary>Boot-time schema strategy (M2 · DD-21):
/// 1. Fresh database → EF migrations create everything (__EFMigrationsHistory included).
/// 2. v1 database created with EnsureCreated (has the tables but no history) → the
///    InitialCreate migration is STAMPED as already applied, so only later migrations
///    actually run against it. This keeps the first customer install (created before
///    migrations existed) valid without hand-editing their data file.
/// The audit chain and all row data are untouched — this only manages schema bookkeeping.</summary>
public static class DatabaseBootstrapper
{
    public const string InitialMigrationId = "20261019142616_InitialCreate";

    public static void Prepare(QsppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        // FIX-15: PRAGMA quick_check BEFORE anything writes to or stamps this file. A corrupt
        // data file stops here with the newest backup named; it must not reach VACUUM INTO or
        // a migration. (The backlog says "after Prepare"; running it first catches the corrupt
        // file before the safety copy reads it, which is the same intent, said plainly.)
        RunQuickCheck(db.Database.GetDbConnection().DataSource);

        conn.Open();
        try
        {
            var hasHistoryTable = Scalar(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'") > 0;
            // FIX-17: rows decide, not the table's existence. A history table that exists
            // but has lost its rows is an install whose history is gone; it must be
            // re-stamped and migrated, not failed with 'table already exists' from a
            // re-run InitialCreate.
            var hasHistory = hasHistoryTable && Scalar(conn, "SELECT COUNT(*) FROM __EFMigrationsHistory") > 0;
            if (!hasHistory)
            {
                var hasSchema = Scalar(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Requirements'") > 0;
                if (hasSchema)
                {
                    if (!hasHistoryTable)
                        Exec(conn, "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (" +
                                   "MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, " +
                                   "ProductVersion TEXT NOT NULL)");
                    Exec(conn, $"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('{InitialMigrationId}', '10.0.13')");
                }
                // No schema and no history → fresh install; leave it to Migrate().
            }
        }
        finally { conn.Close(); }

        // Plan 001 (FIX-03): a database that is about to be migrated gets a VACUUM INTO
        // safety copy first. A fresh install (no file or no user tables) has nothing worth saving.
        var pending = db.Database.GetPendingMigrations().ToList();
        var dbFile = db.Database.GetDbConnection().DataSource;
        if (pending.Count > 0 && File.Exists(dbFile) && new FileInfo(dbFile).Length > 0 && HasUserTables(db))
            PreMigrationBackup(db, dbFile, pending[^1]);

        db.Database.Migrate();

        // FIX-15: WAL mode after the schema settles. journal_mode is persisted in the file
        // header, so it sticks; the connection string keeps foreign keys and a 5 s timeout
        // for every other open.
        conn.Open();
        try { Exec(conn, "PRAGMA journal_mode=WAL;"); }
        finally { conn.Close(); }
    }

    /// <summary>Corruption gate (FIX-15): a file that is not a readable SQLite database, or
    /// one whose quick_check does not return exactly "ok", stops the boot with a
    /// user-ready message naming the newest backup file.</summary>
    private static void RunQuickCheck(string dbFile)
    {
        // A fresh install has no file yet (or a 0-byte leftover); there is nothing to check.
        // This also mirrors the FIX-03 rule that only an existing, non-empty db gets read.
        if (!File.Exists(dbFile) || new FileInfo(dbFile).Length == 0) return;
        // Open a raw connection straight to the file so the check reads the exact bytes on disk.
        using var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbFile};Mode=ReadWrite");
        try
        {
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check";
            var result = Convert.ToString(cmd.ExecuteScalar() ?? "") ?? "";
            if (result != "ok")
                throw new CorruptDatabaseException(BootStopMessage(dbFile, result));
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            throw new CorruptDatabaseException(BootStopMessage(dbFile, "the file is not a readable SQLite database"));
        }
    }

    /// <summary>Names the newest backup file (QsppManager-backup-* in the backups folder
    /// beside the database) so recovery starts from the right place.</summary>
    private static string BootStopMessage(string dbFile, string result)
    {
        var dataDir = Path.GetDirectoryName(dbFile);
        var backupsDir = dataDir is null ? "backups" : Path.Combine(dataDir, "backups");
        var newest = Directory.Exists(backupsDir)
            ? Directory.EnumerateFiles(backupsDir, "QsppManager-backup-*.db")
                .OrderByDescending(Path.GetFileName)
                .FirstOrDefault()
        : null;
        var backupLine = newest is null
            ? "No backup file was found in the backups folder of this data folder."
            : $"Newest backup file: {Path.GetFileName(newest)}.";
        return "The data file did not pass a corruption check before start (" + result + "). " +
               "Nothing was changed. " + backupLine +
               " Stop here: do not delete or overwrite anything; have the newest backup checked and restored before any other step.";
    }

    /// <summary>A fresh install has no data worth saving; a legacy or migrated DB does.
    /// Checks for the Requirements table through the same read-only Scalar helper.</summary>
    private static bool HasUserTables(QsppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        conn.Open();
        try { return Scalar(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Requirements'") > 0; }
        finally { conn.Close(); }
    }

    /// <summary>VACUUM INTO copy kept in pre-migration/ beside the database before any
    /// pending migration runs (plan 001). Throws on failure by contract: the app must
    /// never migrate without the safety copy, and the caller catches to stop the boot.</summary>
    private static void PreMigrationBackup(QsppDbContext db, string dbFile, string pendingMigrationId)
    {
        var dir = Path.Combine(Path.GetDirectoryName(dbFile)!, "pre-migration");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"qspp-{DateTime.Now.ToString("yyyyMMdd-HHmmss")}-before-{pendingMigrationId}.db");
        var escaped = target.Replace("'", "''");
        db.Database.ExecuteSqlRaw($"VACUUM INTO '{escaped}'");
        PrunePreMigration(dir);
    }

    /// <summary>Keeps the newest 5 qspp-*.db safety copies (stamp prefix sorts chronologically).</summary>
    private static void PrunePreMigration(string dir)
    {
        var keep = Directory.EnumerateFiles(dir, "qspp-*.db")
            .OrderByDescending(Path.GetFileName)
            .Take(5)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(dir, "qspp-*.db"))
            if (!keep.Contains(f))
                try { File.Delete(f); } catch { }
    }

    private static long Scalar(System.Data.Common.DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private static void Exec(System.Data.Common.DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.ExecuteNonQuery();
    }
}