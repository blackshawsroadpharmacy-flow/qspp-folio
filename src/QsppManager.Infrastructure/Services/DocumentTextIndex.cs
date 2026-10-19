using System.Text;
using QsppManager.Application;
using QsppManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Storage;
using UglyToad.PdfPig;

namespace QsppManager.Infrastructure.Services;

/// <summary>Extracts full text from managed source PDFs (PdfPig, no native dependencies)
/// and feeds it to the FTS index, so Ctrl+K finds content INSIDE the Standard, UAG, rules
/// and policies — not just their titles. Extracted text is cached by sha256 in appdata;
/// reboots re-index cached docs without re-parsing the PDFs.</summary>
public class DocumentTextIndex(QsppDbContext db, AttachmentStore store, ICorpusIndexer indexer, string cacheRoot)
{
    /// <summary>digit↔letter and camel-hump boundaries get a space so the Unicode61 tokenizer
    /// yields clean word tokens (see Extract()).</summary>
    private static readonly System.Text.RegularExpressions.Regex TokenBoundaryRegex = new(
        "(?<=[0-9])(?=[A-Za-z])|(?<=[A-Za-z])(?=[0-9])|(?<=[a-z])(?=[A-Z])",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private string CachePathFor(string sha256Hex) =>
        Path.Combine(cacheRoot, $"doctext-{sha256Hex[..Math.Min(24, sha256Hex.Length)]}.txt");

    /// <summary>Read access for helper services (SourceCompare) that need the cache dir.</summary>
    public string CacheRootSnapshot => cacheRoot;

    public bool IsCached(string sha256Hex) => sha256Hex.Length > 0 && File.Exists(CachePathFor(sha256Hex));

    public (bool ok, int pages, string error) Extract(Guid documentId)
    {
        var d = db.SourceDocuments.AsNoTracking().FirstOrDefault(x => x.Id == documentId);
        if (d == null) return (false, 0, "source document not found");
        if (string.Equals(Path.GetExtension(d.FileName), ".png", StringComparison.OrdinalIgnoreCase))
            return (false, 0, "image files carry no searchable text (Decision-tree.png)");
        var abs = store.AbsolutePath(d.StoredRelativePath);
        if (!File.Exists(abs)) return (false, 0, $"managed file missing: {d.StoredRelativePath}");

        try
        {
            using var pdf = PdfDocument.Open(abs);
            var pages = pdf.NumberOfPages;
            var sb = new StringBuilder();
            sb.Append(d.DisplayName).Append(' ').Append(d.FileName).Append(' ')
              .Append(d.Role).Append(' ').Append(d.SourceVersion).Append(' ').AppendLine(d.ProvenanceNote);
            foreach (var page in pdf.GetPages())
            {
                var pageText = page.Text;
                if (!string.IsNullOrWhiteSpace(pageText)) sb.AppendLine(pageText);
            }
            // PdfPig often glues adjacent extraction lines ("1.1.1Heading"), so a heading
            // token becomes "3heading" and prefix search can never find it. Split every
            // digit↔letter and lowercase↔uppercase boundary before indexing.
            var text = TokenBoundaryRegex.Replace(sb.ToString(), " ");
            Directory.CreateDirectory(cacheRoot);
            File.WriteAllText(CachePathFor(d.Sha256Hex), text);
            indexer.IndexDocumentText(d.Id, d.DisplayName, text);
            return (true, pages, "");
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    /// <summary>Re-indexes every document that already has an extraction cache (boot path).</summary>
    public int ReindexCached()
    {
        var n = 0;
        foreach (var d in db.SourceDocuments.AsNoTracking())
        {
            if (!IsCached(d.Sha256Hex)) continue;
            var text = File.ReadAllText(CachePathFor(d.Sha256Hex));
            indexer.IndexDocumentText(d.Id, d.DisplayName, text);
            n++;
        }
        return n;
    }
}