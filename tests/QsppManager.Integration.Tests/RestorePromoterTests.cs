using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QsppManager.Infrastructure.Backup;
using QsppManager.Infrastructure.Persistence;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-03 / plan 001: restore promotion and schema migration must never be able
/// to destroy live data. Every path creates temp files only.</summary>
public class RestorePromoterTests
{
    private const string InitialMigrationId = DatabaseBootstrapper.InitialMigrationId;

    private static string Work() => Path.Combine(Path.GetTempPath(), $"qspp-promote-{Guid.NewGuid():N}");

    /// <summary>Creates a realistic legacy database: real InitialCreate schema plus
    /// migration history, so the migration-history check has something real to read.
    /// Pooling is disabled: a pooled handle would keep data out of the file copies
    /// these tests make (Microsoft.Data.Sqlite pools by default).</summary>
    private static string CreateLegacyDb(string path, string? marker = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new DbContextOptionsBuilder<QsppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        using var db = new QsppDbContext(options);
        db.Database.Migrate(InitialMigrationId);
        if (marker is not null)
            db.Database.ExecuteSqlRaw($"CREATE TABLE IF NOT EXISTS {marker} (id INTEGER PRIMARY KEY)");
        db.Database.CloseConnection();
        return path;
    }

    private static IReadOnlyList<string> KnownMigrations()
    {
        using var db = new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        return db.Database.GetMigrations().ToList();
    }

    private static bool TableExists(string dbPath, string table)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    [Fact]
    public void Promote_CopiesLiveDbAside_BeforeSwap()
    {
        var work = Work();
        var stagingDir = Path.Combine(work, "staging");
        var liveDb = Path.Combine(work, "live", "qspp.db");
        var stagedDb = Path.Combine(stagingDir, "qspp.db");
        CreateLegacyDb(liveDb);
        CreateLegacyDb(stagedDb, marker: "restore_marker");
        Directory.CreateDirectory(stagingDir);

        var outcome = RestorePromoter.Promote(stagingDir, liveDb, Path.Combine(work, "attach"), work, KnownMigrations());
        Assert.True(outcome.Promoted, outcome.Message);

        var preRestore = Path.Combine(work, "pre-restore");
        var stamps = Directory.EnumerateDirectories(preRestore).ToList();
        var safetyDb = Path.Combine(Assert.Single(stamps), "qspp.db");
        Assert.True(File.Exists(safetyDb), "safety copy of the live db must exist before the swap");
        Assert.False(TableExists(safetyDb, "restore_marker"), "the safety copy is the OLD live database");
    }

    [Fact]
    public void Promote_RefusesCorruptDb()
    {
        var work = Work();
        var stagingDir = Path.Combine(work, "staging");
        var liveDb = Path.Combine(work, "live", "qspp.db");
        var validStaged = CreateLegacyDb(Path.Combine(work, "valid", "qspp.db"));
        CreateLegacyDb(liveDb);
        Directory.CreateDirectory(stagingDir);
        var stagedDb = Path.Combine(stagingDir, "qspp.db");
        File.WriteAllBytes(stagedDb, File.ReadAllBytes(validStaged).Take(100).ToArray());
        var liveShaBefore = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(liveDb));

        var outcome = RestorePromoter.Promote(stagingDir, liveDb, Path.Combine(work, "attach"), work, KnownMigrations());
        Assert.False(outcome.Promoted);
        Assert.True(outcome.Attempted);
        var liveShaAfter = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(liveDb));
        Assert.True(liveShaBefore.AsSpan().SequenceEqual(liveShaAfter), "live db bytes must be untouched on refusal");
        Assert.True(Directory.Exists(stagingDir), "staging is kept when promotion fails");
        Assert.False(File.Exists(liveDb + ".incoming"));
    }

    [Fact]
    public void Promote_RefusesBackupFromNewerSchema()
    {
        var work = Work();
        var stagingDir = Path.Combine(work, "staging");
        var liveDb = Path.Combine(work, "live", "qspp.db");
        var stagedDb = CreateLegacyDb(Path.Combine(stagingDir, "qspp.db"));
        CreateLegacyDb(liveDb);
        using (var db = new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>().UseSqlite($"Data Source={stagedDb};Pooling=False").Options))
        {
            db.Database.ExecuteSqlRaw(
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('29990101000000_Future', '99.0.0')");
            db.Database.CloseConnection();
        }
        var liveShaBefore = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(liveDb));

        var outcome = RestorePromoter.Promote(stagingDir, liveDb, Path.Combine(work, "attach"), work, KnownMigrations());
        Assert.False(outcome.Promoted);
        Assert.Contains("newer version", outcome.Message);
        var liveShaAfter = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(liveDb));
        Assert.True(liveShaBefore.AsSpan().SequenceEqual(liveShaAfter));
        Assert.True(Directory.Exists(stagingDir));
    }

    [Fact]
    public void Promote_SwapsDb_AndRemovesStagingOnlyOnSuccess()
    {
        var work = Work();
        var stagingDir = Path.Combine(work, "staging");
        var liveDb = Path.Combine(work, "live", "qspp.db");
        var stagedDb = Path.Combine(stagingDir, "qspp.db");
        CreateLegacyDb(liveDb);
        CreateLegacyDb(stagedDb, marker: "restore_new_data");
        Directory.CreateDirectory(stagingDir);

        var outcome = RestorePromoter.Promote(stagingDir, liveDb, Path.Combine(work, "attach"), work, KnownMigrations());
        Assert.True(outcome.Promoted, outcome.Message);
        Assert.True(TableExists(liveDb, "restore_new_data"), "live database is now the staged one");
        Assert.False(Directory.Exists(stagingDir), "staging deleted only after success");
    }

    [Fact]
    public void Promote_KeepsOnlyNewestFiveSafetyCopies()
    {
        var work = Work();
        var stagingDir = Path.Combine(work, "staging");
        var liveDb = Path.Combine(work, "live", "qspp.db");
        CreateLegacyDb(liveDb);

        for (var i = 1; i <= 6; i++)
        {
            Directory.CreateDirectory(stagingDir);
            var stagedDb = Path.Combine(stagingDir, "qspp.db");
            CreateLegacyDb(stagedDb, marker: $"marker_{i}");
            var stamp = $"2026010100000{i}";
            var outcome = RestorePromoter.Promote(stagingDir, liveDb, Path.Combine(work, "attach"),
                work, KnownMigrations(), stampFactory: () => stamp);
            Assert.True(outcome.Promoted, $"round {i}: {outcome.Message}");
        }

        var preRestore = Path.Combine(work, "pre-restore");
        Assert.Equal(5, Directory.EnumerateDirectories(preRestore).Count());
    }

    [Fact]
    public void Prepare_BacksUpBeforePendingMigration()
    {
        var work = Work();
        var dbPath = Path.Combine(work, "qspp.db");
        Directory.CreateDirectory(work);
        var options = new DbContextOptionsBuilder<QsppDbContext>().UseSqlite($"Data Source={dbPath};Pooling=False").Options;
        using (var db = new QsppDbContext(options))
        {
            // v1 database: InitialCreate applied and nothing later stamped.
            db.Database.Migrate(InitialMigrationId);
            DatabaseBootstrapper.Prepare(db);   // has 6 pending migrations -> must back up first
        }
        var preMigration = Path.Combine(Path.GetDirectoryName(dbPath)!, "pre-migration");
        var backups = Directory.EnumerateFiles(preMigration, "qspp-*.db").ToList();
        Assert.Single(backups);
        Assert.True(new FileInfo(backups[0]).Length > 0);

        using var reopened = new QsppDbContext(options);
        reopened.Database.OpenConnection();
        try
        {
            using var cmd = reopened.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory";
            Assert.Equal(7L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        finally { reopened.Database.CloseConnection(); }
    }
}