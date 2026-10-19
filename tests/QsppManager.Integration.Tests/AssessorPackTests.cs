using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using QsppManager.Infrastructure.Backup;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M4 remainder: assessor pack export (scope + redaction exclusions + manifest),
/// reviewer-filtered search, injectable clock, VIC holiday arithmetic.</summary>
public class AssessorPackTests : TestBase
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "seed")) && Directory.Exists(Path.Combine(dir.FullName, "assets", "fonts")))
                return dir.FullName;
        throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void AssessorPack_EmptyScope_FailsHonest_ThenVerifiedScope_ExportsWithExclusions()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), $"qspp-pack-{Guid.NewGuid():N}");
        try
        {
            var composer = new PdfComposer(FindRepoRoot());
            PdfComposer.ConfigureLicense();
            var store = new AttachmentStore(AttachmentRoot);
            var svc = new AssessorPackService(Workspace, Db, composer, store, outRoot);

            // Empty scope: honest refusal, not an empty zip.
            var (ok0, _, err0, _, _, _) = svc.Export(Owner, "empty");
            Assert.False(ok0);
            Assert.Contains("Nothing is in scope yet", err0);

            // Bring one rule requirement into scope.
            var row = Workspace.RequirementRows(Owner).First(r => r.Ref == "Rule 8");
            Workspace.SetApplicability(row.Id, Applicability.Applicable, "core rule", Owner);
            foreach (var t in new[] { RequirementReadiness.InProgress, RequirementReadiness.ReadyForReview, RequirementReadiness.ManagerVerified })
                Assert.True(Workspace.SetReadiness(row.Id, t, Owner).Ok);

            // Evidence A: clean file, manager-verified → included.
            var a = Path.Combine(Path.GetTempPath(), $"pk-a-{Guid.NewGuid():N}.txt");
            File.WriteAllBytes(a, System.Text.Encoding.ASCII.GetBytes("temperature log content"));
            var (_, evA) = Workspace.ImportEvidence(a, "Fridge log", EvidenceKind.Record, null, null, false, "", Owner);
            var pathA = Path.Combine(Path.GetTempPath(), $"pk-a2-{Guid.NewGuid():N}.txt");
            File.WriteAllBytes(pathA, System.Text.Encoding.ASCII.GetBytes("temperature log content"));
            var (_, evA2) = Workspace.ImportEvidence(pathA, "Fridge log v2", EvidenceKind.Record, null, null, false, "", Owner);
            Workspace.LinkEvidence(evA!.Id, row.Id, "", null, null, "log", Owner);
            Workspace.LinkEvidence(evA2!.Id, row.Id, "", null, null, "log", Owner);
            Workspace.SetEvidenceReview(evA2.Id, EvidenceReviewState.ReadyForReview, Owner, "");
            Workspace.SetEvidenceReview(evA2.Id, EvidenceReviewState.ManagerVerified, Owner, "");

            // Evidence B: patient data WITH attestation → included (attestation is the gate).
            var b = Path.Combine(Path.GetTempPath(), $"pk-b-{Guid.NewGuid():N}.txt");
            File.WriteAllBytes(b, System.Text.Encoding.ASCII.GetBytes("redacted patient list"));
            var (_, evB) = Workspace.ImportEvidence(b, "Patient list (redacted)", EvidenceKind.Record, null, null, true,
                "Names/DOB removed before import; reviewed by owner 20 Oct 2026", Owner);
            Workspace.LinkEvidence(evB!.Id, row.Id, "", null, null, "log", Owner);
            Workspace.SetEvidenceReview(evB.Id, EvidenceReviewState.ReadyForReview, Owner, "");
            Workspace.SetEvidenceReview(evB.Id, EvidenceReviewState.ManagerVerified, Owner, "");

            var (ok, zipPath, err, reqCount, files, exclusions) = svc.Export(Owner, "internal dry-run");
            Assert.True(ok, $"pack failed: {err}");
            Assert.Equal(1, reqCount);
            Assert.True(File.Exists(zipPath));
            Assert.True(files >= 1);

            // Unzip and verify structure + manifest honesty.
            var extract = Path.Combine(Path.GetTempPath(), $"qspp-pk-x-{Guid.NewGuid():N}");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extract);
            Assert.True(File.Exists(Path.Combine(extract, "manifest.json")), $"no manifest; files: {string.Join(", ", Directory.GetFiles(extract, "*", SearchOption.AllDirectories))}");
            Assert.True(Directory.Exists(Path.Combine(extract, "checklists")), $"no checklists dir; files: {string.Join(", ", Directory.GetFiles(extract, "*", SearchOption.AllDirectories))}");
            var checklistFiles = Directory.GetFiles(Path.Combine(extract, "checklists"));
            Assert.True(File.Exists(Path.Combine(extract, "checklists", "Rule 8.pdf")), $"expected Rule 8.pdf; got: {string.Join(", ", checklistFiles)}");
            var manifestJson = File.ReadAllText(Path.Combine(extract, "manifest.json"));
            Assert.Contains("Rule 8", manifestJson);
            Assert.Empty(exclusions); // clean + attested evidence: nothing excluded
            Assert.Equal(2, Directory.GetFiles(Path.Combine(extract, "evidence")).Length); // MV evidence A2 + attested B

            // A ManagerVerified item whose managed file was lost (e.g. admin deleted the
            // attachments dir) is EXCLUDED with an honest recorded reason.
            var evD = Workspace.EvidenceById(evA2!.Id)!;
            var lostPath = Path.Combine(AttachmentRoot, evD.StoredRelativePath);
            Assert.True(File.Exists(lostPath), $"expected managed file at {lostPath}");
            File.Delete(lostPath);
            var (ok2, zip2, err2, _, files2, exclusions2) = svc.Export(Owner, "with lost file");
            Assert.True(ok2, err2);
            Assert.Single(exclusions2);
            Assert.Contains("file missing from managed storage", exclusions2[0]);
            var extract2 = Path.Combine(Path.GetTempPath(), $"qspp-pk-y-{Guid.NewGuid():N}");
            System.IO.Compression.ZipFile.ExtractToDirectory(zip2, extract2);
            Assert.Contains("file missing from managed storage", File.ReadAllText(Path.Combine(extract2, "manifest.json")));
            _ = evA; _ = evB; _ = files2;
        }
        finally
        {
            try { Directory.Delete(outRoot, true); } catch { }
        }
    }

    [Fact]
    public void Reviewer_Search_HidesPatientDataEvidence_WhileOthersSeeIt()
    {
        var path1 = Path.Combine(Path.GetTempPath(), $"flt-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path1, System.Text.Encoding.ASCII.GetBytes("patient rows"));
        var path2 = Path.Combine(Path.GetTempPath(), $"flt2-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path2, System.Text.Encoding.ASCII.GetBytes("equipment check"));
        try
        {
            var (_, evP) = Workspace.ImportEvidence(path1, "UniquePatientList-QKZ", EvidenceKind.Record, null, null, true,
                "redacted; reviewed by owner", Owner);
            var (_, evN) = Workspace.ImportEvidence(path2, "UniqueEquipmentLog-QKZ", EvidenceKind.Record, null, null, false, "", Owner);

            var search = new CorpusSearch(Db);
            search.EnsureIndex();
            search.RebuildIndex();

            Assert.Contains(search.Query("UniquePatientList-QKZ"), h => h.Id == evP!.Id);
            Assert.DoesNotContain(search.Query("UniquePatientList-QKZ", hidePatientDataEvidence: true), h => h.Id == evP!.Id);
            Assert.Contains(search.Query("UniqueEquipmentLog-QKZ", hidePatientDataEvidence: true), h => h.Id == evN!.Id);
        }
        finally { try { File.Delete(path1); File.Delete(path2); } catch { } }
    }

    [Fact]
    public void Clock_Pin_ChangesGenerationHorizon_AndIsRestorable()
    {
        try
        {
            Clock.Pin(new DateOnly(2026, 10, 20));
            Assert.True(Workspace.CreateRecurringDef("Weekly def", "", null, null, Owner.UserId,
                RecurrenceCadence.Weekly, 0, new DateOnly(2026, 10, 5), TaskPriority.Normal, Owner).Ok);
            var generated = Workspace.GenerateRecurringOccurrences(horizonDate: null, Owner);
            // Oct 5 → Nov 3 weekly with default horizon (pinned today + 14 = Nov 3): 5 occurrences
            Assert.Equal(5, generated);
            Assert.All(Workspace.OpenTasks().Where(t => t.RecurrenceDefId != null),
                t => Assert.True(t.DueDate <= new DateOnly(2026, 11, 3)));
        }
        finally { Clock.Unpin(); }
    }

    [Fact]
    public void VicHolidays_BusinessDaysSkip_GazettedDays_WithProvenance()
    {
        // Christmas Fri 25 Dec 2026 + Boxing Day observed Mon 28 Dec → Friday 24 Dec + 1 business day = Tue 29 Dec
        Assert.Equal(new DateOnly(2026, 12, 29),
            DeadlineCalculator.AddBusinessDays(new DateOnly(2026, 12, 24), 1, VicHolidays.IsNonBusinessDay));
        // Melbourne Cup day (Tue 3 Nov 2026) is a VIC public holiday
        Assert.True(VicHolidays.IsHoliday(new DateOnly(2026, 11, 3)));
        Assert.False(VicHolidays.IsHoliday(new DateOnly(2026, 11, 4)));
    }
}