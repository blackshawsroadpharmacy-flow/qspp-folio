using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;

namespace QsppManager.Infrastructure.Services;

/// <summary>Local corpus + evidence + document search (v1: EF LIKE; FTS5 arrives in M2).
/// Restricted record families (HR, sensitive incidents) are excluded ahead of M4 role
/// filtering — see docs/milestones.md.</summary>
public class CorpusSearch(QsppDbContext db)
{
    public List<SearchResultRow> Query(string text, int cap = 40)
    {
        text = text.Trim();
        if (text.Length < 2) return [];
        var like = $"%{text.Replace("%", "").Replace("_", "")}%";
        List<SearchResultRow> rows = [];

        rows.AddRange(db.Requirements.AsNoTracking()
            .Where(r => EF.Functions.Like(r.Title, like) || EF.Functions.Like(r.Ref, like)
                        || EF.Functions.Like(r.PlainSummary, like) || EF.Functions.Like(r.WordingExcerpt, like))
            .OrderBy(r => r.RefKind).ThenBy(r => r.Ref)
            .Take(cap)
            .AsEnumerable()
            .Select(r => new SearchResultRow(
                r.RefKind.ToString(), $"{r.Ref} — {r.Title}",
                Snippet(r.PlainSummary, r.WordingExcerpt, text), r.Id,
                $"{r.SourceDocFile} v{r.SourceVersion}{(r.AnchorApproximate ? " · approx. lines " + r.AnchorLines : "")}")));

        rows.AddRange(db.EvidenceItems.AsNoTracking()
            .Where(e => EF.Functions.Like(e.Title, like) || EF.Functions.Like(e.FileName, like))
            .Take(cap / 2)
            .AsEnumerable()
            .Select(e => new SearchResultRow("Evidence", e.Title, $"{e.Kind} · {e.FileName}", e.Id,
                $"sha256 {e.Sha256.Substring(0, Math.Min(12, e.Sha256.Length))}… · review: {e.ReviewState}")));

        rows.AddRange(db.SourceDocuments.AsNoTracking()
            .Where(d => EF.Functions.Like(d.DisplayName, like) || EF.Functions.Like(d.FileName, like))
            .Take(cap / 2)
            .AsEnumerable()
            .Select(d => new SearchResultRow("Source", d.DisplayName, $"{d.Role} · {d.SourceVersion}", d.Id,
                $"sha256 {d.Sha256Hex.Substring(0, Math.Min(12, d.Sha256Hex.Length))}…")));

        return rows.GroupBy(r => (r.Kind, r.Id)).Select(g => g.First()).Take(cap).ToList();
    }

    private static string Snippet(string summary, string wording, string term)
    {
        var hay = string.IsNullOrWhiteSpace(summary) ? wording : summary;
        var idx = hay.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || hay.Length <= 120) return hay.Length > 140 ? hay[..140] + "…" : hay;
        var start = Math.Max(0, idx - 40);
        var len = Math.Min(120, hay.Length - start);
        return (start > 0 ? "…" : "") + hay.Substring(start, len) + (start + len < hay.Length ? "…" : "");
    }
}