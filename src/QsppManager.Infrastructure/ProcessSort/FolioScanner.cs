using System.Security.Cryptography;
using QsppManager.Infrastructure.Storage;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Recursive scan of a source directory: discovers supported files, validates
/// signatures against extensions, applies size guards, and hashes each candidate.
/// The scan never opens document content beyond the 10 KB head used for signatures —
/// extraction happens later, per item, so a scan stays fast and cancellable.</summary>
public sealed class FolioScanner(string sourceDirectory, string outputDirectory, long maxFileBytes)
{
    // v1 local parsers: PDFs (digital + scanned) and images. DOCX/other formats join
    // later stages; they are listed honestly as "unsupported (later stage)" — never guessed.
    private static readonly string[] SupportedExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"];

    public sealed record ScanEntry(string SourceRelativePath, string FullPath, long Bytes, string Extension);
    public sealed record SkippedEntry(string SourceRelativePath, string Reason, string Detail);
    public sealed record ScanResult(List<ScanEntry> Files, List<SkippedEntry> Skipped);

    public ScanResult Scan(CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        var files = new List<ScanEntry>();
        var skipped = new List<SkippedEntry>();
        var outFull = Path.GetFullPath(outputDirectory);
        if (string.Equals(Path.GetFullPath(sourceDirectory), outFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and output must be different directories.");

        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(sourceDirectory, "*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try { ScanOne(path, files, skipped, outFull); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { skipped.Add(new SkippedEntry(Path.GetRelativePath(sourceDirectory, path), "scan-error", ex.Message)); }
        }
        return new ScanResult(files, skipped);
    }

    private void ScanOne(string absolutePath, List<ScanEntry> files, List<SkippedEntry> skipped, string outputFullPath)
    {
        var rel = Path.GetRelativePath(sourceDirectory, absolutePath);
        var name = Path.GetFileName(absolutePath);
        if (name.StartsWith("._") || name.StartsWith('.') || name == ".DS_Store") return; // macOS bookkeeping
        var full = Path.GetFullPath(absolutePath);
        if (full.StartsWith(outputFullPath, StringComparison.OrdinalIgnoreCase))
            return; // never process the folio's own output when it lives inside the source

        var ext = Path.GetExtension(name).ToLowerInvariant();
        var fi = new FileInfo(absolutePath);
        if (!fi.Exists) return;
        if (fi.Length > maxFileBytes) { skipped.Add(new SkippedEntry(rel, "oversized", $"File is larger than the {maxFileBytes / (1024 * 1024)} MB guard ({fi.Length / (1024 * 1024)} MB).")); return; }
        if (!SupportedExtensions.Contains(ext)) { skipped.Add(new SkippedEntry(rel, "unsupported", "Only PDF and image files are processed in this stage; DOCX and other formats come later.")); return; }

        byte[] head = new byte[Math.Min(10240, fi.Length)];
        using (var fs = File.OpenRead(absolutePath)) fs.ReadExactly(head);
        var (ok, sigErr, mime) = AttachmentStore.ValidateSignature(name, head);
        if (!ok) { skipped.Add(new SkippedEntry(rel, "signature-rejected", sigErr)); return; }
        if (ext is ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" && mime is not ("image/png" or "image/jpeg"))
        { skipped.Add(new SkippedEntry(rel, "signature-rejected", $"Image file '{name}' has content of type {mime}.")); return; }

        var sha = Sha256File(absolutePath);
        files.Add(new ScanEntry(rel, full, fi.Length, ext));
        // sha is computed per scan; stored below by the workspace when the item row is created.
        _ = sha;
    }

    /// <summary>Streaming sha256 of a possibly large file.</summary>
    public static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}