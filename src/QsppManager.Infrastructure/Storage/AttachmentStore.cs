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
        var lower = Path.GetFileName(fileName).ToLowerInvariant();
        string? mime = null;

        if (head.StartsWith("%PDF-"u8)) mime = "application/pdf";
        else if (head.StartsWith("\x89PNG"u8)) mime = "image/png";
        else if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xD8) mime = "image/jpeg";
        else if (head.StartsWith("PK\x03\x04"u8)) mime = lower.EndsWith(".docx") ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
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