using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Seed;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>seed/remote-assessment.json import: the QCPP remote surveillance
/// assessment's evidence requests arrive as tracked Surveillance requirements. Unlike the
/// one-shot corpus import this importer UPSERTS on every boot — idempotent wording updates
/// must never disturb an owner's already-recorded evidence links, readiness states or
/// decisions (the later-reprint scenario).</summary>
public class RemoteAssessmentSeedTests : TestBase
{
    private static string ItemJson(string rem, string title, string wording) => $$"""
        {
          "refKind": "SURVEILLANCE",
          "ref": "{{rem}}",
          "title": "{{title}}",
          "sourceDoc": "QSPP Remote Assessment - October 2026.pdf",
          "sourceVersion": "QCPP remote assessment portal — surveillance round",
          "domain": "2",
          "applicabilityHint": "always",
          "wording": "{{wording}}",
          "summary": "{{title}}",
          "evidenceExamples": [
            { "text": "A written protocol is uploaded.", "mandatory": true },
            { "text": "Optional supporting record.", "mandatory": false }
          ],
          "priority": "October 2026 remote surveillance assessment"
        }
        """;

    /// <summary>Write a remote-assessment seed into the temp repo. The TestBase ctor runs
    /// an import WITHOUT this file, so these items first arrive on the test's own import.</summary>
    private void WriteRemoteSeed(params string[] items) =>
        File.WriteAllText(Path.Combine(TempRepo, "seed", "remote-assessment.json"),
            "{ \"requirements\": [" + string.Join(",", items) + "] }");

    [Fact]
    public void Import_AddsItems_AsSurveillanceRequirements()
    {
        WriteRemoteSeed(
            ItemJson("REM-01", "Vaccination procedure", "Provide evidence of the vaccination procedure."),
            ItemJson("REM-02", "Beyond-use dates", "Provide evidence of how BUDs are determined."));

        Seeds.ImportAll();

        var rems = Db.Requirements.Where(r => r.RefKind == RefKind.Surveillance).OrderBy(r => r.Ref).ToList();
        Assert.Equal(2, rems.Count);
        Assert.All(rems, r => Assert.StartsWith("REM-", r.Ref));
        Assert.Equal("Vaccination procedure", rems[0].Title);
        var examples = Db.RequirementEvidenceExamples.Where(x => x.RequirementId == rems[0].Id).ToList();
        Assert.Equal(2, examples.Count);
        Assert.Contains(examples, e => e.Mandatory);
        Assert.Contains(examples, e => !e.Mandatory);
    }

    [Fact]
    public void Import_IsIdempotent_RebootAddsNothing()
    {
        WriteRemoteSeed(ItemJson("REM-01", "Vaccination procedure", "Provide evidence of the vaccination procedure."));
        Seeds.ImportAll();
        var before = Db.Requirements.Count();

        Seeds.ImportAll();

        Assert.Equal(before, Db.Requirements.Count());
        var remId = Db.Requirements.Where(r => r.RefKind == RefKind.Surveillance).Select(r => r.Id).Single();
        Assert.Equal(2, Db.RequirementEvidenceExamples.Count(x => x.RequirementId == remId));
    }

    [Fact]
    public void Import_WordingRevision_UpdatesDisplay_KeepsOwnerState()
    {
        WriteRemoteSeed(ItemJson("REM-01", "Vaccination procedure", "Provide evidence of the vaccination procedure."));
        Seeds.ImportAll();

        var rem = Db.Requirements.Single(r => r.RefKind == RefKind.Surveillance);
        Db.RequirementStates.Add(new RequirementState
        {
            RequirementId = rem.Id,
            PharmacyId = Workspace.EnsurePharmacy().Id,
            Applicability = Applicability.Applicable,
            ApplicabilityReason = "We vaccinate.",
            Readiness = RequirementReadiness.ManagerVerified,
            OwnerUnassigned = false,
        });
        Db.SaveChanges();

        // A later reprint revises the ask and adds a new item.
        WriteRemoteSeed(
            ItemJson("REM-01", "Vaccination procedure (revised)",
                "Provide evidence of the vaccination procedure including screening and consent."),
            ItemJson("REM-03", "Document review", "Provide the document review system."));
        Seeds.ImportAll();

        Assert.Equal(2, Db.Requirements.Count(r => r.RefKind == RefKind.Surveillance));
        var updated = Db.Requirements.AsNoTracking().Single(r => r.Ref == "REM-01");
        Assert.Equal("Vaccination procedure (revised)", updated.Title);
        Assert.Contains("screening and consent", updated.WordingExcerpt);

        // The owner's recorded state survived the wording revision.
        var state = Db.RequirementStates.AsEnumerable().Single(s => s.RequirementId == updated.Id);
        Assert.Equal(RequirementReadiness.ManagerVerified, state.Readiness);
        Assert.Equal("We vaccinate.", state.ApplicabilityReason);
    }
}