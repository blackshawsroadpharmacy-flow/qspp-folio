using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Storage;

namespace QsppManager.Desktop.Views;

/// <summary>Evidence & documents: pharmacy evidence intake + the verified source corpus.
/// Deliberately plain tables; documents keep original versions with checksums.</summary>
public partial class DocumentsView : UserControl
{
    private readonly DataGrid _grid = new() { Height = 260, AutoGenerateColumns = false, IsReadOnly = true };
    private readonly DataGrid _sources = new() { Height = 240, AutoGenerateColumns = false, IsReadOnly = true };
    private readonly ShellViewModel _shell = null!;
    private readonly NoticeHost _resultBar = new();
    private TableHost _evidenceHost = null!;
    private TableHost _sourcesHost = null!;

    public DocumentsView(ShellViewModel shell)
    {
        _shell = shell;
        Content = Build(shell);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(28, 24), Spacing = 14, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.Documents, "Pharmacy evidence and the imported source corpus, checksummed on import.", "Attach pharmacy evidence (reviewed separately from readiness) and browse the imported source corpus. Files are copied into managed storage with checksums — deleting the original file never breaks a record."));

        _grid.Columns.Add(new DataGridTextColumn { Header = "Evidence", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Kind", Binding = new Avalonia.Data.Binding("KindDisplay"), Width = new DataGridLength(120) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Review", Binding = new Avalonia.Data.Binding("ReviewDisplay"), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Avalonia.Data.Binding("FileDisplay"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Effective", Binding = new Avalonia.Data.Binding("Effective"), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Patient data", Binding = new Avalonia.Data.Binding("PatientLabel"), Width = new DataGridLength(90) });
        Ui.Ax(_grid, "Evidence records", "Select a row to review, open or supersede it");
        _evidenceHost = Host(_grid, 260, "files", "No evidence records yet", "Attach the first evidence file — it is copied into managed storage with a checksum.", "Brush.Teal");
        sp.Children.Add(_evidenceHost.Panel);

        // Seven actions on ONE row clipped the window edge in the audit; two filled-green
        // primaries also diluted which action creates here. Colour language:
        // ONE primary (attach), the verify claim green-outlined, rework amber-outlined,
        // the rest quiet outlined defaults — and the row WRAPS at any window width.
        var row = Ui.BtnRow(
            PrimaryBtn("Attach evidence file", "Choose a file — it is imported with signature and checksum verification",
                async (_, _) => await AttachAsync()),
            Btn("Process and Sort…", "Turn a folder of documents into a sorted, reviewable evidence folio",
                async (_, _) => await OpenProcessSortAsync()),
            Btn("Submit selected for review", "Move the selected evidence to ready for review", (_, _) => ReviewSelected(EvidenceReviewState.ReadyForReview)),
            Btn("Manager verifies", "Manager-verifies the selected evidence", (_, _) => ReviewSelected(EvidenceReviewState.ManagerVerified), "verify"),
            Btn("Needs rework", "Send the selected evidence back for rework", (_, _) => ReviewSelected(EvidenceReviewState.NeedsRework), "caution"),
            Btn("Open managed file", "Open the selected managed evidence file in the system viewer", (_, _) => OpenSelected()),
            Btn("Supersede with newer version", "Replace the selected approved evidence with a newer version", async (_, _) => await SupersedeAsync()));
        sp.Children.Add(row);
        sp.Children.Add(_resultBar);

        sp.Children.Add(new TextBlock { Text = "Source documents (verified corpus, read-only)", Classes = { "h3" }, Margin = new(0, 8, 0, 0) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Document", Binding = new Avalonia.Data.Binding("DisplayName"), Width = new DataGridLength(4, DataGridLengthUnitType.Star) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Avalonia.Data.Binding("RoleDisplay"), Width = new DataGridLength(110) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Version", Binding = new Avalonia.Data.Binding("SourceVersion"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Provenance", Binding = new Avalonia.Data.Binding("ProvenanceNote"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        Ui.Ax(_sources, "Source documents", "The verified, read-only source corpus with checksums and provenance");
        _sourcesHost = Host(_sources, 240, "file-text", "No source documents imported", "The verified corpus appears here once documents are imported during setup.", "Brush.Teal");
        sp.Children.Add(_sourcesHost.Panel);
        var row2 = Ui.Row(8, Btn("Open source PDF via system viewer", "Open the selected source document PDF", (_, _) =>
        {
            if (_sources.SelectedItem is SourceDocRow s)
                Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(s.StoredRelativePath));
                        else _resultBar.Show(NoticeTone.Info, "Select a source document row first.");
        }));
        sp.Children.Add(row2);
        return new ScrollViewer { Content = sp };
    }

    private void Load()
    {
        var evidence = App.Workspace.AllEvidence().Select(e => new EvidenceRow(
            e.Title, HumanKind(e.Kind), HumanReview(e.ReviewState), FileNameDisplay(e.FileName),
            e.EffectiveFrom is null ? "" : e.EffectiveFrom.Value.ToString("dd MMM yyyy") + (e.EffectiveTo is null ? "" : " → " + e.EffectiveTo.Value.ToString("dd MMM yyyy")),
            e.ContainsPatientData ? "flagged" : "", e.Id, e.StoredRelativePath, e.ReviewState.ToString())).ToList();
        _grid.ItemsSource = evidence;
        _evidenceHost.Refresh(evidence.Count);

        var sources = App.Workspace.AllSourceDocuments()
            .Select(d => new SourceDocRow(FileNameDisplay(d.DisplayName), HumanRole(d.Role), d.SourceVersion, d.Sha256Hex[..12] + "…", LoneDash(d.ProvenanceNote), d.StoredRelativePath)).ToList();
        _sources.ItemsSource = sources;
        _sourcesHost.Refresh(sources.Count);
    }

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

    private async void ReviewSelected(EvidenceReviewState target)
    {
                if (_grid.SelectedItem is not EvidenceRow row) { _resultBar.Show(NoticeTone.Info, "Select an evidence row first."); return; }
        string note = "";
        if (target == EvidenceReviewState.ManagerVerified || target == EvidenceReviewState.NeedsRework)
            note = await Dialogs.Prompt(this, target == EvidenceReviewState.ManagerVerified ? "Verification note (optional)" : "Rework reason (required)", "Note") ?? "";
        var res = App.Workspace.SetEvidenceReview(row.Id, target, App.Session!, note);
                _resultBar.Show(res.Ok, "Review state updated.", res.Error);
        Load();
    }

    private void OpenSelected()
    {
                if (_grid.SelectedItem is not EvidenceRow row) { _resultBar.Show(NoticeTone.Info, "Select an evidence row first."); return; }
        Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(row.StoredRelativePath));
    }

    private async Task SupersedeAsync()
    {
        if (_grid.SelectedItem is not EvidenceRow old || !old.Title.StartsWith("")) { }
                if (_grid.SelectedItem is not EvidenceRow oldRow) { _resultBar.Show(NoticeTone.Info, "Select the superseded (current approved) evidence row first."); return; }
        if (oldRow.ReviewState != nameof(EvidenceReviewState.ManagerVerified))
                { _resultBar.Show(NoticeTone.Info, "Only ManagerVerified evidence uses the supersession flow. Drafts can simply be deleted or reworked."); return; }
        var picker = new EvidencePickerDialog(this);
        await picker.ShowDialog(App.Main!);
        var replacement = picker.SelectedItem;
        if (replacement == null) return;
        var reason = await Dialogs.Prompt(this, "Supersession reason (required)", "Why is the old version being replaced?");
        if (string.IsNullOrWhiteSpace(reason)) return;
        var res = App.Workspace.SupersedeEvidence(oldRow.Id, replacement.Id, reason, App.Session!);
                _resultBar.Show(res.Ok, "Superseded: affected requirement links are flagged and a re-link task was created for each.", res.Error);
        Load();
    }

    // Display fields (KindDisplay/ReviewDisplay/FileDisplay) carry the human forms (3E #2);
    // the raw ReviewState stays for logic (supersede guard).
    // ---- 3E #2 display formatting (labels only; the data itself never changes) ----
    /// <summary>Raw enum names become the words the program uses ("PolicyOrSop" → "Policy or SOP").</summary>
    internal static string HumanKind(object kind) => CamelToWords(kind?.ToString() ?? "");

    private static string HumanReview(object state) => state?.ToString() switch
    {
        "NotApplicable" => "Not applicable",
        "ReadyForReview" => "Ready for review",
        "ManagerVerified" => "Manager verified",
        "NeedsRework" => "Needs rework",
        var raw => CamelToWords(raw ?? ""),
    };

    private static string HumanRole(string role) => CamelToWords(role);

    /// <summary>"Position-Description-PA-Lvl-3-Dispensary" → "Position description: PA level 3, dispensary".
    /// Unknown shapes pass through unchanged. The real file name stays in the detail.</summary>
    internal static string FileNameDisplay(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var known = new System.Collections.Generic.Dictionary<string, string>
        {
            ["Position-Description-PA-Lvl-3-Dispensary"] = "Position description: PA level 3, dispensary",
            ["Position-Description-PA-Lvl-3"] = "Position description: PA level 3",
        };
        if (known.TryGetValue(name, out var nice)) return nice;
        return name.Replace('-', ' ').Replace('_', ' ') switch { var t => t };
    }

    /// <summary>A lone "—" in a cell becomes empty (the "Not set" wording rides the label).</summary>
    internal static string LoneDash(string? text) =>
        text is null || text.Trim() is "-" or "—" or "–" ? "" : text;

    private static string CamelToWords(string raw)
    {
        if (raw.Length == 0) return "";
        return System.Text.RegularExpressions.Regex.Replace(raw, @"(\B[A-Z])", " $1");
    }

    public record EvidenceRow(string Title, string KindDisplay, string ReviewDisplay, string FileDisplay, string Effective, string PatientLabel, Guid Id, string StoredRelativePath, string ReviewState);
    public record SourceDocRow(string DisplayName, string RoleDisplay, string SourceVersion, string ShaShort, string ProvenanceNote, string StoredRelativePath);

    public void OpenEvidence(Guid id)
    {
        var rows = (List<DocumentsView.EvidenceRow>)_grid.ItemsSource!;
        var hit = rows.FirstOrDefault(r => r.Id == id);
        if (hit == null) { Load(); hit = ((List<DocumentsView.EvidenceRow>)_grid.ItemsSource!).FirstOrDefault(r => r.Id == id); }
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

    /// <summary>Table with an in-place empty state; the visible child tracks the row count.</summary>
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