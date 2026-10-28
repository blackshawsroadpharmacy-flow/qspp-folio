using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-12: task ownership (the assignee or an Owner/Manager completes), assignee
/// existence on create, and the four-eyes rule on evidence verification.</summary>
public class TaskOwnershipTests : TestBase
{
    private (EvidenceItem? item, ServiceResult result) Import(string title)
    {
        var path = Path.Combine(TempRepo, $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake " + Guid.NewGuid()));
        var (result, item) = Workspace.ImportEvidence(path, title, EvidenceKind.PolicyOrSop, null, null, false, "", Owner);
        return (result.Ok ? item : null, result);
    }

    private EvidenceItem SetReady(string title)
    {
        var (item, import) = Import(title);
        Assert.True(import.Ok, import.Error);
        var req = Db.Requirements.First(r => r.Ref == "2.2.3.8");
        Assert.True(Workspace.LinkEvidence(item!.Id, req.Id, "", null, null, "supports the requirement", Owner).Ok);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ReadyForReview, Owner, "").Ok);
        return item;
    }

    [Fact]
    public void CompleteTask_RespectsOwnership_AndCreateValidatesAssignee()
    {
        // create: unknown assignee refused
        var bad = Workspace.CreateTask("Orphan task", "", null, Guid.NewGuid(), TaskPriority.Normal, null, Owner);
        Assert.False(bad.Ok);
        Assert.Contains("assignee does not exist", bad.Error);

        var task = Workspace.CreateTask("Check fridge temps", "", null, Owner.UserId, TaskPriority.Normal, null, Owner);
        Assert.True(task.Ok, task.Error);
        var taskId = Db.Tasks.First(t => t.Title == "Check fridge temps").Id;

        // a different Contributor is refused; Owner/Manager may close on behalf
        var solo = new UserSession(Guid.NewGuid(), "solo-tech", "Technician Solo", UserRole.Contributor);
        var refusal = Workspace.CompleteTask(taskId, "note", solo);
        Assert.False(refusal.Ok);
        Assert.Contains("Only the assignee or an Owner/Manager", refusal.Error);

        // the assignee may complete their own task (a real Contributor account)
        var techRow = Db.Users.Add(new AppUser { UserName = "tech", DisplayName = "Technician", Role = UserRole.Contributor });
        Db.SaveChanges();
        var tech = new UserSession(techRow.Entity.Id, "tech", "Technician", UserRole.Contributor);
        var claimed = Workspace.CreateTask("Reorder labels", "", null, tech.UserId, TaskPriority.Normal, null, Owner);
        Assert.True(claimed.Ok, claimed.Error);
        Assert.True(Workspace.CompleteTask(Db.Tasks.First(t => t.Title == "Reorder labels").Id, "done", tech).Ok);

        // Owner closes on behalf
        Assert.True(Workspace.CompleteTask(taskId, "closed by owner", Owner).Ok);
    }

    [Fact]
    public void FourEyes_ImporterCannotVerifyOwn_WithTwoManagers()
    {
        var item = SetReady("Second-manager SOP");

        var second = Db.Users.Add(new AppUser { UserName = "mgr", DisplayName = "Second Manager", Role = UserRole.ManagerReviewer });
        Db.SaveChanges();

        var refusal = Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ManagerVerified, Owner, "ok");
        Assert.False(refusal.Ok);
        Assert.Contains("Four-eyes", refusal.Error);

        // the other manager verifies; nothing about the row changed meanwhile
        var other = new UserSession(second.Entity.Id, "mgr", "Second Manager", UserRole.ManagerReviewer);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ManagerVerified, other, "reviewed").Ok);
        Assert.Equal(EvidenceReviewState.ManagerVerified,
            Db.EvidenceItems.AsNoTracking().First(e => e.Id == item.Id).ReviewState);
    }

    [Fact]
    public void FourEyes_SingleAccount_ImporterMayVerify()
    {
        // only the one owner account exists, so the exemption applies (single-account pharmacy)
        var item = SetReady("Solo pharmacy SOP");
        var verify = Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ManagerVerified, Owner, "verified alone");
        Assert.True(verify.Ok, verify.Error);
        Assert.Equal(Owner.UserId, Db.EvidenceItems.AsNoTracking().First(e => e.Id == item.Id).ReviewerUserId);
    }
}