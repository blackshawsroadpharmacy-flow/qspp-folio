using System.Text;
using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.ProcessSort;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using QsppManager.Infrastructure.Printing;
using QuestPDF.Fluent;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M8 "Process and Sort": hermetic tests of the local doc-pipeline core —
/// scan guards (signatures, sizes, recursion), PdfPig extraction with scanned-PDF
/// routing, deterministic classification against the LIVE requirement list, failure
/// isolation, resumability, duplicates via sha256, sensitive gate, transactional
/// staging→publish with the clone strategy, and the manifest. Fixtures are synthetic
/// PDFs (QuestPDF, digital) plus byte-level fakes that must be REJECTED (HTML named
/// .pdf, truncated PDFs). OCR/model tiers (M8-B/C) route correctly by assertion here
/// and are exercised for real once the workers land.</summary>
public class ProcessSortPipelineTests : TestBase
{
    private readonly string _srcDir;
    private readonly string _outDir;
    private readonly string _cacheDir;
    private readonly FolioWorkspace _folio;

    public ProcessSortPipelineTests()
    {
        _srcDir = Path.Combine(Path.GetTempPath(), $"qspp-src-{Guid.NewGuid():N}");
        _outDir = Path.Combine(Path.GetTempPath(), $"qspp-out-{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(), $"qspp-foliotext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_srcDir);
        Directory.CreateDirectory(_cacheDir);
        var store = new AttachmentStore(AttachmentRoot);   // SAME root the TestBase workspace writes evidence into
        _folio = new FolioWorkspace(Db, Workspace, store, Audit, _cacheDir);
    }

    public override void Dispose()
    {
        try { Directory.Delete(_srcDir, true); Directory.Delete(_outDir, true); Directory.Delete(_cacheDir, true); } catch { }
        base.Dispose();
    }

    /// <summary>QuestPDF Community licences this repo (DD-9) — used here only to build test fixtures.</summary>
    private static void MakePdf(string path, string text)
    {
        PdfComposer.ConfigureLicense();
        Document.Create(container => container.Page(page =>
        {
            page.Margin(28);
            page.Content().Text(text);
        })).GeneratePdf(path);
    }

    private static System.Text.Json.JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static List<FolioSuggestion> Props(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<List<FolioSuggestion>>(json, Json) ?? [];

    private (Guid batch, Guid item) RunPipeline(string docRelName, string docText)
    {
        MakePdf(Path.Combine(_srcDir, docRelName), docText);
        var (res, batchId) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        var (pres, processed) = _folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok, pres.Error);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        return (batch, item.Id);
    }

    [Fact]
    public void Scan_RejectsLyingExtensions_AndCountsRecursively()
    {
        MakePdf(Path.Combine(_srcDir, "ok.pdf"), "Assessment bookings and cancellations content");
        Directory.CreateDirectory(Path.Combine(_srcDir, "sub"));
        MakePdf(Path.Combine(_srcDir, "sub", "nested.pdf"), "more content");
        File.WriteAllText(Path.Combine(_srcDir, "fake.html"), "<html>wall</html>");
        File.WriteAllText(Path.Combine(_srcDir, "notes.docx-placeholder"), "text only, will be renamed");
        File.Move(Path.Combine(_srcDir, "notes.docx-placeholder"), Path.Combine(_srcDir, "notes.doc"));
        var html = File.ReadAllText(Path.Combine(_srcDir, "fake.html"));
        File.WriteAllText(Path.Combine(_srcDir, "lied.pdf"), html);

        var scanner = new FolioScanner(_srcDir, _outDir, 200L * 1024 * 1024);
        var r = scanner.Scan();
        Assert.Equal(2, r.Files.Count);
        // the .doc unsupported note + the HTML-named .pdf lie are in skipped, with reasons
        Assert.Contains(r.Skipped, s => s.Reason == "signature-rejected" && s.SourceRelativePath == "lied.pdf");
        Assert.Contains(r.Skipped, s => s.Reason == "unsupported" && s.SourceRelativePath.EndsWith(".doc"));
    }

    [Fact]
    public void Scan_OversizedFiles_AreSkippedWithReason()
    {
        MakePdf(Path.Combine(_srcDir, "ok.pdf"), "fine");
        // tight guard: 1 KB — the good PDF (~2 KB) is skipped as oversized
        var scanner = new FolioScanner(_srcDir, _outDir, maxFileBytes: 1024);
        var r = scanner.Scan();
        Assert.Empty(r.Files);
        Assert.Contains(r.Skipped, s => s.Reason == "oversized");
    }

    [Fact]
    public void DigitalPdf_ProposesCorrectRequirement_PublishesWithVerifiedClones()
    {
        var (batch, item) = RunPipeline("bookings-sop.pdf",
            "Assessment bookings and cancellations. Our pharmacy records bookings and cancellations " +
            "within the required windows. Cancellation windows apply to booked assessments, and the " +
            "booking register lists every cancellation with its notice period.");

        var itemRow = Db.FolioItems.AsNoTracking().First(i => i.Id == item);
        Assert.True(itemRow.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates, $"got {itemRow.State}: {itemRow.ErrorText}");
        var props = Props(itemRow.ProposalsJson);
        // BOTH Rule 10 (bookings) and Rule 8 (mid-cycle) genuinely appear. The doc quotes
        // the Rule-10 title verbatim, so Rule 10 must outscore Rule 8's incidental words.
        Assert.True(props.Count > 0 && props[0].Ref == "Rule 10", $"props: {string.Join(" | ", props.Select(p => p.Ref + "=" + p.Score))}");
        var top = props[0];
        Assert.Equal(FolioClassifier.Stage1Model, top.Model);

        var rule10Id = Db.Requirements.AsNoTracking().First(r => r.Ref == "Rule 10").Id;
        var approvedJson = System.Text.Json.JsonSerializer.Serialize(new List<FolioSuggestion>
        {
            new(rule10Id, "Rule 10", "Rule", "Assessment bookings and cancellations", 0, "user-approved",
                FolioClassifier.Stage1Model, FolioClassifier.Stage1ModelVersion)
        });
        var (appr, evidenceId) = _folio.ApproveItem(item, Owner, "Bookings SOP", "PolicyOrSop", approvedJson, false, "");
        Assert.True(appr.Ok, appr.Error);

        var (pub, manifest) = _folio.PublishFolio(batch, Owner);
        Assert.True(pub.Ok, pub.Error);
        Assert.True(File.Exists(manifest));

        // folio tree exists with verified content; source file untouched
        var catDir = Path.Combine(_outDir, "Program rules");
        Assert.True(Directory.Exists(catDir), $"folio category missing: {catDir}");
        var reqDir = Directory.GetDirectories(catDir).FirstOrDefault(d => Path.GetFileName(d).StartsWith("Rule 10"));
        Assert.NotNull(reqDir); // e.g. "Rule 10 Assessment bookings and cancellations"
        var publishedCopy = Path.Combine(reqDir, "bookings-sop.pdf");
        Assert.True(File.Exists(publishedCopy), $"published copy missing under {reqDir}");
        var sourceAfter = File.ReadAllBytes(Path.Combine(_srcDir, "bookings-sop.pdf"));
        Assert.True(sourceAfter.Length > 0); // source untouched

        // link went through the NORMAL proposed path — nothing pretends to be verified
        var link = Db.EvidenceLinks.AsNoTracking().First(l => l.EvidenceItemId == evidenceId);
        Assert.Equal(LinkState.Proposed, link.State);
        Assert.Equal(evidenceId, Db.FolioItems.AsNoTracking().First(i => i.Id == item).EvidenceItemId);

        // audit lines exist for the batch and the approval
        Assert.Contains(Db.AuditEvents.AsEnumerable(), a => a.Action == "folio.item.approved");

        // manifest records origin, sha, model, destinations, review state
        var manifestText = File.ReadAllText(manifest);
        Assert.Contains("\"originalRelativePath\"", manifestText);
        Assert.Contains("bookings-sop.pdf", manifestText);
        Assert.Contains(FolioClassifier.Stage1Model, manifestText);
    }

    [Fact]
    public void CorruptPdf_IsolatesAsFailedItem_BatchStillSucceeds()
    {
        File.WriteAllText(Path.Combine(_srcDir, "broken.pdf"), "%PDF-1.4 truncated garbage without xref");
        MakePdf(Path.Combine(_srcDir, "good.pdf"), "Assessment bookings and cancellations content. Cancellation windows apply.");
        var (res, batchId0) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        var batch = Assert.IsType<Guid>(batchId0);
        var (pres, _) = _folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok || pres.Error.Contains("review"), pres.Error);
        var items = Db.FolioItems.AsNoTracking().Where(i => i.BatchId == batch).ToList();
        var bad = items.First(i => i.SourceRelativePath == "broken.pdf");
        var good = items.First(i => i.SourceRelativePath == "good.pdf");
        Assert.True(bad.State == FolioItemState.Unreadable || bad.State == FolioItemState.ExtractError,
            $"bad state {bad.State}: {bad.ErrorText}");
        Assert.DoesNotContain(good.State.ToString(), new[] { FolioItemState.Unreadable.ToString(), FolioItemState.ExtractError.ToString() });
        Assert.True(good.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates, $"good state {good.State}: {good.ErrorText}");
    }

    [Fact]
    public void ImageFiles_RouteToOcrQueue_NeverMisFiled()
    {
        // smallest valid PNG (1x1 transparent) — signature-legal, content-empty
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(Path.Combine(_srcDir, "scan-of-register.png"), png);
        var (res, batchId0) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var direct = new FolioScanner(_srcDir, _outDir, 200L * 1024 * 1024).Scan();
        Assert.True(direct.Files.Count == 1, $"direct scan: files={direct.Files.Count}; skipped: {string.Join(";", direct.Skipped.Select(s => s.Reason + ":" + s.SourceRelativePath + " [" + s.Detail + "]"))}");
        var batch = Assert.IsType<Guid>(batchId0);
        var queued = Db.FolioItems.Count(i => i.BatchId == batch);
        Assert.True(queued == 1, $"scan queued {queued} items for the png - the scanner lost it");
        _folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().FirstOrDefault(i => i.BatchId == batch && i.SourceRelativePath == "scan-of-register.png");
        Assert.NotNull(item); // the scan MUST have queued the image — silence here means a lost file
        Assert.Equal(FolioItemState.NeedsOcr, item!.State);
        Assert.Contains("OCR", item.ErrorText);
    }

    [Fact]
    public void SensitiveFile_WaitsForReview_AndRequiresAttestation()
    {
        MakePdf(Path.Combine(_srcDir, "medicare-batch-report.pdf"),
            "Medicare card numbers recorded for this patient: 4111 1111 1 plus confidential clinical notes.");
        var (res, batchId0) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        var batch = Assert.IsType<Guid>(batchId0);
        _folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.Equal(FolioItemState.SensitiveHold, item.State);
        Assert.Contains(Db.FolioItems.AsNoTracking().Where(i => i.BatchId == batch), i =>
            System.Text.Json.JsonSerializer.Deserialize<List<string>>(i.SensitiveReasonsJson)!.Count > 0);

        // approval must carry the attestation — the patient gate applies here too
        var rule10 = Db.Requirements.AsNoTracking().First(r => r.Ref == "Rule 10").Id;
        var approvedJson = System.Text.Json.JsonSerializer.Serialize(new List<FolioSuggestion>
        { new(rule10, "Rule 10", "Rule", "Assessment bookings and cancellations", 0, "user-approved", "deterministic-v1", "1") });
        var (denied, _) = _folio.ApproveItem(item.Id, Owner, "Medicare batch", "Record", approvedJson, true, "");
        Assert.False(denied.Ok);
        var (ok, evid) = _folio.ApproveItem(item.Id, Owner, "Medicare batch", "Record",
            System.Text.Json.JsonSerializer.Serialize(new List<FolioSuggestion>
            { new(rule10, "Rule 10", "Rule", "Assessment bookings and cancellations", 0, "user-approved", "deterministic-v1", "1") }),
            true, "Patient identifiers blacked out; reviewed by the owner before filing");
        Assert.True(ok.Ok, ok.Error);
        // attestation survives on the evidence (existing gate, unchanged)
        var ev = Db.EvidenceItems.AsNoTracking().First(e => e.Id == evid);
        Assert.True(ev.ContainsPatientData);
    }

    [Fact]
    public void Rerun_RecognisesUnchangedFiles_NoDuplicateEvidence()
    {
        MakePdf(Path.Combine(_srcDir, "bookings-sop.pdf"), "Assessment bookings and cancellations. Cancellation windows apply to booked assessments.");
        var (res1, b1) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        var batch1 = Assert.IsType<Guid>(b1);
        _folio.ProcessBatch(batch1, Owner);
        var item1 = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch1);
        var rule10 = Db.Requirements.AsNoTracking().First(r => r.Ref == "Rule 10").Id;
        var approvedJson = System.Text.Json.JsonSerializer.Serialize(new List<FolioSuggestion>
        { new(rule10, "Rule 10", "Rule", "Assessment bookings and cancellations", 0, "user-approved", "deterministic-v1", "1") });
        _folio.ApproveItem(item1.Id, Owner, "Bookings SOP", "PolicyOrSop", approvedJson, false, "");
        _folio.PublishFolio(batch1, Owner);
        var evidenceAfterFirst = Db.EvidenceItems.Count();

        // later run: same directory, file unchanged → recognised by sha, processed no further
        var (res2, b2) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        var batch2 = Assert.IsType<Guid>(b2);
        Assert.NotEqual(batch1, batch2);
        var item2 = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch2);
        Assert.Equal(FolioItemState.Duplicate, item2.State);
        _folio.ProcessBatch(batch2, Owner);
        Assert.Equal(evidenceAfterFirst, Db.EvidenceItems.Count()); // nothing re-imported
    }

    [Fact]
    public void Interruption_IsResumable_ContinuesWhereItStopped()
    {
        MakePdf(Path.Combine(_srcDir, "a.pdf"), "Assessment bookings and cancellations. Cancellation windows apply.");
        MakePdf(Path.Combine(_srcDir, "b.pdf"), "Unrelated quarterly weather summary with no requirement wording at all.");
        var (res, b0) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        var batch = Assert.IsType<Guid>(b0);
        using var cancelNow = new System.Threading.CancellationTokenSource();
        cancelNow.Cancel(); // simulate a stop before anything was processed
        var (stopped, _) = _folio.ProcessBatch(batch, Owner, null, cancelNow.Token);
        Assert.True(stopped.Error.Contains("resumable"), stopped.Error);
        Assert.Equal(FolioBatchState.Cancelled, Db.FolioBatches.AsNoTracking().First(x => x.Id == batch).State);

        // resume: same batch id finishes the remaining work
        var (resumed, processed) = _folio.ProcessBatch(batch, Owner);
        Assert.True(resumed.Ok, resumed.Error);
        Assert.Equal(2, processed);
        var final = Db.FolioBatches.AsNoTracking().First(x => x.Id == batch);
        Assert.True(final.State is FolioBatchState.NeedsReview or FolioBatchState.Completed, final.State.ToString());
    }

    [Fact]
    public void StagingDetectsChecksumMismatch_AndAbortsThatItem()
    {
        var a = Path.Combine(Path.GetTempPath(), $"qf-a-{Guid.NewGuid():N}.pdf");
        var b = Path.Combine(Path.GetTempPath(), $"qf-b-{Guid.NewGuid():N}.pdf");
        MakePdf(a, "AAA");
        MakePdf(b, "BBB");
        var stager = new FolioStager(_outDir);
        var staging = stager.StagingDirFor(Guid.NewGuid());
        var shaOfB = FolioScanner.Sha256File(b);
        // claim the copy came from B while the content comes from A — the checksum must blow up
        try
        {
            stager.PlaceIntoStaging(staging, a, shaOfB, "fake.pdf");
            Assert.Fail("Expected checksum mismatch");
        }
        catch (IOException ex)
        {
            Assert.Contains("mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(staging, true); File.Delete(a); File.Delete(b); } catch { }
        }
    }

    [Fact]
    public void CloneStrategy_ProducesVerifiedCopies_SourceUntouched()
    {
        var a = Path.Combine(Path.GetTempPath(), $"qf-clone-{Guid.NewGuid():N}.pdf");
        MakePdf(a, "clone me: assessment bookings and cancellations");
        var stager = new FolioStager(_outDir);
        var staging = Path.Combine(_outDir, ".qspp-staging", Guid.NewGuid().ToString("N")[..12]);
        var sha = FolioScanner.Sha256File(a);
        var strategy = stager.PlaceIntoStaging(staging, a, sha, "cloned.pdf");
        if (OperatingSystem.IsMacOS()) Assert.Equal("clone", strategy); // APFS on this repo's CI/dev volume
        var placed = Path.Combine(staging, "cloned.pdf");
        Assert.True(File.Exists(placed));
        Assert.Equal(sha, FolioScanner.Sha256File(placed));
        Assert.True(File.Exists(a)); // source never moved or deleted
        Directory.Delete(staging, true);
        File.Delete(a);
    }

    [Fact]
    public void BulkApproveProposed_AttachesClearWinners_LeavesHumanChoiceStatesUntouched_IsIdempotent()
    {
        MakePdf(Path.Combine(_srcDir, "bookings-sop.pdf"),
            "Assessment bookings and cancellations. Our pharmacy records bookings and cancellations " +
            "within the required windows. Cancellation windows apply to booked assessments, and the " +
            "booking register lists every cancellation with its notice period.");
        MakePdf(Path.Combine(_srcDir, "medicare-batch-report.pdf"),
            "Medicare card numbers recorded for this patient: 4111 1111 1 plus confidential clinical notes.");
        var (res, batchId0) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId0);
        var (pres, _) = _folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok, pres.Error);

        var proposedCount = Db.FolioItems.AsNoTracking().Count(i => i.BatchId == batch && i.State == FolioItemState.Proposed);
        Assert.True(proposedCount >= 1, "fixture must yield at least one clear proposal");
        var sensitive = Db.FolioItems.AsNoTracking().First(i => i.SourceRelativePath == "medicare-batch-report.pdf");
        Assert.Equal(FolioItemState.SensitiveHold, sensitive.State);

        var (approved, failed, err, evidenceIds) = _folio.ApproveProposed(batch, Owner);
        Assert.True(failed == 0, err);
        Assert.Equal(proposedCount, approved);
        Assert.Equal(proposedCount, evidenceIds.Count);

        // every item that got evidence is now Approved, and the link is Proposed (never verified)
        foreach (var item in Db.FolioItems.AsNoTracking().Where(i => i.BatchId == batch && i.EvidenceItemId != null))
            Assert.Equal(FolioItemState.Approved, item.State);
        var bookings = Db.FolioItems.AsNoTracking().First(i => i.SourceRelativePath == "bookings-sop.pdf");
        Assert.Equal(FolioItemState.Approved, bookings.State);
        var link = Db.EvidenceLinks.AsNoTracking().First(l => l.EvidenceItemId == bookings.EvidenceItemId);
        Assert.Equal(LinkState.Proposed, link.State);
        Assert.Equal(FolioWorkspace.DefaultTitleFrom("folder/bookings-sop.pdf"), "bookings-sop");

        // sensitive item untouched: still waiting, no evidence created
        var after = Db.FolioItems.AsNoTracking().First(i => i.Id == sensitive.Id);
        Assert.Null(after.EvidenceItemId);
        Assert.Equal(FolioItemState.SensitiveHold, after.State);

        // a second press is a no-op (idempotent)
        Assert.Equal(0, _folio.ApproveProposed(batch, Owner).approved);

        // read-only sessions are refused outright
        var reviewer = new UserSession(Guid.NewGuid(), "rev", "Reviewer", UserRole.ReviewerReadOnly);
        var refused = _folio.ApproveProposed(batch, reviewer);
        Assert.Equal(0, refused.approved);
        Assert.Contains("Sign in", refused.firstError);
    }

    /// <summary>Builds a minimal real DOCX (zip + word/document.xml) with the given
    /// paragraph texts in document order — same route real Word files take.</summary>
    private static void MakeDocx(string path, params string[] paragraphs)
    {
        var w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = string.Join("", paragraphs.Select(p =>
            $"<w:p><w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(p)}</w:t></w:r></w:p>"));
        var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"{w}\"><w:body>{body}</w:body></w:document>";
        using var fs = File.Create(path);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        var entry = zip.CreateEntry("word/document.xml");
        using var sw = new StreamWriter(entry.Open());
        sw.Write(xml);
    }

    [Fact]
    public void Docx_ParsesBodyText_ClassifiesLikePdf()
    {
        MakeDocx(Path.Combine(_srcDir, "SOP - Bookings.docx"),
            "Assessment bookings and cancellations. Our pharmacy records bookings and cancellations " +
            "within the required windows. Cancellation windows apply to booked assessments, and the " +
            "booking register lists every cancellation with its notice period.");
        var (res, batchId0) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId0);
        Assert.Equal(1, Db.FolioItems.Count(i => i.BatchId == batch)); // the docx is queued, not skipped
        var (pres, _) = _folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok, pres.Error);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch && i.SourceRelativePath == "SOP - Bookings.docx");
        Assert.Equal("docx-openxml", item.ParserUsed);
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
            $"docx should classify like a pdf, got {item.State}: {item.ErrorText}");
        var props = Props(item.ProposalsJson);
        Assert.True(props.Count > 0, "no proposals for docx");
        // the doc is NOT mis-filed into the scanned/OCR route
        Assert.NotEqual(FolioItemState.NeedsOcr, item.State);

        // cache hit: re-running the SAME file must not change state (extraction cached by sha+parser)
        _folio.ProcessBatch(batch, Owner);   // resumable no-op
        var after = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch && i.SourceRelativePath == "SOP - Bookings.docx");
        Assert.Equal(item.State, after.State);
    }

    [Fact]
    public void ZipWithoutWordDocument_IsUnreadable_NeverCrashes()
    {
        using (var fs = File.Create(Path.Combine(_srcDir, "pretend.docx")))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            zip.CreateEntry("unrelated/readme.txt"); // zip signature legal for .docx, but no body
        var (res, batchId0) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId0);
        _folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.Equal(FolioItemState.Unreadable, item.State);
        Assert.Contains("Not a readable Word document", item.ErrorText);
    }

    [Fact]
    public void LegacyDoc_IsRejectedWithGuidance()
    {
        // OLE2 compound-file magic for a real legacy .doc
        File.WriteAllBytes(Path.Combine(_srcDir, "old-policy.doc"),
            [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0]);
        var out2 = Path.Combine(Path.GetTempPath(), $"qspp-out2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(out2);
        var scanner = new FolioScanner(_srcDir, out2, 200L * 1024 * 1024);
        var r = scanner.Scan();
        Assert.Empty(r.Files);
        Assert.Contains(r.Skipped, s => s.Reason == "unsupported" && s.Detail.Contains(".docx"));
        Directory.Delete(out2, true);
    }
}

/// <summary>Stage-1 classifier invariants at unit level: namespace discipline
/// (same numbers in different namespaces are NOT interchangeable), abstention
/// quality, and the "multiple" signal when a document supports several asks.</summary>
public class ProcessSortClassifierTests
{
    private static FolioCandidate C(Guid id, string ref_, string kind, string title, string summary, params string[] examples) =>
        new(id, ref_, kind, title, summary, examples);

    private static readonly Guid RemId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StdId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UagId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RuleId = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void SameNumber_DifferentNamespaces_NeverConfused()
    {
        var candidates = new List<FolioCandidate>
        {
            C(StdId, "1.1.2", "Std", "Patient safety and quality systems", "A quality management system with defined responsibilities.", "Quality manual"),
            C(UagId, "1.1.2", "Uag", "UAG pharmacy ownership guidance", "Guidance on who owns and runs the pharmacy.", "Lease or ownership documents"),
        };
        var clf = new FolioClassifier(candidates);
        var doc = "This lease and ownership record covers ownership 1.1.2. The quality system wording references 1.1.2 quality manual responsibilities as well.";
        var s = clf.Classify(doc, "ownership.pdf");
        // both namespaces score on their own wording; the suggestions must carry DISTINCT entries
        var std = s.FirstOrDefault(x => x.RefKind == "Std");
        var uag = s.FirstOrDefault(x => x.RefKind == "Uag");
        Assert.NotNull(std);
        Assert.NotNull(uag);
        Assert.Equal(StdId, std!.RequirementId);
        Assert.Equal(UagId, uag!.RequirementId);
    }

    [Fact]
    public void DirectRemReference_BoostsSurveillanceCandidate()
    {
        var candidates = new List<FolioCandidate>
        {
            C(RemId, "REM-05", "Surveillance", "Strive for 5 vaccination records", "Vaccination record audit; five flu vaccinations across the year.", "Vaccination register extracts"),
            C(StdId, "2.2.3.8", "Std", "Consistency and potency testing", "Potency testing of compounded products.", "Potency testing SOP"),
        };
        var clf = new FolioClassifier(candidates);
        var doc = "REM-05 strive for 5 audit: vaccination records for the influenza season show 37 vaccinations completed. Register extracts attached.";
        var s = clf.Classify(doc, "rem05.xlsx.pdf");
        Assert.Equal(RemId, s[0].RequirementId);
        Assert.Equal("REM-05", s[0].Ref);
    }

    [Fact]
    public void UnrelatedText_Abstains_Quietly()
    {
        var candidates = new List<FolioCandidate>
        {
            C(StdId, "2.2.3.8", "Std", "Consistency and potency testing", "Potency testing of compounded products.", "Potency testing SOP"),
        };
        var clf = new FolioClassifier(candidates);
        var s = clf.Classify("Quarterly newsletter: the weather was mild and the picnic was moved to Friday.", "newsletter.pdf");
        Assert.Empty(s); // → Needs Review in the pipeline, never a bogus destination
    }

    [Fact]
    public void MultipleSignals_SignalMultipleCandidates()
    {
        var candidates = new List<FolioCandidate>
        {
            C(RuleId, "Rule 10", "Rule", "Assessment bookings and cancellations", "Cancellation windows apply.", "Booking register"),
            C(StdId, "2.2.3.8", "Std", "Consistency and potency testing", "Potency testing of compounded products.", "Potency testing SOP"),
        };
        var clf = new FolioClassifier(candidates);
        var doc = "Booking policy: assessment bookings and cancellations, cancellation windows apply. " +
                  "Also our compounded potency testing SOP and potency testing register are attached.";
        var s = clf.Classify(doc, "policy-compendium.pdf");
        Assert.True(s.Count >= 2, $"expected multiple viable candidates, got {s.Count}");
        // close top scores must be flagged for human choice (state set by the pipeline)
    }

    [Fact]
    public void KindHint_SetsConservativeEvidenceKinds()
    {
        Assert.Equal("Certificate", FolioWorkspace.FolioKindHint("Certificats/Stephanie - Indemnity Insurance.pdf"));
        Assert.Equal("Certificate", FolioWorkspace.FolioKindHint("Staff/Sophie Hannebery - AHPRA Certificate of Registration.pdf"));
        Assert.Equal("TrainingRecord", FolioWorkspace.FolioKindHint("Staff/New Employee Induction Checklist 2027.pdf"));
        Assert.Equal("Register", FolioWorkspace.FolioKindHint("Program rules/booking register 2026.pdf"));
        Assert.Equal("PolicyOrSop", FolioWorkspace.FolioKindHint("QCPP Remedials/SOP - Cold Chain.pdf"));
    }
}