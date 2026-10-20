using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;

namespace QsppManager.Infrastructure.Backup;

/// <summary>Backup/restore (M4 · DD-23). A backup is a zip containing:
/// qspp.db (taken via `VACUUM INTO` — a consistent copy of the live database),
/// every managed attachment, and manifest.json with per-file sha256.
/// Restore validates every hash, extracts to a target directory, and the app restarts
/// against the restored copy — nothing is swapped in place while running.</summary>
public class BackupService(QsppDbContext db, AttachmentStore store, string backupsDir)
{
    public (bool ok, string zipPath, string error, string shaHead) CreateBackup(string label)
    {
        try
        {
            Directory.CreateDirectory(backupsDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeLabel = string.Join("-", label.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Truncate(40);
            var zipPath = Path.Combine(backupsDir, $"qspp-backup-{stamp}{(safeLabel.Length > 0 ? "-" + safeLabel : "")}.zip");
            var staging = Path.Combine(Path.GetTempPath(), $"qspp-bkp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);

            try
            {
                // 1. Consistent database copy via VACUUM INTO (works while the app is running).
                var dbCopy = Path.Combine(staging, "qspp.db");
                var escaped = dbCopy.Replace("'", "''");
                db.Database.ExecuteSqlRaw($"VACUUM INTO '{escaped}'");
                var dbSha = HashFile(dbCopy);
                var files = new List<object>
                {
                    new { path = "qspp.db", sha256 = dbSha, bytes = new FileInfo(dbCopy).Length },
                };

                // 2. Managed attachments (paths are relative inside the archive).
                var attRoot = store.Root;
                if (Directory.Exists(attRoot))
                    foreach (var f in Directory.EnumerateFiles(attRoot, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(attRoot, f).Replace('\\', '/');
                        var dest = Path.Combine(staging, "attachments", rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(f, dest, overwrite: true);
                        files.Add(new { path = "attachments/" + rel, sha256 = HashFile(dest), bytes = new FileInfo(dest).Length });
                    }

                // 3. Manifest with hashes of everything above.
                var manifest = JsonSerializer.Serialize(new
                {
                    createdAt = DateTime.UtcNow.ToString("O"),
                    format = "qspp-backup-1",
                    files,
                }, new JsonSerializerOptions { WriteIndented = true });
                var manifestPath = Path.Combine(staging, "manifest.json");
                File.WriteAllText(manifestPath, manifest);

                // 4. Zip it.
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                foreach (var f in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(staging, f).Replace('\\', '/');
                    archive.CreateEntryFromFile(f, rel, CompressionLevel.Optimal);
                }
                return (true, zipPath, "", dbSha.Truncate(12));
            }
            finally
            {
                try { Directory.Delete(staging, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message, "");
        }
    }

    /// <summary>Validates every manifest hash, extracts to targetDir, and reports the
    /// database path to restart against. Does NOT touch the running data.</summary>
    public (bool ok, string error, string restoredDbPath, int filesRestored) Restore(string zipPath, string targetDir)
    {
        try
        {
            if (!File.Exists(zipPath)) return (false, $"Backup not found: {zipPath}", "", 0);
            Directory.CreateDirectory(targetDir);
            using var archive = ZipFile.OpenRead(zipPath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null) return (false, "Not a Hugh's QSPP Manager backup (missing manifest.json).", "", 0);

            string manifestJson;
            using (var reader = new StreamReader(manifestEntry.Open())) manifestJson = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("format", out var fmt) || fmt.GetString() != "qspp-backup-1")
                return (false, "Unknown backup format.", "", 0);

            // Validate every listed file hash before trusting the archive.
            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
                expected[f.GetProperty("path").GetString()!] = f.GetProperty("sha256").GetString()!;

            foreach (var (entryPath, sha) in expected)
            {
                var entry = archive.GetEntry(entryPath);
                if (entry == null)
                    throw new InvalidOperationException($"manifest lists '{entryPath}' but it is missing from the archive");
                var tmp = Path.Combine(targetDir, ".hashcheck");
                entry.ExtractToFile(tmp, overwrite: true);
                var actual = HashFile(tmp);
                File.Delete(tmp);
                if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                    return (false, $"Hash mismatch for {entryPath} — the backup is corrupted or tampered with. Nothing was restored.", "", 0);
            }

            // Extract everything (hashes already proven).
            var restored = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var dest = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                restored++;
            }
            return (true, "", Path.Combine(targetDir, "qspp.db"), restored);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message, "", 0);
        }
    }

    private static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}

public static class BackupStringExtensions
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..max];
}