using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-09 / plan 008: hints import with the seed's underscore spelling, existing
/// databases are repaired on the next import, and SuggestApplicability is a pure
/// read-only suggestion while the row is Unknown.</summary>
public class ApplicabilityScopeTests : TestBase
{
    /// <summary>One Std row carrying the seed's underscore spelling of the hint.</summary>
    private void WriteCompoundingHintSeed()
    {
        var req = new object[]
        {
            new
            {
                refKind = "Rule", sourceDoc = "QSPP-Program-Rules-V1.0-Aug2026.txt", sourceVersion = "V1.0, Aug 2026",
                @ref = "Rule 8", title = "Mid-Cycle Review", domain = "Program",
                summary = "Mid-cycle review in the second year.",
                applicabilityHint = "if_compounding",
                evidenceExamples = new object[] { },
                interpretationFlags = new[] { "ISSUE-MIDCYCLE" },
            },
        };
        File.WriteAllText(Path.Combine(TempRepo, "seed", "requirements.json"),
            System.Text.Json.JsonSerializer.Serialize(req));
    }

    [Fact]
    public void ConditionalHints_ImportWithUnderscoreSpelling()
    {
        // Force the first-run path again with a hint-bearing seed.
        Db.Requirements.RemoveRange(Db.Requirements.ToList());
        Db.SaveChanges();
        WriteCompoundingHintSeed();

        Seeds.ImportAll();

        var row = Db.Requirements.First(r => r.Ref == "Rule 8");
        Assert.Equal(ApplicabilityHint.IfCompounding, row.ApplicabilityHint);
    }

    [Fact]
    public void HintRefresh_RepairsExistingRows()
    {
        var row = Db.Requirements.First(r => r.Ref == "Rule 8");
        row.ApplicabilityHint = ApplicabilityHint.Always;   // saved by the old parser
        Db.SaveChanges();
        WriteCompoundingHintSeed();

        Seeds.ImportAll();

        Assert.Equal(ApplicabilityHint.IfCompounding, Db.Requirements.First(r => r.Ref == "Rule 8").ApplicabilityHint);
        Assert.Equal(1, Db.AuditEvents.AsEnumerable().Count(e => e.Action == "corpus.hint_refresh"));

        Seeds.ImportAll();
        Assert.Equal(1, Db.AuditEvents.AsEnumerable().Count(e => e.Action == "corpus.hint_refresh"));   // second run: nothing
    }

    [Fact]
    public void SuggestApplicability_OnlyWhenUnknown_AndWritesNothing()
    {
        var row = Db.Requirements.First(r => r.Ref == "Rule 8");
        row.ApplicabilityHint = ApplicabilityHint.IfCompounding;
        Db.SaveChanges();
        Workspace.SuggestApplicability(row.Id);   // materialises the pharmacy row (EnsurePharmacy)
        var ph = Db.Pharmacies.First();
        ph.ServiceScopeFlags = (long)ServiceScopeFlag.ComplexCompounding;
        Db.SaveChanges();

        var auditsBefore = Db.AuditEvents.Count();
        var statesBefore = Db.RequirementStates.Count();

        var sug = Workspace.SuggestApplicability(row.Id);
        var sug2 = Workspace.SuggestApplicability(row.Id);

        Assert.Equal((Applicability.Applicable, "The pharmacy offers compounding (Settings, services)."), sug);
        Assert.Equal(sug, sug2);
        Assert.Equal(auditsBefore, Db.AuditEvents.Count());
        Assert.Equal(statesBefore, Db.RequirementStates.Count());

        Assert.True(Workspace.SetApplicability(row.Id, Applicability.Applicable, "confirmed by manager", Owner).Ok);
        Assert.Null(Workspace.SuggestApplicability(row.Id));   // decided: no more suggestions
    }
}