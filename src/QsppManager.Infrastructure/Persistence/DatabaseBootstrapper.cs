using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;

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
        conn.Open();
        try
        {
            bool hasHistory = Scalar(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'") > 0;
            if (!hasHistory)
            {
                var hasSchema = Scalar(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Requirements'") > 0;
                if (hasSchema)
                {
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