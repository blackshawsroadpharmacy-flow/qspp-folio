using System.Security.Cryptography;

namespace QsppManager.Infrastructure.Storage;

/// <summary>Managed attachment storage: files are copied into the application data
/// directory, identified by content hash. Deleting an original never breaks records.</summary>
public class AttachmentStore(string rootDirectory)
{
    public string Root { get; } = rootDirectory;

    public async Task<(string relativePath, string sha256, long bytes)> StoreAsync(
        string sourcePath, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(sourcePath, ct);
        return StoreBytes(Path.GetFileName(sourcePath), bytes);
    }

    public (string relativePath, string sha256, long bytes) StoreBytes(string fileName, byte[] bytes)
    {
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var ext = Path.GetExtension(fileName);
        var rel = Path.Combine(sha[..2], sha[2..4], sha + (ext.Length <= 10 ? ext : ".bin"));
        var abs = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        if (!File.Exists(abs))
            File.WriteAllBytes(abs, bytes);   // deterministic content-addressed path
        return (rel, sha, bytes.Length);
    }

    public string AbsolutePath(string relativePath) => Path.Combine(Root, relativePath);

    public bool ExistsSha(string relativePath) => File.Exists(Path.Combine(Root, relativePath));

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Validates document signatures. The capacity-toolkit.html-wall class of
    /// import must fail loudly (imports must verify signatures — owner brief §1.4).</summary>
    public static (bool ok, string error, string mime) ValidateSignature(string fileName, byte[] head10k)
    {
        var head = head10k.AsSpan(0, Math.Min(head10k.Length, 10));
        if (head.Length < 4) return (false, "File is too small to inspect.", "application/octet-stream");
        var lower = Path.GetFileName(fileName).ToLowerInvariant();
        string? mime = null;

        // Binary signatures as NUMBERS, not u8 string literals: "\x89PNG"u8 encodes the
        // U+0089 code point as C2 89 (UTF-8) and never matches a real PNG's raw 0x89 —
        // a latent M1 bug surfaced by the folio image guard (fixed 22 Oct 2026).
        if (head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46 && head[4] == 0x2D) mime = "application/pdf"; // %PDF-
        else if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) mime = "image/png";
        else if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xD8) mime = "image/jpeg";
        else if (head[0] == 0x50 && head[1] == 0x4B && head[2] == 0x03 && head[3] == 0x04) mime = lower.EndsWith(".docx") ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                                                             : lower.EndsWith(".xlsx") ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                                                             : "application/zip";
        else if (head.Length > 5 && System.Text.Encoding.ASCII.GetString(head).Contains("<html", StringComparison.OrdinalIgnoreCase)) mime = "text/html";
        else mime = "application/octet-stream";

        // The signature must match the extension for PDFs — an HTML wall named .pdf
        // (like capacity-toolkit.pdf in the reference pack) is rejected outright.
        if (lower.EndsWith(".pdf") && mime != "application/pdf")
            return (false, $"File '{Path.GetFileName(fileName)}' is not a real PDF (signature: {mime}). It was excluded from import.", mime);
        if (mime == "text/html")
            return (false, $"File '{Path.GetFileName(fileName)}' is HTML, not a supported document type. It was excluded from import.", mime);
        return (true, "", mime);
    }
}