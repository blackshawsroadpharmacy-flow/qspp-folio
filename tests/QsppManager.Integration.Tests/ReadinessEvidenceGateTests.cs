using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Backup;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-02 / plan 002: a requirement cannot reach ManagerVerified without at least
/// one Active link to Manager-verified, in-date evidence, or a written no-evidence reason
/// that is stored and audited. Verified rows without either stop counting in the summary,
/// journey and assessor pack until they qualify.</summary>
public class ReadinessEvidenceGateTests : TestBase
{
    /// <summary>Import, link to Rule 8, leave the evidence in Draft (link Proposed).</summary>
    private EvidenceItem ImportLinkedDraft()
    {
        var reqId = Db.Requirements.First(r => r.Ref == "Rule 8").Id;
        var path = Path.Combine(TempRepo, $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake draft"));
        var (res, item) = Workspace.ImportEvidence(path, "Draft SOP", EvidenceKind.PolicyOrSop, null, null, false, "", Owner);
        Assert.True(res.Ok, res.Error);
        Assert.True(Workspace.LinkEvidence(item!.Id, reqId, "", null, null, "draft link", Owner).Ok);
        return item;
    }

    /// <summary>Verified evidence linked to Rule 8 (Active link), optionally expired.</summary>
    private EvidenceItem VerifiedLinkedToRule8(bool expire = false)
    {
        var reqId = Db.Requirements.First(r => r.Ref == "Rule 8").Id;
        var path = Path.Combine(TempRepo, $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake verified"));
        var (res, item) = Workspace.ImportEvidence(path, "Real SOP", EvidenceKind.PolicyOrSop, null, null, false, "", Owner);
        Assert.True(res.Ok, res.Error);
        Assert.True(Workspace.LinkEvidence(item!.Id, reqId, "", null, null, "supports", Owner).Ok);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ReadyForReview, Owner, "").Ok);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ManagerVerified, Owner, "ok").Ok);
        if (expire)
        {
            Db.EvidenceItems.First(e => e.Id == item.Id).EffectiveTo = Clock.Today().AddDays(-1);
            Db.SaveChanges();
        }
        return item;
    }

    /// <summary>Walk Rule 8 to ReadyForReview without touching ManagerVerified.</summary>
    private Guid ReadyForReviewRule8()
    {
        var reqId = Db.Requirements.First(r => r.Ref == "Rule 8").Id;
        Assert.True(Workspace.SetReadiness(reqId, RequirementReadiness.InProgress, Owner).Ok);
        Assert.True(Workspace.SetReadiness(reqId, RequirementReadiness.ReadyForReview, Owner).Ok);
        return reqId;
    }

    [Fact]
    public void Verify_WithoutEvidence_IsRefused()
    {
        var reqId = ReadyForReviewRule8();

        var res = Workspace.SetReadiness(reqId, RequirementReadiness.ManagerVerified, Owner);
        Assert.False(res.Ok);
        Assert.Contains("Manager-verified", res.Error);

        Assert.Equal(RequirementReadiness.ReadyForReview, Db.RequirementStates.First(s => s.RequirementId == reqId).Readiness);
    }

    [Fact]
    public void Verify_WithReason_IsAllowed_AndAudited()
    {
        var reqId = ReadyForReviewRule8();
        const string reason = "Definition only, nothing to evidence";

        var res = Workspace.SetReadiness(reqId, RequirementReadiness.ManagerVerified, Owner, "", reason);
        Assert.True(res.Ok, res.Error);

        Assert.Equal(reason, Db.RequirementStates.First(s => s.RequirementId == reqId).NoEvidenceReason);
        Assert.Equal(1, Db.AuditEvents.Count(a => a.Action == "requirement.readiness.noevidence" && a.EntityId == reqId.ToString()));
    }

    [Fact]
    public void Verify_WithOnlyProposedLink_IsRefused()
    {
        var reqId = ReadyForReviewRule8();
        ImportLinkedDraft();

        var res = Workspace.SetReadiness(reqId, RequirementReadiness.ManagerVerified, Owner);
        Assert.False(res.Ok);
        Assert.Equal(RequirementReadiness.ReadyForReview, Db.RequirementStates.First(s => s.RequirementId == reqId).Readiness);
    }

    [Fact]
    public void Verify_WithExpiredEvidence_IsRefused()
    {
        var reqId = ReadyForReviewRule8();
        VerifiedLinkedToRule8(expire: true);

        var res = Workspace.SetReadiness(reqId, RequirementReadiness.ManagerVerified, Owner);
        Assert.False(res.Ok);
        Assert.Equal(RequirementReadiness.ReadyForReview, Db.RequirementStates.First(s => s.RequirementId == reqId).Readiness);
    }

    [Fact]
    public void ReadinessSummary_DoesNotCountLegacyVerifiedWithoutEvidence()
    {
        var reqId = ReadyForReviewRule8();
        // Legacy row: verified directly, no evidence, no reason (FIX-02 keeps it, but it stops counting).
        var st = Db.RequirementStates.First(s => s.RequirementId == reqId);
        st.Readiness = RequirementReadiness.ManagerVerified;
        Db.SaveChanges();
        Assert.Equal(0, Workspace.ReadinessSummary().verified);

        VerifiedLinkedToRule8();
        Assert.Equal(1, Workspace.ReadinessSummary().verified);
    }

    [Fact]
    public void AssessorPack_ExcludesVerifiedWithoutEvidence()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), $"qspp-pack-{Guid.NewGuid():N}");
        try
        {
            var reqId = ReadyForReviewRule8();
            var st = Db.RequirementStates.First(s => s.RequirementId == reqId);
            st.Readiness = RequirementReadiness.ManagerVerified;
            st.Applicability = Applicability.Applicable;
            Db.SaveChanges();

            var composer = new PdfComposer(FindRepoRoot());
            PdfComposer.ConfigureLicense();
            var svc = new AssessorPackService(Workspace, Db, composer, new AttachmentStore(AttachmentRoot), outRoot);
            var (ok, _, err, _, _, _) = svc.Export(Owner, "legacy rows excluded");
            Assert.False(ok);
            Assert.Contains("Nothing is in scope yet", err);
        }
        finally
        {
            try { Directory.Delete(outRoot, recursive: true); } catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "seed")) && Directory.Exists(Path.Combine(dir.FullName, "assets", "fonts")))
                return dir.FullName;
        throw new InvalidOperationException("repo root not found");
    }
}