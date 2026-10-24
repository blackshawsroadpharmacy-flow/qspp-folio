using System.Text.Json;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Seed;
using QsppManager.Infrastructure.Storage;
using QsppManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Regenerates the full display-title review table (docs/review/) by running the
/// SHIPPED RequirementDisplay service over the REAL repository seed corpus. Gated behind
/// the QSPP_DUMP_TITLES=1 environment variable so the normal test run stays side-effect free.
/// Output: <repo>/docs/review/display-titles-material.json — every NeedsHumanTitle row with
/// the source material an editorial title must be written from, keyed by the human seed id.</summary>
public class DisplayTitlesRunnerTests
{
    [Fact]
    public void Dump_needsHumanTitleCorpus()
    {
        var gate = Environment.GetEnvironmentVariable("QSPP_DUMP_TITLES");
        if (gate != "1") return; // gated: no side effects in the normal 220-green run

        var repoRoot = FindRepoRoot();
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "seed")), $"seed not found at {repoRoot}");

        var dbPath = Path.Combine(Path.GetTempPath(), $"qspp-dump-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<QsppDbContext>().UseSqlite($"Data Source={dbPath}").Options;
        using var db = new QsppDbContext(options);
        db.Database.EnsureCreated();
        var store = new AttachmentStore(Path.Combine(Path.GetTempPath(), $"qspp-dump-att-{Guid.NewGuid():N}"));
        var audit = new AuditService(db);
        var importer = new SeedImporter(db, store, audit, repoRoot);
        importer.ImportAll();

        var rows = db.Requirements.IgnoreQueryFilters().AsNoTracking().ToList();

        // Shipped service over the full entity set (uniqueness needs the whole kind set).
        var resolved = QsppManager.Domain.Services.RequirementDisplay.Resolve(rows);

        // Seed ids joined on the import key tuple (kind, sourceDoc, ref): the importer derives
        // each Guid from exactly that tuple, so entity rows join seed rows with no mirroring.
        var seedIds = new Dictionary<string, string>(); // guid → seed-file id
        using (var seedDoc = JsonDocument.Parse(
                    System.IO.File.ReadAllText(Path.Combine(repoRoot, "seed", "requirements.json")),
                    new JsonDocumentOptions { AllowTrailingCommas = true }))
        {
            var reqsEl = seedDoc.RootElement.ValueKind == JsonValueKind.Array
                ? seedDoc.RootElement : seedDoc.RootElement.GetProperty("requirements");
            var byTuple = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in reqsEl.EnumerateArray())
            {
                var idEl = el.TryGetProperty("id", out var ie) ? (ie.GetString() ?? "") : "";
                if (idEl == "" || Guid.TryParse(idEl, out _)) continue; // Guid-keyed rows need no translation
                var tuple = $"{el.GetProperty("refKind").GetString()}|{el.GetProperty("sourceDoc").GetString()}|{el.GetProperty("ref").GetString()}";
                byTuple[tuple] = idEl;
            }
            foreach (var r in rows)
            {
                if (byTuple.TryGetValue($"{r.RefKind}|{r.SourceDocFile}|{r.Ref}", out var seedId))
                    seedIds[r.Id.ToString()] = seedId;
            }
        }

        var material = resolved
            .Where(t => t.NeedsHumanTitle)
            .Select(t =>
            {
                var r = rows.First(x => x.Id.ToString() == t.Id);
                string subclauseTitle = "";
                try
                {
                    using var doc = JsonDocument.Parse(r.ExtraJson == "" ? "{}" : r.ExtraJson);
                    if (doc.RootElement.TryGetProperty("subclauseTitle", out var st))
                        subclauseTitle = st.GetString() ?? "";
                }
                catch { }
                var seedId = seedIds.TryGetValue(t.Id, out var s) ? s : "";
                return new
                {
                    id = r.Id.ToString(),
                    seedId,
                    kind = r.RefKind.ToString(),
                    @ref = r.Ref,
                    sourceTitle = r.Title,
                    resolvedTitle = t.Title,
                    basis = t.Basis.ToString(),
                    wording = r.WordingExcerpt,
                    summary = r.PlainSummary,
                    subclauseTitle,
                    domain = r.Domain.ToString(),
                    area = t.Area,
                    domainLabel = t.DomainLabel,
                    priority = r.Priority,
                };
            })
            .OrderBy(m => m.seedId)
            .ToList();

        var outPath = Path.Combine(repoRoot, "docs", "review", "display-titles-material.json");
        System.IO.File.WriteAllText(outPath,
            JsonSerializer.Serialize(material, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "seed")))
            dir = dir.Parent!;
        return dir?.FullName ?? "";
    }
}