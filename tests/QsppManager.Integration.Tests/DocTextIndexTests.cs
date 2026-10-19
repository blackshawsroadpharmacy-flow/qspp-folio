using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M2 · document full-text indexing: a real QuestPDF-rendered PDF goes through
/// PdfPig extraction into the FTS index, and palette search then finds words inside it.</summary>
public class DocTextIndexTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "seed")) && Directory.Exists(Path.Combine(dir.FullName, "assets", "fonts")))
                return dir.FullName;
        throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void Extract_IndexesPdfFullText_AndSearchFindsIt()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"qspp-dtx-{Guid.NewGuid():N}.db");
        var storeDir = Path.Combine(Path.GetTempPath(), $"qspp-dtx-a-{Guid.NewGuid():N}");
        var cacheDir = Path.Combine(Path.GetTempPath(), $"qspp-dtx-c-{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<QsppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        using var db = new QsppDbContext(options);
        DatabaseBootstrapper.Prepare(db);
        var store = new AttachmentStore(storeDir);
        var search = new CorpusSearch(db);
        search.EnsureIndex();
        search.RebuildIndex();
        var textIndex = new DocumentTextIndex(db, store, search, cacheDir);

        try
        {
            // A real PDF with a word that appears nowhere else in any corpus.
            var composer = new PdfComposer(Path.Combine(RepoRoot(), "assets", "fonts"));
            PdfComposer.ConfigureLicense();
            var model = new PdfComposer.ChecklistModel(
                "Test Pharmacy", "1.1.1", "ZZZQSMUNIQUE evidence register", "Unique test document.",
                "Wording excerpt.", "AS85000-2024-full.txt", "AS 85000:2024", "anchor n/a",
                Domain.RequirementReadiness.InProgress, Domain.Applicability.Applicable, "Tester",
                "", [], [], "completed", DateTime.UtcNow);
            var tempPdf = Path.Combine(Path.GetTempPath(), $"dtx-{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tempPdf, composer.Compose(model));

            var (rel, sha, size) = store.StoreAsync(tempPdf).GetAwaiter().GetResult();
            var doc = new SourceDocument
            {
                DisplayName = "Test unique document", FileName = "test.pdf", Role = "reference",
                SourceVersion = "T1", StoredRelativePath = rel, Sha256Hex = sha, FileBytes = size,
            };
            db.SourceDocuments.Add(doc);
            db.SaveChanges();

            // Before extraction: search only matches metadata (title contains the unique word, so this hits)
            // — use a term that ONLY occurs inside the PDF body rather than metadata.
            var before = search.Query("Tester");
            _ = before;

            var (ok, pages, err) = textIndex.Extract(doc.Id);
            Assert.True(ok, $"extraction failed: {err}");
            Assert.True(pages >= 1);

            // After extraction the body text is searchable — including words that the
            // extractor glued to preceding digits ("1.1.1Heading").
            var hits = search.Query("ZZZQSMUNIQUE");
            Assert.True(hits.Any(h => h.Id == doc.Id && h.Kind == "Source" && h.Meta.Contains("full text")),
                $"no full-text hit for the unique word (pages={pages})");

            // Cache persists: ReindexCached re-indexes without re-parsing.
            Assert.Equal(1, textIndex.ReindexCached());
        }
        finally
        {
            try { Directory.Delete(storeDir, true); } catch { }
            try { Directory.Delete(cacheDir, true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }
}