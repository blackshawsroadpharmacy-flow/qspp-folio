using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

/// <summary>Library: the reading room. Corpus search over requirements/evidence/documents
/// (FTS5), the verified source documents with provenance, the interpretation issues with
/// their competing quotes, and the supersession impact review.</summary>
public partial class LibraryView : UserControl
{
    private readonly DataGrid _sources = new() { Height = 220, AutoGenerateColumns = false, IsReadOnly = true };
    private readonly DataGrid _issues = new() { Height = 140, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _superseded = new() { Height = 150, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly TextBox _searchBox = new() { Watermark = "Search the whole corpus — try potency, mid-cycle, attestation…", Height = 36 };
    private readonly ListBox _results = new() { Height = 220, BorderThickness = new(0, 0, 0, 1) };
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _issueDetail = new() { Spacing = 6 };

    public LibraryView(ShellViewModel shell)
    {
        Content = Build(shell);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 12, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Library" });
        sp.Children.Add(new TextBlock { Classes = { "lead" }, Text = "Search every imported requirement, evidence record and source document, read the governing documents with their checksums, and track the interpretation questions that are deliberately kept open until a source settles them." });

        // ---- corpus search ----
        sp.Children.Add(new TextBlock { Text = "Corpus search", Classes = { "h3" } });
        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _searchBox.Width = 640;
        _searchBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) RunSearch(); };
        searchRow.Children.Add(_searchBox);
        searchRow.Children.Add(new Button { Content = "Search", Classes = { "primary" }, Height = 36, MinWidth = 110 }.With(b => b.Click += (_, _) => RunSearch()));
        sp.Children.Add(searchRow);
        _results.BorderBrush = Helpers.Brush("Brush.Divider");
        _results.DoubleTapped += (_, _) => OpenSelected(shell);
        sp.Children.Add(_results);
        sp.Children.Add(new TextBlock { Classes = { "small" }, Text = "Enter in the box or double-click a result opens the record. Bracketed highlights mark the matched text." });

        // ---- source documents ----
        sp.Children.Add(new TextBlock { Text = "Source documents (verified corpus)", Classes = { "h3" }, Margin = new(0, 10, 0, 0) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Document", Binding = new Avalonia.Data.Binding("DisplayName"), Width = new DataGridLength(430) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Avalonia.Data.Binding("Role"), Width = new DataGridLength(90) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Version", Binding = new Avalonia.Data.Binding("Version"), Width = new DataGridLength(170) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "sha256", Binding = new Avalonia.Data.Binding("ShaShort"), Width = new DataGridLength(110) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Provenance", Binding = new Avalonia.Data.Binding("Provenance"), Width = new DataGridLength(280) });
        sp.Children.Add(_sources);
        var docRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        docRow.Children.Add(new Button { Content = "Open source PDF via system viewer", Height = 34 }.With(b => b.Click += (_, _) =>
        {
            if (_sources.SelectedItem is SourceDocRow s)
                Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(s.StoredRelativePath));
            else _message.Text = "Select a source document first.";
        }));
        sp.Children.Add(docRow);

        // ---- interpretation issues ----
        sp.Children.Add(new TextBlock { Text = "Interpretation issues deliberately kept open", Classes = { "h3" }, Margin = new(0, 10, 0, 0) });
        sp.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Where the program sources disagree (or wording needs confirmation), the app refuses to pick a side silently. The conservative reading is used for planning dates and is labelled PLANNING ONLY everywhere it appears. Acknowledging records that the pharmacy has seen it; resolving requires a note recording who settled it and which source is authoritative." });
        _issues.Columns.Add(new DataGridTextColumn { Header = "Key", Binding = new Avalonia.Data.Binding("Key"), Width = new DataGridLength(170) });
        _issues.Columns.Add(new DataGridTextColumn { Header = "Question", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(470) });
        _issues.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Avalonia.Data.Binding("Status"), Width = new DataGridLength(150) });
        _issues.Columns.Add(new DataGridTextColumn { Header = "Conservative planning", Binding = new Avalonia.Data.Binding("Planning"), Width = new DataGridLength(280) });
        sp.Children.Add(_issues);
        sp.Children.Add(_issueDetail);
        var issueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        issueRow.Children.Add(new Button { Content = "Show competing sources", Height = 34 }.With(b => b.Click += (_, _) => ShowIssueSources()));
        issueRow.Children.Add(new Button { Content = "Acknowledge", Height = 34 }.With(b => b.Click += (_, _) => Acknowledge()));
        issueRow.Children.Add(new Button { Content = "Resolve…", Height = 34 }.With(b => b.Click += async (_, _) => await ResolveAsync()));
        sp.Children.Add(issueRow);

        // ---- supersession review ----
        sp.Children.Add(new TextBlock { Text = "Supersession review", Classes = { "h3" }, Margin = new(0, 10, 0, 0) });
        sp.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Retired evidence versions with the requirement links that were affected. Each affected link already has a re-link task assigned to the requirement owner — this table is the impact overview." });
        _superseded.Columns.Add(new DataGridTextColumn { Header = "Retired evidence", Binding = new Avalonia.Data.Binding("OldTitle"), Width = new DataGridLength(330) });
        _superseded.Columns.Add(new DataGridTextColumn { Header = "Replacement", Binding = new Avalonia.Data.Binding("Replacement"), Width = new DataGridLength(280) });
        _superseded.Columns.Add(new DataGridTextColumn { Header = "Reason", Binding = new Avalonia.Data.Binding("Reason"), Width = new DataGridLength(220) });
        _superseded.Columns.Add(new DataGridTextColumn { Header = "Links affected", Binding = new Avalonia.Data.Binding("AffectedReqs"), Width = new DataGridLength(110) });
        _superseded.Columns.Add(new DataGridTextColumn { Header = "Open re-link tasks", Binding = new Avalonia.Data.Binding("OpenTasks"), Width = new DataGridLength(130) });
        sp.Children.Add(_superseded);

        sp.Children.Add(_message);
        return new ScrollViewer { Content = sp };
    }

    private void Load()
    {
        _sources.ItemsSource = App.Workspace.AllSourceDocuments()
            .Select(d => new SourceDocRow(d.DisplayName, d.Role, d.SourceVersion, d.Sha256Hex[..12] + "…", d.ProvenanceNote, d.StoredRelativePath)).ToList();
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
        _results.ItemsSource = App.Search.Query(_searchBox.Text, 60)
            .Select(r => new SearchRow(r.Kind, r.Title, r.Excerpt, r.Meta, r.Id)).ToList();
        if (_results.ItemCount == 0) _message.Text = "Nothing matched. The LIKE fallback also ran — try a shorter term.";
    }

    private void OpenSelected(ShellViewModel shell)
    {
        if (_results.SelectedItem is SearchRow row)
            shell.OpenResult(new QsppManager.Application.SearchResultRow(row.Kind, row.Title, row.Excerpt, row.Id, row.Meta));
    }

    private IssueRow? SelectedIssue => _issues.SelectedItem as IssueRow;

    private void ShowIssueSources()
    {
        if (SelectedIssue is not { } row) { _message.Text = "Select an interpretation issue first."; return; }
        var issue = App.Workspace.Issues().First(i => i.Id == row.Id);
        _issueDetail.Children.Clear();
        _issueDetail.Children.Add(new TextBlock { Classes = { "label" }, Text = "SOURCES — VERBATIM, AS RETRIEVED" });
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
                _issueDetail.Children.Add(new Border
                {
                    BorderBrush = Helpers.Brush("Brush.Divider"), BorderThickness = new(1),
                    Padding = new(10, 6), Background = Helpers.Brush("Brush.White"),
                    Child = new StackPanel { Spacing = 2, Children =
                    {
                        new TextBlock { FontWeight = FontWeight.Medium, Text = reference ?? "", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { FontFamily = new FontFamily("JetBrains Mono, Consolas"), Classes = { "small" }, Text = text ?? "", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Classes = { "small" }, Text = $"{file}{(string.IsNullOrEmpty(lines) ? "" : " · approx. lines " + lines)}", TextWrapping = TextWrapping.Wrap },
                    } },
                });
            }
        }
        catch (JsonException)
        {
            _issueDetail.Children.Add(new TextBlock { Classes = { "small" }, Text = "(source list unreadable — see coverage ledger)" });
        }
        var meta = new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = $"Retrieved {issue.RetrievedDate} · pack {issue.PackVersion}" };
        _issueDetail.Children.Add(meta);
        if (issue.Status == "resolved")
            _issueDetail.Children.Add(new TextBlock { Classes = { "lead" }, TextWrapping = TextWrapping.Wrap, Text = $"RESOLVED: {issue.ResolutionNote}" });
    }

    private void Acknowledge()
    {
        if (SelectedIssue is not { } row) { _message.Text = "Select an interpretation issue first."; return; }
        var res = App.Workspace.AcknowledgeIssue(row.Id, App.Session!);
        _message.Text = res.Ok ? "Acknowledged — recorded in the audit trail. The conservative planning reading still applies." : res.Error;
        Load();
    }

    private async Task ResolveAsync()
    {
        if (SelectedIssue is not { } row) { _message.Text = "Select an interpretation issue first."; return; }
        var note = await Dialogs.Prompt(this, "Resolution record (required)",
            "Who settled this, and which source is taken as authoritative?");
        if (string.IsNullOrWhiteSpace(note)) return;
        var res = App.Workspace.ResolveIssue(row.Id, note, App.Session!);
        _message.Text = res.Ok ? "Resolved and recorded. Planning flags for this question can now follow the settled source." : res.Error;
        Load();
    }

    public record SourceDocRow(string DisplayName, string Role, string Version, string ShaShort, string Provenance, string StoredRelativePath);
    public record IssueRow(Guid Id, string Key, string Title, string Status, string Planning);
    public record SupersededRow(string OldTitle, string Replacement, string Reason, string AffectedReqs, string OpenTasks);
    public record SearchRow(string Kind, string Title, string Excerpt, string Meta, Guid Id);

    public void OpenLibrarySearch(string text)
    {
        _searchBox.Text = text;
        RunSearch();
    }
}