using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Desktop.Views;

/// <summary>Library: the reading room. Corpus search over requirements/evidence/documents
/// (FTS5), the verified source documents with provenance, the interpretation issues with
/// their competing quotes, and the supersession impact review.</summary>
public partial class LibraryView : UserControl, IPageSearchFocus
{
    private readonly DataGrid _sources = Ui.Grid().With(_ => _.Height = 220);
    private readonly DataGrid _issues = Ui.Grid(rowHeight: 34).With(_ => _.Height = 140);
    private readonly DataGrid _superseded = Ui.Grid(rowHeight: 34).With(_ => _.Height = 150);
    private readonly TextBox _searchBox = new() { Watermark = "Search the whole corpus — try potency, mid-cycle, attestation…" };
    private readonly ListBox _results = new() { Height = 220, Classes = { "list" } };
    private readonly Grid _resultsHost = new();
    private readonly Control _resultsEmpty = Ui.EmptyState("search", "Search the corpus",
        "Find requirements, evidence records and source documents. Press Enter or click Search; double-click a result to open the record.");
    private readonly TextBox _resultBar = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 34, Watermark = "Result messages appear here", Classes = { "result" } };
    private readonly StackPanel _issueDetail = new() { Spacing = 6 };
    private readonly ListBox _diffLines = new() { Height = 160, Background = Brushes.Transparent };

    public LibraryView(ShellViewModel shell)
    {
        Content = Build(shell);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 14, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.H1("Library"));
        sp.Children.Add(Ui.Lead("Search every imported requirement, evidence record and source document, read the governing documents with their checksums, and track the interpretation questions that are deliberately kept open until a source settles them."));
        sp.Children.Add(_resultBar);

        sp.Children.Add(SearchSection(shell));
        sp.Children.Add(SourcesSection());
        sp.Children.Add(IssuesSection());
        sp.Children.Add(SupersessionSection());
        sp.Children.Add(CompareSection());

        return new ScrollViewer { Content = sp };
    }

    // ---- corpus search ----
    private Control SearchSection(ShellViewModel shell)
    {
        Ui.Ax(_searchBox, "Corpus search", "Searches every requirement, evidence record and source document");
        Ui.Ax(_results, "Corpus search results", "Double-click a result to open the record");
        _results.ItemTemplate = SearchRowTemplate();

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _searchBox.Width = 640;
        _searchBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) RunSearch(); };
        searchRow.Children.Add(_searchBox);
        searchRow.Children.Add(Ui.PrimaryBtn("Search", null, 110, null, "Run the corpus search")
            .With(b => { b.Height = 32; b.Click += (_, _) => RunSearch(); }));

        _resultsHost.Children.Add(_results);
        _resultsHost.Children.Add(_resultsEmpty);
        UpdateResultsHost();
        _results.DoubleTapped += (_, _) => OpenSelected(shell);

        return Ui.Section("Corpus search", Ui.Col(10, searchRow, _resultsHost,
            Ui.Small("Enter in the box or double-click a result opens the record. Bracketed highlights mark the matched text.")));
    }

    // ---- source documents ----
    private Control SourcesSection()
    {
        _sources.Columns.Add(Ui.Col("Document", "DisplayName", 430));
        _sources.Columns.Add(Ui.Col("Role", "Role", 90));
        _sources.Columns.Add(Ui.Col("Version", "Version", 170));
        _sources.Columns.Add(Ui.Col("sha256", "ShaShort", 110));
        _sources.Columns.Add(Ui.Col("Full text", "Indexed", 80));
        _sources.Columns.Add(Ui.Col("Provenance", "Provenance", 230));

        var docRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        docRow.Children.Add(Ui.Btn("Open source PDF via system viewer").With(b =>
        {
            b.Height = 32;
            b.Click += (_, _) =>
            {
                if (_sources.SelectedItem is SourceDocRow s)
                    Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(s.StoredRelativePath));
                else _resultBar.Text = "Select a source document first.";
            };
        }));
        docRow.Children.Add(Ui.Btn("Extract full text (selected)").With(b => b.Height = 32).
            With(b => b.Click += (_, _) =>
            {
                if (_sources.SelectedItem is not SourceDocRow s) { _resultBar.Text = "Select a source document first."; return; }
                var (ok, pages, err) = App.TextIndex.Extract(s.Id);
                _resultBar.Text = ok
                    ? $"Extracted {pages} pages from “{s.DisplayName}” into the search index. Ctrl+K and the search box now find words inside that document."
                    : $"Extraction failed: {err}";
                Load();
            }));
        docRow.Children.Add(Ui.Btn("Extract & index all documents").With(b => b.Height = 32).
            With(b => b.Click += (_, _) =>
            {
                var docs = App.Workspace.AllSourceDocuments();
                var done = 0; var failed = 0; var pagesTotal = 0; var lastError = "";
                foreach (var d in docs)
                {
                    var (ok, pages, err) = App.TextIndex.Extract(d.Id);
                    if (ok) { done++; pagesTotal += pages; } else { failed++; lastError = $"{d.DisplayName}: {err}"; }
                }
                _resultBar.Text = done == docs.Count
                    ? $"Indexed {done} documents ({pagesTotal} pages). The full corpus text is now searchable."
                    : $"Indexed {done} of {docs.Count} documents ({pagesTotal} pages); {failed} failed — first failure: {lastError}";
                Load();
            }));

        return Ui.Section("Source documents (verified corpus)", Ui.Col(10, _sources, docRow));
    }

    // ---- interpretation issues ----
    private Control IssuesSection()
    {
        _issues.Columns.Add(Ui.Col("Key", "Key", 170));
        _issues.Columns.Add(Ui.Col("Question", "Title", 470));
        _issues.Columns.Add(Ui.Col("Status", "Status", 150));
        _issues.Columns.Add(Ui.Col("Conservative planning", "Planning", 280));

        var issueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        issueRow.Children.Add(Ui.Btn("Show competing sources").With(b => { b.Height = 32; b.Click += (_, _) => ShowIssueSources(); }));
        issueRow.Children.Add(Ui.Btn("Acknowledge").With(b => { b.Height = 32; b.Click += (_, _) => Acknowledge(); }));
        issueRow.Children.Add(Ui.Btn("Resolve…").With(b => { b.Height = 32; b.Click += async (_, _) => await ResolveAsync(); }));

        return Ui.Section("Interpretation issues deliberately kept open", Ui.Col(10,
            Ui.Small("Where the program sources disagree (or wording needs confirmation), the app refuses to pick a side silently. The conservative reading is used for planning dates and is labelled PLANNING ONLY everywhere it appears. Acknowledging records that the pharmacy has seen it; resolving requires a note recording who settled it and which source is authoritative."),
            _issues, _issueDetail, issueRow));
    }

    // ---- supersession review ----
    private Control SupersessionSection()
    {
        _superseded.Columns.Add(Ui.Col("Retired evidence", "OldTitle", 330));
        _superseded.Columns.Add(Ui.Col("Replacement", "Replacement", 280));
        _superseded.Columns.Add(Ui.Col("Reason", "Reason", 220));
        _superseded.Columns.Add(Ui.Col("Links affected", "AffectedReqs", 110));
        _superseded.Columns.Add(Ui.Col("Open re-link tasks", "OpenTasks", 130));

        return Ui.Section("Supersession review", Ui.Col(10,
            Ui.Small("Retired evidence versions with the requirement links that were affected. Each affected link already has a re-link task assigned to the requirement owner — this table is the impact overview."),
            _superseded));
    }

    // ---- source version compare ----
    private Control CompareSection()
    {
        _diffLines.ItemTemplate = DiffRowTemplate();
        var cmpRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        cmpRow.Children.Add(Ui.Btn("Compare full text of two documents…").With(b => { b.Height = 32; b.Click += async (_, _) =>
        {
            var docs = App.Workspace.AllSourceDocuments().Where(d => d.Sha256Hex.Length > 0).ToList();
            if (docs.Count < 2) { _resultBar.Text = "Two source documents are needed for a compare."; return; }
            var list = string.Join("\n", docs.Select((d, i) => $"{i + 1}. {d.DisplayName} ({d.FileName})"));
            var aIdx = await Dialogs.Prompt(this, "Compare source versions — the OLDER version", "Number of the first document…\n" + list);
            if (aIdx is null || !int.TryParse(aIdx, out var ai) || ai < 1 || ai > docs.Count) return;
            var bIdx = await Dialogs.Prompt(this, "Compare source versions — the NEWER version", "Number of the second document");
            if (bIdx is null || !int.TryParse(bIdx, out var bi) || bi < 1 || bi > docs.Count) return;
            var (ok, err, summary, lines) = SourceCompare.DiffDocuments(App.TextIndex, docs[ai - 1], docs[bi - 1]);
            _resultBar.Text = ok ? summary : err;
            _diffLines.ItemsSource = lines.Take(150).Select(l => new DiffRow(l)).ToList();
        }; }));

        return Ui.Section("Compare source versions", Ui.Col(10, cmpRow,
            Ui.Small("Diff runs on the cached full-text extractions (both documents get extracted on demand). '+ ' lines exist only in the newer document; '- ' lines only in the older."),
            _diffLines));
    }

    private void Load()
    {
        _sources.ItemsSource = App.Workspace.AllSourceDocuments()
            .Select(d => new SourceDocRow(d.Id, d.DisplayName, d.Role, d.SourceVersion, d.Sha256Hex[..12] + "…",
                App.TextIndex.IsCached(d.Sha256Hex) ? "indexed" : "—", d.ProvenanceNote, d.StoredRelativePath)).ToList();
        _issues.ItemsSource = App.Workspace.Issues()
            .Select(i => new IssueRow(i.Id, i.Key, i.Title, i.Status, i.ConservativePlanning)).ToList();
        _superseded.ItemsSource = App.Workspace.SupersessionReview()
            .Select(r => new SupersededRow(r.OldTitle, r.ReplacementTitle, r.Reason, r.AffectedRequirementLinks.ToString(), r.OpenTaskCount.ToString())).ToList();
        SelectFirstIssue();
    }

    private void SelectFirstIssue()
    {
        if (_issues.ItemsSource is List<IssueRow> rows && rows.Count > 0) _issues.SelectedIndex = 0;
    }

    private void RunSearch()
    {
        _results.ItemsSource = App.Search.Query(_searchBox.Text, 60, hidePatientDataEvidence: App.Session?.ReadOnly == true)
            .Select(r => new SearchRow(r.Kind, r.Title, r.Excerpt, r.Meta, r.Id)).ToList();
        UpdateResultsHost();
        if (_results.ItemCount == 0) _resultBar.Text = "Nothing matched. The LIKE fallback also ran — try a shorter term.";
    }

    private void UpdateResultsHost()
    {
        var hasResults = _results.ItemCount > 0;
        _results.IsVisible = hasResults;
        _resultsEmpty.IsVisible = !hasResults;
    }

    private void OpenSelected(ShellViewModel shell)
    {
        if (_results.SelectedItem is SearchRow row)
            shell.OpenResult(new QsppManager.Application.SearchResultRow(row.Kind, row.Title, row.Excerpt, row.Id, row.Meta));
    }

    private IssueRow? SelectedIssue => _issues.SelectedItem as IssueRow;

    private void ShowIssueSources()
    {
        if (SelectedIssue is not { } row) { _resultBar.Text = "Select an interpretation issue first."; return; }
        var issue = App.Workspace.Issues().First(i => i.Id == row.Id);
        _issueDetail.Children.Clear();
        _issueDetail.Children.Add(Ui.Label("SOURCES — VERBATIM, AS RETRIEVED"));
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(issue.SourcesJson) ? "[]" : issue.SourcesJson);
            foreach (var s in doc.RootElement.ValueKind == JsonValueKind.Array
                         ? doc.RootElement.EnumerateArray().ToList()
                         : new List<JsonElement>())
            {
                var reference = s.TryGetProperty("reference", out var rEl) ? rEl.GetString() : "";
                var text = s.TryGetProperty("text", out var tEl) ? tEl.GetString() : "";
                var file = s.TryGetProperty("file", out var fEl) ? fEl.GetString() : "";
                var lines = s.TryGetProperty("approxLines", out var lEl) ? (lEl.ValueKind == JsonValueKind.String ? lEl.GetString() : "") : "";
                _issueDetail.Children.Add(Ui.Card(new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { FontWeight = FontWeight.Medium, Text = reference ?? "", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Classes = { "monoSm" }, Text = text ?? "", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Classes = { "small" }, Text = $"{file}{(string.IsNullOrEmpty(lines) ? "" : " · approx. lines " + lines)}", TextWrapping = TextWrapping.Wrap },
                    },
                }, vPad: 8, hPad: 12));
            }
        }
        catch (JsonException)
        {
            _issueDetail.Children.Add(new TextBlock { Classes = { "small" }, Text = "(source list unreadable — see coverage ledger)" });
        }
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        statusRow.Children.Add(Ui.Chip(issue.Status, IssueTone(issue.Status)));
        statusRow.Children.Add(new TextBlock
        {
            Classes = { "small" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Text = $"Retrieved {issue.RetrievedDate} · pack {issue.PackVersion}",
        });
        _issueDetail.Children.Add(statusRow);
        if (issue.Status == "resolved")
            _issueDetail.Children.Add(Ui.Lead($"RESOLVED: {issue.ResolutionNote}"));
    }

    private static string IssueTone(string status) => status switch
    {
        "resolved" => "ok",
        "acknowledged" => "info",
        _ => "warn",
    };

    private void Acknowledge()
    {
        if (SelectedIssue is not { } row) { _resultBar.Text = "Select an interpretation issue first."; return; }
        var res = App.Workspace.AcknowledgeIssue(row.Id, App.Session!);
        _resultBar.Text = res.Ok ? "Acknowledged — recorded in the audit trail. The conservative planning reading still applies." : res.Error;
        Load();
    }

    private async Task ResolveAsync()
    {
        if (SelectedIssue is not { } row) { _resultBar.Text = "Select an interpretation issue first."; return; }
        var note = await Dialogs.Prompt(this, "Resolution record (required)",
            "Who settled this, and which source is taken as authoritative?");
        if (string.IsNullOrWhiteSpace(note)) return;
        var res = App.Workspace.ResolveIssue(row.Id, note, App.Session!);
        _resultBar.Text = res.Ok ? "Resolved and recorded. Planning flags for this question can now follow the settled source." : res.Error;
        Load();
    }

    public record SourceDocRow(Guid Id, string DisplayName, string Role, string Version, string ShaShort, string Indexed, string Provenance, string StoredRelativePath);
    public record IssueRow(Guid Id, string Key, string Title, string Status, string Planning);
    public record SupersededRow(string OldTitle, string Replacement, string Reason, string AffectedReqs, string OpenTasks);
    public record SearchRow(string Kind, string Title, string Excerpt, string Meta, Guid Id);
    public record DiffRow(string Line);

    /// <summary>Kit two-line search-result row: name, kind + meta strip, excerpt.</summary>
    private static FuncDataTemplate<object> SearchRowTemplate() => new((_, _) =>
    {
        var sp = new StackPanel { Spacing = 2 };
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.TextProperty, new Binding("Title"));

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var kind = new TextBlock { Classes = { "monoSm" }, Foreground = Ui.Br("Brush.Green") };
        kind.Bind(TextBlock.TextProperty, new Binding("Kind"));
        var meta = new TextBlock { Classes = { "small" } };
        meta.Bind(TextBlock.TextProperty, new Binding("Meta"));
        metaRow.Children.Add(kind);
        metaRow.Children.Add(meta);

        var excerpt = new TextBlock { Classes = { "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
        excerpt.Bind(TextBlock.TextProperty, new Binding("Excerpt"));

        sp.Children.Add(title);
        sp.Children.Add(metaRow);
        sp.Children.Add(excerpt);
        return sp;
    }, true);

    private static FuncDataTemplate<object> DiffRowTemplate() => new((_, _) =>
    {
        var tb = new TextBlock { Classes = { "monoSm" }, VerticalAlignment = VerticalAlignment.Center };
        tb.Bind(TextBlock.TextProperty, new Binding("Line"));
        return tb;
    }, true);

    public void OpenLibrarySearch(string text)
    {
        _searchBox.Text = text;
        RunSearch();
    }

    public void FocusSearch()
    {
        _searchBox.Focus();
        _searchBox.SelectAll();
    }
    public void SetFilter(string text) => OpenLibrarySearch(text);
}