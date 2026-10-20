using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Owner assignment ergonomics (M6 feedback): the 470-requirement wall of
/// "owner not assigned" chips was overwhelming, so rows stay quiet and one click assigns
/// every unassigned requirement to the signed-in user as a single audited event.</summary>
public class OwnerBulkAssignTests : TestBase
{
    private RequirementRow Row(Guid id) =>
        Workspace.RequirementRows(Owner).First(r => r.Id == id);

    [Fact]
    public void BulkAssign_ClaimsEveryUnassigned_AsOneAuditEntry()
    {
        var before = Workspace.RequirementRows(Owner);
        var unassignedBefore = before.Count(r => r.OwnerUnassigned);
        Assert.True(unassignedBefore > 0, "test corpus should import with blank owners");

        var changed = Workspace.AssignAllOwnersToUser(Owner.UserId, Owner);

        Assert.Equal(unassignedBefore, changed);
        Assert.All(Workspace.RequirementRows(Owner), r => Assert.False(r.OwnerUnassigned));

        // Exactly ONE audit line for the whole batch, naming the actor.
        var events = Db.AuditEvents.OrderBy(e => e.Id).ToList();
        var bulk = events.Where(e => e.Action == "requirement.owner.bulk").ToList();
        var single = events.Count(e => e.Action == "requirement.owner");
        Assert.Single(bulk);
        Assert.Equal(0, single);
        Assert.Contains("assigned=" + changed, bulk[0].Details);
    }

    [Fact]
    public void BulkAssign_ThenSingleRequirement_CanChangeOwnerIndependently()
    {
        var row = Workspace.RequirementRows(Owner).First();
        Workspace.AssignAllOwnersToUser(Owner.UserId, Owner);

        var admin = Db.Users.Add(new AppUser { UserName = "manager", DisplayName = "Manager", Role = UserRole.ManagerReviewer });
        Db.SaveChanges();
        Workspace.AssignOwner(row.Id, admin.Entity.Id, Owner);

        Assert.True(Row(row.Id).OwnerName == "Manager");
        Assert.All(Workspace.RequirementRows(Owner), r => Assert.False(r.OwnerUnassigned));
    }

    [Fact]
    public void ReadOnly_Sessions_CanNeitherBulkNorSingleAssign()
    {
        var reviewer = new UserSession(Guid.NewGuid(), "reviewer", "Reviewer", UserRole.ReviewerReadOnly);
        var row = Workspace.RequirementRows(Owner).First();

        Assert.Equal(0, Workspace.AssignAllOwnersToUser(reviewer.UserId, reviewer));
        var res = Workspace.AssignOwner(row.Id, reviewer.UserId, reviewer);
        Assert.False(res.Ok);
        Assert.True(Row(row.Id).OwnerUnassigned);
    }
}