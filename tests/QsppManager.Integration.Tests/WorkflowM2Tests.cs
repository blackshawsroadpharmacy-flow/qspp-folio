using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M2: FTS5 corpus search, journey snapshot counts, interpretation-issue
/// resolution rules, and the supersession impact review.</summary>
public class SearchTests : TestBase
{
    [Fact]
    public void BuildFtsQuery_QuotesTokens_AndPrefixesTheLast()
    {
        Assert.Equal("\"potency\" AND \"test*\"", CorpusSearch.BuildFtsQuery("potency test"));
        // Single-token queries also get the prefix star — "attestation" must find "attestations".
        Assert.Equal("\"mid-cycle*\"", CorpusSearch.BuildFtsQuery("mid-cycle"));
    }

    [Fact]
    public void Query_FindsRequirement_ByTitleToken_AndKeepsTheKind()
    {
        var search = new CorpusSearch(Db);
        search.EnsureIndex();
        search.RebuildIndex();

        var hits = search.Query("mid-cycle");
        var rule = hits.FirstOrDefault(h => h.Id == Workspace.RequirementRows(Owner).First(r => r.Ref == "Rule 8").Id);
        Assert.NotNull(rule);
        Assert.Equal("Rule", rule!.Kind);

        // evidence becomes searchable immediately after import (indexer upsert)
        var path = Path.Combine(Path.GetTempPath(), $"ev-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("cleaning log"));
        try
        {
            var (res, item) = Workspace.ImportEvidence(path, "Temperature log April", EvidenceKind.Record, null, null, false, "", Owner);
            Assert.True(res.Ok, res.Error);
            var evHits = search.Query("temperature");
            Assert.Contains(search.Query("Temperature"), h => h.Id == item!.Id); // indexer.IndexEvidence ran inside ImportEvidence
            _ = evHits;
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Query_FallsBackToLike_AndNeverThrowsOnOddInput()
    {
        var search = new CorpusSearch(Db);
        search.EnsureIndex();
        search.RebuildIndex();
        _ = search.Query("\"unclosed phrase and (stuff");   // malformed FTS syntax must not throw
        Assert.NotEmpty(search.Query("Rule"));              // LIKE fallback finds the seeded rules
    }
}

public class JourneyAndIssuesTests : TestBase
{
    [Fact]
    public void Journey_CountsReflectRealChanges()
    {
        var before = Workspace.Journey();
        Assert.Equal(0, before.ManagerVerified);

        var ruleRow = Workspace.RequirementRows(Owner).First(r => r.Ref == "Rule 8");
        Assert.True(Workspace.SetApplicability(ruleRow.Id, Applicability.Applicable, "Core program rule", Owner).Ok);
        Assert.True(Workspace.SetReadiness(ruleRow.Id, RequirementReadiness.InProgress, Owner).Ok);
        Assert.True(Workspace.SetReadiness(ruleRow.Id, RequirementReadiness.ReadyForReview, Owner).Ok);
        Assert.True(Workspace.SetReadiness(ruleRow.Id, RequirementReadiness.ManagerVerified, Owner).Ok);

        var after = Workspace.Journey();
        Assert.Equal(before.ApplicabilityDecided + 1, after.ApplicabilityDecided);
        Assert.Equal(before.ManagerVerified + 1, after.ManagerVerified);
        Assert.True(Workspace.AssignOwner(ruleRow.Id, Owner.UserId, Owner).Ok);
        Assert.Equal(before.OwnedCount + 1, Workspace.Journey().OwnedCount);
        Assert.Equal(before.TotalIssues, after.TotalIssues);
    }

    [Fact]
    public void ResolveIssue_RequiresANote_AndRecordsIt()
    {
        var issue = Workspace.Issues().First();
        var fail = Workspace.ResolveIssue(issue.Id, "", Owner);
        Assert.False(fail.Ok);

        Assert.True(Workspace.ResolveIssue(issue.Id, "Confirmed by pharmacist manager 20 Oct 2026: Program Rules wording is authoritative.", Owner).Ok);
        Assert.Equal("resolved", Workspace.Issues().First(i => i.Id == issue.Id).Status);

        // read-only reviewers cannot resolve
        var reviewer = new UserSession(Guid.NewGuid(), "rev", "Reviewer", UserRole.ReviewerReadOnly);
        var other = Workspace.Issues().First(i => i.Status != "resolved");
        Assert.False(Workspace.ResolveIssue(other.Id, "note", reviewer).Ok);
    }

    [Fact]
    public void SupersessionReview_ListsRetiredEvidence_WithAffectedLinks()
    {
        var rows = Workspace.RequirementRows(Owner).Where(r => r.RefKind == RefKind.Rule).Take(2).ToList();
        var path = Path.Combine(Path.GetTempPath(), $"sup-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("old version"));
        var path2 = Path.Combine(Path.GetTempPath(), $"sup2-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path2, System.Text.Encoding.ASCII.GetBytes("new version"));
        try
        {
            var (r1, oldItem) = Workspace.ImportEvidence(path, "Cleaning register v1", EvidenceKind.Record, null, null, false, "", Owner);
            var (r2, newItem) = Workspace.ImportEvidence(path2, "Cleaning register v2", EvidenceKind.Record, null, null, false, "", Owner);
            Assert.True(r1.Ok && r2.Ok);
            Assert.True(Workspace.LinkEvidence(oldItem!.Id, rows[0].Id, "whole evidence", null, null, "register", Owner).Ok);
            Assert.True(Workspace.LinkEvidence(oldItem.Id, rows[1].Id, "whole evidence", null, null, "register", Owner).Ok);
            Assert.True(Workspace.SetEvidenceReview(oldItem.Id, EvidenceReviewState.ReadyForReview, Owner, "").Ok);
            Assert.True(Workspace.SetEvidenceReview(oldItem.Id, EvidenceReviewState.ManagerVerified, Owner, "").Ok);
            Assert.Equal(LinkState.Active, Db.EvidenceLinks.First(l => l.EvidenceItemId == oldItem.Id && l.RequirementId == rows[1].Id).State);

            Assert.True(Workspace.SupersedeEvidence(oldItem.Id, newItem!.Id, "annual revision", Owner).Ok);

            var review = Workspace.SupersessionReview();
            var row = review.First(x => x.OldId == oldItem.Id);
            Assert.Equal(2, row.AffectedRequirementLinks);
            Assert.Equal("Cleaning register v2", row.ReplacementTitle);
            Assert.Equal(2, row.OpenTaskCount); // one re-link task per affected requirement, none completed
        }
        finally { try { File.Delete(path); File.Delete(path2); } catch { } }
    }
}