using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;

namespace QsppManager.Infrastructure.Services;

/// <summary>Local corpus + evidence + document search backed by SQLite FTS5 (M2).
/// The index is rebuilt after seed import and kept fresh per record on evidence/document
/// changes. Requirement rows carry ref/heading wording; source-document rows let people
/// find the governing document; issue and evidence rows round out the palette.
/// Restricted record families still appear — M4 introduces role filtering here.</summary>
public class CorpusSearch(QsppDbContext db) : ICorpusIndexer
{
    public void EnsureIndex()
    {
        db.Database.ExecuteSqlRaw("""
            CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
                kind UNINDEXED, ref, title, body, meta UNINDEXED, target UNINDEXED)
            """);
    }

    /// <summary>Full rebuild from the current database contents. Cheap at this scale.</summary>
    public int RebuildIndex()
    {
        EnsureIndex();
        db.Database.ExecuteSqlRaw("DELETE FROM search_index");
        var n = 0;

        foreach (var r in db.Requirements.AsNoTracking().Include(x => x.EvidenceExamples))
        {
            var body = string.Join(" ", r.Title, r.PlainSummary, r.WordingExcerpt,
                string.Join(" ", r.EvidenceExamples.Select(e => e.Text)));
            IndexRow(r.RefKind.ToString(), r.Ref, r.Title, body, MetaRequirement(r), r.Id);
            n++;
        }

        foreach (var e in db.EvidenceItems.AsNoTracking())
        {
            IndexRow("Evidence", "", e.Title, string.Join(" ", e.Title, e.FileName, e.ReviewState, e.Kind),
                $"evidence · {e.FileName} · review {e.ReviewState}", e.Id);
            n++;
        }

        foreach (var d in db.SourceDocuments.AsNoTracking())
        {
            IndexRow("Source", "", d.DisplayName, string.Join(" ", d.DisplayName, d.FileName, d.Role, d.SourceVersion, d.ProvenanceNote),
                $"source document · {d.Role} · {d.SourceVersion}", d.Id);
            n++;
        }

        foreach (var i in db.InterpretationIssues.AsNoTracking())
        {
            IndexRow("Issue", i.Key, i.Title, string.Join(" ", i.Title, i.ConservativePlanning, i.Status),
                $"interpretation issue · {i.Status}", i.Id);
            n++;
        }
        return n;
    }

    private static string MetaRequirement(Requirement r) => $"{EnumTitle(r.RefKind)} · {r.SourceDocFile} · {r.SourceVersion}"
        + (r.AnchorPage.HasValue ? $" · PDF p{r.AnchorPage}" : r.AnchorApproximate && r.AnchorLines != "" ? $" · approx. lines {r.AnchorLines}" : "");

    private static string EnumTitle(RefKind k) => k switch
    {
        RefKind.Std => "Standard",
        RefKind.Uag => "UAG section",
        RefKind.UagKeyTask => "UAG key task",
        RefKind.Rule => "Program rule",
        RefKind.Tc => "Terms & conditions",
        RefKind.Policy => "Policy",
        RefKind.Obligation => "Obligation",
        RefKind.Hitlist => "Readiness hitlist",
        RefKind.Surveillance => "Remote assessment item",
        _ => k.ToString(),
    };

    private void IndexRow(string kind, string req, string title, string body, string meta, Guid target)
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection == null) return;
        cmd.CommandText = "INSERT INTO search_index (kind, ref, title, body, meta, target) VALUES (@k, @r, @t, @b, @m, @g)";
        AddParam(cmd, "@k", kind); AddParam(cmd, "@r", req); AddParam(cmd, "@t", title);
        AddParam(cmd, "@b", body); AddParam(cmd, "@m", meta); AddParam(cmd, "@g", target.ToString());
        if (cmd.Connection.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
        _ = cmd.ExecuteNonQuery();
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    public void IndexEvidence(Guid evidenceId)
    {
        var e = db.EvidenceItems.AsNoTracking().FirstOrDefault(x => x.Id == evidenceId);
        if (e == null) return;
        db.Database.ExecuteSqlRaw("DELETE FROM search_index WHERE target = {0}", evidenceId.ToString());
        IndexRow("Evidence", "", e.Title, string.Join(" ", e.Title, e.FileName, e.ReviewState, e.Kind),
            $"evidence · {e.FileName} · review {e.ReviewState}", e.Id);
    }

    public void RetireIndexEntry(Guid targetId)
    {
        db.Database.ExecuteSqlRaw("DELETE FROM search_index WHERE target = {0}", targetId.ToString());
    }

    public void IndexDocumentText(Guid documentId, string displayName, string text)
    {
        db.Database.ExecuteSqlRaw("DELETE FROM search_index WHERE target = {0}", documentId.ToString());
        IndexRow("Source", "", displayName, text, "source document · full text indexed", documentId);
    }

    public List<SearchResultRow> Query(string text, int cap = 40, bool hidePatientDataEvidence = false)
    {
        text = text.Trim();
        if (text.Length < 2) return [];
        var fts = BuildFtsQuery(text);
        List<SearchResultRow> rows = [];
        try
        {
            rows = QueryFts(fts, cap);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            rows = []; // malformed FTS syntax — fall through to LIKE
        }
        if (rows.Count == 0) rows = QueryLike(text, cap);
        if (hidePatientDataEvidence)
            rows = rows
                .Where(r => r.Kind != "Evidence" || !db.EvidenceItems.AsNoTracking()
                        .Any(e => e.Id == r.Id && e.ContainsPatientData))
                .ToList();
        return rows;
    }

    /// <summary>"potency test" → '"potency" AND "test"*' — every token required, last-token
    /// prefix enabled. The star goes OUTSIDE the quotes: inside them it's a literal token.</summary>
    public static string BuildFtsQuery(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => "\"" + t.Replace("\"", "") + "\"").ToList();
        if (tokens.Count == 0) return "\"\"";
        tokens[^1] += "*";
        return string.Join(" AND ", tokens);
    }

    private List<SearchResultRow> QueryFts(string match, int cap)
    {
        var rows = new List<SearchResultRow>();
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection == null) return rows;
        if (cmd.Connection.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
        cmd.CommandText = """
            SELECT kind, ref, title, snippet(search_index, 3, '[', ']', '…', 14), meta, target,
                   bm25(search_index, 0.0, 2.0, 10.0, 1.0, 0.0, 0.0) AS score
            FROM search_index WHERE search_index MATCH @q
            ORDER BY score LIMIT @cap
            """;
        var q = cmd.CreateParameter(); q.ParameterName = "@q"; q.Value = match; cmd.Parameters.Add(q);
        var c = cmd.CreateParameter(); c.ParameterName = "@cap"; c.Value = cap; cmd.Parameters.Add(c);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var kind = reader.GetString(0);
            var refTxt = reader.GetString(1);
            var title = reader.GetString(2);
            var excerpt = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var meta = reader.GetString(4);
            _ = Guid.TryParse(reader.GetString(5), out var id);
            rows.Add(new SearchResultRow(kind, string.IsNullOrEmpty(refTxt) ? title : $"{refTxt} — {title}",
                excerpt, id, meta));
        }
        return rows;
    }

    /// <summary>LIKE fallback (also used when FTS finds nothing — substring beats token match).</summary>
    private List<SearchResultRow> QueryLike(string text, int cap)
    {
        var like = $"%{text.Replace("%", "").Replace("_", "")}%";
        List<SearchResultRow> rows = [];

        rows.AddRange(db.Requirements.AsNoTracking()
            .Where(r => EF.Functions.Like(r.Title, like) || EF.Functions.Like(r.Ref, like)
                        || EF.Functions.Like(r.PlainSummary, like) || EF.Functions.Like(r.WordingExcerpt, like))
            .OrderBy(r => r.RefKind).ThenBy(r => r.Ref)
            .Take(cap)
            .AsEnumerable()
            .Select(r => new SearchResultRow(
                EnumTitle(r.RefKind), $"{r.Ref} — {r.Title}",
                Snippet(r.PlainSummary, r.WordingExcerpt, text), r.Id, MetaRequirement(r))));

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