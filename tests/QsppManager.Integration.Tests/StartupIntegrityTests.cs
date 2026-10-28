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
}