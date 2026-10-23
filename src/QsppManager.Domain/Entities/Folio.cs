using System.Text.Json;

using QsppManager.Domain;

namespace QsppManager.Domain.Entities;

/// <summary>Folio JSON contract: case-insensitive round-trips (the UI dialog and the
/// workspace both serialize; records bind positionally — a camelCase policy on write
/// with a case-insensitive read keeps every Guid intact).</summary>
public static class FolioJson
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>An expiry found IN a document's text during pipeline processing. The date plus
/// the exact source line (hint) so the human can verify the detection before or after
/// approval. Never a stored status — expired/due-soon is computed at display time.</summary>
public sealed record FolioExpiryGuess(DateOnly ExpiresOn, string Hint)
{
    public static readonly FolioExpiryGuess None = new(default, "");
    public static FolioExpiryGuess? FromJson(string json)
    {
        try
        {
            var g = JsonSerializer.Deserialize<FolioExpiryGuess>(json, FolioJson.Opts);
            return g is { } guess && guess.ExpiresOn != default ? guess : null;
        }
        catch { return null; }
    }
    public string ToJson() => ExpiresOn == default ? "{}" : JsonSerializer.Serialize(this, FolioJson.Opts);

    /// <summary>Display status computed LIVE from today's date (stored dates stay honest):
    /// "expired", "due soon" (within 90 days), or "" (current).</summary>
    public string Status(DateOnly today)
    {
        if (ExpiresOn == default) return "";
        if (ExpiresOn < today) return "expired";
        if (ExpiresOn <= today.AddDays(90)) return "due soon";   // inclusive 90-day window
        return "";
    }
}

/// <summary>One "Process and Sort" run: a source directory scanned, classified and staged
/// into a QSPP evidence folio at an output location. The batch is resumable: items persist
/// their state, so an interrupted run picks up where it stopped.</summary>
public class FolioBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceDirectory { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public FolioBatchState State { get; set; } = FolioBatchState.Scanning;

    public int ScannedCount { get; set; }
    public int SkippedCount { get; set; }      // unsupported/oversized/signature-rejected exclusions
    public string ScanErrorsJson { get; set; } = "[]";

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    /// <summary>Processing mode actually used, e.g. "digital-pdf; ocr=pending" —
    /// recorded here and in Settings so the chosen tier is always auditable.</summary>
    public string ModeDescription { get; set; } = "";

    public string SettingsJson { get; set; } = "{}";   // OcrMode, MaxFileMb, concurrency, model ids/versions at run time
    public string ManifestRelativePath { get; set; } = "";

    public List<FolioItem> Items { get; set; } = new();
}

/// <summary>One scanned source file through the pipeline. Never edits the source file;
/// carries proposal + review + publication outcome per item. Unchanged files re-appear
/// on later scans with the same Sha256 and are recognised by it.</summary>
public class FolioItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; }

    public string SourceRelativePath { get; set; } = "";  // relative to the batch source directory
    public string SourceSha256 { get; set; } = "";
    public long FileBytes { get; set; }
    public string MimeType { get; set; } = "";

    /// <summary>Which parser produced the text and its version; "" until extraction happens.</summary>
    public string ParserUsed { get; set; } = "";           // e.g. "pdfpig-1.7", "vision-ocr-3", "mineru-2.0.4"
    public int PageCount { get; set; }
    public string TextCacheRelative { get; set; } = "";   // appdata folio-cache json with per-page text

    public FolioItemState State { get; set; } = FolioItemState.Pending;

    /// <summary>Deterministic (and later model) proposal: [{ref,refKind,score,rationale,model,modelVersion}].</summary>
    public string ProposalsJson { get; set; } = "[]";

    /// <summary>User-corrected/approved destinations: same shape as proposals, but owned by the user.</summary>
    public string ApprovedJson { get; set; } = "[]";

    /// <summary>The managed evidence created at approval (one canonical managed copy).</summary>
    public Guid? EvidenceItemId { get; set; }
    public EvidenceItem? EvidenceItem { get; set; }

    public string DestinationClonesJson { get; set; } = "[]"; // published folio paths for this item
    public string ErrorText { get; set; } = "";
    public string SensitiveReasonsJson { get; set; } = "[]";

    /// <summary>Expiry detected IN THE DOCUMENT TEXT during processing:
    /// {"expiresOn":"2024-03-31","hint":"policy expiry 31 March 2024 (lapsed)"} or "{}".
    /// A detection, never an assertion (the source line rides along so the human can
    /// verify it); copied to EvidenceItem at approval, still editable there. Expired /
    /// due-soon status is COMPUTED for display, never stored — a stored status rots.</summary>
    public string ExpiryJson { get; set; } = "{}";

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}

public enum FolioBatchState
{
    Scanning,       // gathering files (fast, cancellable)
    AwaitingStart,  // scan summary shown; user presses Process and Sort
    Running,
    Cancelling,
    NeedsReview,    // finished processing; review queue has items
    Completed,
    Cancelled,
    Failed,
}

/// <summary>Per-file pipeline outcome. One file is NOT the batch's failure.</summary>
public enum FolioItemState
{
    Pending,          // scanned, waiting to be processed
    Processing,       // extraction/classification in flight
    Proposed,         // classified with a clear single destination
    MultipleCandidates, // close top scores — user chooses (may accept several)
    Abstained,        // no destination above threshold — user chooses
    SensitiveHold,    // likely patient/staff/security content — manual review first
    Duplicate,        // identical sha already managed in the app
    NeedsOcr,         // image or scanned PDF — waits for the local OCR tier (M8-B)
    Unreadable,       // damaged or empty content
    Unsupported,      // kind not supported by local parsers
    Oversized,        // above the configured guard
    SignatureRejected, // extension lied about content (e.g. HTML named .pdf)
    Approved,         // user approved destinations; staged
    Published,        // folio tree written + verified
    ExtractError,
    Cancelled,
}