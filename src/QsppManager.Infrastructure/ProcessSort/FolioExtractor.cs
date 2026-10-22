using System.Text.Json;
using System.Text.Json.Serialization;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Extracts text (with page references) from manageable local documents.
/// Digital PDFs are parsed with PdfPig where the text is good; images and
/// low-text PDFs are ROUTED to the OCR tier instead of being mis-processed.
/// Results are cached by source sha256 + parser, so unchanged files cost
/// nothing on later runs. No network access happens anywhere in this class.</summary>
public sealed class FolioExtractor(string cacheRoot)
{
    /// <summary>Effectively EMPTY text (a scanned page contributes only OCR noise/zero
    /// characters) routes to the OCR tier. A short-but-real digital PDF (~90 chars) is
    /// perfectly usable — do not over-gate healthy documents into the OCR queue.</summary>
    public const int ScannedTextThresholdCharsPerPage = 30;

    public sealed record Page(int Number, int Chars, string Text);
    public sealed record Extraction(string Path_, string Sha256, string Parser, bool NeedsOcr, bool Ok, string Error, int PageCount, List<Page> Pages)
    {
        [JsonIgnore] public string FullText => string.Join("\n", Pages.Select(p => p.Text));
    }

    public string CachePathFor(string sha256, string parser) =>
        Path.Combine(cacheRoot, $"folio-{sha256[..Math.Min(24, sha256.Length)]}-{parser}.json");

    public bool IsCached(string sha256, string parser) => File.Exists(CachePathFor(sha256, parser));

    /// <summary>Extract (or load from cache) text of a supported file. Returns
    /// NeedsOcr=true for images and scanned PDFs — the caller routes them onward.</summary>
    public Extraction Extract(string fullPath, string sha256, string mimeType, CancellationToken ct = default)
    {
        var lowerExt = Path.GetExtension(fullPath).ToLowerInvariant();
        if (mimeType == "image/png" || mimeType == "image/jpeg" || lowerExt is ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff")
            return RouteToOcr(fullPath, sha256, "image");

        // PDF v1 parser: PdfPig (already a project dependency, no native pieces).
        if (IsCached(sha256, "pdfpig"))
        {
            var cached = JsonSerializer.Deserialize<Extraction>(File.ReadAllText(CachePathFor(sha256, "pdfpig")));
            if (cached != null) return cached;
        }
        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(fullPath);
            var pages = new List<Page>();
            var n = 0;
            foreach (var page in pdf.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                n++;
                var text = (page.Text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
                pages.Add(new Page(n, text.Length, text));
            }
            var meanChars = pages.Count == 0 ? 0 : (int)pages.Average(p => p.Chars);
            Extraction result;
            if (meanChars < ScannedTextThresholdCharsPerPage)
                result = new Extraction(fullPath, sha256, "pdfpig", NeedsOcr: true, Ok: true, Error: "", PageCount: pages.Count,
                    Pages: []); // scanned: discard the useless text layer; OCR tier will produce real text
            else
                result = new Extraction(fullPath, sha256, "pdfpig", NeedsOcr: false, Ok: true, Error: "", PageCount: pages.Count, Pages: pages);

            Directory.CreateDirectory(cacheRoot);
            File.WriteAllText(CachePathFor(sha256, "pdfpig"), JsonSerializer.Serialize(result));
            return result;
        }
        catch (Exception ex)
        {
            return new Extraction(fullPath, sha256, "pdfpig", NeedsOcr: false, Ok: false, Error: $"PDF unreadable: {ex.Message}", PageCount: 0, Pages: []);
        }
    }

    private static Extraction RouteToOcr(string fullPath, string sha256, string kind)
    {
        // OCR runs in stage M8-B (Apple Vision locally, MinerU for tier-2 Macs);
        // recording the route now means later runs never re-parse the file.
        return new Extraction(fullPath, sha256, $"route-{kind}", NeedsOcr: true, Ok: true, Error: "", PageCount: 0, Pages: []);
    }
}