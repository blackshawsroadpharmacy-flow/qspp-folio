using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Seed;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M7 surveillance round: the round brief (real counts over the REM evidence
/// requests) and the one-click response-pack build (whole round, verbatim asks, recorded
/// responses, linked evidence, sign-off). Rendering reads only — it must never change
/// stored state.</summary>
public class SurveillanceRoundTests : TestBase
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

    private void WriteRemoteSeed(params string[] items) =>
        File.WriteAllText(Path.Combine(TempRepo, "seed", "remote-assessment.json"),
            "{ \"requirements\": [" + string.Join(",", items) + "] }");

    [Fact]
    public void Summary_CountsMatchRealStates_AndFindsPlannedEvent()
    {
        WriteRemoteSeed(
            ItemJson("REM-01", "Vaccination procedure", "Provide evidence of the vaccination procedure."),
            ItemJson("REM-02", "Beyond-use dates", "Provide evidence of how BUDs are determined."));
        Seeds.ImportAll();
        var phId = Workspace.EnsurePharmacy().Id;
        var rems = Db.Requirements.AsNoTracking().Where(r => r.RefKind == RefKind.Surveillance)
            .ToDictionary(r => r.Ref, r => r.Id);

        // One item with evidence attached (proposed link counts: evidence exists), one in progress, one verified.
        var item = new EvidenceItem
        {
            Title = "Vaccination procedure SOP", Kind = EvidenceKind.PolicyOrSop,
            FileName = "vaccination-sop.pdf", CreatedByUserId = Owner!.UserId,
        };
        Db.EvidenceItems.Add(item);
        Db.EvidenceLinks.Add(new EvidenceLink
        {
            EvidenceItemId = item.Id, RequirementId = rems["REM-01"],
            State = LinkState.Proposed, CreatedByUserId = Owner.UserId,
        });
        Db.RequirementStates.AddRange(
            new RequirementState
            {
                RequirementId = rems["REM-01"], PharmacyId = phId,
                Readiness = RequirementReadiness.InProgress, OwnerUnassigned = false,
            },
            new RequirementState
            {
                RequirementId = rems["REM-02"], PharmacyId = phId,
                Readiness = RequirementReadiness.ManagerVerified, OwnerUnassigned = false,
            });
        Db.AssessmentEvents.Add(new AssessmentEvent
        {
            Kind = "Other", ScheduledDate = DateOnly.FromDateTime(DateTime.Today).AddDays(14),
            State = "Planned", Note = "remote round response window",
        });
        Db.SaveChanges();

        var round = Workspace.SurveillanceRound();

        Assert.Equal(2, round.Items);
        Assert.Equal(1, round.WithEvidence);
        Assert.Equal(1, round.InProgress);
        Assert.Equal(1, round.Verified);
        Assert.NotNull(round.NextEvent);
        Assert.Contains("Other", round.NextEvent);
        Assert.Contains("remote round response window", round.NextEvent);
    }

    [Fact]
    public void PackItems_ReactToNothing_WhenNoRemItemsExist()
    {
        var round = Workspace.SurveillanceRound();
        Assert.Equal(0, round.Items);
        Assert.Null(round.NextEvent);
        Assert.Empty(Workspace.SurveillanceRoundPackItems());
    }

    [Fact]
    public void RoundPack_ComposesRealPdf_WithoutChangingState()
    {
        WriteRemoteSeed(
            ItemJson("REM-01", "Vaccination procedure", "Provide evidence of the vaccination procedure."),
            ItemJson("REM-02", "Beyond-use dates", "Provide evidence of how BUDs are determined."));
        Seeds.ImportAll();
        var pharmacyId = Workspace.EnsurePharmacy().Id;
        var rem1 = Db.Requirements.AsNoTracking().Single(r => r.Ref == "REM-01");
        Db.RequirementStates.Add(new RequirementState
        {
            RequirementId = rem1.Id, PharmacyId = pharmacyId,
            Applicability = Applicability.Applicable,
            Readiness = RequirementReadiness.InProgress,
            Notes = "SOP drafted with the responsible pharmacist.",
            NextAction = "Upload the signed SOP and link it here.",
            OwnerUnassigned = false,
        });
        Db.SaveChanges();
        var auditsBefore = Db.AuditEvents.Count();
        var statesBefore = Db.RequirementStates.Count();

        var items = Workspace.SurveillanceRoundPackItems();
        Assert.Equal(2, items.Count);
        Assert.Equal("REM-01", items[0].Ref);
        Assert.Equal(2, items[0].ExpectedExamples.Count);
        Assert.Contains(items, i => i.ExpectedExamples.Any(e => e.Mandatory));

        var composer = new PdfComposer(Path.Combine(RepoRoot(), "assets", "fonts"));
        PdfComposer.ConfigureLicense();
        var model = new PdfComposer.RoundPackModel(
            "Burke Road Compounding",
            "QCPP remote assessment portal — surveillance round",
            "vendor questionnaire pack (REM items)",
            $"{items.Count} items · 0 with evidence · 0 verified",
            items, DateTime.UtcNow);
        var bytes = composer.ComposeRoundPack(model);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(bytes.Length > 10_000, $"PDF unexpectedly small: {bytes.Length} bytes");
        Assert.Equal(auditsBefore, Db.AuditEvents.Count());   // rendering is read-only
        Assert.Equal(statesBefore, Db.RequirementStates.Count());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "seed")) && Directory.Exists(Path.Combine(dir.FullName, "assets", "fonts")))
                return dir.FullName;
        throw new InvalidOperationException("repo root with assets not found from test base dir");
    }
}