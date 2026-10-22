using System.Text.RegularExpressions;

namespace QsppManager.Infrastructure.ProcessSort;

public sealed record FolioCandidate(Guid Id, string Ref, string RefKind, string Title, string Summary, IReadOnlyList<string> Examples);
public sealed record FolioSuggestion(Guid RequirementId, string Ref, string RefKind, string Title, double Score, string Rationale, string Model, string ModelVersion);

/// <summary>Stage-1 deterministic classifier: scores extracted text and metadata against
/// the app's ACTUAL requirement wording, evidence examples and current surveillance
/// requests (REM-01…19). Produces a short candidate list with stable requirement ids,
/// or abstains. No model is involved in this stage; every suggestion records
/// model="deterministic-v1". Namespaces are separate database rows, so 1.1.2 in the
/// Standard can never be confused with 1.1.2 in the UAG.</summary>
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
    public const string Stage1ModelVersion = "1";

    public List<FolioSuggestion> Classify(string text, string fileName)
    {
        if (candidates.Count == 0) return [];
        var docTokens = Tokenize(fileName);
        docTokens.UnionWith(Tokenize(text.Length > 8000 ? text[..8000] : text));
        var docTokenList = docTokens.ToHashSet();

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
        if (c.Title.Length >= 8 && text.Contains(c.Title, StringComparison.OrdinalIgnoreCase)) { score += 3; matched.Add("title-phrase"); }
            // (b) wording overlap: title (w3), summary (w2), examples (w1) — tokens are
            // lower-cased, digits preserved (potency stays distinct from pH and 21CFR).
            var titleTokens = Tokenize(c.Title).ToHashSet();
            var summaryTokens = Tokenize(c.Summary).ToHashSet();
            var overlapTitle = docTokenList.Count(t => titleTokens.Contains(t));
            var overlapSummary = docTokenList.Count(t => summaryTokens.Contains(t));
            double overlapExamples = 0;
            foreach (var ex in c.Examples)
            {
                var exTokens = Tokenize(ex).ToHashSet();
                overlapExamples += Math.Min(3.0, docTokenList.Count(t => exTokens.Contains(t)));
            }
            score += overlapTitle * 3 + overlapSummary * 2 + overlapExamples;
            if (overlapTitle + overlapSummary > 0 && matched.Count < 4)
                matched.Add($"tokens:{overlapTitle}t+{overlapSummary}s");

            if (score > 0) scored.Add((c, score, string.Join(", ", matched.Take(4))));
        }

        scored.Sort((a, b) => b.score.CompareTo(a.score));

        var results = new List<FolioSuggestion>();
        if (scored.Count == 0) return results;
        var top = scored[0];
        if (top.score < 6) return results; // abstain: nothing convincing — Needs Review

        // Multiple: top-2 within 15% of each other → let the user pick one or several.
        foreach (var (c, score, matched) in scored.Take(5))
        {
            if (score < 6 && !ReferenceEquals(c, top.c)) continue;
            var rationale = $"matched: {matched}" + (score < top.score * 0.85 ? " (lower than top)" : "");
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

    /// <summary>Lower-cardinality token set: words, digits kept, junk dropped.</summary>
    public static HashSet<string> Tokenize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return [];
        var set = new HashSet<string>();
        foreach (var raw in s.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = raw.ToLowerInvariant();
            if (t.Length < 3 && !t.Contains('.')) continue;           // tiny tokens: keep clause dots
            if (t.Any(char.IsPunctuation) && !Regex.IsMatch(t, @"^\d+(\.\d+)*$")) t = t.Replace("\"", "");
            if (t.Length == 0 || Stopwords.Contains(t)) continue;
            set.Add(t);
        }
        return set;
    }

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","with","shall","that","this","from","was","were","has","have","are","are","not","all","any","its","must","may","their","they","which","where","when","will","can","into","than","then","only","also","such","being","other","these","those","per","use","used","using","each","both",
    };
}