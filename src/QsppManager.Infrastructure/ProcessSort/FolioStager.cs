using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Builds the sorted folio tree. Write goes to a staging folder inside the
/// output; files are verified by checksum; only then does the result publish.
/// Multi-destination documents keep ONE canonical managed copy (the imported
/// evidence item) and are placed at every relevant destination through the safest
/// clone the volume supports: APFS clonefile, then a hard link, then a plain copy —
/// the strategy used is recorded per item. Source files are never moved or deleted.</summary>
public sealed class FolioStager(string outputDirectory)
{
    private const string StagingDirName = ".qspp-staging";

    public sealed record DestinationPlan(string Category, string RequirementRef, string FolderName, string FileName);
    public sealed record PublishOutcome(string DestinationPath, string Strategy, string Sha256Verified);

    private static string SafeFolderName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(s.Where(c => !invalid.Contains(c) && c != '"').ToArray());
        return clean.Trim().Length == 0 ? "untitled" : clean.Trim();
    }

    /// <summary>Destination folder for one requirement ref: "REM-05 Strive for 5" or
    /// "Rule 10 Assessment bookings and cancellations". Titles that already begin with
    /// the ref are used as-is (no doubling); others get the ref prefixed.</summary>
    public static DestinationPlan PlanDestination(string category, string ref_, string title, string sourceFileName)
    {
        var t = title.Trim();
        var combined = t.StartsWith(ref_, StringComparison.OrdinalIgnoreCase) ? t : $"{ref_} {t}".Trim();
        if (combined.Length > 64) combined = combined[..64].Trim();
        var folder = SafeFolderName(combined);
        return new DestinationPlan(category, ref_, folder, SafeFolderName(sourceFileName));
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "clonefile")]
    private static extern int CloneFile(string src, string dst, int flags);

    /// <summary>Copies source into staging using the best local clone strategy.
    /// Returns the strategy actually used ("clone", "link", "copy").
    /// The staging copy IS verified afterwards by its sha256.</summary>
    public string PlaceIntoStaging(string stagingDir, string sourceFullPath, string sha256, string fileName, bool sourceIsManagedStore = false)
    {
        Directory.CreateDirectory(stagingDir);
        var dest = Path.Combine(stagingDir, fileName);
        var strategy = "copy";
        if (!File.Exists(dest))
        {
            if (TryCloneFile(sourceFullPath, dest)) strategy = "clone";
            else if (sourceIsManagedStore && TryHardLink(sourceFullPath, dest)) strategy = "link";
            else File.Copy(sourceFullPath, dest, overwrite: false);
        }

        var actual = FolioScanner.Sha256File(dest);
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Checksum mismatch after staging '{fileName}' (expected {sha256[..12]}…, got {actual[..12]}…). Publication aborted.");
        return strategy;
    }

    /// <summary>The canonical managed evidence copy already exists in app data; when a
    /// destination needs the file, cloning FROM the managed copy keeps every placement
    /// bit-identical to the stored evidence.</summary>
    public string PlaceFromManaged(string stagingDir, string managedFullPath, string sha256, string fileName)
        => PlaceIntoStaging(stagingDir, managedFullPath, sha256, fileName, sourceIsManagedStore: true);

    private static bool TryCloneFile(string src, string dst)
    {
        try { return CloneFile(src, dst, 0) == 0; }
        catch { return false; } // no libc on other platforms → fall through
    }

    private static bool TryHardLink(string src, string dst)
    {
        // Hard links only work on the same volume; link(2) fails elsewhere and we copy.
        try { return Link2(src, dst) == 0; }
        catch { return false; }
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "link")]
    private static extern int Link2(string path1, string path2);

    private static bool LinkCore(string src, string dst)
    {
        try { return Link2(src, dst) == 0; }
        catch { return false; }
    }

    /// <summary>Publishes a prepared staging tree into the output directory:
    /// moves each top-level destination folder into place, removes the staging
    /// dir, and writes the folio manifest next to the tree.</summary>
    public void Publish(string stagingDir, string manifestJson)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (var dir in Directory.GetDirectories(stagingDir))
            Directory.Move(dir, Path.Combine(outputDirectory, Path.GetFileName(dir)));
        foreach (var file in Directory.GetFiles(stagingDir))
            File.Move(file, Path.Combine(outputDirectory, Path.GetFileName(file)), overwrite: true);
        var manifestPath = Path.Combine(outputDirectory, "qspp-folio-manifest.json");
        File.WriteAllText(manifestPath, manifestJson);
        Directory.Delete(stagingDir, recursive: true);
    }

    public string StagingDirFor(Guid batchId) => Path.Combine(outputDirectory, StagingDirName, batchId.ToString("N")[..12]);

    /// <summary>Manifest content — the durable record of what was processed and why.</summary>
    public static string BuildManifest(FolioManifestBatch batch) => JsonSerializer.Serialize(batch, ManifestJsonOpts);

    public static readonly JsonSerializerOptions ManifestJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

/// <summary>Manifest root as written to the folio's manifest.json.</summary>
public sealed record FolioManifestBatch(
    string GeneratedAtUtc,
    Guid BatchId,
    string SourceDirectory,
    string OutputDirectory,
    string ModeDescription,
    List<FolioManifestItem> Items);

public sealed record FolioManifestItem(
    string OriginalRelativePath,
    string SourceSha256,
    Guid? ManagedEvidenceId,
    string ParserModel,
    string? ModelVersion,
    string ReviewState,
    List<string> ProposedDestinations,
    List<string> ApprovedDestinations,
    List<string> PublishedPaths,
    List<string> SensitiveReasons,
    string ProcessingError);