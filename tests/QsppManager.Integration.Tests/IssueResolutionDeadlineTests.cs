using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-08 / plan 007: resolving a deadline-bearing interpretation issue carries a
/// structured choice, and PharmacyDeadlines then uses that settled reading (planning-only
/// flag drops). Unresolved issues keep the strictest, now holiday-aware, reading.</summary>
public class IssueResolutionDeadlineTests : TestBase
{
    [Fact]
    public void Resolve_Cancel_Calendar_ChangesDateAndClearsPlanningFlag()
    {
        var ph = Workspace.EnsurePharmacy();
        ph.OnsiteAssessmentDate = new DateOnly(2026, 11, 13);
        Db.SaveChanges();

        var before = Workspace.PharmacyDeadlines().First(d => d.InterpretationIssueKey == "ISSUE-CANCELWINDOW");
        Assert.Equal(new DateOnly(2026, 10, 29), before.Date);   // 10 business days, Melbourne Cup skipped
        Assert.True(before.PlanningOnly);

        var issue = Db.InterpretationIssues.AsEnumerable().First(i => i.Key == "ISSUE-CANCELWINDOW");
        Assert.True(Workspace.ResolveIssue(issue.Id, "Program Rules V1.1 confirmed with the provider", Owner, "calendar").Ok);

        var after = Workspace.PharmacyDeadlines().First(d => d.InterpretationIssueKey == "ISSUE-CANCELWINDOW");
        Assert.Equal(new DateOnly(2026, 11, 3), after.Date);     // 10 calendar days
        Assert.False(after.PlanningOnly);
        Assert.Contains("(resolved)", after.Source);
    }

    [Fact]
    public void Resolve_Cancel_Business_ClearsPlanningFlag_KeepsBusinessDate()
    {
        var ph = Workspace.EnsurePharmacy();
        ph.OnsiteAssessmentDate = new DateOnly(2026, 11, 13);
        Db.SaveChanges();

        var issue = Db.InterpretationIssues.AsEnumerable().First(i => i.Key == "ISSUE-CANCELWINDOW");
        Assert.True(Workspace.ResolveIssue(issue.Id, "Provider confirmed business days", Owner, "business").Ok);

        var after = Workspace.PharmacyDeadlines().First(d => d.InterpretationIssueKey == "ISSUE-CANCELWINDOW");
        Assert.Equal(new DateOnly(2026, 10, 29), after.Date);
        Assert.False(after.PlanningOnly);
        Assert.Contains("(resolved)", after.Source);
    }

    [Fact]
    public void Resolve_Midcycle_1324_ChangesWindow()
    {
        var ph = Workspace.EnsurePharmacy();
        ph.CycleStartDate = new DateOnly(2026, 1, 1);
        Db.SaveChanges();

        var before = Workspace.PharmacyDeadlines().First(d => d.InterpretationIssueKey == "ISSUE-MIDCYCLE");
        Assert.Equal(new DateOnly(2027, 4, 1), before.Date);     // month 15 under months 15-21
        Assert.True(before.PlanningOnly);

        var issue = Db.InterpretationIssues.AsEnumerable().First(i => i.Key == "ISSUE-MIDCYCLE");
        Assert.True(Workspace.ResolveIssue(issue.Id, "T&C definitions confirmed: months 13-24", Owner, "13-24").Ok);

        var after = Workspace.PharmacyDeadlines().First(d => d.InterpretationIssueKey == "ISSUE-MIDCYCLE");
        Assert.Equal(new DateOnly(2027, 2, 1), after.Date);      // month 13 under months 13-24
        Assert.False(after.PlanningOnly);
        Assert.Contains("(resolved)", after.Source);
    }

    [Fact]
    public void Resolve_RequiresChoice_ForDeadlineIssues()
    {
        var ph = Workspace.EnsurePharmacy();
        ph.OnsiteAssessmentDate = new DateOnly(2026, 11, 13);
        Db.SaveChanges();
        var issue = Db.InterpretationIssues.AsEnumerable().First(i => i.Key == "ISSUE-CANCELWINDOW");

        Assert.False(Workspace.ResolveIssue(issue.Id, "settled today", Owner).Ok);
        Assert.False(Workspace.ResolveIssue(issue.Id, "settled today", Owner, "weekly").Ok);
        Assert.True(Workspace.ResolveIssue(issue.Id, "settled today", Owner, "calendar").Ok);
    }

    [Fact]
    public void Resolve_Potency_NeedsNoChoice()
    {
        var issue = Db.InterpretationIssues.AsEnumerable().First(i => i.Key == "ISSUE-POTENCY");
        Assert.True(Workspace.ResolveIssue(issue.Id, "cadence confirmed with the manager", Owner).Ok);
        Assert.Equal("resolved", Db.InterpretationIssues.AsNoTracking().First(i => i.Key == "ISSUE-POTENCY").Status);
        Assert.Equal("", Db.InterpretationIssues.AsNoTracking().First(i => i.Key == "ISSUE-POTENCY").ResolvedChoice);
    }
}