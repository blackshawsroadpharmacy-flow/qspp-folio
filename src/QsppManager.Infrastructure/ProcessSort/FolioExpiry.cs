using System.Text.RegularExpressions;
using QsppManager.Domain.Entities;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Honest date-of-expiry detection on a document's own text. QSPP evidence
/// expires: insurance certificates, registrations, CPRS training, calibration records.
/// Detection rules, all conservative:
///   • a candidate line must CONTAIN an expiry keyword (expiry/expired/expires/expiring,
///     valid until, valid thru, next due, renewal date, review date, lapsed);
///   • the date must appear ON THE SAME LINE, after the keyword — a date anywhere in a
///     body sentence is never claimed (too many policy documents quote other dates);
///   • "valid from X to Y" shapes prefer the SECOND (binding) date,
///     and only when that second date exists;
///   • only plausible years (1995–2099) parse; garbled and malformed text silently yields
///     NO detection rather than a guessed one.
/// The detector NEVER decides whether the document actually proves a requirement — it
/// records what the document says about its own expiry, and the human verifies.</summary>
public static partial class FolioExpiry
{
    [GeneratedRegex(@"(?i)\b(expir(?:y|es|ed|ing)|valid until|valid thru|valid from|next due|renewal date|review date|lapsed|not valid after)\b")]
    private static partial Regex KeywordRegex();

    /// <summary>The binding-end preference: a month-name range or "valid from" line keeps
    /// looking for a SECOND date within this many characters of the first.</summary>
    private const int RangeLookahead = 60;

    public static FolioExpiryGuess? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.Length > 400) continue;

            var kw = KeywordRegex().Match(line);
            if (!kw.Success) continue;

            var (first, afterFirst) = FirstDate(line, kw.Index + kw.Length);
            if (first is null) continue;

            // "valid from X to Y" / "valid from X until Y": the first bound after the
            // keyword is a START — take the binding end when the line carries one;
            // a "from" with no end yields NO guess rather than the start date.
            var windowEnd = Math.Min(line.Length, afterFirst);
            var fromShape = Regex.IsMatch(line[kw.Index..windowEnd], @"(?i)\bfrom\b");
            if (fromShape)
            {
                var (second, _) = FirstDate(line, afterFirst);
                if (second is { } s) return new FolioExpiryGuess(s, HintOf(line));
                continue;
            }
            return new FolioExpiryGuess(first.Value, HintOf(line));
        }
        return null;
    }

    /// <summary>First plausible date starting at or after `from`, as (date, index-after-date).</summary>
    private static (DateOnly? date, int after) FirstDate(string line, int from)
    {
        if (from >= line.Length) return (null, 0);
        foreach (Match m in DateRegex().Matches(line[from..]))
            if (m.Value is { } raw && TryParse(raw) is { } d && d.Year is >= 1995 and <= 2099
                && m.Index >= 0)
                return (d, from + m.Index + m.Length);
        return (null, 0);
    }

    private static string HintOf(string line) =>
        line.Length <= 200 ? line : line[..197] + "…";

    [GeneratedRegex(@"\b(?:\d{1,2}(?:st|nd|rd|th)?\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}|\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4}|\d{1,2}-(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)-\d{4}|\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{4}|\d{1,2}\.\d{1,2}\.\d{4})")]
    private static partial Regex DateRegex();

    /// <summary>Australian-first parsing: d/M/yyyy beats M/d/yyyy; month names are locale-free;
    /// ordinal suffixes are stripped with a targeted regex (never a blanket "st " replace,
    /// which would corrupt the word "August").</summary>
    public static DateOnly? TryParse(string raw)
    {
        var s = Regex.Replace(raw.Trim().ToLowerInvariant(), @"(\d{1,2})(st|nd|rd|th)\b", "$1");
        var formats = new[]
        {
            "d MMMM yyyy", "d MMM yyyy", "d-MMM-yyyy", "yyyy-MM-dd", "d/M/yyyy", "d.M.yyyy",
        };
        foreach (var f in formats)
            if (DateOnly.TryParseExact(s, f, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d))
                return d;
        // slash fallback for US-shaped dates: d/M failed (second slot > 12), so the
        // FIRST slot must be the month ("3/31/2025" → 31 March); anything else yields null
        if (Regex.IsMatch(s, @"^\d{1,2}/\d{1,2}/\d{4}$"))
        {
            var parts = s.Split('/');
            if (int.TryParse(parts[0], out var month) && month is >= 1 and <= 12
                && int.TryParse(parts[1], out var day) && day is >= 1 and <= 31
                && int.TryParse(parts[2], out var year))
            {
                try { return new DateOnly(year, month, day); } catch { return null; }
            }
        }
        return null;
    }
}