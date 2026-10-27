using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-06 / plan 005: retiring evidence needs a verifier role, a reason, marks its
/// links Superseded, and raises the same re-link tasks supersession raises. Superseding with
/// an already-retired replacement is refused.</summary>
public class EvidenceRetireTests : TestBase
{
    private (EvidenceItem item, ServiceResult result) ImportEvidence(string title, string content, bool patientData = false, string attestation = "")
    {
        var path = Path.Combine(TempRepo, $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake " + content));
        var res = Workspace.ImportEvidence(path, title, EvidenceKind.PolicyOrSop, null, null, patientData, attestation, Owner);
        var (importResult, item) = res;
        return (item, importResult);
    }

    /// <summary>Import, link to 2.2.3.8, walk Draft -> ReadyForReview -> ManagerVerified.</summary>
    private EvidenceItem VerifiedLinked()
    {
        var (item, import) = ImportEvidence("BUD procedure", "verified-content");
        Assert.True(import.Ok, import.Error);
        var req = Db.Requirements.First(r => r.Ref == "2.2.3.8");
        Workspace.LinkEvidence(item!.Id, req.Id, "compounding", null, null, "initial", Owner);
        Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ReadyForReview, Owner, "");
        Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ManagerVerified, Owner, "");
        return Db.EvidenceItems.First(e => e.Id == item.Id);
    }

    [Fact]
    public void Retire_FlagsLinks_AndRaisesTasks()
    {
        var item = VerifiedLinked();
        var req = Db.Requirements.First(r => r.Ref == "2.2.3.8");

        var res = Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.Retired, Owner, "obsolete");
        Assert.True(res.Ok, res.Error);

        Assert.Equal(EvidenceReviewState.Retired, Db.EvidenceItems.First(e => e.Id == item.Id).ReviewState);
        Assert.Equal("obsolete", Db.EvidenceItems.First(e => e.Id == item.Id).SupersededReason);
        Assert.Contains(
            Db.EvidenceLinks.Where(l => l.EvidenceItemId == item.Id).ToList(),
            l => l.State == LinkState.Superseded);
        Assert.All(Db.EvidenceLinks.Where(l => l.EvidenceItemId == item.Id).ToList(), l => Assert.Equal(LinkState.Superseded, l.State));
        var tasks = Db.Tasks.Where(t => t.RequirementId == req.Id && t.EvidenceItemId == item.Id).ToList();
        var openTasks = Workspace.RequirementDetail(req.Id, Owner)!.Tasks.Where(t => t.State == TaskState.Open).ToList();
        Assert.Single(openTasks);
        Assert.All(tasks, t => Assert.Equal(TaskPriority.High, t.Priority));
    }

    [Fact]
    public void Retire_RequiresReason()
    {
        var item = VerifiedLinked();

        var res = Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.Retired, Owner, "");
        Assert.False(res.Ok);

        Assert.Equal(EvidenceReviewState.ManagerVerified, Db.EvidenceItems.First(e => e.Id == item.Id).ReviewState);
        Assert.All(Db.EvidenceLinks.Where(l => l.EvidenceItemId == item.Id).ToList(), l => Assert.Equal(LinkState.Active, l.State));
    }

    [Fact]
    public void Retire_ByContributor_IsRefused()
    {
        var item = VerifiedLinked();
        var (added, contrib) = Auth.AddUser(Owner.DisplayName, Owner.UserId, "contrib", "Contrib", UserRole.Contributor, "passphras8");
        Assert.True(added.Ok, added.Error);
        var session = new UserSession(contrib!.Id, contrib.UserName, contrib.DisplayName, contrib.Role);

        var res = Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.Retired, session, "contributed retire");
        Assert.False(res.Ok);
        Assert.Equal(EvidenceReviewState.ManagerVerified, Db.EvidenceItems.First(e => e.Id == item.Id).ReviewState);
        Assert.All(Db.EvidenceLinks.Where(l => l.EvidenceItemId == item.Id).ToList(), l => Assert.Equal(LinkState.Active, l.State));
    }

    [Fact]
    public void Supersede_RefusesRetiredReplacement()
    {
        var itemA = VerifiedLinked();
        var itemB = VerifiedLinked();
        itemB = Db.EvidenceItems.First(e => e.Id == itemB!.Id);
        Assert.True(Workspace.SetEvidenceReview(itemB.Id, EvidenceReviewState.Retired, Owner, "retired first").Ok);

        var res = Workspace.SupersedeEvidence(itemA.Id, itemB.Id, "swap in the retired one", Owner);
        Assert.False(res.Ok);
        Assert.Contains("retired", res.Error);
        Assert.Equal(EvidenceReviewState.ManagerVerified, Db.EvidenceItems.First(e => e.Id == itemA.Id).ReviewState);
    }

    [Fact]
    public void Retire_IsIdempotentOnSecondCall()
    {
        var item = VerifiedLinked();
        var req = Db.Requirements.First(r => r.Ref == "2.2.3.8");
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.Retired, Owner, "obsolete").Ok);
        var tasksAfterFirst = Db.Tasks.Count(t => t.RequirementId == req.Id && t.EvidenceItemId == item.Id);

        var second = Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.Retired, Owner, "again");
        Assert.False(second.Ok);
        var tasksAfterSecond = Db.Tasks.Count(t => t.RequirementId == req.Id && t.EvidenceItemId == item.Id);
        Assert.Equal(tasksAfterFirst, tasksAfterSecond);
    }
}