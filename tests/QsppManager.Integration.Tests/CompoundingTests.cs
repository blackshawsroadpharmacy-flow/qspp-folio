using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M3 compounding module: supplier register, recall closure gate, per-person
/// potency coverage honouring the unresolved ISSUE-POTENCY wording.</summary>
public class CompoundingTests : TestBase
{
    [Fact]
    public void Suppliers_RegisterAndReview()
    {
        Assert.True(Workspace.AddSupplier("Sigma Chemicals", "Hydrocortisone powder, bases", "APVMA-registered wholesaler", Owner).Ok);
        Assert.False(Workspace.AddSupplier("", "x", "", Owner).Ok);
        var sup = Workspace.Suppliers().First();
        Assert.True(Workspace.SetSupplierReviewed(sup.Id, null, new DateOnly(2026, 10, 20), Owner).Ok);
        Assert.Equal(new DateOnly(2026, 10, 20), Workspace.Suppliers().First(s => s.Id == sup.Id).LastReviewed);
    }

    [Fact]
    public void Recalls_RequireStockAction_BeforeClosing_AndRoleGate()
    {
        Assert.True(Workspace.AddRecall("Lidocaine HCl 2% gel", "B2026-11", new DateOnly(2026, 10, 18), "", Owner).Ok);
        var recall = Workspace.Recalls().First();
        Assert.Equal("Open", recall.State);

        Assert.False(Workspace.CloseRecall(recall.Id, "", Owner).Ok, "closing without a stock action is refused");
        var reviewer = new UserSession(Guid.NewGuid(), "rev", "Reviewer", UserRole.ReviewerReadOnly);
        Assert.False(Workspace.CloseRecall(recall.Id, "returned to supplier", reviewer).Ok, "read-only cannot close");
        Assert.True(Workspace.CloseRecall(recall.Id, "stock quarantined and returned to supplier", Owner).Ok);
        Assert.Equal("Closed", Workspace.Recalls().First(r => r.Id == recall.Id).State);
    }

    [Fact]
    public void Potency_CoverageTracks_PerPerson_DrugsAndForms()
    {
        Assert.True(Workspace.AddStaffMember("A. Tech", "Compounding technician", Owner).Ok);
        var staff = Workspace.Staff().First();

        Assert.True(Workspace.AddPotencyRecord(staff.Id, "Compounding technician", "Hydrocortisone", "cream", new DateOnly(2026, 2, 10), "pass", Owner).Ok);
        var (rows1, _) = Workspace.PotencyCoverage();
        Assert.Contains("partial", rows1.First(r => r.name == "A. Tech").status);

        Assert.True(Workspace.AddPotencyRecord(staff.Id, "Compounding technician", "Lidocaine", "suspension", new DateOnly(2026, 5, 2), "pass", Owner).Ok);
        var (rows2, note) = Workspace.PotencyCoverage();
        Assert.Contains("coverage met", rows2.First(r => r.name == "A. Tech").status);
        Assert.Contains("ISSUE-POTENCY", note); // the interpretation is never presented as settled

        // Same drug in another form doesn't add a distinct drug; another person's record doesn't count for this person.
        Assert.True(Workspace.AddPotencyRecord(staff.Id, "Compounding technician", "hydrocortisone", "ointment", new DateOnly(2026, 6, 2), "pass", Owner).Ok);
        var staff2 = Workspace.Staff().Count();
        Assert.True(Workspace.AddStaffMember("B. Pharm", "Compounding pharmacist", Owner).Ok);
        var (rows3, _) = Workspace.PotencyCoverage();
        var rowA = rows3.First(r => r.name == "A. Tech");
        Assert.Equal(2, rowA.drugs.Count); // case-insensitive distinct
        Assert.Equal(3, rowA.forms.Count);
        var rowB = rows3.First(r => r.name == "B. Pharm");
        Assert.Contains("no testing recorded", rowB.status);
        Assert.Equal(3, Workspace.PotencyRecords().Count());
    }
}