using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Infrastructure.Backup;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M4: assessment events, findings, and backup/restore with manifest hashes.</summary>
public class WorkflowM4Tests : TestBase
{
    [Fact]
    public void AssessmentEvent_OutcomeReceived_RequiresAnExternalReference()
    {
        Assert.True(Workspace.AddAssessmentEvent("Onsite", new DateOnly(2026, 12, 1), "Booked with EY, conf #EY-77", Owner).Ok);
        var ev = Workspace.AssessmentEvents().First();

        Assert.False(Workspace.SetAssessmentEventState(ev.Id, "OutcomeReceived", "", Owner).Ok,
            "an outcome cannot be recorded without the report/letter reference");
        Assert.True(Workspace.SetAssessmentEventState(ev.Id, "Completed", "", Owner).Ok);
        Assert.True(Workspace.SetAssessmentEventState(ev.Id, "OutcomeReceived", "QSPP report 2026-114", Owner).Ok);
        Assert.Equal(("OutcomeReceived", "QSPP report 2026-114"),
            (Workspace.AssessmentEvents().First(e => e.Id == ev.Id).State,
             Workspace.AssessmentEvents().First(e => e.Id == ev.Id).OutcomeRef));

        var reviewer = new UserSession(Guid.NewGuid(), "rev", "Reviewer", UserRole.ReviewerReadOnly);
        Assert.False(Workspace.SetAssessmentEventState(ev.Id, "Completed", "", reviewer).Ok);
    }

    [Fact]
    public void Finding_ClosesOnlyWithCorrectiveAction_AndRecordsTheChain()
    {
        Assert.True(Workspace.AddAssessmentEvent("Onsite", new DateOnly(2026, 12, 1), "", Owner).Ok);
        var ev = Workspace.AssessmentEvents().First();
        Assert.True(Workspace.AddFinding(ev.Id, "2.2.3.8", "Potency log missing two months of entries.", "moderate", Owner.UserId, Owner).Ok);
        var f = Workspace.FindingsFor(ev.Id).First();

        Assert.False(Workspace.SetFindingState(f.Id, "Closed", "", Owner).Ok);
        Assert.True(Workspace.SetFindingState(f.Id, "Actioned", "Monitoring catch-up schedule set; owner assigned.", Owner).Ok);
        Assert.True(Workspace.SetFindingState(f.Id, "Closed", "Back-entries completed and verified by the pharmacist.", Owner).Ok);
        Assert.Equal("Closed", Workspace.FindingsFor(ev.Id).First(x => x.Id == f.Id).State);
    }

    [Fact]
    public void BackupRestore_Roundtrip_PreservesData_AndDetectsTampering()
    {
        // Give the database some real content, including a managed attachment.
        var txt = Path.Combine(Path.GetTempPath(), $"bkp-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(txt, System.Text.Encoding.ASCII.GetBytes("restore-roundtrip"));
        var (_, evidence) = Workspace.ImportEvidence(txt, "Restore probe", EvidenceKind.Record, null, null, false, "", Owner);
        Assert.NotNull(evidence);

        var backupsDir = Path.Combine(Path.GetTempPath(), $"qspp-bkps-{Guid.NewGuid():N}");
        var svc = new BackupService(Db, new AttachmentStore(Path.GetTempPath()), backupsDir);
        // NOTE: the service inventories the attachment store passed in — construct a second
        // service with the test's actual store root so attachments are included.
        var storeRoot = Directory.GetParent(evidence!.StoredRelativePath)!.Parent!.Parent!.FullName; // attachments root
        svc = new BackupService(Db, new AttachmentStore(storeRoot), backupsDir);

        var (ok, zipPath, err, shaHead) = svc.CreateBackup("m4-test");
        Assert.True(ok, $"backup failed: {err}");
        Assert.True(File.Exists(zipPath));
        Assert.False(string.IsNullOrEmpty(shaHead));

        // Happy path: restore into a fresh directory and reopen the database from there.
        var target = Path.Combine(Path.GetTempPath(), $"qspp-rstr-{Guid.NewGuid():N}");
        var (rok, rerr, restoredDb, restoredFiles) = svc.Restore(zipPath, target);
        Assert.True(rok, $"restore failed: {rerr}");
        Assert.True(restoredFiles >= 2, $"expected db + attachment + manifest, got {restoredFiles}");
        var roptions = new DbContextOptionsBuilder<QsppDbContext>().UseSqlite($"Data Source={restoredDb}").Options;
        using var rdb = new QsppDbContext(roptions);
        Assert.Equal(1, rdb.EvidenceItems.Count(e => e.Title == "Restore probe"));
        Assert.Equal(Db.Users.Count(), rdb.Users.Count());

        // Tamper path: rewrite the archived database inside the zip → restore must refuse.
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("qspp.db")!;
            byte[] bytes;
            using (var s = entry.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            bytes[100] ^= 0xFF;
            entry.Delete();
            var newEntry = archive.CreateEntry("qspp.db", System.IO.Compression.CompressionLevel.Optimal);
            using (var os = newEntry.Open()) os.Write(bytes, 0, bytes.Length);
        }
        var (tok, terr, _, _) = svc.Restore(zipPath, Path.Combine(Path.GetTempPath(), $"qspp-tp-{Guid.NewGuid():N}"));
        Assert.False(tok, "a tampered archive must be refused");
        Assert.Contains("mismatch", terr, StringComparison.OrdinalIgnoreCase);

        try { Directory.Delete(backupsDir, true); Directory.Delete(target, true); File.Delete(txt); } catch { }
        _ = ok;
    }
}