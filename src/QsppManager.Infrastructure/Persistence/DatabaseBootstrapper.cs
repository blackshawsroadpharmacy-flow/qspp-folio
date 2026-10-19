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
        db.Database.Migrate();
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