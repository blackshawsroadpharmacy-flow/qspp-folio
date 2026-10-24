using System.Text.Json;
using QsppManager.Domain.Entities;

namespace QsppManager.Domain.Services;

/// <summary>How a display title was obtained. Editorial titles are pharmacy-written
/// (approved voice); Wording titles quote the source text verbatim; Source titles are
/// derived from the source document's own heading structure.</summary>
public enum DisplayTitleBasis
{
    Wording,
    Source,
    Editorial,
}

/// <summary>A requirement as it should appear in the UI: a human title, its reference
/// code, the kind label with acronyms expanded, and the area (section/clause) it sits in.
/// Deterministic: same inputs → same output. Editorial overrides win; verbatim wording
/// wins where the source text is short and imperative; structured headings are used
/// where they are unique; everything else keeps the original seeded title.</summary>
public sealed record DisplayTitle(
    string Id,
    string RefCode,
    string Title,
    DisplayTitleBasis Basis,
    string Area,
    string DomainLabel,
    string KindLabel,
    string SourceTitle)
{
    /// <summary>True when the title still reads like a planning artefact
    /// ("Hit-List #1 P1", "UAG 1.1 section 1.1 … — quoted subclause …"). Obligations and
    /// surveillance requests are already human by design, so they never raise it.</summary>
    public bool NeedsHumanTitle => Basis == DisplayTitleBasis.Source && Title == SourceTitle
        && SourceTitle != ""
        && KindLabel is not ("Obligation" or "Remote assessment");
}

public static class RequirementDisplay
{
    /// <summary>Resolve display titles for a set of requirements. The full set is needed
    /// because heading-derived titles must be unique within their kind to be usable.</summary>
    public static List<DisplayTitle> Resolve(
        IEnumerable<Requirement> requirements,
        IReadOnlyDictionary<string, string>? editorial = null)
    {
        var list = requirements.ToList();

        var stdSubclauseTitles = CountUsable(list, StdSubclauseTitle);
        var ruleHeadings = CountUsable(list, RuleHeading);

        var result = new List<DisplayTitle>(list.Count);
        foreach (var r in list)
        {
            var (title, basis) = Derive(r, stdSubclauseTitles, ruleHeadings);
            if (editorial is not null && editorial.TryGetValue(r.Id.ToString(), out var text) && text != "")
            {
                title = text;
                basis = DisplayTitleBasis.Editorial;
            }
            result.Add(new DisplayTitle(
                r.Id.ToString(), r.Ref, title, basis, Area(r), DomainLabel(r), KindLabel(r), r.Title));
        }
        return result;
    }

    /// <summary>Build an id-keyed editorial set from a JSON map of requirement id → human title.</summary>
    public static Dictionary<string, string> ParseEditorialJson(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return dict is null ? new Dictionary<string, string>() : new Dictionary<string, string>(dict);
    }

    private static (string title, DisplayTitleBasis basis) Derive(
        Requirement r,
        IReadOnlyDictionary<string, int> stdSubclauseTitles,
        IReadOnlyDictionary<string, int> ruleHeadings)
    {
        switch (r.RefKind)
        {
            case RefKind.UagKeyTask:
                return PhraseFromWording(r.WordingExcerpt)
                    ? (CapLength(StripLetterPrefix(r.WordingExcerpt)), DisplayTitleBasis.Wording)
                    : (r.Title, DisplayTitleBasis.Source);

            case RefKind.Uag:
                return (SentenceCase(StripMapsSuffix(StripLeadingNumber(r.Title))), DisplayTitleBasis.Source);

            case RefKind.Std:
                var st = StdSubclauseTitle(r);
                if (st != "" && stdSubclauseTitles.TryGetValue(st, out var n) && n == 1)
                    return (SentenceCase(st), DisplayTitleBasis.Source);
                return (r.Title, DisplayTitleBasis.Source);

            case RefKind.Rule:
                var h = RuleHeading(r);
                if (h != "" && ruleHeadings.TryGetValue(h, out var m) && m == 1)
                    return (SentenceCase(h), DisplayTitleBasis.Source);
                return (r.Title, DisplayTitleBasis.Source);

            case RefKind.Tc:
                var idx = r.Title.IndexOf(": ", StringComparison.Ordinal);
                return idx > 0
                    ? (SentenceCase(r.Title[(idx + 2)..]), DisplayTitleBasis.Source)
                    : (r.Title, DisplayTitleBasis.Source);

            case RefKind.Policy:
                return (SentenceCase(PolicyCore(r.Title)), DisplayTitleBasis.Source);

            case RefKind.Hitlist:
                var s = r.PlainSummary.Trim().TrimEnd('.');
                return s != "" && s.Length <= 60
                    ? (s, DisplayTitleBasis.Wording)
                    : (r.Title, DisplayTitleBasis.Source);

            default:
                return (r.Title, DisplayTitleBasis.Source); // Obligation, Surveillance are already human
        }
    }

    /// <summary>UAG key-task wording is usable only when it reads as a complete
    /// instruction: imperative-verb first, not a subject-first fragment, and not a
    /// question. The fragment list comes from observed seed patterns (e.g. "A substitute
    /// decision-maker if a patient …", "Mechanism for providing feedback …").</summary>
    public static bool PhraseFromWording(string wording)
    {
        var text = StripLetterPrefix(wording);
        if (text == "") return false;
        var first = text.Split(' ', 2)[0].ToLowerInvariant();
        return first is not ("a" or "an" or "the" or "mechanism" or "process" or "system" or "details"
            or "who" or "whose" or "which" or "that" or "this" or "if" or "when" or "where" or "why" or "how");
    }

    /// <summary>Cap at word boundary, ≤ target characters. Never splits a word.</summary>
    public static string CapLength(string text, int max = 70)
    {
        if (text.Length <= max) return text;
        var cut = text[..max];
        var space = cut.LastIndexOf(' ');
        return (space > max * 3 / 5 ? cut[..space] : cut.TrimEnd()) + " …";
    }

    /// <summary>Strip the quoted task letter ("a. ", "g. ") that opens key-task wording.</summary>
    public static string StripLetterPrefix(string wording)
    {
        var text = wording.Trim();
        var dot = text.IndexOf(". ");
        if (dot > 0 && dot <= 4 && text[..dot].All(char.IsAsciiLetter))
            text = text[(dot + 2)..].Trim();
        return text.TrimEnd('.').Trim();
    }

    private static string StripMapsSuffix(string title)
    {
        var idx = title.IndexOf("— maps to", StringComparison.Ordinal);
        if (idx < 0) idx = title.IndexOf("- maps to", StringComparison.Ordinal);
        return idx > 0 ? title[..idx].Trim() : title.Trim();
    }

    private static string StripLeadingNumber(string title)
    {
        var sp = title.IndexOf(' ');
        return sp > 0 && sp <= 5 && title[..sp].All(c => char.IsDigit(c) || c == '.')
            ? title[(sp + 1)..]
            : title;
    }

    /// <summary>Sentence case: keep acronyms/proper nouns, lowercase the rest except the first word.</summary>
    public static string SentenceCase(string text)
    {
        if (text == "") return "";
        var lower = text.ToLowerInvariant();
        var words = lower.Split(' ');
        // Preserve tokens that are acronyms or proper nouns in the original.
        var original = text.Split(' ');
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            var keepUpper = original[i] is "QSPP" or "AS85000:2024" or "UAG" or "QCPP" or "JASA"
                || HasAcronymLetters(original[i]);
            sb.Append(i == 0 && !keepUpper ? char.ToUpperInvariant(words[i][0]) + words[i][1..] : keepUpper ? original[i] : words[i]);
            if (i < words.Length - 1) sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>"IT", "(IT)", "IT," count as acronym tokens; "and", "of" do not.</summary>
    private static bool HasAcronymLetters(string token)
    {
        var letters = new string(token.Where(char.IsLetter).ToArray());
        return letters.Length >= 2 && letters == letters.ToUpperInvariant();
    }

    private static string? StdSubclauseTitle(Requirement r)
    {
        try
        {
            using var doc = JsonDocument.Parse(r.ExtraJson);
            return doc.RootElement.TryGetProperty("subclauseTitle", out var v)
                && v.ValueKind == JsonValueKind.String
                ? v.GetString()?.Trim() ?? ""
                : "";
        }
        catch (JsonException) { return ""; }
    }

    /// <summary>Rule heading: the text after the em dash ("Rule 1 — Pharmacy Conduct").</summary>
    private static string RuleHeading(Requirement r)
    {
        var idx = r.Title.IndexOf('—');
        return idx > 0 ? r.Title[(idx + 1)..].Trim() : "";
    }

    private static string PolicyCore(string title)
    {
        var t = title.Trim();
        if (t.StartsWith("QSPP ", StringComparison.OrdinalIgnoreCase)) t = t[5..];
        if (t.EndsWith(" Policy", StringComparison.OrdinalIgnoreCase)) t = t[..^7];
        return t.Trim();
    }

    private static Dictionary<string, int> CountUsable(IEnumerable<Requirement> list, Func<Requirement, string> key)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in list)
        {
            var k = key(r);
            if (k != "" && !counts.TryAdd(k, 1)) counts[k]++;
        }
        return counts;
    }

    /// <summary>Area: the section or clause the requirement belongs to. UAG key tasks sit
    /// in their UAG section; standard subclauses sit in the clause's subject; rules,
    /// terms, policy, obligations, hit lists and surveillance requests sit at program
    /// level (domain label alone).</summary>
    public static string Area(Requirement r) => r.RefKind switch
    {
        RefKind.Std => SentenceCase(StdSubject(r)),
        RefKind.Uag => SentenceCase(StripMapsSuffix(StripLeadingNumber(r.Title))),
        RefKind.UagKeyTask => SentenceCase(SectionTitleOf(r)),
        RefKind.Rule => "Program rules",
        RefKind.Tc => "Program terms",
        RefKind.Policy => "Program policy",
        RefKind.Hitlist or RefKind.Surveillance or RefKind.Obligation => DomainLabel(r),
        _ => DomainLabel(r),
    };

    // UAG key tasks carry their UAG section title inside the seeded title
    // ("UAG 1.1 section 1.1 Governance, Leadership, and Culture — quoted subclause …").
    private static string SectionTitleOf(Requirement r)
    {
        var idx = r.Title.IndexOf("section ", StringComparison.Ordinal);
        if (idx < 0) return r.Title;
        var rest = r.Title[(idx + 8)..];
        var end = rest.IndexOf(" — quoted", StringComparison.Ordinal);
        return end > 0 ? rest[..end].Trim() : rest.Trim();
    }

    private static string StdSubject(Requirement r)
    {
        var st = StdSubclauseTitle(r);
        return st != "" ? st : r.Title;
    }

    /// <summary>The stable area label for the whole domain (used where a requirement
    /// has no finer section). Names match the UAG's four domains.</summary>
    public static string DomainLabel(Requirement r) => r.Domain switch
    {
        DomainPart.Domain1 => "Governance and culture",
        DomainPart.Domain2 => "Service delivery",
        DomainPart.Domain3 => "Partnering and communicating",
        DomainPart.Domain4 => "Business management",
        _ => "Program",
    };

    /// <summary>Kind label with QSPP acronyms expanded for a reader who is not
    /// steeped in the documents (owner brief §4).</summary>
    public static string KindLabel(Requirement r) => r.RefKind switch
    {
        RefKind.Std => "Standard",
        RefKind.Uag => "Assessment guide section",
        RefKind.UagKeyTask => "Key task",
        RefKind.Rule => "Program rule",
        RefKind.Tc => "Terms and conditions",
        RefKind.Policy => "Policy",
        RefKind.Obligation => "Obligation",
        RefKind.Hitlist => "Readiness hit list",
        RefKind.Surveillance => "Remote assessment",
        _ => "Requirement",
    };
}

/// <summary>QSPP acronym expansion for detail panes, tooltips and generated letters.</summary>
public static class AcronymMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UAG"] = "User Assessment Guide",
        ["STD"] = "the Standard (AS 85000:2024)",
        ["T&C"] = "Terms and Conditions",
        ["REM"] = "Remote assessment",
        ["SURVEILLANCE"] = "Remote assessment",
        ["HITLIST"] = "Readiness hit list",
        ["P1"] = "Priority 1",
        ["P2"] = "Priority 2",
        ["P3"] = "Priority 3",
        ["P4"] = "Priority 4",
    };

    public static bool TryExpand(string acronym, out string expansion) => Map.TryGetValue(acronym, out expansion!);
}