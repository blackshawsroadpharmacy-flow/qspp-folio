using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Opt-in reality check against the REAL imported corpus (the library the pharmacy
/// actually uses). Runs only when QSPP_REAL_CORPUS=1 points at an initialised data dir:
///   QSPP_REAL_CORPUS=1 QSPP_DATA_DIR=/tmp/qspp-runtime-m1c dotnet test --filter DocFullTextRealCorpusTests
/// Verifies PdfPig extraction succeeds across the pack's actual PDFs and that palette search
/// finds wording that lives INSIDE them (e.g. page text of the Standard).</summary>
public class DocFullTextRealCorpusTests
{
    [Fact]
    public void ExtractRealCorpus_AndSearchInsidePdfs()
    {
        if (Environment.GetEnvironmentVariable("QSPP_REAL_CORPUS") != "1") return; // opt-in only
        var dataDir = Environment.GetEnvironmentVariable("QSPP_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(dataDir), "QSPP_DATA_DIR must point at an initialised data dir");

        var options = new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dataDir, "qspp.db")}").Options;
        using var db = new QsppDbContext(options);
        var store = new AttachmentStore(Path.Combine(dataDir, "attachments"));
        var search = new CorpusSearch(db);
        search.EnsureIndex();
        var textIndex = new DocumentTextIndex(db, store, search, Path.Combine(dataDir, "document-text"));

        var docs = db.SourceDocuments.AsNoTracking().ToList();
        Assert.True(docs.Count >= 20, $"expected the real corpus (21 documents), found {docs.Count}");
        var extracted = new List<(string name, int pages)>();
        var failures = new List<string>();
        foreach (var d in docs)
        {
            var (ok, pages, err) = textIndex.Extract(d.Id);
            if (ok) extracted.Add((d.DisplayName, pages));
            else if (d.Role != "image") failures.Add($"{d.DisplayName}: {err}"); // Decision-tree.png is expected to fail
        }
        Assert.True(extracted.Count >= 19, $"extracted only {extracted.Count}/{docs.Count}; failures: {string.Join("; ", failures.Take(3))}");

        // Search must now find wording that lives INSIDE a PDF, not just in metadata:
        // 'beyond use' appears throughout the Standard/UAG bodies.
        var bodyHits = search.Query("beyond use");
        Assert.True(bodyHits.Count > 0, "corpus body search ('beyond use') returned nothing after full-text indexing");
        Assert.Contains(bodyHits, h => h.Meta.Contains("full text") || h.Kind is "Source" or "Std");

        // Reboot path: cached re-index must restore the same rows without re-parsing.
        Assert.Equal(extracted.Count, textIndex.ReindexCached());
    }
}