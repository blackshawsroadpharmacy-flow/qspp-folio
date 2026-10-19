using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using QsppManager.Domain.Entities;

namespace QsppManager.Infrastructure.Services;

/// <summary>Source-change compare (M4): line-level diff between two managed documents —
/// the hub-vs-original UAG class of change lands here verbatim, with a countable summary.</summary>
public static class SourceCompare
{
    public static (string summary, List<string> lines) Diff(string titleA, string textA, string titleB, string textB)
    {
        var builder = InlineDiffBuilder.Instance;
        var result = builder.BuildDiffModel(textA ?? "", textB ?? "", ignoreWhitespace: true);

        var lines = new List<string>();
        var changed = 0;
        foreach (var line in result.Lines)
        {
            var prefix = line.Type switch
            {
                ChangeType.Inserted => "+ ",
                ChangeType.Deleted => "- ",
                ChangeType.Unchanged => "  ",
                _ => "? ",
            };
            if (line.Type is ChangeType.Inserted or ChangeType.Deleted)
            {
                changed++;
                if (lines.Count < 400) lines.Add(prefix + line.Text);
            }
            else if (lines.Count < 400 && lines.Count > 0 && lines[^1].StartsWith("…") == false && line.Text.Trim().Length > 0 && lines.Count % 12 == 0)
            {
                lines.Add("… context continues unchanged …");
            }
        }

        var summary = $"{titleA} → {titleB}: {changed} differing line(s) " +
                      "(+ added in B, - removed from A; whitespace-only differences ignored)";
        return (summary, lines);
    }

    /// <summary>Convenience: diff two managed source documents using their cached full text
    /// (extracted on demand through the DocumentTextIndex cache).</summary>
    public static (bool ok, string error, string summary, List<string> lines) DiffDocuments(
        DocumentTextIndex textIndex, SourceDocument a, SourceDocument b)
    {
        var ta = GetText(textIndex, a);
        var tb = GetText(textIndex, b);
        if (ta == null || tb == null) return (false, $"Extract full text for both documents first (missing: {(ta == null ? a.DisplayName : b.DisplayName)})", "", []);
        var (summary, lines) = Diff(a.DisplayName, ta, b.DisplayName, tb!);
        return (true, "", summary, lines);
    }

    private static string? GetText(DocumentTextIndex textIndex, SourceDocument d)
    {
        if (!textIndex.IsCached(d.Sha256Hex))
        {
            var (ok, _, err) = textIndex.Extract(d.Id);
            if (!ok) return null;
        }
        textIndex.IsCached(d.Sha256Hex);
        var cachePath = Path.Combine(textIndex.CacheRootSnapshot, $"doctext-{d.Sha256Hex[..Math.Min(24, d.Sha256Hex.Length)]}.txt");
        return File.Exists(cachePath) ? File.ReadAllText(cachePath) : null;
    }
}