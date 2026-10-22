using System.Text.Json;
using System.Text.Json.Serialization;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Extracts text (with page references) from manageable local documents.
/// Digital PDFs are parsed with PdfPig where the text is good; images and
/// low-text PDFs are ROUTED to the OCR tier instead of being mis-processed.
/// Results are cached by source sha256 + parser, so unchanged files cost
/// nothing on later runs. No network access happens anywhere in this class.</summary>
public sealed class FolioExtractor(string cacheRoot, FolioOcr? ocr = null)
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

    /// <summary>Extract (or load from cache) text of a supported file. Images and
    /// scanned PDFs go to the local OCR tier when available (cached under
    /// apple-vision-2); otherwise the item stays NeedsOcr — queued honestly.</summary>
    public Extraction Extract(string fullPath, string sha256, string mimeType, CancellationToken ct = default)
    {
        var lowerExt = Path.GetExtension(fullPath).ToLowerInvariant();
        if (mimeType == "image/png" || mimeType == "image/jpeg" || lowerExt is ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff")
            return ExtractViaOcr(fullPath, sha256, ct);

        // PDF v1 parser: PdfPig (already a project dependency, no native pieces).
        if (IsCached(sha256, "pdfpig"))
        {
            var cached = JsonSerializer.Deserialize<Extraction>(File.ReadAllText(CachePathFor(sha256, "pdfpig")));
            if (cached != null && !cached.NeedsOcr) return cached;
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
            var result = meanChars < ScannedTextThresholdCharsPerPage
                ? ExtractViaOcr(fullPath, sha256, ct)   // scanned: the text layer is useless; OCR produces real text
                : new Extraction(fullPath, sha256, "pdfpig", NeedsOcr: false, Ok: true, Error: "", PageCount: pages.Count, Pages: pages);

            Directory.CreateDirectory(cacheRoot);
            File.WriteAllText(CachePathFor(sha256, "pdfpig"), JsonSerializer.Serialize(result));
            return result;
        }
        catch (Exception ex)
        {
            return new Extraction(fullPath, sha256, "pdfpig", NeedsOcr: false, Ok: false, Error: $"PDF unreadable: {ex.Message}", PageCount: 0, Pages: []);
        }
    }

    /// <summary>Local OCR tier: Apple Vision via the packaged qspp-ocr helper.
    /// Cached by sha256 + engine key. On failure the result stays honest
    /// (NeedsOcr with the reason), never a silent mis-file.</summary>
    private Extraction ExtractViaOcr(string fullPath, string sha256, CancellationToken ct)
    {
        if (ocr == null)
            return RouteToOcr(fullPath, sha256, "no-helper",
                "the Apple Vision OCR helper is not installed on this machine");

        if (IsCached(sha256, FolioOcr.EngineKey))
        {
            var cached = JsonSerializer.Deserialize<Extraction>(File.ReadAllText(CachePathFor(sha256, FolioOcr.EngineKey)));
            if (cached != null) return cached;
        }

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var result = ext == ".pdf"
            ? OcrPdf(fullPath, sha256, ct)
            : OcrImage(fullPath, sha256, ct);
        if (result.Ok || result.Error.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            // failures are NOT cached (a later run may retry an install path);
            // a timeout is cached as NeedsOcr so the batch stays resumable.
            Directory.CreateDirectory(cacheRoot);
            File.WriteAllText(CachePathFor(sha256, FolioOcr.EngineKey), JsonSerializer.Serialize(result));
        }
        return result;
    }

    private Extraction OcrImage(string fullPath, string sha256, CancellationToken ct)
    {
        var res = ocr!.Ocr(fullPath, null, 200, ct);
        return res.Ok
            ? new Extraction(fullPath, sha256, FolioOcr.EngineKey, NeedsOcr: false, Ok: true, Error: "", PageCount: 1,
                Pages: res.Pages.Select(p => new Page(p.Page, p.Text.Length, p.Text)).ToList())
            : new Extraction(fullPath, sha256, FolioOcr.EngineKey, NeedsOcr: true, Ok: false, Error: $"OCR: {res.Error}", PageCount: 0, Pages: []);
    }

    private static Extraction ToExtraction(FolioOcr.OcrResult r, string fullPath, string sha256) =>
        new(fullPath, sha256, FolioOcr.EngineKey, NeedsOcr: false, Ok: true, Error: "",
            PageCount: r.PageCount, Pages: r.Pages.Select(p => new Page(p.Page, p.Text.Length, p.Text)).ToList());

    private Extraction OcrPdf(string fullPath, string sha256, CancellationToken ct)
    {
        int? pageCount = null;
        try { using var probe = UglyToad.PdfPig.PdfDocument.Open(fullPath); pageCount = probe.NumberOfPages; }
        catch { /* page count unknown — OCR runs whole-file */ }

        var res = ocr!.Ocr(fullPath, null, 200, ct);
        return res.Ok
            ? (pageCount is { } count && count != res.PageCount
                ? new Extraction(fullPath, sha256, FolioOcr.EngineKey, NeedsOcr: true, Ok: false,
                    Error: $"OCR produced {res.PageCount} pages, PDF has {count}.", count, [])
                : ToExtraction(res, fullPath, sha256))
            : new Extraction(fullPath, sha256, FolioOcr.EngineKey, NeedsOcr: true, Ok: false,
                Error: $"OCR: {res.Error}", PageCount: pageCount ?? 0, Pages: []);
    }

    private static Extraction RouteToOcr(string fullPath, string sha256, string kind, string reason = "")
    {
        // Without the helper the item is queued for the OCR tier honestly, with
        // the reason shown — never mis-filed, never silently dropped.
        return new Extraction(fullPath, sha256, $"route-{kind}", NeedsOcr: true, Ok: true, Error: reason, PageCount: 0, Pages: []);
    }
}