using Microsoft.EntityFrameworkCore;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M1 print pipeline: real QuestPDF rendering (fonts from the repo assets dir)
/// and the honest CUPS adapter behaviour. No physical printer is claimed or needed here:
/// hardware verification is an M5 owner-run check.</summary>
public class PrintPipelineTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "seed")) && Directory.Exists(Path.Combine(dir.FullName, "assets", "fonts")))
                return dir.FullName;
        throw new InvalidOperationException("repo root with assets not found from test base dir");
    }

    [Fact]
    public void Checklist_Completed_Variant_RendersARealPdf()
    {
        var composer = new PdfComposer(Path.Combine(RepoRoot(), "assets", "fonts"));
        PdfComposer.ConfigureLicense();
        var model = new PdfComposer.ChecklistModel(
            "Burke Road Compounding", "2.2.3.8", "Consistency and potency testing of compounded products",
            "Consistent compounding with a documented potency-testing system.",
            "The pharmacy shall maintain and follow a system to ensure consistency of compounded products, including appropriate instructions for compounding and a system for final product potency testing.",
            "AS85000-2024-full.txt", "AS 85000:2024", "anchor approx. lines 1442-1444 (provisional)",
            Domain.RequirementReadiness.ManagerVerified, Domain.Applicability.Applicable, "Test Owner",
            "Confirm potency cadence interpretation with the manager (ISSUE-POTENCY).",
            [new PdfComposer.EvidenceRow("", "Potency testing SOP v1", "PolicyOrSop", "potency-sop.pdf", "01 Oct 2026", "ManagerVerified")],
            [new PdfComposer.TasksRow("Book potency testing round", "Test Owner", "30 Nov 2026", "Open")],
            "completed", DateTime.UtcNow);
        var bytes = composer.Compose(model);
        Assert.True(bytes.Length > 5000, $"PDF unexpectedly small: {bytes.Length} bytes");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Checklist_Blank_Variant_RendersARealPdf()
    {
        var composer = new PdfComposer(Path.Combine(RepoRoot(), "assets", "fonts"));
        PdfComposer.ConfigureLicense();
        var model = new PdfComposer.ChecklistModel(
            "Burke Road Compounding", "2.2.3.10", "Beyond-use dates", "BUD assignment process.",
            "The pharmacy shall implement and follow a process for assigning beyond use by dates.",
            "AS85000-2024-full.txt", "AS 85000:2024", "anchor approximate",
            Domain.RequirementReadiness.InProgress, Domain.Applicability.Applicable, "",
            "", [], [], "blank", DateTime.UtcNow);
        var bytes = composer.Compose(model);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Cups_DiscoverPrinters_DegradesGracefully()
    {
        // On a machine with no CUPS printers configured this returns an empty list (no throw);
        // with CUPS configured it lists what lpstat sees (Burke Road environment varies).
        using var db = new QsppManager.Infrastructure.Persistence.QsppDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<QsppManager.Infrastructure.Persistence.QsppDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
        var svc = new CupsPrintService(db, new AuditService(db));
        var printers = svc.DiscoverPrinters();
        Assert.NotNull(printers);
    }

    [Fact]
    public void Cups_UnknownPrinter_FailsHonestlyWithoutRetry()
    {
        using var db = new QsppManager.Infrastructure.Persistence.QsppDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<QsppManager.Infrastructure.Persistence.QsppDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
        var svc = new CupsPrintService(db, new AuditService(db));
        var printers = svc.DiscoverPrinters();
        var fake = "qsm-nonexistent-printer-9f3a";
        if (printers.Any(p => p.Name.Equals(fake, StringComparison.OrdinalIgnoreCase)))
            return; // impossible in practice; guard for exotic test environments
        var temp = Path.Combine(Path.GetTempPath(), $"qsm-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(temp, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 t"));
        try
        {
            var outcome = svc.SubmitToPrinter(temp, fake, 1, false);
            // Two honest failure modes are acceptable: the adapter knows the printer list and
            // refuses up-front, or the spooler itself rejects the unknown destination.
            Assert.False(outcome.Ok);
            Assert.True(
                outcome.detail.Contains("not visible to the print system", StringComparison.OrdinalIgnoreCase) ||
                outcome.detail.Contains("rejected the job", StringComparison.OrdinalIgnoreCase),
                $"unexpected outcome detail: {outcome.detail}");
        }
        finally { try { File.Delete(temp); } catch { } }
    }
}