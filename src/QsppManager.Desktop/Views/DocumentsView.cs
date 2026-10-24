using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Storage;

namespace QsppManager.Desktop.Views;

/// <summary>Evidence & documents: a list + detail browser for pharmacy evidence; the verified
/// source corpus lives in Library and is only linked from here (§5).</summary>
public partial class DocumentsView : UserControl
{
    private static readonly string[] _filterLabels = ["All", "Awaiting review", "Approved", "Expiring soon"];

    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly ShellViewModel _shell = null!;
    private readonly NoticeHost _resultBar = new();
    private TableHost _evidenceHost = null!;
    private readonly List<Button> _filterButtons = [];
    private StackPanel _detailPane = null!;
    private int _filter;

    public DocumentsView(ShellViewModel shell)
    {
        _shell = shell;
        Content = Build(shell);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(28, 24), Spacing = 14, MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.Documents, "Every document you hold, and what it proves.",
            "Attach pharmacy evidence (reviewed separately from readiness) and browse the imported source corpus. Files are copied into managed storage with checksums; deleting the original file never breaks a record."));

        // Page toolbar (§5): "Add evidence…" primary, "Process and Sort…" quiet; review,
        // open and supersede actions belong to the selected row's detail pane.
        var row = Ui.BtnRow(
            PrimaryBtn("Add evidence…", "Choose a file; it is imported with signature and checksum verification",
                async (_, _) => await AttachAsync()),
            Btn("Process and Sort…", "Turn a folder of documents into a sorted, reviewable evidence folio",
                async (_, _) => await OpenProcessSortAsync()));
        sp.Children.Add(row);
        sp.Children.Add(_resultBar);
        sp.Children.Add(BuildBrowser());
        sp.Children.Add(BuildSourceDisclosure());
        return new ScrollViewer { Content = sp };
    }

    /// <summary>The list + detail browser (§5): filter chips and the document grid on the
    /// left; the selected document's review state, links, versions and actions on the
    /// right. Column set is Document / Kind / Review only — file name, effective dates and
    /// the patient-data flag live in the detail, where they matter.</summary>
    private Control BuildBrowser()
    {
        _grid.Columns.Add(new DataGridTextColumn { Header = "Document", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Kind", Binding = new Avalonia.Data.Binding("KindDisplay"), Width = new DataGridLength(112) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Review", Binding = new Avalonia.Data.Binding("ReviewDisplay"), Width = new DataGridLength(118) });
        // Every row acts directly (owner brief: actions on every file): a quiet Open button
        // on each row, plus double-click and Enter. Review and supersede stay in the detail
        // pane, where they have room for their notes (§5).
        _grid.Columns.Add(OpenColumn());
        Ui.Ax(_grid, "Evidence records", "Select a row to review, open or supersede it; double-click opens the file");
        _grid.DoubleTapped += GridDoubleTapped;
        _grid.KeyDown += GridKeyDown;
        _evidenceHost = Host(_grid, "files", "No evidence records yet", "Attach the first evidence file; it is copied into managed storage with a checksum.", "Brush.Teal");

        var listDock = new DockPanel { Margin = new(10, 10, 10, 12), LastChildFill = true };
        var chips = BuildFilterChips();
        DockPanel.SetDock(chips, Dock.Top);
        listDock.Children.Add(chips);
        listDock.Children.Add(_evidenceHost.Panel);
        var listPane = new Border
        {
            Background = Ui.Br("Brush.Surface"),
            BorderBrush = Ui.Br("Brush.ControlBorder"),
            BorderThickness = new(1),
            CornerRadius = new(9),
            Child = listDock,
        };

        _detailPane = new StackPanel { Margin = new(20, 18, 20, 18), Spacing = 12 };
        var detailPane = new Border
        {
            Background = Ui.Br("Brush.Surface"),
            BorderBrush = Ui.Br("Brush.ControlBorder"),
            BorderThickness = new(1),
            CornerRadius = new(9),
            Child = new ScrollViewer { Content = _detailPane },
        };

        _grid.SelectionChanged += (_, _) => RefreshDetail();
        var splitter = new GridSplitter { Width = 8, Background = Brushes.Transparent };
        var browser = new Avalonia.Controls.Grid
        {
            MinHeight = 430,
            ColumnDefinitions = new ColumnDefinitions("600,Auto,*"),
        };
        // Splitter clamps: the list can widen but never swallows the detail pane, and the
        // detail pane can never be dragged away entirely — actions stay on screen. At the
        // 1120 content cap the detail pane keeps at least 352pt (760 + 8 + 352).
        browser.ColumnDefinitions[0].MinWidth = 420;
        browser.ColumnDefinitions[0].MaxWidth = 760;
        browser.ColumnDefinitions[2].MinWidth = 330;
        browser.Children.Add(listPane);
        browser.Children.Add(splitter);
        browser.Children.Add(detailPane);
        Avalonia.Controls.Grid.SetColumn(listPane, 0);
        Avalonia.Controls.Grid.SetColumn(splitter, 1);
        Avalonia.Controls.Grid.SetColumn(detailPane, 2);
        return browser;
    }

    /// <summary>Filter chips (§5): All · Awaiting review · Approved · Expiring soon, in the
    /// joined single-row segmented style the Requirements list uses.</summary>
    private Control BuildFilterChips()
    {
        var host = new Border
        {
            CornerRadius = new(6),
            BorderBrush = Ui.Br("Brush.ControlBorder"),
            BorderThickness = new(1),
            Background = Ui.Br("Brush.Surface"),
            Padding = new(2, 2),
            Margin = new(0, 0, 0, 8),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < _filterLabels.Length; i++)
        {
            if (i > 0)
                row.Children.Add(new Border { Width = 1, Margin = new(1, 5), VerticalAlignment = VerticalAlignment.Stretch, Background = Ui.Br("Brush.Divider") });
            var index = i;
            var b = new Button { Height = 24, FontSize = 12, FontWeight = FontWeight.Medium, Padding = new(8, 0), Content = _filterLabels[i] };
            b.Classes.Add("segcell");
            b.Click += (_, _) => SetFilter(index);
            _filterButtons.Add(b);
            row.Children.Add(b);
        }
        host.Child = row;
        return host;
    }

    private void SetFilter(int i)
    {
        _filter = i;
        for (var k = 0; k < _filterButtons.Count; k++)
        {
            if (k == _filter) _filterButtons[k].Classes.Add("on"); else _filterButtons[k].Classes.Remove("on");
        }
        ApplyFilter();
        RefreshDetail();
    }

    private Control BuildSourceDisclosure()
    {
        // The source corpus grid duplicated Library (owner brief): one quiet link instead.
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Ui.Small("The verified source corpus is read-only and lives in Library, with checksums and provenance."));
        body.Children.Add(Btn("Open Library", "Browse the verified source documents in Library", (_, _) => _shell.Navigate(ViewModels.ShellPage.Library), "verify"));
        return Ui.TextDisclosure("Source documents", body);
    }

    // ---------------- data ----------------
    private List<EvidenceRow> _allRows = [];

    private void Load()
    {
        var todayPlus90 = DateOnly.FromDateTime(DateTime.Today).AddDays(90);
        _allRows = App.Workspace.AllEvidence().Select(e => new EvidenceRow(
            TitleDisplay(e.Title),
            HumanKind(e.Kind),
            HumanReview(e.ReviewState),
            FileNameDisplay(e.FileName),
            e.Title,
            e.FileName,
            e.EffectiveFrom is null ? "" : e.EffectiveFrom.Value.ToString("d MMM yyyy") + (e.EffectiveTo is null ? "" : " until " + e.EffectiveTo.Value.ToString("d MMM yyyy")),
            e.ContainsPatientData ? "Patient data: flagged" : "",
            e.Sha256.Length > 12 ? e.Sha256[..12] + "…" : e.Sha256,
            e.Id, e.StoredRelativePath, e.ReviewState.ToString(),
            e.EffectiveTo is { } eff && eff <= todayPlus90,
            e.SupersededById)).ToList();
        ApplyFilter();
        // First paint: the pane shows its empty-state hint until a row is selected (Load
        // builds the list without a SelectionChanged, so refresh it once here).
        RefreshDetail();
    }

    private void ApplyFilter()
    {
        var rows = _filter switch
        {
            1 => _allRows.Where(r => r.ReviewState != nameof(EvidenceReviewState.ManagerVerified)).ToList(),
            2 => _allRows.Where(r => r.ReviewState == nameof(EvidenceReviewState.ManagerVerified)).ToList(),
            3 => _allRows.Where(r => r.ExpiringSoon).ToList(),
            _ => _allRows.ToList(),
        };
        _grid.ItemsSource = rows;
        if (_grid.SelectedItem is EvidenceRow sel && !rows.Contains(sel)) _grid.SelectedItem = null;
        _evidenceHost.Refresh(rows.Count);
    }

    private void RefreshDetail()
    {
        _detailPane.Children.Clear();
        if (_grid.SelectedItem is not EvidenceRow row)
        {
            _detailPane.Children.Add(Ui.EmptyState("file-text", "No document selected", "Select a document on the left to review and action it."));
            return;
        }
        try
        {
            _detailPane.Children.Add(BuildDocumentDetail(row));
        }
        catch (Exception ex)
        {
            // The pane must never go blank on a data hiccup: identity and the open action stay.
            var fallback = new StackPanel { Spacing = 10 };
            fallback.Children.Add(new TextBlock { Text = row.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            fallback.Children.Add(Ui.Row(6, Ui.Chip(row.KindDisplay), Ui.Chip(row.ReviewDisplay, ReviewTone(row.ReviewState))));
            fallback.Children.Add(Ui.Callout($"The detail pane could not be shown: {ex.Message}"));
            fallback.Children.Add(Btn("Open file", "Open the managed evidence file in the system viewer", (_, _) => OpenSelected()));
            _detailPane.Children.Add(fallback);
        }
    }

    private Control BuildDocumentDetail(EvidenceRow row)
    {
        var sp = new StackPanel { Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = row.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var chipRow = Ui.Row(6, Ui.Chip(row.KindDisplay), Ui.Chip(row.ReviewDisplay, ReviewTone(row.ReviewState)));
        if (row.PatientLabel.Length > 0) chipRow.Children.Add(Ui.Chip(row.PatientLabel, "warn"));
        sp.Children.Add(chipRow);
        sp.Children.Add(Ui.Label("Recorded file"));
        sp.Children.Add(new TextBlock { Text = row.FileNameRaw, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Br("Brush.Muted") });
        sp.Children.Add(Ui.Footnote($"Checksum {row.ShaShort}"));
        if (row.Effective.Length > 0)
            sp.Children.Add(Ui.Row(6, Ui.Label("Effective"), Ui.Small(row.Effective)));

        var note = App.Db.EvidenceItems.AsNoTracking().FirstOrDefault(e => e.Id == row.Id)?.ReviewNote ?? "";
        if (note.Length > 0) sp.Children.Add(Ui.Callout(note));

        sp.Children.Add(Ui.Label("Linked requirements"));
        var linkRows = LinkedRequirementRows(row.Id);
        if (linkRows.Count == 0)
            sp.Children.Add(Ui.Small("No requirements reference this document yet."));
        else
        {
            var linksHost = new StackPanel { Spacing = 2 };
            linksHost.Children.AddRange(linkRows);
            sp.Children.Add(linksHost);
        }

        var versionRows = VersionHistoryRows(row);
        if (versionRows.Count > 0)
        {
            sp.Children.Add(Ui.Label("Version history"));
            var versionsHost = new StackPanel { Spacing = 2 };
            versionsHost.Children.AddRange(versionRows);
            sp.Children.Add(versionsHost);
        }

        sp.Children.Add(Ui.BtnRow(
            Btn("Submit for review", "Move this evidence to ready for review", (_, _) => ReviewSelected(EvidenceReviewState.ReadyForReview)),
            Btn("Manager verifies", "Manager-verifies this evidence", (_, _) => ReviewSelected(EvidenceReviewState.ManagerVerified), "verify"),
            Btn("Needs rework", "Send this evidence back for rework", (_, _) => ReviewSelected(EvidenceReviewState.NeedsRework), "caution")));
        sp.Children.Add(Ui.BtnRow(
            Btn("Open file", "Open the managed evidence file in the system viewer", (_, _) => OpenSelected()),
            Btn("Supersede with newer version", "Replace the selected approved evidence with a newer version", async (_, _) => await SupersedeAsync())));
        return sp;
    }

    private static string ReviewTone(string state) => state switch
    {
        nameof(EvidenceReviewState.ManagerVerified) => "ok",
        nameof(EvidenceReviewState.NeedsRework) => "warn",
        nameof(EvidenceReviewState.ReadyForReview) => "info",
        _ => "neutral",
    };

    private List<Control> LinkedRequirementRows(Guid id)
    {
        var titles = App.Workspace.RequirementRows(App.Session).ToDictionary(r => r.Id, r => (r.DisplayTitleText, r.Ref));
        var links = App.Db.EvidenceLinks.AsNoTracking().Where(l => l.EvidenceItemId == id).ToList();
        var outRows = new List<Control>();
        foreach (var l in links)
        {
            if (!titles.TryGetValue(l.RequirementId, out var t)) continue;
            var lp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            lp.Children.Add(new TextBlock { Text = t.Item1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            lp.Children.Add(new TextBlock { Text = t.Item2, Classes = { "monoSm" }, Foreground = Ui.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center });
            outRows.Add(lp);
        }
        return outRows;
    }

    private List<Control> VersionHistoryRows(EvidenceRow row)
    {
        var rows = new List<Control>();
        var all = App.Workspace.AllEvidence().ToDictionary(e => e.Id);
        if (row.SupersededById is { } newer && all.TryGetValue(newer, out var successor))
            rows.Add(Ui.Small($"Superseded by {successor.Title}."));
        foreach (var oldItem in all.Values.Where(e => e.SupersededById == row.Id))
            rows.Add(Ui.Small($"Replaced \"{oldItem.Title}\"."));
        return rows;
    }

    // ---------------- actions (unchanged behaviour) ----------------
    private async Task AttachAsync()
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl == null) return;
        var files = await tl.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { AllowMultiple = false, Title = "Attach an evidence document" });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) { _resultBar.Show(NoticeTone.Info, "The chosen file has no local path."); return; }
        var dlg = new EvidenceDialog(this);
        await dlg.ShowDialog(App.Main!);
        if (!dlg.Confirmed) return;
        var (evidResult, _) = App.Workspace.ImportEvidence(path, dlg.TitleText, dlg.Kind, dlg.EffectiveFrom, dlg.EffectiveTo, dlg.HasPatientData, dlg.PatientDataAttestation, App.Session!);
        _resultBar.Show(evidResult.Ok, "Attached. Signature and checksum verified; review starts at Draft.", evidResult.Error);
        Load();
    }

    private async Task OpenProcessSortAsync()
    {
        var dlg = new ProcessSortDialog(_shell);
        await dlg.ShowDialog(App.Main!);
        Load();
    }

    private void ReviewSelected(EvidenceReviewState target)
    {
        if (_grid.SelectedItem is not EvidenceRow row) { _resultBar.Show(NoticeTone.Info, "Select an evidence row first."); return; }
        _ = ReviewSelectedAsync(target, row);
    }

    private async Task ReviewSelectedAsync(EvidenceReviewState target, EvidenceRow rowArg)
    {
        string note = "";
        if (target == EvidenceReviewState.ManagerVerified || target == EvidenceReviewState.NeedsRework)
            note = await Dialogs.Prompt(this, target == EvidenceReviewState.ManagerVerified ? "Verification note (optional)" : "Rework reason (required)", "Note") ?? "";
        var res = App.Workspace.SetEvidenceReview(rowArg.Id, target, App.Session!, note);
        _resultBar.Show(res.Ok, "Review state updated.", res.Error);
        KeepSelection(rowArg.Id);
    }

    private void KeepSelection(Guid id)
    {
        Load();
        if (_grid.ItemsSource is List<EvidenceRow> rows && rows.FirstOrDefault(r => r.Id == id) is { } hit)
            _grid.SelectedItem = hit;
    }

    private void OpenSelected()
    {
        if (_grid.SelectedItem is not EvidenceRow row) { _resultBar.Show(NoticeTone.Info, "Select an evidence row first."); return; }
        OpenRow(row);
    }

    /// <summary>Open one row's managed file; selecting it first keeps the detail pane in
    /// step. A missing stored file is reported, never swallowed silently.</summary>
    private void OpenRow(EvidenceRow row)
    {
        if (_grid.SelectedItem is not EvidenceRow current || current.Id != row.Id) _grid.SelectedItem = row;
        var abs = App.Store.AbsolutePath(row.StoredRelativePath);
        if (Helpers.OpenWithSystemViewer(abs)) return;
        if (System.IO.File.Exists(abs)) return; // the system-open toast already surfaced the failure
        _resultBar.Show(NoticeTone.Error, "The stored file is missing on disk.", abs);
    }

    private async Task SupersedeAsync()
    {
        if (_grid.SelectedItem is not EvidenceRow row) { _resultBar.Show(NoticeTone.Info, "Select the superseded (current approved) evidence row first."); return; }
        if (row.ReviewState != nameof(EvidenceReviewState.ManagerVerified))
        { _resultBar.Show(NoticeTone.Info, "Only ManagerVerified evidence uses the supersession flow. Drafts can simply be deleted or reworked."); return; }
        var picker = new EvidencePickerDialog(this);
        await picker.ShowDialog(App.Main!);
        var replacement = picker.SelectedItem;
        if (replacement == null) return;
        var reason = await Dialogs.Prompt(this, "Supersession reason (required)", "Why is the old version being replaced?");
        if (string.IsNullOrWhiteSpace(reason)) return;
        var res = App.Workspace.SupersedeEvidence(row.Id, replacement.Id, reason, App.Session!);
        _resultBar.Show(res.Ok, "Superseded: affected requirement links are flagged and a re-link task was created for each.", res.Error);
        KeepSelection(row.Id);
    }

    // ---------------- display formatting ----------------
    // ---- 3E #2 display formatting (labels only; the data itself never changes) ----
    private static string CamelToWords(string raw)
    {
        if (raw.Length == 0) return "";
        return System.Text.RegularExpressions.Regex.Replace(raw, @"(\B[A-Z])", " $1");
    }

    /// <summary>Raw enum names become the words the program uses ("PolicyOrSop" → "Policy or SOP").</summary>
    internal static string HumanKind(object kind) => kind?.ToString() switch
    {
        "PolicyOrSop" => "Policy or SOP",
        var raw => CamelToWords(raw ?? ""),
    };

    private static string HumanReview(object state) => state?.ToString() switch
    {
        "NotApplicable" => "Not applicable",
        "ReadyForReview" => "Ready for review",
        "ManagerVerified" => "Manager verified",
        "NeedsRework" => "Needs rework",
        var raw => CamelToWords(raw ?? ""),
    };

    /// <summary>"Position-Description-PA-Lvl-3-Dispensary-A" → "Position description: PA level 3,
    /// dispensary A". Known prefixes keep a composed form; a short uppercase suffix rides on a
    /// space. Unknown shapes pass through with separators spaced out. The real file name stays
    /// in the detail pane.</summary>
    internal static string FileNameDisplay(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var known = new System.Collections.Generic.Dictionary<string, string>
        {
            ["Position-Description-PA-Lvl-3-Dispensary"] = "Position description: PA level 3, dispensary",
            ["Position-Description-PA-Lvl-3"] = "Position description: PA level 3",
        };
        foreach (var (key, nice) in known)
        {
            if (string.Equals(stem, key, StringComparison.OrdinalIgnoreCase)) return nice;
            if (stem.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase))
            {
                var remainder = stem[(key.Length + 1)..].Replace('-', ' ').Replace('_', ' ');
                var joiner = remainder.Length <= 3 && remainder.ToUpperInvariant() == remainder ? " " : ", ";
                return nice + joiner + remainder;
            }
        }
        return stem.Replace('-', ' ').Replace('_', ' ').Replace("Lvl", "level");
    }

    /// <summary>The list title prefers a display form; plain human titles pass through unchanged.</summary>
    internal static string TitleDisplay(string title) =>
        string.IsNullOrWhiteSpace(title) || title.IndexOfAny(['-', '_']) < 0 ? title : FileNameDisplay(title);

    public record EvidenceRow(string Title, string KindDisplay, string ReviewDisplay, string FileNameDisplay,
        string TitleRaw, string FileNameRaw, string Effective, string PatientLabel, string ShaShort,
        Guid Id, string StoredRelativePath, string ReviewState, bool ExpiringSoon, Guid? SupersededById);

    public void OpenEvidence(Guid id)
    {
        var rows = (List<EvidenceRow>)_grid.ItemsSource!;
        var hit = rows.FirstOrDefault(r => r.Id == id);
        if (hit == null) { Load(); hit = ((List<EvidenceRow>)_grid.ItemsSource!).FirstOrDefault(r => r.Id == id); }
        _grid.SelectedItem = hit;
    }

    // ---------------- design-system helpers (local, view-level) ----------------
    private static Button Btn(string label, string axHelp, EventHandler<RoutedEventArgs> onClick, string classes = "")
    {
        var b = new Button { Content = label };
        foreach (var cls in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(cls);
        Ui.Ax(b, label, axHelp);
        b.Click += onClick;
        return b;
    }
    private static Button PrimaryBtn(string label, string axHelp, EventHandler<RoutedEventArgs> onClick) => Btn(label, axHelp, onClick, "primary");

    /// <summary>The quiet per-row Open button (textlink green): a visible action on every
    /// row, available no matter which pane state it is in.</summary>
    private DataGridTemplateColumn OpenColumn() =>
        new()
        {
            Header = "Open",
            Width = new DataGridLength(86),
            CellTemplate = new FuncDataTemplate<EvidenceRow>((row, _) =>
            {
                var b = new Button
                {
                    Content = "Open",
                    Classes = { "textlink" },
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new(0, 0, 12, 0),
                };
                b.Click += (_, _) => OpenRow(row);
                Ui.Ax(b, "Open file", "Open the managed evidence file in the system viewer");
                return b;
            }, true),
        };

    /// <summary>The evidence row under a pointer event, via its containing DataGridRow.</summary>
    private EvidenceRow? RowAt(object? source) =>
        (source as Control)?.GetSelfAndVisualAncestors()
            .OfType<Avalonia.Controls.DataGridRow>()
            .FirstOrDefault()?.DataContext as EvidenceRow;

    private void GridDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        // A double-tap on the Open button belongs to the button's own handler.
        if ((e.Source as Control)?.GetSelfAndVisualAncestors().OfType<Button>().Any() == true) return;
        if (RowAt(e.Source) is not { } row) return;
        OpenRow(row);
    }

    private void GridKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter || _grid.SelectedItem is not EvidenceRow row) return;
        OpenRow(row);
    }

    /// <summary>Table with an in-place empty state; the visible child tracks the row count.</summary>
    private static TableHost Host(Control table, string icon, string title, string hint, string hue = "Brush.Stone")
    {
        var empty = Ui.EmptyState(icon, title, hint, null, hue);
        empty.IsVisible = false;
        var panel = new Panel { MinHeight = 380 };
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