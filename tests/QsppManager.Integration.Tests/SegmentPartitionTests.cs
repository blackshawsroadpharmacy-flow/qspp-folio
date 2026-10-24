using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Storage;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Seed;
using QsppManager.Domain;
using QsppManager.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>The four Requirements list segments partition the corpus (owner brief item 6):
/// "Rules and terms" is RULE + TC + POLICY + OBLIGATION only — not "everything else" — and
/// the segments must sum to the corpus total with no overlap. Locked on the seeded corpus:
/// 350 (Std + UAG sections + key tasks) + 19 (remote round) + 14 (readiness list) + 87
/// (rules and terms) = 470.</summary>
public class SegmentPartitionTests
{
    [Fact]
    public void Segments_partition_the_whole_corpus()
    {
        var repoRoot = FindRepoRoot();
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "seed")), $"seed not found at {repoRoot}");

        var dbPath = Path.Combine(Path.GetTempPath(), $"qspp-seg-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<QsppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        using var db = new QsppDbContext(options);
        db.Database.EnsureCreated();
        var store = new AttachmentStore(Path.Combine(Path.GetTempPath(), $"qspp-seg-att-{Guid.NewGuid():N}"));
        var importer = new SeedImporter(db, store, new AuditService(db), repoRoot);
        importer.ImportAll();

        var rows = db.Requirements.AsNoTracking().ToList();
        var counts = new int[4];
        foreach (var r in rows) counts[QsppManager.Domain.Services.RequirementDisplay.SegmentIndex(r.RefKind)]++;

        Assert.Collection(counts,
            at => Assert.Equal(350, at),   // Std + UAG sections + key tasks, grouped by area
            at => Assert.Equal(19, at),    // Remote round
            at => Assert.Equal(14, at),    // Readiness list
            at => Assert.Equal(87, at));   // Rules and terms (rules, terms, policies, obligations)
        Assert.Equal(470, counts.Sum());
        Assert.Equal(rows.Count, counts.Sum());   // no overlap, no row dropped
    }

    [Fact]
    public void SegmentIndex_is_exhaustive_and_singleValued()
    {
        foreach (var kind in Enum.GetValues<RefKind>())
        {
            var seg = QsppManager.Domain.Services.RequirementDisplay.SegmentIndex(kind);
            Assert.InRange(seg, 0, 3);
        }
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "seed")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}