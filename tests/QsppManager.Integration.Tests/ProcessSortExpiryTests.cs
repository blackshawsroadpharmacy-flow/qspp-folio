using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.ProcessSort;
using QsppManager.Infrastructure.Storage;
using QuestPDF.Fluent;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Expiry detection through the REAL pipeline: a certificate PDF whose own text
/// names its expiry is staged with the detection recorded; approving it copies the date
/// onto the evidence (EffectiveTo + ExpiresHintText) exactly as shown in the review
/// dialog; a document without expiry wording stores nothing and invents no dates.</summary>
public class ProcessSortExpiryTests : TestBase
{
    private readonly string _srcDir;
    private readonly string _outDir;
    private readonly string _cacheDir;
    private readonly FolioWorkspace _folio;

    public ProcessSortExpiryTests()
    {
        _srcDir = Path.Combine(Path.GetTempPath(), $"exp-src-{Guid.NewGuid():N}");
        _outDir = Path.Combine(Path.GetTempPath(), $"exp-out-{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(), $"exp-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_srcDir);
        var store = new AttachmentStore(AttachmentRoot);
        _folio = new FolioWorkspace(Db, Workspace, store, Audit, _cacheDir);
    }

    public override void Dispose()
    {
        try { Directory.Delete(_srcDir, true); } catch { }
        try { Directory.Delete(_outDir, true); } catch { }
        try { Directory.Delete(_cacheDir, true); } catch { }
        base.Dispose();
    }

    private static void MakePdf(string path, string text)
    {
        PdfComposer.ConfigureLicense();
        Document.Create(c => c.Page(p =>
        {
            p.Margin(28);
            p.Content().Text(text);
        })).GeneratePdf(path);
    }

    private static List<FolioSuggestion> Props(string json) =>
        JsonSerializer.Deserialize<List<FolioSuggestion>>(json) ?? [];

    [Fact]
    public void CertificatePdf_StagedWithDetection_ApprovalCarriesDateOntoEvidence()
    {
        MakePdf(Path.Combine(_srcDir, "indemnity.pdf"),
            "Professional indemnity insurance certificate.\n" +
            "This policy expires 30 June 2026. Underwritten for Burke Road Compounding Pharmacy.");

        var (res, batchId) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        var (pres, _) = _folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok || pres.Error.Contains("review"), pres.Error);

        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        var guess = FolioExpiryGuess.FromJson(item.ExpiryJson);
        Assert.NotNull(guess);
        Assert.Equal(new DateOnly(2026, 6, 30), guess!.ExpiresOn);
        Assert.Contains("expires 30 June 2026", guess.Hint);
        // live status, computed not stored: 30 June 2026 vs today (2026-10-24 session) = expired
        Assert.Equal("expired", guess.Status(DateOnly.FromDateTime(DateTime.Today)));

        // approve via the SAME path the dialog uses: the date lands on the evidence.
        // The test seed carries no REM items; Rule 10 is a valid seeded destination, and
        // expiry detection does not depend on classification at all.
        var ruleId = Db.Requirements.AsNoTracking().First(r => r.Ref == "Rule 10").Id;
        var approvedJson = JsonSerializer.Serialize(new List<FolioSuggestion>
        {
            new(ruleId, "Rule 10", "Rule", "Assessment bookings and cancellations", 0, "user-approved",
                FolioClassifier.Stage1Model, FolioClassifier.Stage1ModelVersion)
        });
        var (appr, evidenceId) = _folio.ApproveItem(item.Id, Owner, "Indemnity 2024", "Certificate", approvedJson, false, "");
        Assert.True(appr.Ok, appr.Error);

        var evidence = Db.EvidenceItems.AsNoTracking().First(e => e.Id == evidenceId);
        Assert.Equal(new DateOnly(2026, 6, 30), evidence.EffectiveTo);
        Assert.Contains("expires 30 June 2026", evidence.ExpiresHintText);
        // links stay untouched: link periods are a review decision, not a detection
        Assert.True(Db.EvidenceLinks.AsNoTracking().Where(l => l.EvidenceItemId == evidenceId)
            .All(l => l.PeriodTo == null));
    }

    [Fact]
    public void DocumentWithoutExpiryWording_StoresNoDetection_ApprovalLeavesDatesEmpty()
    {
        MakePdf(Path.Combine(_srcDir, "bookings-sop.pdf"),
            "Assessment bookings and cancellations: our pharmacy records assessment bookings " +
            "within the required windows and keeps the cancellation register current.");

        var (res, batchId) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        var (pres, _) = _folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.Equal("{}", item.ExpiryJson);

        var rule10Id = Db.Requirements.AsNoTracking().First(r => r.Ref == "Rule 10").Id;
        var approvedJson = JsonSerializer.Serialize(new List<FolioSuggestion>
        {
            new(rule10Id, "Rule 10", "Rule", "Assessment bookings and cancellations", 0, "user-approved",
                FolioClassifier.Stage1Model, FolioClassifier.Stage1ModelVersion)
        });
        var (appr, evidenceId) = _folio.ApproveItem(item.Id, Owner, "Bookings SOP", "PolicyOrSop", approvedJson, false, "");
        Assert.True(appr.Ok, appr.Error);
        var evidence = Db.EvidenceItems.AsNoTracking().First(e => e.Id == evidenceId);
        Assert.Null(evidence.EffectiveTo);
        Assert.Equal("", evidence.ExpiresHintText);
    }
}