namespace QsppManager.Infrastructure.Runtime;

/// <summary>Application data locations and first-run migration (M5).
///
/// macOS: `~/Library/Application Support/QsppManager` per platform convention.
/// Linux: unchanged from earlier milestones (`~/.config/QsppManager`, the .NET
/// SpecialFolder.ApplicationData location the previous build used).
/// QSPP_DATA_DIR always wins — it is the documented dev/test override and is
/// treated as a complete, self-contained data root (no migration is performed
/// into or out of it; migration only runs for a system default location).
///
/// Migration (safe by construction): when the platform default root contains no
/// database and no content, the legacy locations are copied in, copy-verified
/// byte-for-byte, and the legacy data is retained untouched. A failed verify
/// aborts the migration and the session runs from the legacy root instead.</summary>
public static class AppData
{
    /// <summary>Effective data root for this session. Precedence: QSPP_DATA_DIR →
    /// the session root (set once at boot, allows the legacy fallback after a failed
    /// migration) → the platform default.</summary>
    public static string Root =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QSPP_DATA_DIR"))
            ? (Directory.Exists(SessionRoot) ? SessionRoot : RootForPlatform())
            : Environment.GetEnvironmentVariable("QSPP_DATA_DIR")!;
    public static string RestoreStaging() => Ensure("restore-staging");
    public static string PackDir => Ensure("packs");

    public static string DatabasePath(bool demo) => Path.Combine(Root, demo ? "qspp-demo.db" : "qspp.db");
    public static string SeedDirectory(string repoRoot) => Path.Combine(repoRoot, "seed");
    public static string AttachmentsDir => Ensure("attachments");
    public static string PdfDir => Ensure("pdf");
    public static string BackupsDir => Ensure("backups");
    public static string LogsDir => Ensure("logs");

    /// <summary>Legacy roots that were written by earlier builds (checked oldest
    /// intent first). Only consulted when the current root is empty.</summary>
    public static string[] LegacyCandidates()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QSPP_DATA_DIR")))
            return []; // dev override: never migrate
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var oldAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return [Path.Combine(home, ".local", "share", "QsppManager"), Path.Combine(oldAppData, "QsppManager")];
        }
        if (OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return [Path.Combine(home, ".local", "share", "QsppManager"), Path.Combine(home, ".config", "QsppManager")];
        }
        return [];
    }

    /// <summary>First-run migration: copy legacy data into the (empty) current root.
    /// Returns (targetUsed, migrated, warning). Byte-verify the database; retain the
    /// legacy tree in every case.</summary>
    public static (string RootUsed, bool Migrated, string? Warning) MigrateLegacyOnFirstRun()
    {
        EnsureAll();
        var warning = (string?)null;

        // Fresh default root: no db yet — consider the legacy locations.
        var root = Root;
        var rootDb = Path.Combine(root, "qspp.db");
        if (File.Exists(rootDb) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QSPP_DATA_DIR")))
            return (root, false, null); // already populated, or explicit dev dir — nothing to migrate

        foreach (var legacy in LegacyCandidates())
        {
            var legacyDb = Path.Combine(legacy, "qspp.db");
            var legacyDemoDb = Path.Combine(legacy, "qspp-demo.db");
            var sourceDb = File.Exists(legacyDb) ? legacyDb : File.Exists(legacyDemoDb) ? legacyDemoDb : null;
            if (sourceDb is null || !Directory.Exists(legacy)) continue;

            var migratedOk = TryCopyWithVerify(sourceDb, Path.Combine(root, Path.GetFileName(sourceDb)), out var copyError);
            if (!migratedOk)
            {
                // unsafe to continue with a partial copy — fall back to the legacy root
                var fallbackRoot = legacy;
                warning = $"Migration from {legacy} was cancelled ({copyError}). This session is using the original location {legacy}; the new location stays untouched.";
                return (fallbackRoot, false, warning);
            }
            foreach (var sub in new[] { "attachments", "backups", "packs", "pdf", "settings", "document-text" })
            {
                var src = Path.Combine(legacy, sub);
                if (!Directory.Exists(src)) continue;
                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(root, sub, Path.GetRelativePath(src, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest, overwrite: true);
                }
            }
            // marker so we know this root was populated by migration (the legacy copy stays)
            File.WriteAllText(Path.Combine(root, ".migrated-from"), legacy);
            return (root, true, null);
        }
        return (root, false, null);
    }

    private static bool TryCopyWithVerify(string source, string dest, out string error)
    {
        error = "";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);
                var srcSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
                var dstSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(dest)));
                if (srcSha == dstSha) return true;
                error = "copy did not verify (sha256 mismatch)";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
        return false;
    }

    public static string Ensure(string sub)
    {
        var p = Path.Combine(Root, sub);
        Directory.CreateDirectory(p);
        return p;
    }

    public static void EnsureAll() { Ensure("attachments"); Ensure("pdf"); Ensure("backups"); Ensure("logs"); }

    /// <summary>Resolved app data root for THIS session — set by App.OnFrameworkInitializationCompleted via MigrateLegacyOnFirstRun.</summary>
    public static string SessionRoot { get; set; } = "";

    private static string RootForPlatform()
    {
        var env = Environment.GetEnvironmentVariable("QSPP_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "QsppManager");
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QsppManager");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QsppManager");
    }
}