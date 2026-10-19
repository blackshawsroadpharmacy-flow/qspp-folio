using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Seed;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Hermetic persistence tests: temp SQLite file per test, temp repo root with a
/// minimal seed; the pack's real manifest is NOT touched (a documents.json seed file
/// disables that import in tests).</summary>
public abstract class TestBase : IDisposable
{
    protected readonly string DbPath = Path.Combine(Path.GetTempPath(), $"qspp-test-{Guid.NewGuid():N}.db");
    protected readonly string TempRepo = Path.Combine(Path.GetTempPath(), $"qspp-repo-{Guid.NewGuid():N}");

    protected QsppWorkspace Workspace = null!;
    protected AuthService Auth = null!;
    protected AuditService Audit = null!;
    protected SeedImporter Seeds = null!;
    protected QsppDbContext Db = null!;
    protected UserSession Owner = null!;
    /// <summary>Root of the attachment store the Workspace writes to — services under test
    /// that must read managed files (assessor pack, backup) use this root.</summary>
    protected string AttachmentRoot = null!;

    protected TestBase()
    {
        Directory.CreateDirectory(TempRepo);
        Directory.CreateDirectory(Path.Combine(TempRepo, "seed"));
        WriteSeedRequirements();
        File.WriteAllText(Path.Combine(TempRepo, "seed", "documents.json"), "[]"); // no real corpus in tests

        var options = new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={DbPath}")
            .Options;
        Db = new QsppDbContext(options);
        Db.Database.EnsureCreated();

        var store = new AttachmentStore(Path.Combine(Path.GetTempPath(), $"qspp-att-{Guid.NewGuid():N}"));
        AttachmentRoot = store.Root;
        Audit = new AuditService(Db);
        Auth = new AuthService(Db, Audit);
        Seeds = new SeedImporter(Db, store, Audit, TempRepo);
        Workspace = new QsppWorkspace(Db, store, Audit);
        Seeds.ImportAll();

        var (r, user) = Auth.CreateFirstUser("owner", "Test Owner", "passphras8");
        Assert.True(r.Ok, r.Error);
        Owner = new UserSession(user!.Id, user.UserName, user.DisplayName, user.Role);
    }

    public void Dispose()
    {
        Db.Dispose();
        try { File.Delete(DbPath); } catch { }
        try { Directory.Delete(TempRepo, recursive: true); } catch { }
    }

    private void WriteSeedRequirements()
    {
        var req = new object[]
        {
            new
            {
                refKind = "Rule", sourceDoc = "QSPP-Program-Rules-V1.0-Aug2026.txt", sourceVersion = "V1.0, Aug 2026",
                @ref = "Rule 8", title = "Mid-Cycle Review", domain = "Program",
                summary = "Mid-cycle review in the second year.",
                evidenceExamples = new object[] { },
                interpretationFlags = new[] { "ISSUE-MIDCYCLE" },
            },
            new
            {
                refKind = "Rule", sourceDoc = "QSPP-Program-Rules-V1.0-Aug2026.txt", sourceVersion = "V1.0, Aug 2026",
                @ref = "Rule 10", title = "Assessment bookings and cancellations", domain = "Program",
                summary = "Cancellation windows apply.",
                evidenceExamples = new object[] { },
                interpretationFlags = new[] { "ISSUE-CANCELWINDOW" },
            },
            new
            {
                refKind = "Std", sourceDoc = "AS85000-2024-full.txt", sourceVersion = "AS 85000:2024",
                @ref = "2.2.3.8", title = "Consistency and potency testing", domain = 2,
                summary = "Consistent compounding with potency testing.",
                evidenceExamples = new object[] { },
                interpretationFlags = new[] { "ISSUE-POTENCY" },
            },
        };
        File.WriteAllText(Path.Combine(TempRepo, "seed", "requirements.json"),
            System.Text.Json.JsonSerializer.Serialize(req));
    }
}

public class SeedImporterTests : TestBase
{
    [Fact]
    public void Import_IsIdempotent_SameRequirementsTwice_StillThree()
    {
        var first = Seeds.ImportAll();
        var second = Seeds.ImportAll();
        Assert.Equal(3, Db.Requirements.Count());
        Assert.Equal(3, Db.InterpretationIssues.Count());
    }

    [Fact]
    public void Requirement_Ids_AreDeterministic_AcrossDatabaseInstances()
    {
        var ids1 = Db.Requirements.AsEnumerable().Select(r => (r.Ref, r.Id)).ToList();
        var second = new SeedImporter(new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"qspp-test-{Guid.NewGuid():N}.db")}").Options),
            new AttachmentStore(Path.Combine(Path.GetTempPath(), $"qspp-att2-{Guid.NewGuid():N}")),
            new AuditService(Db), TempRepo);
        var thirdDb = new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), $"qspp-test-{Guid.NewGuid():N}.db")}").Options);
        try
        {
            thirdDb.Database.EnsureCreated();
            var seeds2 = new SeedImporter(thirdDb, new AttachmentStore(Path.Combine(Path.GetTempPath(), $"att-{Guid.NewGuid():N}")),
                new AuditService(thirdDb), TempRepo);
            seeds2.ImportAll();
            var ids2 = thirdDb.Requirements.AsEnumerable().Select(r => (r.Ref, r.Id)).ToList();
            Assert.Equal(ids1.OrderBy(x => x.Ref).ToList(), ids2.OrderBy(x => x.Ref).ToList());
        }
        finally { thirdDb.Dispose(); }
    }
}

public class WorkspaceWorkflowTests : TestBase
{
    private (EvidenceItem item, ServiceResult result) ImportEvidence(string title, string content, bool patientData = false, string attestation = "")
    {
        var path = Path.Combine(TempRepo, $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake " + content));
        var res = Workspace.ImportEvidence(path, title, EvidenceKind.PolicyOrSop, null, null, patientData, attestation, Owner);
        var (importResult, item) = res;
        return (item, importResult);
    }

    [Fact]
    public void SetApplicability_RequiresReason()
    {
        var req = Db.Requirements.First(r => r.Ref == "2.2.3.8");
        var fail = Workspace.SetApplicability(req.Id, Applicability.Applicable, "", Owner);
        Assert.False(fail.Ok);
        var ok = Workspace.SetApplicability(req.Id, Applicability.Applicable, "Pharmacy offers complex compounding under UAG 2.4 applicability scope.", Owner);
        Assert.True(ok.Ok);
    }

    [Fact]
    public void Readiness_RejectsJumpingToVerified()
    {
        var req = Db.Requirements.First(r => r.Ref == "Rule 8");
        var jump = Workspace.SetReadiness(req.Id, RequirementReadiness.ManagerVerified, Owner);
        Assert.False(jump.Ok);
        Assert.True(Workspace.SetReadiness(req.Id, RequirementReadiness.InProgress, Owner).Ok);
        Assert.True(Workspace.SetReadiness(req.Id, RequirementReadiness.ReadyForReview, Owner).Ok);
        Assert.True(Workspace.SetReadiness(req.Id, RequirementReadiness.ManagerVerified, Owner).Ok);
        // Assessor acceptance without external ref is refused
        var accept = Workspace.SetReadiness(req.Id, RequirementReadiness.AssessorAccepted, Owner, "");
        Assert.False(accept.Ok);
        Assert.True(Workspace.SetReadiness(req.Id, RequirementReadiness.AssessorAccepted, Owner, "Assessor report AR-2026-001").Ok);
    }

    [Fact]
    public void Evidence_PatientData_WithoutAttestation_IsRefused_OrBlockedAtReview()
    {
        var (item, importResult) = ImportEvidence("Patient list", "contains patient rows", patientData: true, attestation: "");
        if (item == null)
        {
            // Preferred behaviour: the import is refused outright without an attestation.
            Assert.False(importResult.Ok);
            return;
        }
        // If an installation allows the import, the privacy gate must still catch submission for review.
        var submit = Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ReadyForReview, Owner, "");
        Assert.False(submit.Ok);
    }

    [Fact]
    public void Evidence_NonPatient_LinksAndBecomesVerified()
    {
        var (item, importResult) = ImportEvidence("Spill kit procedure", "clean text");
        Assert.True(importResult.Ok && item != null, importResult.Error);
        var req = Db.Requirements.First(r => r.Ref == "2.2.3.8");
        var link = Workspace.LinkEvidence(item.Id, req.Id, "compounding facility", null, null, "SOP covers potency verification records", Owner);
        Assert.True(link.Ok, link.Error);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ReadyForReview, Owner, "").Ok);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ManagerVerified, Owner, "verified").Ok);
        // link is Active once evidence is verified
        var detail = Workspace.RequirementDetail(req.Id, Owner);
        Assert.Contains(detail!.Links, l => l.State == LinkState.Active);
    }

    [Fact]
    public void Supersede_RetiresOld_FlagsLinks_CreatesRelinkTask()
    {
        var (oldItem, _) = ImportEvidence("BUD procedure v1", "old");
        var (newItem, _) = ImportEvidence("BUD procedure v2", "new");
        var req = Db.Requirements.First(r => r.Ref == "2.2.3.8");
        Workspace.LinkEvidence(oldItem!.Id, req.Id, "compounding", null, null, "initial", Owner);
        Workspace.SetEvidenceReview(oldItem.Id, EvidenceReviewState.ReadyForReview, Owner, "");
        Workspace.SetEvidenceReview(oldItem.Id, EvidenceReviewState.ManagerVerified, Owner, "");

        var res = Workspace.SupersedeEvidence(oldItem.Id, newItem!.Id, "updated after review", Owner);
        Assert.True(res.Ok, res.Error);

        var detail = Workspace.RequirementDetail(req.Id, Owner);
        Assert.Contains(detail!.Links, l => l.State == LinkState.Superseded);
        Assert.All(detail.Tasks, t => Assert.Equal(TaskState.Open, t.State));
        Assert.Single(detail.Tasks);
        Assert.Equal(EvidenceReviewState.Retired, Db.EvidenceItems.First(e => e.Id == oldItem.Id).ReviewState);

        // history preserved: retired item still readable
        var all = Workspace.AllEvidence();
        Assert.Contains(all, e => e.Id == oldItem.Id);
    }

    [Fact]
    public void Deadlines_CarryPlanningOnlyFlags_ForIssuedConflicts()
    {
        var ph = Workspace.EnsurePharmacy();
        ph.CycleStartDate = new DateOnly(2026, 1, 1);
        ph.OnsiteAssessmentDate = new DateOnly(2026, 11, 6);
        Db.SaveChanges();
        var list = Workspace.PharmacyDeadlines();
        var mid = list.First(d => d.InterpretationIssueKey == "ISSUE-MIDCYCLE");
        Assert.True(mid.PlanningOnly);
        var cancel = list.First(d => d.InterpretationIssueKey == "ISSUE-CANCELWINDOW");
        Assert.True(cancel.PlanningOnly);
        // planning-only variants can be excluded for reporting
        Assert.DoesNotContain(Workspace.PharmacyDeadlines(includePlanningOnly: false), d => d.PlanningOnly);
    }

    [Fact]
    public void AuditChain_VerifiesAfterMultipleActions()
    {
        var req = Db.Requirements.First(r => r.Ref == "Rule 10");
        Workspace.SetNextAction(req.Id, "Confirm cancellation policy reading with the manager", Owner);
        Workspace.CreateTask("Check fee schedule", "", req.Id, Owner.UserId, TaskPriority.Normal, null, Owner);
        var (valid, broken) = Audit.VerifyChain();
        Assert.True(valid, $"chain broken at {broken}");
    }

    [Fact]
    public void RestrictedReviewer_CannotWrite()
    {
        var (r, user) = Auth.AddUser(Owner.DisplayName, Owner.UserId, "reviewer", "Read Only Reviewer", UserRole.ReviewerReadOnly, "passphras8");
        Assert.True(r.Ok);
        var session = new UserSession(user!.Id, user.UserName, user.DisplayName, user.Role);
        var req = Db.Requirements.First(r => r.Ref == "Rule 10");
        var res = Workspace.SetNextAction(req.Id, "should fail", session);
        Assert.False(res.Ok);
    }
}