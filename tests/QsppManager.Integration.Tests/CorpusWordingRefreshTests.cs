using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M7.2: the corpus seed's wording was capped at 400 characters mid-sentence at the
/// M1 build (trim400). DD-35 re-derives each quote as the requirement's full anchored source
/// segment; these tests pin the boot-time refresh that brings already-imported rows in line
/// WITHOUT disturbing anything the pharmacy has done (states, links, examples, owners).</summary>
public class CorpusWordingRefreshTests : TestBase
{
    private string SeedPath => Path.Combine(TempRepo, "seed", "requirements.json");

    private string StoredWord(string @ref) =>
        Db.Requirements.AsEnumerable().First(r => r.Ref == @ref).WordingExcerpt;

    private void RewriteSeedWith(bool longerRule10)
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
                wording = longerRule10
                    // full anchored segment as the enriched seed now carries (well past 400 chars)
                    ? "Rule 10 A Customer Satisfaction Survey will be shared with pharmacies once the interim report is issued, "
                      + "and pharmacies are encouraged to provide feedback on any aspect of their QSPP experience, including "
                      + "complaints, concerns, compliments or suggestions. Cancellation windows apply to booked assessments; "
                      + "a cancelled or rescheduled assessment within the notice period still incurs the assessor's travel "
                      + "expenses, payable by and invoiced to the Pharmacy as an Additional Assessment Expenses Invoice expense."
                    : "Rule 10 A Customer Satisfaction Survey will be shared with pharmacies once the interim report is issued, an..."
            },
            new
            {
                refKind = "Std", sourceDoc = "AS85000-2024-full.txt", sourceVersion = "AS 85000:2024",
                @ref = "2.2.3.8", title = "Consistency and potency testing", domain = 2,
                summary = longerRule10 ? "Summary updated by the enriched seed." : "Consistent compounding with potency testing.",
                evidenceExamples = new object[] { },
                interpretationFlags = new[] { "ISSUE-POTENCY" },
            },
        };
        File.WriteAllText(SeedPath, System.Text.Json.JsonSerializer.Serialize(req));
    }

    [Fact]
    public void WordingRefresh_UpdatesStoredQuotes_AndPreservesState()
    {
        // base state: 3 requirements imported from the original (cut) seed; pharmacy has worked
        Assert.Equal(3, Db.Requirements.Count());
        Assert.True(Workspace.SetNextAction(Db.Requirements.AsEnumerable().First(r => r.Ref == "Rule 10").Id,
            "Complete the feedback survey before the next assessment booking.", Owner).Ok);
        var stateBefore = Db.RequirementStates.AsEnumerable().Single(s => s.RequirementId == Db.Requirements.AsEnumerable().First(r => r.Ref == "Rule 10").Id);

        // the enriched seed arrives: fuller wording + one summary correction
        RewriteSeedWith(longerRule10: true);
        var refresh = Seeds.ImportAll();

        Assert.Equal(3, refresh.reqs);                          // no rows added
        Assert.Equal(3, Db.Requirements.Count());
        var r10 = Db.Requirements.AsEnumerable().First(r => r.Ref == "Rule 10");
        Assert.True(r10.WordingExcerpt.Length > 400);           // quote now full
        Assert.DoesNotContain("...", r10.WordingExcerpt);
        Assert.Equal("Summary updated by the enriched seed.",
            Db.Requirements.AsEnumerable().First(r => r.Ref == "2.2.3.8").PlainSummary);

        // nothing the pharmacy did got touched
        var stateAfter = Db.RequirementStates.AsEnumerable().Single(s => s.RequirementId == r10.Id);
        Assert.Equal(stateBefore.Id, stateAfter.Id);
        Assert.Equal("Complete the feedback survey before the next assessment booking.", stateAfter.NextAction);
        Assert.Equal(1, Db.AuditEvents.Count(e => e.Action == "corpus.wording_refresh"));
        var detail = Db.AuditEvents.AsEnumerable().First(e => e.Action == "corpus.wording_refresh");
        Assert.Contains("updated=2", detail.Details);           // wording for Rule 10 + summary for 2.2.3.8... wording row counts
    }

    [Fact]
    public void WordingRefresh_IsIdempotent_OnSecondBoot()
    {
        RewriteSeedWith(longerRule10: true);
        Seeds.ImportAll();
        var auditAfterFirst = Db.AuditEvents.Count(e => e.Action == "corpus.wording_refresh");
        Seeds.ImportAll();
        Assert.Equal(auditAfterFirst, Db.AuditEvents.Count(e => e.Action == "corpus.wording_refresh"));
        Assert.Equal(3, Db.Requirements.Count());
    }

    [Fact]
    public void WordingRefresh_KeepsExamplesIntact_AndRowsStable()
    {
        var idsBefore = Db.Requirements.AsEnumerable().Select(r => r.Id).OrderBy(x => x).ToList();
        RewriteSeedWith(longerRule10: true);
        Seeds.ImportAll();
        var idsAfter = Db.Requirements.AsEnumerable().Select(r => r.Id).OrderBy(x => x).ToList();
        Assert.Equal(idsBefore, idsAfter);                      // same identities — links stay valid
        Assert.Equal(3, Db.InterpretationIssues.Count());       // issues untouched
    }
}