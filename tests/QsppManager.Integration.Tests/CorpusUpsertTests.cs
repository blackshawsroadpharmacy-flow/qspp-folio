using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-07 / plan 006: seed import is insert-missing. New requirements, issues and
/// documents reach existing installs; a requirement whose (RefKind, Ref) exists under a
/// different deterministic id is re-sourced in place (same id, states, owners and links
/// stay attached); resolved issues are never touched; a partial first run heals on the
/// next import without rewriting anything when the seed is unchanged (idempotent).</summary>
public class CorpusUpsertTests : TestBase
{
    /// <summary>The original three seed rows plus one extra row (same anonymous shape as
    /// WriteSeedRequirements), written over seed/requirements.json.</summary>
    private void WriteSeedWithExtra(string extraRef, string sourceDoc = "QSPP-Program-Rules-V1.0-Aug2026.txt", string sourceVersion = "V1.0, Aug 2026")
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
            new
            {
                refKind = "Rule", sourceDoc, sourceVersion,
                @ref = extraRef, title = $"New rule {extraRef}", domain = "Program",
                summary = "Arrives with a later pack.",
                evidenceExamples = new object[] { },
                interpretationFlags = new[] { "ISSUE-MIDCYCLE" },
            },
        };
        File.WriteAllText(Path.Combine(TempRepo, "seed", "requirements.json"),
            System.Text.Json.JsonSerializer.Serialize(req));
    }

    /// <summary>The three original rows, with Rule 8 re-sourced to a new document+version.</summary>
    private void WriteSeedRule8Resourced()
    {
        var req = new object[]
        {
            new
            {
                refKind = "Rule", sourceDoc = "QSPP-Program-Rules-V1.1-Dec2026.txt", sourceVersion = "V1.1, Dec 2026",
                @ref = "Rule 8", title = "Mid-Cycle Review (updated)", domain = "Program",
                summary = "Mid-cycle review window reworded in the December pack.",
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

    [Fact]
    public void Reimport_AddsNewRequirement_KeepsExistingStateAndLinks()
    {
        var rule8 = Db.Requirements.First(r => r.Ref == "Rule 8");
        Assert.True(Workspace.SetReadiness(rule8.Id, RequirementReadiness.InProgress, Owner).Ok);

        WriteSeedWithExtra("Rule 11");
        var result = Seeds.ImportAll();

        Assert.Equal(4, Db.Requirements.Count());
        Assert.Equal(RequirementReadiness.InProgress,
            Db.RequirementStates.First(s => s.RequirementId == rule8.Id).Readiness);
        Assert.True(Db.Requirements.Any(r => r.Ref == "Rule 11"));
        // Reimport is recorded as added, not first import
        var added = Db.AuditEvents.AsEnumerable().LastOrDefault(e => e.Action == "corpus.added");
        Assert.NotNull(added);
    }

    [Fact]
    public void Reimport_NewSourceVersion_UpdatesInPlace_KeepsIdAndState()
    {
        var rule8 = Db.Requirements.First(r => r.Ref == "Rule 8");
        var originalId = rule8.Id;
        Assert.True(Workspace.AssignOwner(rule8.Id, Owner.UserId, Owner).Ok);

        WriteSeedRule8Resourced();
        var result = Seeds.ImportAll();

        var resourced = Db.Requirements.First(r => r.Ref == "Rule 8");
        Assert.Equal(originalId, resourced.Id);
        Assert.Equal("V1.1, Dec 2026", resourced.SourceVersion);
        Assert.Equal("QSPP-Program-Rules-V1.1-Dec2026.txt", resourced.SourceDocFile);
        Assert.False(resourced.AnchorVerified);
        Assert.Null(resourced.AnchorPage);
        var st = Db.RequirementStates.First(s => s.RequirementId == originalId);
        Assert.False(st.OwnerUnassigned);
        Assert.Equal(Owner.UserId, st.OwnerUserId);
        Assert.Equal(3, Db.Requirements.Count());
        // One per-row re-source line plus one run summary (added == 0, resourced > 0).
        Assert.Single(Db.AuditEvents.AsEnumerable().Where(e => e.Action == "corpus.resourced" && e.EntityId == originalId.ToString()));
        Assert.Contains(Db.AuditEvents.AsEnumerable(), e => e.Action == "corpus.resourced" && e.EntityId == "*");
    }

    [Fact]
    public void Reimport_IsIdempotent_ZeroWritesWhenUnchanged()
    {
        Seeds.ImportAll();
        var auditsBefore = Db.AuditEvents.AsEnumerable().Count(e =>
            e.Action is "corpus.added" or "corpus.resourced" or "issues.added" or "document.imported");

        Seeds.ImportAll();
        Seeds.ImportAll();

        var auditsAfter = Db.AuditEvents.AsEnumerable().Count(e =>
            e.Action is "corpus.added" or "corpus.resourced" or "issues.added" or "document.imported");
        Assert.Equal(auditsBefore, auditsAfter);
    }

    [Fact]
    public void Reimport_AddsNewIssue_KeepsResolvedStatus()
    {
        var cancel = Db.InterpretationIssues.First(i => i.Key == "ISSUE-CANCELWINDOW");
        // FIX-08: this issue bears deadlines, so resolving needs the structured choice too.
        Assert.True(Workspace.ResolveIssue(cancel.Id, "confirmed with pharmacist manager: 10 business days", Owner, "business").Ok);

        var issues = new object[]
        {
            new { key = "ISSUE-CANCELWINDOW", title = "Assessment cancellation window: 10 business days vs 10 calendar days", conservativePlanning = "10 business days (Program Rule 10), the strictest reading", sources = new object[] { }, retrievedDate = "2026-10-19", packVersion = "qspp-pack 26 Sep 2026" },
            new { key = "ISSUE-MIDCYCLE", title = "Mid-Cycle Review window wording differs across sources", conservativePlanning = "Schedule within months 15-21", sources = new object[] { }, retrievedDate = "2026-10-19", packVersion = "qspp-pack 26 Sep 2026" },
            new { key = "ISSUE-POTENCY", title = "Potency testing cadence wording", conservativePlanning = "Per person per year: two drugs and two dosage forms", sources = new object[] { }, retrievedDate = "2026-10-19", packVersion = "qspp-pack 26 Sep 2026" },
            new { key = "ISSUE-NEW", title = "New issue from a later pack", conservativePlanning = "Plan conservatively while unresolved", sources = new object[] { }, retrievedDate = "2026-11-01", packVersion = "qspp-pack 26 Dec 2026" },
        };
        File.WriteAllText(Path.Combine(TempRepo, "seed", "issues.json"),
            System.Text.Json.JsonSerializer.Serialize(issues));

        Seeds.ImportAll();

        Assert.Equal(4, Db.InterpretationIssues.Count());
        var stillResolved = Db.InterpretationIssues.First(i => i.Key == "ISSUE-CANCELWINDOW");
        Assert.Equal("resolved", stillResolved.Status);
        Assert.True(Db.InterpretationIssues.Any(i => i.Key == "ISSUE-NEW"));
    }

    [Fact]
    public void Reimport_RetriesAfterPartialFailure()
    {
        // Simulate a first run that died mid-import: only Rule 8 made it.
        var keep = Db.Requirements.First(r => r.Ref == "Rule 8").Id;
        Db.Requirements.RemoveRange(Db.Requirements.Where(r => r.Id != keep));
        Db.SaveChanges();
        Assert.Equal(1, Db.Requirements.Count());

        Seeds.ImportAll();

        Assert.Equal(3, Db.Requirements.Count());
        Assert.All(new[] { "Rule 8", "Rule 10", "2.2.3.8" }, @ref => Assert.True(Db.Requirements.Any(r => r.Ref == @ref), @ref));
    }
}