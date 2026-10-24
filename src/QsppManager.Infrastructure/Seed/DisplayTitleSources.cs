using System.Text.Json;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;

namespace QsppManager.Infrastructure.Seed;

/// <summary>Read-only loader for seed/display-titles.json: the pharmacy-approved editorial
/// titles that sit ON TOP of the imported wording (never a replacement for it). The file is
/// keyed by the human seed id ("UAGKT-3.4-2_3_2_4-c"); this loader translates ids to the
/// import tuple so RequirementDisplay.Resolve can pick the overrides up keyed by requirement
/// id. Absent file changes nothing (the deterministic fallback rules still apply). No
/// database access, no writes — a pure display-layer file next to requirements.json.</summary>
public static class DisplayTitleSources
{
    private static readonly object Lock = new();
    private static Dictionary<string, string>? _tupleTitles;   // "kind|sourceDoc|ref" → title
    private static HashSet<string>? _unreviewedSeedIds;
    private static string _loadedFrom = "";

    /// <summary>Optional hint set at boot by the app shell (App.RepoRoot) so the packaged
    /// bundle's seed location resolves identically everywhere; discovery is the fallback.</summary>
    public static string? RepoRootHint { get; set; }

    /// <summary>Approved editorial titles keyed by import tuple. Empty when the file or its
    /// requirements.json companion is missing (or when no entries parse).</summary>
    public static IReadOnlyDictionary<string, string> TupleTitles
    {
        get { EnsureLoaded(); return _tupleTitles!; }
    }

    /// <summary>Seed ids whose reviewed flag is still false — the honesty trail, surfaced in
    /// review docs; never a display filter.</summary>
    public static IReadOnlySet<string> UnreviewedSeedIds
    {
        get { EnsureLoaded(); return _unreviewedSeedIds!; }
    }

    /// <summary>Resolve display titles for a corpus with the editorial overrides applied.
    /// The standard entry point UI surfaces call; keeps the seed files loaded once.</summary>
    public static List<DisplayTitle> ResolveAll(IEnumerable<Requirement> requirements)
    {
        var byTuple = TupleTitles;
        var editorial = new Dictionary<string, string>();
        foreach (var r in requirements)
        {
            var key = $"{r.RefKind}|{r.SourceDocFile}|{r.Ref}".ToLowerInvariant();
            if (byTuple.TryGetValue(key, out var title))
                editorial[r.Id.ToString()] = title;
        }
        return RequirementDisplay.Resolve(requirements, editorial);
    }

    private static void EnsureLoaded()
    {
        lock (Lock)
        {
            var repoRoot = RepoRootHint ?? FindRepoRoot();
            if (_tupleTitles is not null && _loadedFrom == repoRoot) return;
            _loadedFrom = repoRoot;
            _tupleTitles = [];
            _unreviewedSeedIds = [];
            var titlesPath = Path.Combine(repoRoot, "seed", "display-titles.json");
            var reqsPath = Path.Combine(repoRoot, "seed", "requirements.json");
            if (!File.Exists(titlesPath) || !File.Exists(reqsPath)) return;

            try
            {
                var titles = RequirementDisplay.ParseEditorialJson(File.ReadAllText(titlesPath));
                _unreviewedSeedIds = RequirementDisplay.ParseEditorialUnreviewed(File.ReadAllText(titlesPath));
                if (titles.Count == 0) return;

                using var doc = JsonDocument.Parse(File.ReadAllText(reqsPath),
                    new JsonDocumentOptions { AllowTrailingCommas = true });
                var reqsEl = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement : doc.RootElement.GetProperty("requirements");

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in reqsEl.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var seedId = el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? idEl.GetString() ?? "" : "";
                    if (seedId == "" || !titles.TryGetValue(seedId, out var title)) continue;
                    var kind = el.TryGetProperty("refKind", out var k) ? k.GetString() ?? "" : "";
                    var docName = el.TryGetProperty("sourceDoc", out var sd) ? sd.GetString() ?? "" : "";
                    var refk = el.TryGetProperty("ref", out var rf) ? rf.GetString() ?? "" : "";
                    dict[$"{kind}|{docName}|{refk}".ToLowerInvariant()] = title;
                }
                _tupleTitles = dict;
            }
            catch (JsonException)
            {
                // A malformed file must never block boot: fall back to the derived rules.
                _tupleTitles = [];
                _unreviewedSeedIds = [];
            }
        }
    }

    /// <summary>Walk up from the executing location to the repo root (seed/ marks it);
    /// the same discovery App.axaml.cs uses for RepoRoot.</summary>
    private static string FindRepoRoot()
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "seed")))
                dir = dir.Parent;
            return dir?.FullName ?? "";
        }
        catch { return ""; }
    }
}