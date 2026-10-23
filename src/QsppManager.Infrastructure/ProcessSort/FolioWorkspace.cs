using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Orchestrates "Process and Sort" batches end-to-end: scan → per-file
/// extract/classify → user review → staged publication. Failure ISOLATION is the
/// contract: one unreadable or unsupported file becomes a failed item with a
/// clear reason and never stops the rest of the batch. Every item state persists
/// as it happens, so an interrupted batch resumes exactly where it stopped.
/// Publication reuses the EXISTING evidence pipeline (ImportEvidence + proposed
/// EvidenceLink) — a model or classifier suggestion never manufactures review
/// trust: approval is always a user action.</summary>
public sealed class FolioWorkspace(
    QsppDbContext db,
    QsppWorkspace work,
    AttachmentStore store,
    AuditService audit,
    string folioCacheRoot)
{
    private FolioOcrEngine? ocrEngine = FolioOcr.LocateHelper() is { } helper ? new FolioOcr(helper) : null;
    private FolioExtractor _extractor = null!;
    private FolioDecisionStage2? stage2;

    public void SetOcrEngine(FolioOcr? engine) { ocrEngine = engine; heavyOcrEnabled = false; heavyCliPath = null; _extractor = new FolioExtractor(folioCacheRoot, ocrEngine); }

    /// <summary>The heavy OCR tier (MinerU 4, standard tier, per-batch opt-in) is
    /// enabled/disabled from Settings. When enabled AND the pinned CLI is installed,
    /// scans route their OCR through the LOCAL MinerU doclib server; a server that
    /// is off falls back to Apple Vision per file (never a failed file, never a
    /// mis-file). When disabled, the engine stays Apple Vision. Any missing piece
    /// degrades honestly — see OcrMode.</summary>
    private sealed class HeavyWithVisionFallback(FolioHeavyOcr heavy, FolioOcr? vision) : FolioOcrEngine
    {
        public override string EngineKeyName => heavy.EngineKeyName;
        public override string UnavailableReason => heavy.UnavailableReason;
        public override FolioOcr.OcrResult Ocr(string fullPath, int? page, int dpi, CancellationToken ct = default)
        {
            var heavyRes = heavy.Ocr(fullPath, page, dpi, ct);
            if (heavyRes.Ok) return heavyRes;
            // ONLY "server not running" falls back: a real parse failure must stay
            // honest (queued with its reason) rather than silently re-parsed.
            if (heavyRes.Error.Contains(FolioHeavyOcr.ServerDownMarker, StringComparison.OrdinalIgnoreCase)
                && vision is { } v)
            {
                var vres = v.Ocr(fullPath, page, dpi, ct);
                return vres.Ok ? vres : heavyRes; // both failed → keep the heavy-tier reason visible
            }
            return heavyRes;
        }
    }
    private bool heavyOcrEnabled;
    private string? heavyCliPath;

    /// <summary>Enable the opt-in heavy tier. Pass an explicit CLI path for tests;
    /// null uses the standard discovery (env QSPP_MINERU_CLI → repo venv walk-up).</summary>
    public void SetHeavyOcr(bool enabled, string? cliPath = null)
    {
        heavyOcrEnabled = enabled;
        heavyCliPath = enabled ? (cliPath ?? FolioHeavyOcr.LocateCli()) : null;
        // a CLI path that does not exist degrades quietly to the Vision engine —
        // the heavy tier is opt-in, never load-bearing
        if (heavyCliPath is { } p && !File.Exists(p)) heavyCliPath = null;
        var vision = FolioOcr.LocateHelper() is { } helper ? new FolioOcr(helper) : null;
        ocrEngine = heavyCliPath is { } path
            ? new HeavyWithVisionFallback(new FolioHeavyOcr(path), vision)
            : vision;
        _extractor = new FolioExtractor(folioCacheRoot, ocrEngine);
    }

    public bool HeavyOcrReady => heavyOcrEnabled && heavyCliPath is not null;

    /// <summary>Stage-2 is opt-in wiring: only reachable when a LOCAL loopback
    /// decision server is up. A null or unreachable server leaves stage-1 exact.</summary>
    public void SetStage2(FolioDecisionStage2? client) => stage2 = client;

    /// <summary>What Settings shows the user about the active OCR tier.</summary>
    public string OcrMode => ocrEngine switch
    {
        null => "queued honestly (no OCR engine is installed on this Mac)",
        HeavyWithVisionFallback => "HEAVY tier (mineru-4-standard) opt-in; falls back to Apple Vision when the local server is off — nothing ever leaves this Mac",
        FolioOcr => "on-device, Apple Vision, no download",
        _ => $"on-device engine {ocrEngine.EngineKeyName}",
    };

    /// <summary>What Settings shows about stage-2 (probe is loopback, 1 s).</summary>
    public string Stage2Mode
    {
        get
        {
            if (stage2 == null) return "stage-2 not wired — deterministic stage 1 stands alone.";
            return stage2.Available()
                ? $"optional stage-2 refinement from a LOCAL typed-decision model ({FolioDecisionStage2.ModelName}, confidence-gated, loopback-only)."
                : "stage-2 decision server not running (optional; stage 1 stands alone).";
        }
    }

    private FolioExtractor Extractor => _extractor ??= new FolioExtractor(folioCacheRoot, ocrEngine);

    private const string AuditActor = "folio";

    // ---------- scanning ----------

    public (ServiceResult result, Guid? batchId) ScanAndQueue(UserSession session, string sourceDirectory, string outputDirectory, CancellationToken ct = default)
    {
        if (session == null || session.ReadOnly) return (ServiceResult.Fail("Sign in as Contributor or above to run Process and Sort."), null);
        if (!Directory.Exists(sourceDirectory)) return (ServiceResult.Fail($"Source directory not found: {sourceDirectory}"), null);
        if (!Directory.Exists(outputDirectory))
        {
            try { Directory.CreateDirectory(outputDirectory); }
            catch (Exception ex) { return (ServiceResult.Fail($"Output directory could not be created: {ex.Message}"), null); }
        }
        if (string.Equals(Path.GetFullPath(sourceDirectory), Path.GetFullPath(outputDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and output must be different directories.");

        var stager = new FolioStager(outputDirectory);
        var scanner = new FolioScanner(sourceDirectory, outputDirectory, MaxFileBytes());
        FolioScanner.ScanResult scan;
        try { scan = scanner.Scan(ct); }
        catch (OperationCanceledException) { return (ServiceResult.Fail("Scan cancelled."), null); }
        catch (Exception ex) { return (ServiceResult.Fail($"Scan failed: {ex.Message}"), null); }

        // An interrupted or previous batch for the SAME source dir resumes; a new
        // scan in the same directory supersedes the old running/awaiting batch.
        var existing = db.FolioBatches.Include(b => b.Items)
            .Where(b => b.SourceDirectory == sourceDirectory &&
                        (b.State == FolioBatchState.AwaitingStart || b.State == FolioBatchState.Scanning || b.State == FolioBatchState.Running || b.State == FolioBatchState.Cancelling || b.State == FolioBatchState.Cancelled))
            .ToList();
        FolioBatch batch;
        if (existing.Count > 0)
        {
            batch = existing.OrderByDescending(b => b.StartedAtUtc).First();
            foreach (var old in existing.Where(b => b.Id != batch.Id))
            {
                old.State = FolioBatchState.Cancelled;
                old.FinishedAtUtc = DateTime.UtcNow;
            }
        }
        else
        {
            batch = new FolioBatch
            {
                SourceDirectory = sourceDirectory,
                OutputDirectory = outputDirectory,
                CreatedByUserId = session.UserId,
                SettingsJson = JsonSerializer.Serialize(new
                {
                    maxFileMb = 200,
                    ocrMode = OcrMode,
                    model = FolioClassifier.Stage1Model,
                }, FolioJson.Opts),
            };
            db.FolioBatches.Add(batch);
        }
        batch.State = FolioBatchState.AwaitingStart;
        batch.ScanErrorsJson = JsonSerializer.Serialize(scan.Skipped.Select(s => new { s.SourceRelativePath, s.Reason, s.Detail }).ToList(), FolioJson.Opts);

        var managedSha = db.EvidenceItems.AsNoTracking().Select(e => e.Sha256).ToHashSet();
        var publishedSha = db.FolioItems.AsNoTracking()
            .Where(i => i.State == FolioItemState.Published && i.SourceSha256 != "")
            .Select(i => i.SourceSha256).ToHashSet();

        foreach (var f in scan.Files)
        {
            if (batch.Items.Any(i => i.SourceRelativePath == f.SourceRelativePath)) continue; // resume keeps rows for resumability
            var sha = FolioScanner.Sha256File(f.FullPath);
            var item = new FolioItem
            {
                BatchId = batch.Id,
                SourceRelativePath = f.SourceRelativePath,
                SourceSha256 = sha,
                FileBytes = f.Bytes,
                MimeType = mimeGuess(f.Extension),
                State = publishedSha.Contains(sha) ? FolioItemState.Duplicate : FolioItemState.Pending,
            };
            if (managedSha.Contains(sha) && item.State != FolioItemState.Duplicate) item.State = FolioItemState.Duplicate;
            batch.Items.Add(item);
        }
        batch.ScannedCount = scan.Files.Count(f => true);
        batch.SkippedCount = scan.Skipped.Count;
        batch.ModeDescription = (ocrEngine == null
            ? "digital-pdf: pdfpig; docx: docx-openxml; ocr: queued (helper not installed)"
            : $"digital-pdf: pdfpig; docx: docx-openxml; ocr: {ocrEngine.EngineKeyName} (on-device)")
            + (stage2 != null && stage2.Available()
                ? $"; stage-2: {FolioDecisionStage2.ModelName} (local, loopback)"
                : "; decision model: deterministic-v1 (stage 1 only)");
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "folio.batch.scan", "folio", batch.Id.ToString(),
            $"scanned={batch.ScannedCount}; skipped={batch.SkippedCount}; source={sourceDirectory}");
        return (ServiceResult.Succeed(batch.Id), batch.Id);
    }

    private static string mimeGuess(string ext) => ext switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    private static long MaxFileBytes() => 200L * 1024 * 1024; // guard; Settings override lands with M8-B

    // ---------- processing ----------

    /// <summary>Processes every Pending/failed item one by one. Progress arrives via
    /// callback ON THE CALLING CONTEXT after each item (UI posts to its own thread).
    /// Cancellation between items — never mid-write — keeps items consistent.</summary>
    public (ServiceResult result, int processed) ProcessBatch(Guid batchId, UserSession session, IProgress<(string relPath, string state)>? progress = null, CancellationToken ct = default)
    {
        if (session == null || session.ReadOnly) return (ServiceResult.Fail("Sign in as Contributor or above to run Process and Sort."), 0);
        var batch = db.FolioBatches.Include(b => b.Items).FirstOrDefault(b => b.Id == batchId);
        if (batch == null) return (ServiceResult.Fail("Batch not found."), 0);
        if (batch.State is FolioBatchState.Completed or FolioBatchState.Failed) return (ServiceResult.Fail("This batch is already finished. Scan the directory again for new or changed files."), 0);

        var candidates = LoadCandidates();
        var classifier = new FolioClassifier(candidates);
        batch.State = FolioBatchState.Running;
        db.SaveChanges();

        var processed = 0;
        try
        {
            foreach (var item in batch.Items.Where(i => i.State is FolioItemState.Pending or FolioItemState.Processing or FolioItemState.ExtractError).ToList())
            {
                ct.ThrowIfCancellationRequested();
                ProcessItem(batch, item, classifier, session, candidates);
                processed++;
                progress?.Report((item.SourceRelativePath, item.State.ToString()));
                db.SaveChanges(); // per item: resumability + progress durability
            }
        }
        catch (OperationCanceledException)
        {
            batch.State = FolioBatchState.Cancelled;
            db.SaveChanges();
            return (ServiceResult.Fail("Processing stopped by request. Batch is resumable."), processed);
        }

        var needsReview = batch.Items.Count(i => i.State is FolioItemState.MultipleCandidates or FolioItemState.Abstained or FolioItemState.SensitiveHold or FolioItemState.Duplicate or FolioItemState.NeedsOcr or FolioItemState.Unreadable or FolioItemState.ExtractError);
        batch.State = needsReview > 0 ? FolioBatchState.NeedsReview : FolioBatchState.Completed;
        batch.FinishedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "folio.batch.processed", "folio", batch.Id.ToString(),
            $"processed={processed}; needsReview={needsReview}; complete={batch.State}");
        return (ServiceResult.Succeed(), processed);
    }

    private void ProcessItem(FolioBatch batch, FolioItem item, FolioClassifier classifier, UserSession session, List<FolioCandidate> candidates)
    {
        try
        {
            var fullPath = Path.Combine(batch.SourceDirectory, item.SourceRelativePath);
            if (!File.Exists(fullPath)) { item.State = FolioItemState.ExtractError; item.ErrorText = "Source file is gone — it may have been moved or deleted."; return; }

            item.State = FolioItemState.Processing;
            var extraction = Extractor.Extract(fullPath, item.SourceSha256, item.MimeType);
            item.PageCount = extraction.PageCount;
            item.ParserUsed = extraction.Parser;

            if (!extraction.Ok && !extraction.NeedsOcr) { item.State = FolioItemState.Unreadable; item.ErrorText = extraction.Error; return; }
            if (extraction.NeedsOcr)
            {
                item.State = FolioItemState.NeedsOcr;
                item.ErrorText = extraction.Error.Length > 0
                    ? $"Queued for the local OCR tier: {extraction.Error}"
                    : "Queued for the local OCR tier (scanned or image content).";
                return;
            }

            // Sensitive gate BEFORE proposals: patient/staff/security content waits.
            var text = extraction.FullText;
            var sensitive = SensitiveDetector.Detect(text, Path.GetFileName(item.SourceRelativePath));
            item.SensitiveReasonsJson = JsonSerializer.Serialize(sensitive, FolioJson.Opts);
            var cleanText = text ?? "";
            if (sensitive.Count > 0) { item.State = FolioItemState.SensitiveHold; return; }
            var suggestions = classifier.Classify(cleanText, Path.GetFileName(item.SourceRelativePath));
            var itemState = suggestions.Count == 0 ? FolioItemState.Abstained
                : suggestions[0].Score != 0 && suggestions.Count > 1 && suggestions[1].Score >= suggestions[0].Score * 0.85
                    ? FolioItemState.MultipleCandidates
                    : FolioItemState.Proposed;

            // Stage-2 refinement, strictly optional: a local typed-decision model
            // re-ranks the stage-1 shortlist (choice / multiple / abstain). It can
            // never invent destinations or set review trust; unreachable servers,
            // low confidence and failures leave stage-1 exactly as computed.
            if (stage2 != null && suggestions.Count > 0 &&
                (itemState is FolioItemState.Proposed or FolioItemState.MultipleCandidates))
            {
                try
                {
                    var answer = stage2.Decide(Path.GetFileName(item.SourceRelativePath), cleanText, ClassifierTopFor(suggestions, candidates));
                    if (answer != null)
                    {
                        var (refined, refinedState) = FolioStage2Mapping.Map(suggestions, itemState, answer, candidates);
                        suggestions = refined;
                        itemState = refinedState;
                        audit.Append(AuditActor, null, "folio.item.stage2", "folio", item.Id.ToString(),
                            $"{answer.Identifier}; choice={answer.Choice[..Math.Min(12, answer.Choice.Length)]}; conf={answer.AnswerConfidence:0.00}");
                    }
                }
                catch { /* stage-2 failure must never break the batch */ }
            }

            item.ProposalsJson = JsonSerializer.Serialize(suggestions, FolioJson.Opts);
            item.State = itemState;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            item.State = FolioItemState.ExtractError;
            item.ErrorText = ex.Message;
        }
    }

    private static List<FolioCandidate> ClassifierTopFor(List<FolioSuggestion> suggestions, List<FolioCandidate> all)
    {
        var order = suggestions.Select(s => s.RequirementId).Take(5).ToHashSet();
        return all.Where(c => order.Contains(c.Id)).ToList();
    }

    /// <summary>Candidates come from the LIVE database — wording, examples, REM items as imported.</summary>
    internal List<FolioCandidate> LoadCandidates() =>
        db.Requirements.AsNoTracking()
            .Where(r => !string.IsNullOrEmpty(r.Title))
            .Select(r => new FolioCandidate(r.Id, r.Ref, r.RefKind.ToString(), r.Title, r.PlainSummary,
                db.RequirementEvidenceExamples.Where(x => x.RequirementId == r.Id).Select(x => x.Text).ToList()))
            .ToList();

    /// <summary>Ranks candidates for the approval dialog: the file's own proposal list
    /// first (starred by the dialog), then current surveillance items, then the rest.</summary>
    public List<FolioCandidate> CandidatesForDialog(List<FolioSuggestion>? proposals)
    {
        var all = LoadCandidates();
        if (proposals is not { Count: > 0 }) return all.OrderByDescending(c => c.RefKind == "Surveillance" ? 1 : 0).ThenBy(c => c.Ref).ToList();
        var topIds = proposals.Select(p => p.RequirementId).ToHashSet();
        return all
            .OrderByDescending(c => topIds.Contains(c.Id) ? 2 : c.RefKind == "Surveillance" ? 1 : 0)
            .ThenByDescending(c => proposals.FirstOrDefault(p => p.RequirementId == c.Id)?.Score ?? 0)
            .ThenBy(c => c.Ref)
            .ToList();
    }

    // ---------- review ----------

    /// <summary>User approval: corrects/accepts destinations, then the item is imported
    /// as EXISTING evidence (Draft, patient gate enforced) with PROPOSED links that
    /// still need review — identical to manual attach semantics.</summary>
    public (ServiceResult result, Guid? evidenceId) ApproveItem(Guid itemId, UserSession session, string title, string kind,
        string approvedJson, bool containsPatientData, string patientDataAttestation)
    {
        if (session == null || session.ReadOnly) return (ServiceResult.Fail("Sign in as Contributor or above to approve folio items."), null);
        var item = db.FolioItems.Include(i => i.EvidenceItem).FirstOrDefault(i => i.Id == itemId);
        if (item == null) return (ServiceResult.Fail("Folio item not found."), null);
        var batch = db.FolioBatches.FirstOrDefault(b => b.Id == item.BatchId);
        if (batch == null) return (ServiceResult.Fail("Batch not found."), null);
        var fullPath = Path.Combine(batch.SourceDirectory, item.SourceRelativePath);

        if (item.SourceSha256 == "") item.SourceSha256 = FolioScanner.Sha256File(fullPath);

        var approved = JsonSerializer.Deserialize<List<FolioSuggestion>>(approvedJson, FolioJson.Opts);
        if (approved == null || approved.Count == 0) return (ServiceResult.Fail("Choose at least one destination."), null);

        Guid evidenceId;
        if (item.EvidenceItemId != null && item.EvidenceItem != null)
        {
            evidenceId = item.EvidenceItemId.Value; // duplicate-sha path: link the EXISTING managed copy
        }
        else
        {
            var (result, _) = work.ImportEvidence(fullPath, title, Enum.Parse<EvidenceKind>(kind, true),
                null, null, containsPatientData, patientDataAttestation, session);
            if (!result.Ok) return (result, null);
            var created = db.EvidenceItems.AsNoTracking().OrderByDescending(e => e.CreatedAtUtc).First(e => e.FileName == Path.GetFileName(fullPath) && e.Sha256 == item.SourceSha256);
            evidenceId = created.Id;
            item.EvidenceItemId = evidenceId;
        }

        foreach (var a in approved)
        {
            var lk = work.LinkEvidence(evidenceId, a.RequirementId,
                $"Process and Sort ({FolioClassifier.Stage1Model})", null, null,
                $"Destinations approved with Process and Sort; stage-1 {FolioClassifier.Stage1Model} rationale", session);
            if (!lk.Ok) return (lk, null); // e.g. self-link or invalid requirement — surfaced to the user
        }
        item.ApprovedJson = approvedJson;
        item.State = FolioItemState.Approved;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "folio.item.approved", "folio", item.Id.ToString(),
            $"destinations={string.Join(",", approved.Select(a => a.Ref))}; evidence={evidenceId.ToString()[..8]}…");
        return (ServiceResult.Succeed(evidenceId), evidenceId);
    }

    /// <summary>Evidence kind guessed from the file name for bulk approval — deliberately
    /// conservative: a certificate-shaped name becomes Certificate, training-shaped names
    /// become TrainingRecord, register-shaped names Register, everything else PolicyOrSop.
    /// Any kind is correctable afterwards in the evidence detail (supersede flow).</summary>
    public static string FolioKindHint(string relativePath)
    {
        var name = Path.GetFileName(relativePath).ToLowerInvariant();
        if (name.Contains("indemnity") || name.Contains("insurance") || name.Contains("certificate")
            || name.Contains("ahpra") || name.Contains(" registration")) return Enum.GetName(EvidenceKind.Certificate)!;
        if (name.Contains("training") || name.Contains("induction") || name.Contains("in-service")) return Enum.GetName(EvidenceKind.TrainingRecord)!;
        if (name.Contains("register") || name.Contains("log-") || name.Contains("log.")) return Enum.GetName(EvidenceKind.Register)!;
        return Enum.GetName(EvidenceKind.PolicyOrSop)!;
    }

    /// <summary>Bulk approval of the review queue's CLEAR proposals: every item with a
    /// single confident destination (State = Proposed) becomes managed evidence linked
    /// to its proposed requirement, exactly like a manual attach per item. Multiple-
    /// candidate, sensitive, duplicate and unreadable items always wait for a human
    /// choice. One failed item never stops the bulk; results are counted and the
    /// first failure surfaced.</summary>
    public (int approved, int failed, string firstError, List<Guid> evidenceIds) ApproveProposed(Guid batchId, UserSession session)
    {
        var batch = db.FolioBatches.Include(b => b.Items).FirstOrDefault(b => b.Id == batchId);
        if (batch == null) return (0, 0, "Batch not found.", []);
        if (session == null || session.ReadOnly) return (0, 0, "Sign in as Contributor or above to approve folio items.", []);

        int approved = 0, failed = 0;
        string firstError = "";
        var evidenceIds = new List<Guid>();
        foreach (var item in batch.Items.Where(i => i.State == FolioItemState.Proposed && i.EvidenceItemId == null).ToList())
        {
            try
            {
                var suggestions = JsonSerializer.Deserialize<List<FolioSuggestion>>(item.ProposalsJson, FolioJson.Opts) ?? [];
                if (suggestions.Count == 0)
                {
                    failed++;
                    if (firstError.Length == 0) firstError = $"{item.SourceRelativePath}: no proposal stored.";
                    continue;
                }
                var top = suggestions[0];
                var approvedJson = JsonSerializer.Serialize(new List<FolioSuggestion>
                {
                    new(top.RequirementId, top.Ref, top.RefKind, top.Title, 0, "user-approved (bulk)",
                        FolioClassifier.Stage1Model, FolioClassifier.Stage1ModelVersion)
                }, FolioJson.Opts);
                var (res, evidenceId) = ApproveItem(item.Id, session, DefaultTitleFrom(item.SourceRelativePath),
                    FolioKindHint(item.SourceRelativePath), approvedJson, containsPatientData: false, patientDataAttestation: "");
                if (res.Ok && evidenceId != null) { approved++; evidenceIds.Add(evidenceId.Value); }
                else { failed++; if (firstError.Length == 0) firstError = $"{item.SourceRelativePath}: {res.Error}"; }
            }
            catch (Exception ex)
            {
                failed++;
                if (firstError.Length == 0) firstError = $"{item.SourceRelativePath}: {ex.Message}";
            }
        }
        audit.Append(session.DisplayName, session.UserId, "folio.batch.bulk-approved", "folio", batchId.ToString(),
            $"proposed approved={approved}; failed={failed}; remaining in review={batch.Items.Count(i => i.State != FolioItemState.Approved && i.State != FolioItemState.Published)}");
        return (approved, failed, firstError, evidenceIds);
    }

    /// <summary>Evidence title derived from the file name (shared with the review dialog).</summary>
    public static string DefaultTitleFrom(string relativePath) => Path.GetFileNameWithoutExtension(relativePath);

    /// <summary>Publishes all approved items into the folio tree (staging → verify → publish).</summary>
    public (ServiceResult result, string manifestPath) PublishFolio(Guid batchId, UserSession session)
    {
        if (session == null || session.ReadOnly) return (ServiceResult.Fail("Sign in to publish."), "");
        var batch = db.FolioBatches.Include(b => b.Items).ThenInclude(i => i.EvidenceItem).FirstOrDefault(b => b.Id == batchId);
        if (batch == null) return (ServiceResult.Fail("Batch not found."), "");

        var stager = new FolioStager(batch.OutputDirectory);
        var staging = stager.StagingDirFor(batch.Id);
        Directory.CreateDirectory(staging);
        var published = new List<FolioManifestItem>();
        foreach (var item in batch.Items.Where(i => i.State is FolioItemState.Approved or FolioItemState.Published))
        {
            // Approved or previously published: ensure ALL approved destinations exist.
            var approved = JsonSerializer.Deserialize<List<FolioSuggestion>>(item.ApprovedJson, FolioJson.Opts) ?? [];
            if (item.EvidenceItem == null) continue; // link-only duplicate rows publish via their canonical copy
            var managedAbs = store.AbsolutePath(item.EvidenceItem.StoredRelativePath);
            var strategySeen = "";
            var paths = new List<string>();
            foreach (var a in approved)
            {
                var category = FolioClassifier.CategoryFor(a.RefKind);
                var plan = FolioStager.PlanDestination(category, a.Ref, a.Title, Path.GetFileName(item.SourceRelativePath));
                var dir = Path.Combine(staging, plan.Category, plan.FolderName);
                Directory.CreateDirectory(dir);
                strategySeen = stager.PlaceFromManaged(dir, managedAbs, item.SourceSha256, plan.FileName);
                paths.Add(Path.Combine(category, plan.FolderName, plan.FileName));
            }
            item.DestinationClonesJson = JsonSerializer.Serialize(new { strategy = strategySeen, paths }, FolioJson.Opts);
            item.PublishedAtUtc = DateTime.UtcNow;
            item.State = FolioItemState.Published;
            published.Add(new FolioManifestItem(item.SourceRelativePath, item.SourceSha256, item.EvidenceItemId,
                item.ParserUsed, FolioClassifier.Stage1ModelVersion, item.State.ToString(),
                PropsOf(item.ProposalsJson), approved.Select(a => a.Ref).ToList(), paths,
                JsonSerializer.Deserialize<List<string>>(item.SensitiveReasonsJson, FolioJson.Opts) ?? [], item.ErrorText));
        }

        var manifest = new FolioManifestBatch(DateTime.UtcNow.ToString("o"), batch.Id, batch.SourceDirectory, batch.OutputDirectory, batch.ModeDescription, published);
        var manifestJson = FolioStager.BuildManifest(manifest);
        stager.Publish(staging, manifestJson);
        batch.State = FolioBatchState.Completed;
        batch.FinishedAtUtc = DateTime.UtcNow;
        batch.ManifestRelativePath = "qspp-folio-manifest.json";
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "folio.published", "folio", batch.Id.ToString(), $"{published.Count} items published to {batch.OutputDirectory}");
        return (ServiceResult.Succeed(), Path.Combine(batch.OutputDirectory, "qspp-folio-manifest.json"));
    }

    private static List<string> PropsOf(string proposalsJson) =>
        JsonSerializer.Deserialize<List<FolioSuggestion>>(proposalsJson, FolioJson.Opts)?
            .Select(p => p.Ref).ToList() ?? [];

    public void CancelBatch(Guid batchId, UserSession session)
    {
        var batch = db.FolioBatches.FirstOrDefault(b => b.Id == batchId);
        if (batch == null || batch.State is FolioBatchState.Completed or FolioBatchState.Failed) return;
        batch.State = FolioBatchState.Cancelling;
        db.SaveChanges();
    }
}