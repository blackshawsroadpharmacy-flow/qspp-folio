using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;

namespace QsppManager.Infrastructure.Backup;

/// <summary>Assessor pack export (M4 · DD-25): one evidence checklist PDF per in-scope
/// requirement (Manager-verified/assessor-accepted, applicable), plus the currently-linked
/// verified evidence files that pass the redaction review, plus manifest.json with
/// per-file sha256 — the same tamper-evident pattern as backups. Patient-data evidence
/// WITHOUT an attestation is excluded and the exclusion is recorded in the manifest.</summary>
public class AssessorPackService(QsppWorkspace workspace, QsppDbContext db, PdfComposer composer,
    AttachmentStore store, string outRoot)
{
    public (bool ok, string zipPath, string error, int requirements, int evidenceFiles, List<string> exclusions) Export(
        UserSession session, string label)
    {
        try
        {
            var rows = workspace.RequirementRows(session);
            var inScope = rows
                .Where(r => r.Readiness is RequirementReadiness.ManagerVerified or RequirementReadiness.AssessorAccepted
                            && r.Applicability != Domain.Applicability.NotApplicable)
                .OrderBy(r => r.Ref).ToList();
            if (inScope.Count == 0)
                return (false, "", "Nothing is in scope yet: the pack includes requirements that are manager-verified (or assessor-accepted) and applicable. Verify more requirements first.", 0, 0, []);

            var exclusions = new List<string>();
            var staging = Path.Combine(Path.GetTempPath(), $"qspp-pack-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            var files = new List<object>();
            var includedEvidence = 0;
            var reqRefs = new List<string>();

            foreach (var row in inScope)
            {
                var detail = workspace.RequirementDetail(row.Id, session);
                if (detail == null) continue;
                reqRefs.Add(row.Ref);

                var safeRef = row.Ref.SafeName(60);
                var model = new PdfComposer.ChecklistModel(
                    workspace.EnsurePharmacy().Name, detail.Requirement.Ref, detail.Requirement.Title,
                    detail.Requirement.PlainSummary, detail.Requirement.WordingExcerpt,
                    detail.Requirement.SourceDocFile, detail.Requirement.SourceVersion,
                    AnchorNote(detail),
                    detail.State.Readiness, detail.State.Applicability, detail.OwnerName,
                    detail.State.NextAction,
                    detail.Links.Select(l => new PdfComposer.EvidenceRow("", l.EvidenceTitle, l.Kind.ToString(),
                        l.FileName, l.EffectiveFrom is { } ef ? ef.ToString("dd MMM yyyy") : "", l.ReviewState.ToString())).ToList(),
                    detail.Tasks.Select(t => new PdfComposer.TasksRow(t.Title,
                        "", t.DueDate?.ToString("dd MMM yyyy") ?? "", t.State.ToString())).ToList(),
                    "completed", DateTime.UtcNow);

                var pdfPath = Path.Combine(staging, "checklists", $"{safeRef}.pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
                File.WriteAllBytes(pdfPath, composer.Compose(model));
                files.Add(new { path = Path.GetRelativePath(staging, pdfPath).Replace('\\', '/'), sha256 = Hash(pdfPath), bytes = new FileInfo(pdfPath).Length });

                // Evidence copies: manager-verified links only; patient-data requires its attestation.
                foreach (var link in detail.Links)
                {
                    if (link.ReviewState != EvidenceReviewState.ManagerVerified || link.State == LinkState.Superseded) continue;
                    var e = db.EvidenceItems.AsNoTracking().FirstOrDefault(x => x.Id == link.EvidenceItemId);
                    if (e == null) continue;
                    // Redaction gate BEFORE file lookup: patient-data without an attestation is
                    // excluded even when the managed file is (or was) missing.
                    if (e.ContainsPatientData && string.IsNullOrWhiteSpace(e.PatientDataAttestation))
                    {
                        exclusions.Add($"{row.Ref}: evidence '{e.Title}' contains patient data without a redaction attestation — excluded");
                        continue;
                    }
                    var src = store.AbsolutePath(e.StoredRelativePath);
                    if (!File.Exists(src)) { exclusions.Add($"{row.Ref}: evidence '{e.Title}' file missing from managed storage"); continue; }
                    var dest = Path.Combine(staging, "evidence", $"{e.Sha256[..12]}-{e.FileName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(src, dest, overwrite: true);
                    files.Add(new { path = Path.GetRelativePath(staging, dest).Replace('\\', '/'), sha256 = Hash(dest), bytes = new FileInfo(dest).Length });
                    includedEvidence++;
                }
            }

            var manifest = JsonSerializer.Serialize(new
            {
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                format = "qspp-assessor-pack-1",
                pharmacy = workspace.EnsurePharmacy().Name,
                label,
                requirementRefs = reqRefs,
                redactionExclusions = exclusions,
                files,
            }, new JsonSerializerOptions { WriteIndented = true });
            var manifestPath = Path.Combine(staging, "manifest.json");
            File.WriteAllText(manifestPath, manifest);
            files.Add(new { path = "manifest.json", sha256 = Hash(manifestPath), bytes = new FileInfo(manifestPath).Length });

            Directory.CreateDirectory(outRoot);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var zipPath = Path.Combine(outRoot, $"assessor-pack-{stamp}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                foreach (var f in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                    archive.CreateEntryFromFile(f, Path.GetRelativePath(staging, f).Replace('\\', '/'), CompressionLevel.Optimal);

            try { Directory.Delete(staging, recursive: true); } catch { }
            return (true, zipPath, "", inScope.Count, includedEvidence, exclusions);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message, 0, 0, []);
        }
    }

    private static string AnchorNote(RequirementDetail detail)
    {
        var d = detail.Requirement;
        return (d.AnchorPage is { } pg
            ? $"PDF p{pg}{(d.AnchorVerified ? " (verified)" : " (unverified)")}"
            : d.AnchorApproximate ? $"anchor approx. lines {d.AnchorLines} (provisional)" : $"anchor lines {d.AnchorLines}");
    }

    private static string Hash(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}

public static class AssessorPackStringExtensions
{
    public static string SafeName(this string s, int max) =>
        string.Join("", s.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Truncate(max);
}