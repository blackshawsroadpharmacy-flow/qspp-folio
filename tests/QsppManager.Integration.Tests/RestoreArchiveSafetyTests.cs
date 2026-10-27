using Microsoft.EntityFrameworkCore;
using QsppManager.Infrastructure.Backup;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-04 / plan 003: backup restore must refuse any archive whose entries
/// escape their manifest, the target directory, or their own names. No database is
/// opened: Restore uses none of the constructor arguments for validation.</summary>
public class RestoreArchiveSafetyTests
{
    private static (BackupService Svc, string ZipPath, string Target) BuildCase(
        string name, params (string entryName, byte[] bytes, bool listed)[] entries)
    {
        var work = Path.Combine(Path.GetTempPath(), $"qspp-zs-{Guid.NewGuid():N}");
        var parent = Path.Combine(work, "parent");
        var target = Path.Combine(parent, "t");
        Directory.CreateDirectory(parent);
        var backupsDir = Path.Combine(work, "backups");
        var zipPath = Path.Combine(backupsDir, name + ".zip");
        Directory.CreateDirectory(backupsDir);

        System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create).Dispose();
        using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Update);

        void Add(string entryName, byte[] bytes)
        {
            var e = zip.CreateEntry(entryName);
            using var s = e.Open();
            s.Write(bytes, 0, bytes.Length);
        }

        var manifestFiles = new System.Text.StringBuilder();
        foreach (var (entryName, bytes, listed) in entries)
        {
            Add(entryName, bytes);
            if (listed)
                manifestFiles.Append("{\"path\":\"" + entryName.Replace("\\", "/")
                    + "\",\"sha256\":\"" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
                    + "\",\"bytes\":" + bytes.Length + "},");
        }
        if (manifestFiles.Length > 0) manifestFiles.Length--;
        var manifest = "{\"format\":\"qspp-backup-1\",\"createdUtc\":\"2026-10-28T00:00:00Z\",\"sha256Head\":\"0\",\"files\":["
            + manifestFiles + "]}";
        Add("manifest.json", System.Text.Encoding.UTF8.GetBytes(manifest));
        zip.Dispose();

        using var zipRead = System.IO.Compression.ZipFile.OpenRead(zipPath);
        Assert.All(zipRead.Entries, e => Assert.False(e.FullName.EndsWith("/")));

        var db = new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>().UseSqlite("Data Source=:memory:").Options);
        var store = new AttachmentStore(Path.GetTempPath());
        var svc = new BackupService(db, store, backupsDir);
        return (svc, zipPath, target);
    }

    private static byte[] Text(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Restore_RejectsEntryNotInManifest()
    {
        var (svc, zip, target) = BuildCase("notmanifest",
            ("qspp.db", Text("fake-db"), true),
            ("extra.txt", Text("sneaky"), false));
        var (ok, error, _, files) = svc.Restore(zip, target);
        Assert.False(ok);
        Assert.Contains("does not list", error);
        Assert.True(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.Equal(0, files);
    }

    [Fact]
    public void Restore_RejectsTraversalEntry_NotListed()
    {
        var (svc, zip, target) = BuildCase("traversal-unlisted",
            ("qspp.db", Text("fake-db"), true),
            ("../escaped-1.txt", Text("escape"), false));
        var (ok, error, _, _) = svc.Restore(zip, target);
        Assert.False(ok);
        Assert.Contains("does not list", error);
        var escaped = Path.Combine(Path.GetDirectoryName(target)!, "escaped-1.txt");
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void Restore_RejectsTraversalPath_ListedInManifest_WithMatchingHash()
    {
        var (svc, zip, target) = BuildCase("traversal-listed",
            ("qspp.db", Text("fake-db"), true),
            ("../escaped-2.txt", Text("escape2"), true));
        var (ok, error, _, _) = svc.Restore(zip, target);
        Assert.False(ok);
        Assert.Contains("Unsafe path", error);
        var escaped = Path.Combine(Path.GetDirectoryName(target)!, "escaped-2.txt");
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void Restore_RejectsDuplicateEntryNames()
    {
        var work = Path.Combine(Path.GetTempPath(), $"qspp-zs-{Guid.NewGuid():N}");
        var target = Path.Combine(work, "t");
        Directory.CreateDirectory(work);
        var backupsDir = Path.Combine(work, "backups");
        Directory.CreateDirectory(backupsDir);
        var zipPath = Path.Combine(backupsDir, "dup.zip");
        var a = Text("first-version");
        var b = Text("second-version");

        System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create).Dispose();
        using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Update);
        foreach (var bytes in new[] { a, b })
        {
            var e = zip.CreateEntry("qspp.db");
            using var s = e.Open();
            s.Write(bytes, 0, bytes.Length);
        }
        var manifest = "{\"format\":\"qspp-backup-1\",\"files\":[{\"path\":\"qspp.db\",\"sha256\":\""
            + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(a)).ToLowerInvariant() + "\"}]}";
        var me = zip.CreateEntry("manifest.json");
        using (var ms = me.Open()) ms.Write(System.Text.Encoding.UTF8.GetBytes(manifest));
        zip.Dispose();

        var db = new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>().UseSqlite("Data Source=:memory:").Options);
        var store = new AttachmentStore(Path.GetTempPath());
        var svc = new BackupService(db, store, backupsDir);
        var (ok, error, _, _) = svc.Restore(zipPath, target);
        Assert.False(ok);
        Assert.Contains("two entries", error);
    }

    [Fact]
    public void Restore_AcceptsNestedAttachmentPaths()
    {
        var (svc, zip, target) = BuildCase("nested-ok",
            ("qspp.db", Text("fake-db"), true),
            ("attachments/ab/cd/abcd.txt", Text("nested-attachment"), true));
        var (ok, error, restoredDb, files) = svc.Restore(zip, target);
        Assert.True(ok, error);
        Assert.Equal(3, files); // qspp.db + nested attachment + manifest.json
        Assert.Equal(Path.Combine(target, "qspp.db"), restoredDb);
        Assert.True(File.Exists(Path.Combine(target, "attachments", "ab", "cd", "abcd.txt")));
        Assert.Contains("nested-attachment", File.ReadAllText(Path.Combine(target, "attachments", "ab", "cd", "abcd.txt")));
    }
}