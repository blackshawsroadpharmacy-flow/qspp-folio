namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>An OCR/parse engine the Folio pipeline uses for documents whose text
/// layer is useless (images and scanned pages). Every engine is on-device only:
/// requests go to local processes bound to loopback, and nothing ever leaves this
/// machine (M8 privacy rules). Engines are swapped at runtime through
/// FolioWorkspace (SetOcrEngine / SetHeavyOcr); the extraction cache is keyed by
/// file sha256 + the engine's EngineKeyName, so switching engines never reuses
/// stale cache files.</summary>
public abstract class FolioOcrEngine
{
    /// <summary>Stable engine/version key used in extraction-cache file names and
    /// recorded on each item as its ParserUsed.</summary>
    public abstract string EngineKeyName { get; }

    /// <summary>The honest reason shown when the engine is not usable on this
    /// machine — the item is queued, never mis-filed.</summary>
    public abstract string UnavailableReason { get; }

    /// <summary>OCR/parse for one PDF or a whole image file. Implementations must
    /// bound runtime (wall-clock timeout) and return FolioOcr.OcrResult-shaped
    /// output; dpi is advisory (engines with fixed pipelines may ignore it).</summary>
    public abstract FolioOcr.OcrResult Ocr(string fullPath, int? page, int dpi, CancellationToken ct = default);

    public FolioOcr.OcrResult Unavailable =>
        new(false, UnavailableReason, "", 0, []);
}