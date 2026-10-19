using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Avalonia.Layout;
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
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };

    public DocumentsView(ShellViewModel shell)
    {
        Content = Build(shell);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 14, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Evidence & documents" });
        sp.Children.Add(new TextBlock { Classes = { "lead" }, Text = "Attach pharmacy evidence (reviewed separately from readiness) and browse the imported source corpus. Files are copied into managed storage with checksums — deleting the original file never breaks a record." });

        _grid.Columns.Add(new DataGridTextColumn { Header = "Evidence", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(300) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Kind", Binding = new Avalonia.Data.Binding("Kind"), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Review", Binding = new Avalonia.Data.Binding("ReviewState"), Width = new DataGridLength(130) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Avalonia.Data.Binding("FileName"), Width = new DataGridLength(180) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "sha256", Binding = new Avalonia.Data.Binding("ShaShort"), Width = new DataGridLength(120) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Effective", Binding = new Avalonia.Data.Binding("Effective"), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Patient data", Binding = new Avalonia.Data.Binding("PatientLabel"), Width = new DataGridLength(90) });
        sp.Children.Add(_grid);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Button { Content = "Attach evidence file", Classes = { "primary" }, Height = 34 }.With(_ => _.Click += async (_, _) => await AttachAsync()));
        row.Children.Add(new Button { Content = "Submit selected for review", Height = 34 }.With(_ => _.Click += (_, _) => ReviewSelected(EvidenceReviewState.ReadyForReview)));
        row.Children.Add(new Button { Content = "Manager verifies", Height = 34 }.With(_ => _.Click += (_, _) => ReviewSelected(EvidenceReviewState.ManagerVerified)));
        row.Children.Add(new Button { Content = "Needs rework", Height = 34 }.With(_ => _.Click += (_, _) => ReviewSelected(EvidenceReviewState.NeedsRework)));
        row.Children.Add(new Button { Content = "Open managed file", Height = 34 }.With(_ => _.Click += (_, _) => OpenSelected()));
        row.Children.Add(new Button { Content = "Supersede with newer version", Height = 34 }.With(_ => _.Click += async (_, _) => await SupersedeAsync()));
        sp.Children.Add(row);
        sp.Children.Add(_message);

        sp.Children.Add(new TextBlock { Text = "Source documents (verified corpus, read-only)", Classes = { "h3" }, Margin = new(0, 8, 0, 0) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Document", Binding = new Avalonia.Data.Binding("DisplayName"), Width = new DataGridLength(420) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Avalonia.Data.Binding("Role"), Width = new DataGridLength(90) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Version", Binding = new Avalonia.Data.Binding("SourceVersion"), Width = new DataGridLength(150) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "sha256", Binding = new Avalonia.Data.Binding("ShaShort"), Width = new DataGridLength(110) });
        _sources.Columns.Add(new DataGridTextColumn { Header = "Provenance", Binding = new Avalonia.Data.Binding("ProvenanceNote"), Width = new DataGridLength(300) });
        sp.Children.Add(_sources);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row2.Children.Add(new Button { Content = "Open source PDF via system viewer", Height = 34 }.With(_ => _.Click += (_, _) =>
        {
            if (_sources.SelectedItem is SourceDocRow s)
                Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(s.StoredRelativePath));
            else _message.Text = "Select a source document row first.";
        }));
        sp.Children.Add(row2);
        return new ScrollViewer { Content = sp };
    }

    private void Load()
    {
        var ev = App.Workspace.AllEvidence().Select(e => new EvidenceRow(
            e.Title, e.Kind.ToString(), e.ReviewState.ToString(), e.FileName, e.Sha256[..12] + "…",
            (e.EffectiveFrom?.ToString("dd MMM yyyy") ?? "—") + (e.EffectiveTo is null ? "" : " → " + e.EffectiveTo?.ToString("dd MMM yyyy")),
            e.ContainsPatientData ? "flagged" : "", e.Id, e.StoredRelativePath)).ToList();
        _grid.ItemsSource = ev;

        _sources.ItemsSource = App.Workspace.AllSourceDocuments()
            .Select(d => new SourceDocRow(d.DisplayName, d.Role, d.SourceVersion, d.Sha256Hex[..12] + "…", d.ProvenanceNote, d.StoredRelativePath)).ToList();
    }

    private async Task AttachAsync()
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl == null) return;
        var files = await tl.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { AllowMultiple = false, Title = "Attach an evidence document" });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) { _message.Text = "The chosen file has no local path."; return; }
        var dlg = new EvidenceDialog(this);
        await dlg.ShowDialog(App.Main!);
        if (!dlg.Confirmed) return;
        var (evidResult, _) = App.Workspace.ImportEvidence(path, dlg.TitleText, dlg.Kind, dlg.EffectiveFrom, dlg.EffectiveTo, dlg.HasPatientData, dlg.PatientDataAttestation, App.Session!);
        _message.Text = evidResult.Ok ? $"Attached. Signature and checksum verified; review starts at Draft." : evidResult.Error;
        Load();
    }

    private async void ReviewSelected(EvidenceReviewState target)
    {
        if (_grid.SelectedItem is not EvidenceRow row) { _message.Text = "Select an evidence row first."; return; }
        string note = "";
        if (target == EvidenceReviewState.ManagerVerified || target == EvidenceReviewState.NeedsRework)
            note = await Dialogs.Prompt(this, target == EvidenceReviewState.ManagerVerified ? "Verification note (optional)" : "Rework reason (required)", "Note") ?? "";
        var res = App.Workspace.SetEvidenceReview(row.Id, target, App.Session!, note);
        _message.Text = res.Ok ? "Review state updated." : res.Error;
        Load();
    }

    private void OpenSelected()
    {
        if (_grid.SelectedItem is not EvidenceRow row) { _message.Text = "Select an evidence row first."; return; }
        Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(row.StoredRelativePath));
    }

    private async Task SupersedeAsync()
    {
        if (_grid.SelectedItem is not EvidenceRow old || !old.Title.StartsWith("")) { }
        if (_grid.SelectedItem is not EvidenceRow oldRow) { _message.Text = "Select the superseded (current approved) evidence row first."; return; }
        if (oldRow.ReviewState != nameof(EvidenceReviewState.ManagerVerified))
        { _message.Text = "Only ManagerVerified evidence uses the supersession flow. Drafts can simply be deleted or reworked."; return; }
        var picker = new EvidencePickerDialog(this);
        await picker.ShowDialog(App.Main!);
        var replacement = picker.SelectedItem;
        if (replacement == null) return;
        var reason = await Dialogs.Prompt(this, "Supersession reason (required)", "Why is the old version being replaced?");
        if (string.IsNullOrWhiteSpace(reason)) return;
        var res = App.Workspace.SupersedeEvidence(oldRow.Id, replacement.Id, reason, App.Session!);
        _message.Text = res.Ok ? "Superseded: affected requirement links are flagged and a re-link task was created for each." : res.Error;
        Load();
    }

    public record EvidenceRow(string Title, string Kind, string ReviewState, string FileName, string ShaShort, string Effective, string PatientLabel, Guid Id, string StoredRelativePath);
    public record SourceDocRow(string DisplayName, string Role, string SourceVersion, string ShaShort, string ProvenanceNote, string StoredRelativePath);

    public void OpenEvidence(Guid id)
    {
        var rows = (List<DocumentsView.EvidenceRow>)_grid.ItemsSource!;
        var hit = rows.FirstOrDefault(r => r.Id == id);
        if (hit == null) { Load(); hit = ((List<DocumentsView.EvidenceRow>)_grid.ItemsSource!).FirstOrDefault(r => r.Id == id); }
        _grid.SelectedItem = hit;
    }
}