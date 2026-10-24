using System.Text.RegularExpressions;

namespace QsppManager.Infrastructure.ProcessSort;

public sealed record FolioCandidate(Guid Id, string Ref, string RefKind, string Title, string Summary, IReadOnlyList<string> Examples);
public sealed record FolioSuggestion(Guid RequirementId, string Ref, string RefKind, string Title, double Score, string Rationale, string Model, string ModelVersion);

/// <summary>Stage-1 deterministic classifier: scores extracted text and metadata against
/// the app's ACTUAL requirement wording, evidence examples and current surveillance
/// requests (REM-01…19). Produces a short candidate list with stable requirement ids,
/// or abstains. No model is involved in this stage; every suggestion records
/// model="deterministic-v1". Namespaces are separate database rows, so 1.1.2 in the
/// Standard can never be confused with 1.1.2 in the UAG.
///
/// v2 (24 Oct, tuned on the real 182-document corpus where 119 documents could not
/// decide): token overlap is weighted by INVERSE CANDIDATE FREQUENCY — a token that
/// appears in many requirements' wording ("delivery", "pharmacy", "service") moves at
/// a fraction of the weight of a token that only a few requirements carry ("first aid",
/// "cytotoxic", "workcover", "consent"). The file NAME is weighted on top of the body
/// (a file titled "Award Wages 2026" or "…First_Aid_Certificate" is a label the
/// classifier should listen to twice). Reference hits, verbatim title matches and the
/// confident floor (≥6) are unchanged from v1; three additions refine it: tokens are
/// lightly singularised deterministically (bookings→booking); a two-word title phrase
/// found adjacent in the document earns a +3 phrase bonus (single-token IDF deflates a
/// family's shared wording, so phrase precision is restored); and a WEAK CLUSTER
/// (top 4.5–6 with a near-tied companion) is offered as MultipleCandidates instead of
/// abstaining, because a document aimed at a family of related requirements is a real
/// cluster, not a silence. The 182-document replay before/after is recorded in the
/// milestones ledger.</summary>
public sealed partial class FolioClassifier(IReadOnlyList<FolioCandidate> candidates)
{
    [GeneratedRegex(@"\b(REM-\d{2}|RM-\d{2})\b")]
    private static partial Regex RemHitRegex();

    [GeneratedRegex(@"\b(\d{1,2}\.\d{1,2}(\.\d{1,3})?)\b")]
    private static partial Regex ClauseNumberRegex();

    [GeneratedRegex(@"\bUAG\s+\d+\.\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex UagHitRegex();

    /// <summary>Absolutely honest naming: this stage is deterministic retrieval, not a model.</summary>
    public const string Stage1Model = "deterministic-v1";
    public const string Stage1ModelVersion = "2";

    /// <summary>Per-token candidate frequency across title+summary+example wording.
    /// Built once; drives the inverse-frequency weights below.</summary>
    private readonly Dictionary<string, int> tokenDf = BuildTokenDf(candidates);

    private static Dictionary<string, int> BuildTokenDf(IReadOnlyList<FolioCandidate> candidates)
    {
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            var seen = new HashSet<string>(Tokenize(c.Title), StringComparer.OrdinalIgnoreCase);
            seen.UnionWith(Tokenize(c.Summary));
            foreach (var ex in c.Examples) seen.UnionWith(Tokenize(ex));
            foreach (var t in seen)
                df[t] = df.TryGetValue(t, out var n) ? n + 1 : 1;
        }
        return df;
    }

    /// <summary>Weight of one token: distinctive tokens (few candidates carry them)
    /// reach ~2.8×; corpus-wide fillers ("pharmacy", "service") sink toward the 0.35
    /// floor. Deterministic and rebuilt from the live candidate list every run.</summary>
    private double WeightOf(string token)
    {
        var df = tokenDf.TryGetValue(token, out var n) ? n : 1;
        return Math.Max(0.35, 2.5 / (0.8 + Math.Log2(1 + df)));
    }

    public List<FolioSuggestion> Classify(string text, string fileName)
    {
        if (candidates.Count == 0) return [];
        var bodyText = text.Length > 8000 ? text[..8000] : text;
        var docTokens = Tokenize(fileName);
        docTokens.UnionWith(Tokenize(bodyText));
        var docTokenList = docTokens.ToHashSet();
        var fileNameTokens = Tokenize(fileName).ToHashSet();
        var docBigrams = new HashSet<string>(StringComparer.Ordinal);
        docBigrams.UnionWith(BigramSet(OrderedTokens(fileName)));
        docBigrams.UnionWith(BigramSet(OrderedTokens(bodyText)));

        var scored = new List<(FolioCandidate c, double score, string matched)>(candidates.Count);
        foreach (var c in candidates)
        {
            double score = 0;
            var matched = new List<string>();

            // (a) exact reference hits in the document text — strongest signal.
            var rem = RemHitRegex().Match(text);
            if (rem.Success && c.Ref.Equals(rem.Value, StringComparison.OrdinalIgnoreCase)) { score += 8; matched.Add(rem.Value); }
            var uag = UagHitRegex().Match(text);
            if (uag.Success && c.Ref.Replace(" ", "").Equals(uag.Value.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) { score += 8; matched.Add(uag.Value); }
            foreach (var m in ClauseNumberRegex().Matches(text).Take(6))
                if (c.Ref.Replace(" ", "") == m.Groups[1].Value.Replace(" ", "") && c.RefKind is "Std" or "Uag" or "UagKeyTask" or "Rule" or "Tc" or "Policy")
                { score += 4; matched.Add(m.Groups[1].Value); break; }

            // (a1) the full title appearing verbatim is a genuinely stronger signal than
            // token soup — a doc quoting "assessment bookings and cancellations" whole.
            // The FILE name quoting the title is an even sharper label (+4).
            if (c.Title.Length >= 8 && text.Contains(c.Title, StringComparison.OrdinalIgnoreCase)) { score += 3; matched.Add("title-phrase"); }
            if (c.Title.Length >= 8 && fileName.Contains(c.Title, StringComparison.OrdinalIgnoreCase)) { score += 4; matched.Add("filename-title"); }

            // (b) wording overlap with inverse-candidate-frequency weighting: title (w3),
            // summary (w2), examples (w1), file name (w2 on top). Distinctive tokens move
            // the score; corpus-wide fillers barely do.
            var titleTokens = Tokenize(c.Title).ToHashSet();
            var summaryTokens = Tokenize(c.Summary).ToHashSet();
            double overlapTitle = 0, overlapSummary = 0, overlapFiles = 0;
            int nTitle = 0, nSummary = 0, nFiles = 0;
            foreach (var t in docTokenList)
            {
                if (titleTokens.Contains(t)) { overlapTitle += WeightOf(t); nTitle++; }
                if (summaryTokens.Contains(t)) { overlapSummary += WeightOf(t); nSummary++; }
                if (fileNameTokens.Contains(t) && (titleTokens.Contains(t) || summaryTokens.Contains(t)))
                { overlapFiles += WeightOf(t); nFiles++; }
            }
            double overlapExamples = 0;
            foreach (var ex in c.Examples)
            {
                var exTokens = Tokenize(ex).ToHashSet();
                overlapExamples += Math.Min(3.0, docTokenList.Where(exTokens.Contains).Sum(WeightOf));
            }
            score += overlapTitle * 3 + overlapSummary * 2 + overlapExamples + overlapFiles * 2;

            // (b2) phrase precision: single-token IDF deflates a family's shared wording
            // ("assessment bookings" is in five refs' titles, so every token is common),
            // but a two-word title PHRASE appearing adjacent in the document is a precise
            // match. +3 per title bigram found adjacent in the doc, capped at two.
            int phraseHits = 0;
            if (docBigrams.Count > 0)
            {
                foreach (var bg in TitleBigrams(c.Title))
                    if (docBigrams.Contains(bg) && ++phraseHits == 2) break;
                score += 3.0 * phraseHits;
            }
            if (nTitle + nSummary + nFiles > 0 && matched.Count < 4)
                matched.Add($"tokens:{nTitle}t+{nSummary}s{(nFiles > 0 ? $"+{nFiles}f" : "")}{(phraseHits > 0 ? $"+{phraseHits}p" : "")}");

            if (score > 0) scored.Add((c, score, string.Join(", ", matched.Take(4))));
        }

        scored.Sort((a, b) => b.score.CompareTo(a.score));

        var results = new List<FolioSuggestion>();
        if (scored.Count == 0) return results;
        var top = scored[0];

        // Abstain: nothing convincing — Needs Review. Two ways to clear the bar:
        // (1) a confident top (≥6) as in v1; or (2) a WEAK CLUSTER: top between 4.5 and 6
        // with a near-tied companion — a document aimed at a FAMILY of related
        // requirements (the "Assessment bookings" rules share wording by design, so IDF
        // deflates every family token) is honestly a MultipleCandidates cluster, not a
        // silence. A single weak signal alone still abstains.
        var weakCluster = top.score < 6 && top.score >= 4.5
            && scored.Count > 1 && scored[1].score >= top.score * 0.85;
        if (top.score < 4.5 || (top.score < 6 && !weakCluster)) return results;

        // Multiple: let the user pick one or several. v1 inclusion semantics: every
        // candidate scoring ≥6 is a confident match and must appear (no silent swallow
        // of a strong second topic); below that, only near-ties of a weak cluster.
        foreach (var (c, score, matched) in scored.Take(5))
        {
            if (score < 6.0 && !weakCluster && !ReferenceEquals(c, top.c)) continue;
            if (score < 6.0 && !weakCluster) continue;          // single weak top — abstain
            var isTopNearTie = score >= top.score * 0.85 || ReferenceEquals(c, top.c);
            var rationale = $"matched: {matched}" + (isTopNearTie ? "" : " (lower than top)") + (weakCluster && top.score < 6 ? " (weak cluster — review)" : "");
            results.Add(new FolioSuggestion(c.Id, c.Ref, c.RefKind, c.Title, Math.Round(score, 2), rationale, Stage1Model, Stage1ModelVersion));
        }
        return results;
    }

    public static string CategoryFor(string refKind) => refKind switch
    {
        "Surveillance" => "Surveillance",
        "Std" => "Standard",
        "Uag" or "UagKeyTask" => "UAG",
        "Rule" => "Program rules",
        "Tc" => "Terms and conditions",
        "Policy" => "Policies",
        "Obligation" => "Program rules",
        "Hitlist" => "Internal priorities",
        _ => "Needs Review",
    };

    private static readonly char[] SplitChars = [' ', '\t', '\n', '\r', ',', ';', ':', '.', '(', ')', '[', ']', '/', '-', '–', '—', '"', '\''];

    /// <summary>Lower-cardinality ordered token list: words, digits kept, junk dropped,
    /// then lightly singularised. Order preserved so bigrams (phrase matching) work.</summary>
    public static List<string> OrderedTokens(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(s)) return list;
        foreach (var raw in s.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = raw.ToLowerInvariant();
            if (t.Length < 3 && !t.Contains('.')) continue;           // tiny tokens: keep clause dots
            if (t.Any(char.IsPunctuation) && !Regex.IsMatch(t, @"^\d+(\.\d+)*$")) t = t.Replace("\"", "");
            if (t.Length == 0 || Stopwords.Contains(t)) continue;
            list.Add(Stem(t));
        }
        return list;
    }

    /// <summary>Token set view of OrderedTokens (deduplicated, unordered).</summary>
    public static HashSet<string> Tokenize(string s) => OrderedTokens(s).ToHashSet(StringComparer.Ordinal);

    /// <summary>Consecutive two-word phrases ("assessment booking", "cold chain") from an
    /// ordered token list.</summary>
    public static HashSet<string> BigramSet(List<string> ordered)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < ordered.Count; i++) set.Add(ordered[i] + " " + ordered[i + 1]);
        return set;
    }

    private static HashSet<string> TitleBigrams(string title) => BigramSet(OrderedTokens(title));

    /// <summary>Light deterministic singularisation (no stemming library, no external
    /// model): final "s" dropped on tokens of five or more letters (bookings→booking,
    /// windows→window, registers→register), "ies"→"y" (policies→policy). Protected
    /// endings keep status/analysis/synthesis intact. Unifies the plural mismatches the
    /// replay showed losing real summary matches ("cancellations" vs "cancellation").</summary>
    public static string Stem(string w)
    {
        if (w.Length >= 5 && w.EndsWith("ies")) return w[..^3] + "y";
        if (w.Length >= 5 && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is") && w.EndsWith("s"))
            return w[..^1];
        return w;
    }

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","with","shall","that","this","from","was","were","has","have","are","are","not","all","any","its","must","may","their","they","which","where","when","will","can","into","than","then","only","also","such","being","other","these","those","per","use","used","using","each","both",
    };
}