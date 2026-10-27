namespace QsppManager.Infrastructure.Backup;

/// <summary>Promotes a verified restore staging dir into the live data directory
/// (plan 001, FIX-03). The old behaviour copied the staged database over the live
/// one with no safety copy and no integrity check. Now: the live database is copied
/// aside into pre-restore/ and hash-verified first, the staged database is checked
/// with PRAGMA integrity_check and a migration-history check, the swap is atomic,
/// and staging is deleted only after success. Any failure leaves the live data
/// exactly as it was.</summary>
public static class RestorePromoter
{
    public sealed record Outcome(bool Promoted, bool Attempted, string Message);

    /// <summary>staging/qspp.db + staging/attachments -> live db path + attachments dir.
    /// Never leaves the live DB half-written; never deletes staging unless promotion
    /// succeeded. stampFactory exists for tests; production callers omit it.</summary>
    public static Outcome Promote(string stagingDir, string liveDbPath, string attachmentsDir, string dataRoot,
        IReadOnlyCollection<string> knownMigrationIds, Func<string>? stampFactory = null)
    {
        var stagedDb = Path.Combine(stagingDir, "qspp.db");
        if (!File.Exists(stagedDb)) return new Outcome(false, false, "");
        try
        {
            // Safety copy of the live DB before anything is touched.
            string? safetyDb = null;
            if (File.Exists(liveDbPath))
            {
                var dir = Path.Combine(dataRoot, "pre-restore", stampFactory?.Invoke() ?? DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(dir);
                safetyDb = Path.Combine(dir, "qspp.db");
                File.Copy(liveDbPath, safetyDb);
                if (!SameBytes(liveDbPath, safetyDb))
                    return new Outcome(false, true, "Safety copy did not verify; restore not applied. Live data untouched.");
                foreach (var side in new[] { "-wal", "-shm" })
                {
                    var src = liveDbPath + side;
                    if (File.Exists(src)) File.Copy(src, safetyDb + side, overwrite: true);
                }
                PruneSafetyCopies(Path.Combine(dataRoot, "pre-restore"));
            }

            // Verify the incoming database before it replaces anything.
            var incoming = liveDbPath + ".incoming";
            try { File.Copy(stagedDb, incoming, overwrite: true); }
            catch (Exception ex)
            {
                return new Outcome(false, true, "Could not read the staged backup database: " + ex.Message);
            }
            var verdict = VerifyIncoming(incoming, knownMigrationIds);
            if (verdict is not null)
            {
                TryDelete(incoming);
                return new Outcome(false, true, verdict);
            }

            // Atomic swap. On any failure, roll the safety copy back.
            try
            {
                foreach (var side in new[] { "-wal", "-shm" })
                    TryDelete(liveDbPath + side);   // a stale journal must not replay onto the new file
                File.Move(incoming, liveDbPath, overwrite: true);
                CopyStagedAttachments(stagingDir, attachmentsDir);
                try { Directory.Delete(stagingDir, recursive: true); } catch { }
                var kept = safetyDb is null ? "" : " Previous data kept in pre-restore/" +
                    Path.GetFileName(Path.GetDirectoryName(safetyDb)) + ".";
                return new Outcome(true, true, "Restore applied." + kept);
            }
            catch (Exception ex)
            {
                if (safetyDb is { } back)
                {
                    foreach (var side in new[] { "-wal", "-shm" }) TryDelete(liveDbPath + side);
                    try { File.Copy(back, liveDbPath, overwrite: true); } catch { }
                }
                return new Outcome(false, true, "Restore failed and was rolled back: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            return new Outcome(false, true, "Restore failed: " + ex.Message);
        }
    }

    /// <summary>Returns null when the incoming database is safe, otherwise the refusal message.
    /// integrity_check must be the single row ok, and every migration id must be one this
    /// app knows (a backup from a newer app is refused rather than half-migrated).</summary>
    private static string? VerifyIncoming(string incomingPath, IReadOnlyCollection<string> knownMigrationIds)
    {
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = incomingPath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                }.ToString());
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA integrity_check;";
                var verdict = Convert.ToString(cmd.ExecuteScalar() ?? "") ?? "";
                if (!string.Equals(verdict, "ok", StringComparison.OrdinalIgnoreCase))
                    return "The backup database is corrupt (" + verdict + "). Restore not applied. Live data untouched.";
            }
            var hasHistory = Scalar(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'") > 0;
            if (!hasHistory) return null;
            var unknown = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    if (!knownMigrationIds.Contains(id)) unknown.Add(id);
                }
            }
            if (unknown.Count > 0)
                return "This backup was made by a newer version of QSPP Folio. Update the app, then restore again. Live data untouched.";
            return null;
        }
        catch (Exception ex)
        {
            return "Could not verify the backup database: " + ex.Message + " Restore not applied. Live data untouched.";
        }
    }

    private static void CopyStagedAttachments(string stagingDir, string attachmentsDir)
    {
        var stagedAtt = Path.Combine(stagingDir, "attachments");
        if (!Directory.Exists(stagedAtt)) return;
        foreach (var f in Directory.EnumerateFiles(stagedAtt, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(attachmentsDir, Path.GetRelativePath(stagedAtt, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // The store is content-addressed (AttachmentStore names files by their own
            // SHA-256), so identical content is identical bytes; copy only real changes.
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(f).Length)
                File.Copy(f, dest, overwrite: true);
        }
    }

    private static bool SameBytes(string a, string b)
    {
        return System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a)).AsSpan()
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(b)));
    }

    private static long Scalar(Microsoft.Data.Sqlite.SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    /// <summary>Keeps the newest 5 stamp-named directories; older ones are deleted.</summary>
    private static void PruneSafetyCopies(string root)
    {
        if (!Directory.Exists(root)) return;
        var keep = Directory.EnumerateDirectories(root)
            .OrderByDescending(Path.GetFileName)
            .Take(5)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var d in Directory.EnumerateDirectories(root))
            if (!keep.Contains(d))
                try { Directory.Delete(d, recursive: true); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}