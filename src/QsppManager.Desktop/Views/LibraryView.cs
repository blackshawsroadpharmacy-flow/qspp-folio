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
    private readonly DataGrid _sources = Ui.Grid();
    private TableHost _sourcesHost = null!;
    private readonly TextBlock _sourceDetail = new() { Classes = { "small" }, Foreground = Ui.Br("Brush.Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(4, 2, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private bool _searchRan;
    private readonly DataGrid _issues = Ui.Grid(rowHeight: 34).With(_ => _.Height = 140);
    private readonly DataGrid _superseded = Ui.Grid(rowHeight: 34).With(_ => _.Height = 150);
    private readonly TextBox _searchBox = new() { Watermark = "Search the whole corpus. Try potency, mid-cycle, attestation." };
    private readonly ListBox _results = new() { Height = 220, Classes = { "list" } };
    private readonly Grid _resultsHost = new();
    // One quiet hint line until a search runs (owner brief, Library — the old big empty card
    // plus a second hint line said the same thing twice).
    private readonly Control _resultsEmpty = new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(4, 8),
        Children =
        {
            new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Ui.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Classes = { "small" }, Foreground = Ui.Br("Brush.Muted"), Text = "Type a term and press Enter. Double-click a result opens the record; bracketed highlights mark the matched text.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
        },
    };
    private readonly NoticeHost _resultBar = new();
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
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.Library, "Search requirements, evidence and sources; open questions stay visible.", "Search every imported requirement, evidence record and source document, read the governing documents with their checksums, and track the interpretation questions that are deliberately kept open until a source settles them."));
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

        var searchRow = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new(0, 0, 0, 8) };
        _searchBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) RunSearch(); };
        searchRow.Children.Add(_searchBox);
        searchRow.Children.Add(Ui.PrimaryBtn("Search", null, 110, null, "Run the corpus search")
            .With(b => { b.Height = 32; b.Click += (_, _) => RunSearch(); b.Margin = new(8, 0, 0, 0); }));
        Avalonia.Controls.Grid.SetColumn(searchRow.Children[1], 1);

        _resultsHost.Children.Add(_results);
        _resultsHost.Children.Add(_resultsEmpty);
        UpdateResultsHost();
        _results.DoubleTapped += (_, _) => OpenSelected(shell);

        return Ui.Section("Corpus search", Ui.Col(10, searchRow, _resultsHost));
    }

    // ---- source documents ----
    private Control SourcesSection()
    {
        // sha256 left the grid for the selection detail line (owner brief — the column was
        // clipped); "Full text" keeps its own comfortably wide column.
        _sources.Columns.Add(Ui.Col("Document", "DisplayName", 430));
        _sources.Columns.Add(Ui.Col("Role", "Role", 110));
        _sources.Columns.Add(Ui.Col("Version", "Version", 170));
        _sources.Columns.Add(Ui.Col("Full text", "Indexed", 100));
        _sources.Columns.Add(Ui.Col("Provenance", "Provenance", 230));
        _sources.MaxHeight = 260;
        _sources.SelectionChanged += (_, _) => ShowSourceDetail();

        // The three actions live in a "…" menu on the section header (owner brief, §5).
        var more = new Button { Classes = { "ghost" }, MinWidth = 32, Content = "…" };
        var openPdf = new MenuItem { Header = "Open source PDF…" };
        openPdf.Click += (_, _) =>
        {
            if (_sources.SelectedItem is SourceDocRow s)
                Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(s.StoredRelativePath));
            else _resultBar.Show(NoticeTone.Info, "Select a source document first.");
        };
        var extractSel = new MenuItem { Header = "Extract full text (selected)" };
        extractSel.Click += (_, _) =>
        {
            if (_sources.SelectedItem is not SourceDocRow s) { _resultBar.Show(NoticeTone.Info, "Select a source document first."); return; }
            var (ok, pages, err) = App.TextIndex.Extract(s.Id);
            _resultBar.Show(ok,
                $"Extracted {pages} pages from \u201C{s.DisplayName}\u201D into the search index. Ctrl+K and the search box now find words inside that document.",
                $"Extraction failed: {err}");
            Load();
        };
        var extractAll = new MenuItem { Header = "Extract and index all documents" };
        extractAll.Click += (_, _) =>
        {
            var docs = App.Workspace.AllSourceDocuments();
            var done = 0; var failed = 0; var pagesTotal = 0; var lastError = "";
            foreach (var d in docs)
            {
                var (ok, pages, err) = App.TextIndex.Extract(d.Id);
                if (ok) { done++; pagesTotal += pages; } else { failed++; lastError = $"{d.DisplayName}: {err}"; }
            }
            _resultBar.Show(done == docs.Count ? NoticeTone.Ok : NoticeTone.Error,
                $"Indexed {done} documents ({pagesTotal} pages). The full corpus text is now searchable.",
                $"Indexed {done} of {docs.Count} documents ({pagesTotal} pages); {failed} failed; first failure: {lastError}");
            Load();
        };
        more.Flyout = new Avalonia.Controls.Flyout { Content = new StackPanel { Children = { openPdf, extractSel, extractAll } } };

        _sourcesHost = Host(_sources, 260, "file-text", "No source documents imported", "The verified corpus appears here as documents are imported during setup - each with its checksum and provenance.", "Brush.Ink");
        return Ui.Section("Source documents (verified corpus)", Ui.Col(10, _sourcesHost.Panel, _sourceDetail), more);
    }

    /// <summary>sha256 lives on the selection detail line, not a clipped column (owner brief).</summary>
    private void ShowSourceDetail()
    {
        _sourceDetail.Text = _sources.SelectedItem is SourceDocRow s
            ? $"{s.DisplayName} · {s.Version} · sha256 {s.ShaShort}"
            : "";
    }

    // ---- interpretation issues ----
    private Control IssuesSection()
    {
        _issues.Columns.Add(Ui.Col("Key", "Key", 170));
        _issues.Columns.Add(Ui.Col("Question", "Title", 470));
        _issues.Columns.Add(Ui.Col("Status", "Status", 150));
        _issues.Columns.Add(Ui.Col("Conservative planning", "Planning", 280));

        var issueRow = new WrapPanel();
        issueRow.Children.Add(Ui.Btn("Show competing sources").With(b => { b.Height = 32; b.Click += (_, _) => ShowIssueSources(); }));
        issueRow.Children.Add(Ui.Btn("Acknowledge").With(b => { b.Height = 32; b.Click += (_, _) => Acknowledge(); }));
        issueRow.Children.Add(Ui.Btn("Resolve…").With(b => { b.Height = 32; b.Click += async (_, _) => await ResolveAsync(); }));
        Ui.WrapGaps(issueRow);

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
            Ui.Small("Retired evidence versions with the requirement links that were affected. Each affected link already has a re-link task assigned to the requirement owner; this table is the impact overview."),
            _superseded));
    }

    // ---- source version compare ----
    private Control CompareSection()
    {
        _diffLines.ItemTemplate = DiffRowTemplate();
        var cmpRow = new WrapPanel();
        cmpRow.Children.Add(Ui.Btn("Compare full text of two documents…").With(b => { b.Height = 32; b.Click += async (_, _) =>
        {
            var docs = App.Workspace.AllSourceDocuments().Where(d => d.Sha256Hex.Length > 0).ToList();
                        if (docs.Count < 2) { _resultBar.Show(NoticeTone.Info, "Two source documents are needed for a compare."); return; }
            var list = string.Join("\n", docs.Select((d, i) => $"{i + 1}. {d.DisplayName} ({d.FileName})"));
            var aIdx = await Dialogs.Prompt(this, "Compare source versions · the OLDER version", "Number of the first document…\n" + list);
            if (aIdx is null || !int.TryParse(aIdx, out var ai) || ai < 1 || ai > docs.Count) return;
            var bIdx = await Dialogs.Prompt(this, "Compare source versions · the NEWER version", "Number of the second document");
            if (bIdx is null || !int.TryParse(bIdx, out var bi) || bi < 1 || bi > docs.Count) return;
            var (ok, err, summary, lines) = SourceCompare.DiffDocuments(App.TextIndex, docs[ai - 1], docs[bi - 1]);
                        _resultBar.Show(ok ? NoticeTone.Ok : NoticeTone.Error, ok ? summary : err);
            _diffLines.ItemsSource = lines.Take(150).Select(l => new DiffRow(l)).ToList();
        }; }));

        Ui.WrapGaps(cmpRow);
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
        _sourcesHost?.Refresh(_sources.ItemsSource?.Cast<object>().Count() ?? 0);
        SelectFirstIssue();
    }

    private void SelectFirstIssue()
    {
        if (_issues.ItemsSource is List<IssueRow> rows && rows.Count > 0) _issues.SelectedIndex = 0;
    }

    private void RunSearch()
    {
        _searchRan = true;
        _results.ItemsSource = App.Search.Query(_searchBox.Text, 60, hidePatientDataEvidence: App.Session?.ReadOnly == true)
            .Select(r => new SearchRow(r.Kind, r.Title, r.Excerpt, r.Meta, r.Id, r.RefCode)).ToList();
        UpdateResultsHost();
                if (_results.ItemCount == 0) _resultBar.Show(NoticeTone.Info, "Nothing matched. The LIKE fallback also ran. Try a shorter term.");
    }

    /// <summary>Results visible when found; the hint line only until the first search runs
    /// (after that the result bar and empty grid speak for themselves).</summary>
    private void UpdateResultsHost()
    {
        var hasResults = _results.ItemCount > 0;
        _results.IsVisible = hasResults;
        _resultsEmpty.IsVisible = !hasResults && !_searchRan;
    }

    private void OpenSelected(ShellViewModel shell)
    {
        if (_results.SelectedItem is SearchRow row)
            shell.OpenResult(new QsppManager.Application.SearchResultRow(row.Kind, row.Title, row.Excerpt, row.Id, row.Meta));
    }

    private IssueRow? SelectedIssue => _issues.SelectedItem as IssueRow;

    private void ShowIssueSources()
    {
                if (SelectedIssue is not { } row) { _resultBar.Show(NoticeTone.Info, "Select an interpretation issue first."); return; }
        var issue = App.Workspace.Issues().First(i => i.Id == row.Id);
        _issueDetail.Children.Clear();
        _issueDetail.Children.Add(Ui.Label("SOURCES · VERBATIM, AS RETRIEVED"));
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
            _issueDetail.Children.Add(new TextBlock { Classes = { "small" }, Text = "(source list unreadable; see coverage ledger)" });
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
                if (SelectedIssue is not { } row) { _resultBar.Show(NoticeTone.Info, "Select an interpretation issue first."); return; }
        var res = App.Workspace.AcknowledgeIssue(row.Id, App.Session!);
                _resultBar.Show(res.Ok, "Acknowledged; recorded in the audit trail. The conservative planning reading still applies.", res.Error);
        Load();
    }

    private async Task ResolveAsync()
    {
                if (SelectedIssue is not { } row) { _resultBar.Show(NoticeTone.Info, "Select an interpretation issue first."); return; }
        var note = await Dialogs.Prompt(this, "Resolution record (required)",
            "Who settled this, and which source is taken as authoritative?");
        if (string.IsNullOrWhiteSpace(note)) return;
        var res = App.Workspace.ResolveIssue(row.Id, note, App.Session!);
                _resultBar.Show(res.Ok, "Resolved and recorded. Planning flags for this question can now follow the settled source.", res.Error);
        Load();
    }

    public record SourceDocRow(Guid Id, string DisplayName, string Role, string Version, string ShaShort, string Indexed, string Provenance, string StoredRelativePath);
    public record IssueRow(Guid Id, string Key, string Title, string Status, string Planning);
    public record SupersededRow(string OldTitle, string Replacement, string Reason, string AffectedReqs, string OpenTasks);
    public record SearchRow(string Kind, string Title, string Excerpt, string Meta, Guid Id, string RefCode = "");
    public record DiffRow(string Line);

    /// <summary>Kit two-line search-result row: name, kind + meta strip, excerpt.</summary>
    private static FuncDataTemplate<object> SearchRowTemplate() => new((_, _) =>
    {
        var sp = new StackPanel { Spacing = 2 };
        // Title leads; requirement rows repeat their code AFTER it, muted mono (item 15).
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var title = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.TextProperty, new Binding("Title"));
        labelRow.Children.Add(title);
        var refCode = new TextBlock { Classes = { "monoSm" }, Foreground = Ui.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center };
        refCode.Bind(TextBlock.TextProperty, new Binding("RefCode"));
        labelRow.Children.Add(refCode);

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var kind = new TextBlock { Classes = { "monoSm" }, Foreground = Ui.Br("Brush.Green") };
        kind.Bind(TextBlock.TextProperty, new Binding("Kind"));
        var meta = new TextBlock { Classes = { "small" } };
        meta.Bind(TextBlock.TextProperty, new Binding("Meta"));
        metaRow.Children.Add(kind);
        metaRow.Children.Add(meta);

        var excerpt = new TextBlock { Classes = { "small" }, TextTrimming = TextTrimming.CharacterEllipsis };
        excerpt.Bind(TextBlock.TextProperty, new Binding("Excerpt"));

        sp.Children.Add(labelRow);
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
    /// <summary>Table with an in-place empty state; the visible child tracks the row count
    /// (premium-polish 3.8 — Library source documents).</summary>
    private static TableHost Host(Control table, double minHeight, string icon, string title, string hint, string hue = "Brush.Stone")
    {
        var empty = Ui.EmptyState(icon, title, hint, null, hue);
        empty.IsVisible = false;
        var panel = new Panel { MinHeight = minHeight };
        panel.Children.Add(table);
        panel.Children.Add(empty);
        return new TableHost(table, empty, panel);
    }

    private sealed record TableHost(Control Table, Control Empty, Panel Panel)
    {
        public void Refresh(int rowCount)
        {
            Table.IsVisible = rowCount > 0;
            Empty.IsVisible = rowCount == 0;
        }
    }

}