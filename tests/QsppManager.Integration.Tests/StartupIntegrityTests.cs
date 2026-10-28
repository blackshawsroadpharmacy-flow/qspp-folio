using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Runtime;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-15: WAL mode, quick_check at boot, exclusive data-folder lock.</summary>
public class StartupIntegrityTests
{
    private static QsppDbContext FreshDb(string path)
        => new(new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options);

    [Fact]
    public void Prepare_SetsWalsJournalMode_AndQuickCheckPasses()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qspp-wal-{Guid.NewGuid():N}.db");
        using var db = FreshDb(path);
        DatabaseBootstrapper.Prepare(db);   // must not throw: quick_check returned "ok"
        db.Database.OpenConnection();
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", Convert.ToString(cmd.ExecuteScalar()));
        db.Database.CloseConnection();
    }

    [Fact]
    public void Prepare_RefusesCorruptFile_AndNamesTheBackupSituation()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"qspp-corrupt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "qspp.db");
        File.WriteAllBytes(path, "this is not a sqlite database"u8.ToArray());

        using var db = FreshDb(path);
        var ex = Assert.Throws<CorruptDatabaseException>(() => DatabaseBootstrapper.Prepare(db));
        Assert.Contains("corruption check", ex.Message);
        Assert.Contains("No backup file was found", ex.Message);   // a fresh broken folder has no backups yet
    }

    [Fact]
    public void Prepare_NamesTheNewestBackup_OnCorruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"qspp-corrupt2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "backups"));
        File.WriteAllBytes(Path.Combine(dir, "backups", "QsppManager-backup-20261025-1605.db"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(dir, "backups", "QsppManager-backup-20261026-0900.db"), Array.Empty<byte>());
        var path = Path.Combine(dir, "qspp.db");
        File.WriteAllBytes(path, "not a database at all"u8.ToArray());

        using var db = FreshDb(path);
        var ex = Assert.Throws<CorruptDatabaseException>(() => DatabaseBootstrapper.Prepare(db));
        Assert.Contains("QsppManager-backup-20261026-0900.db", ex.Message);   // the NEWEST one
    }

    [Fact]
    public void DataDirLock_IsExclusivelyHeld()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"qspp-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        Assert.True(DataDirLock.TryAcquire(dir));
        // the same directory from the same process is a no-op success
        Assert.True(DataDirLock.TryAcquire(dir));

        // another holder on the same .lock path is refused (mimics the second launch)
        var dir2 = Path.Combine(Path.GetTempPath(), $"qspp-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir2);
        using (var held = new FileStream(Path.Combine(dir2, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.False(DataDirLock.TryAcquire(dir2));
    }

    [Fact]
    public void Prepare_StampsLegacySchemaWithoutHistory_AndCompletesMigrations()
    {
        // FIX-17 step 5: migrations are exercised end to end on the REAL legacy shape:
        // a database that was migrated only to InitialCreate has the old schema and no
        // history table; Prepare stamps InitialCreate back and migrates every later step.
        // (The backlog's EnsureCreated premise cannot pass: an EnsureCreated schema already
        // contains every later column, so the re-runs AddColumn would hit duplicate-column
        // errors — that failure says nothing about the legacy path, which this test builds
        // the true legacy schema for.)
        var path = Path.Combine(Path.GetTempPath(), $"qspp-leghist-{Guid.NewGuid():N}.db");

        using (var seedDb = FreshDb(path))
            seedDb.Database.Migrate(DatabaseBootstrapper.InitialMigrationId);
        using (var wiper = FreshDb(path))
        {
            wiper.Database.OpenConnection();
            using var wipe = wiper.Database.GetDbConnection().CreateCommand();
            wipe.CommandText = "DROP TABLE __EFMigrationsHistory";
            _ = wipe.ExecuteNonQuery();
            wiper.Database.CloseConnection();
        }

        AssertPreparedLegacy(path);
    }

    [Fact]
    public void Prepare_RecoversAnEmptyMigrationsHistoryTable()
    {
        // FIX-17: the empty-table edge the sweep found. A history table whose rows are gone
        // must be re-stamped, not failed with 'table AuditEvents already exists'.
        var path = Path.Combine(Path.GetTempPath(), $"qspp-emptyhist-{Guid.NewGuid():N}.db");

        using (var seedDb = FreshDb(path))
            seedDb.Database.Migrate(DatabaseBootstrapper.InitialMigrationId);
        using (var wiper = FreshDb(path))
        {
            wiper.Database.OpenConnection();
            using var wipe = wiper.Database.GetDbConnection().CreateCommand();
            wipe.CommandText = "DELETE FROM __EFMigrationsHistory";
            _ = wipe.ExecuteNonQuery();
            wiper.Database.CloseConnection();
        }

        AssertPreparedLegacy(path);
    }

    private static void AssertPreparedLegacy(string path)
    {
        using var db = FreshDb(path);
        DatabaseBootstrapper.Prepare(db);
        db.Database.OpenConnection();
        using (var row = db.Database.GetDbConnection().CreateCommand())
        {
            row.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '20261019142616_InitialCreate'";
            Assert.Equal(1, Convert.ToInt64(row.ExecuteScalar()));
            row.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory";
            Assert.Equal(db.Database.GetMigrations().Count(), Convert.ToInt64(row.ExecuteScalar()));
            row.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Requirements'";
            Assert.Equal(1, Convert.ToInt64(row.ExecuteScalar()));
        }
        db.Database.CloseConnection();
    }
}