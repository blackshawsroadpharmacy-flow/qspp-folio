using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.ProcessSort;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop.Views;

/// <summary>"Process and Sort" modal: pick source + output, scan, process, review
/// uncertain files, then publish the folio. All work is local; the per-file grid
/// shows state as it happens and a failed file never stops the batch.</summary>
public partial class ProcessSortDialog : Window
{
    private readonly TextBox _sourceBox = new() { IsReadOnly = true, Watermark = "Folder holding the pharmacy documents…" };
    private readonly TextBox _outputBox = new() { IsReadOnly = true, Watermark = "Where the folio is built (never the source folder)…" };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<Row> _rows = new();
    private readonly DataGrid _grid = new() { Height = 330, AutoGenerateColumns = false, IsReadOnly = true };
    private readonly ComboBox _batchPicker = new() { MinWidth = 560, PlaceholderText = "Batch history…" };
    private readonly Button _bulkApprove = new();
    private readonly List<Guid> _batchIds = [];
    private CancellationTokenSource? _cts;
    private Guid? _batchId;
    private string _sourceDir = "", _outputDir = "";
    private readonly ShellViewModel? _shell;
    private readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false, Height = 4 };
    private readonly TextBlock _busyText = new() { Classes = { "small" }, IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private bool _working;

    public sealed record Row(string File, string State, string Detail, Guid ItemId, Guid BatchId);

    public ProcessSortDialog(ShellViewModel? shell = null)
    {
        _shell = shell;
        Title = "Process and Sort";
        Width = 1080; Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var sp = new StackPanel { Margin = new(24, 20), Spacing = 10 };
        sp.Children.Add(new TextBlock { Classes = { "h2" }, Text = "Process and Sort a document folder" });
        sp.Children.Add(new TextBlock
        {
            Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Everything stays on this Mac: text extraction (PdfPig), the OCR tier and destination suggestions (deterministic matching against the imported corpus). No document content ever leaves the device. Source files are never moved or deleted.",
        });

        var chooseSource = new Button { Content = "Choose…" };
        var chooseOutput = new Button { Content = "Choose…" };
        chooseSource.Click += async (_, _) => { var d = await PickFolder(); if (d != null) { _sourceDir = d; _sourceBox.Text = d; } };
        chooseOutput.Click += async (_, _) => { var d = await PickFolder(); if (d != null) { _outputDir = d; _outputBox.Text = d; } };
        sp.Children.Add(Ui.Row(8,
            Ui.FormField("Documents folder", _sourceBox),
            chooseSource,
            Ui.FormField("Output folder", _outputBox),
            chooseOutput));

        var approveBtn = Ui.PrimaryBtn("Approve selected…", axHelpText: "Correct or accept destinations for the selected file");
        _bulkApprove = Ui.PrimaryBtn("Approve proposed…", axHelpText: "Attach every clear proposal to its requirement — a human pressed the button once for the batch");
        var actionRow = Ui.Row(8,
            Ui.PrimaryBtn("Scan folder", axHelpText: "Count supported files and show what will happen"),
            Ui.PrimaryBtn("Process and Sort", axHelpText: "Extract, classify and prepare destinations"),
            Ui.PrimaryBtn("Cancel run", axHelpText: "Stop after the current file; processed files keep their state"),
            approveBtn,
            _bulkApprove,
            Ui.PrimaryBtn("Publish folio", axHelpText: "Write the sorted tree with checksum-verified copies and manifest"),
            Ui.PrimaryBtn("Open output", axHelpText: "Reveal the built folio in Finder"));
        var buttons = actionRow.Children.OfType<Button>().ToArray();

        buttons[0].Click += async (_, _) => await ScanAsync();
        buttons[1].Click += async (_, _) => await ProcessAsync();
        buttons[2].Click += (_, _) => CancelRun();
        approveBtn.Click += async (_, _) => await ApproveSelectedAsync();
        _bulkApprove.Click += async (_, _) => await BulkApproveAsync();
        buttons[5].Click += async (_, _) => await PublishAsync();
        buttons[6].Click += (_, _) => OpenOutput();
        sp.Children.Add(actionRow);

        var openReqs = Ui.PrimaryBtn("Open Requirements →", axHelpText: "See the attachments on their requirements");
        openReqs.Click += (_, _) => { Close(); _shell?.Navigate(ShellPage.Requirements); };
        sp.Children.Add(Ui.Row(8, Ui.FormField("Batch history", _batchPicker), openReqs));
        _batchPicker.SelectionChanged += async (_, _) => await LoadSelectedBatch();

        sp.Children.Add(_summary);
        sp.Children.Add(_busy);
        sp.Children.Add(_busyText);

        _grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Avalonia.Data.Binding("File"), Width = new DataGridLength(340) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new Avalonia.Data.Binding("Detail"), Width = new DataGridLength(500) });
        Ui.Ax(_grid, "Per-file progress", "One failed file never stops the batch — correct and retry after the run");
        _grid.ItemsSource = _rows;
        sp.Children.Add(_grid);

        Content = new ScrollViewer { Content = sp };
        Opened += async (_, _) => await LoadBatchList();
    }

    /// <summary>Batch history: past batches stay reachable, so review and publish are
    /// resumable in a later session (a completed batch is not a dead end).</summary>
    private async Task LoadBatchList()
    {
        var batches = await App.Db.FolioBatches.AsNoTracking()
            .OrderByDescending(b => b.StartedAtUtc).Take(15).ToListAsync();
        _batchIds.Clear();
        _batchPicker.Items.Clear();
        foreach (var b in batches)
        {
            _batchIds.Add(b.Id);
            var tail = string.Join('/', b.SourceDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).TakeLast(2));
            _batchPicker.Items.Add($"{b.StartedAtUtc:dd MMM HH:mm} — {b.State} — {b.ScannedCount} scanned, {b.SkippedCount} skipped — …{tail}");
        }
        if (_batchIds.Count > 0 && _batchId == null) _batchPicker.SelectedIndex = 0;
    }

    private async Task LoadSelectedBatch()
    {
        var i = _batchPicker.SelectedIndex;
        if (i < 0 || i >= _batchIds.Count) return;
        var batch = await App.Db.FolioBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == _batchIds[i]);
        if (batch == null) return;
        _batchId = batch.Id;
        _sourceDir = batch.SourceDirectory; _outputDir = batch.OutputDirectory;
        _sourceBox.Text = _sourceDir; _outputBox.Text = _outputDir;
        await ReloadHistory();
    }

    /// <summary>One press attaches every CLEAR proposal (single confident destination) to
    /// its requirement: evidence is imported and linked exactly like a manual attach per
    /// item. Files needing a human choice (multiple, sensitive, duplicate, unreadable,
    /// OCR-queued) stay in the queue.</summary>
    private async Task BulkApproveAsync()
    {
        if (_batchId == null) { Say("Load or scan a batch first."); return; }
        var batchId = _batchId.Value;
        var session = App.Session!;
        Say("Approving every clear proposal…");
        _working = true; BusyOn("Attaching clear proposals — each one imports and links through the normal pipeline…");
        var result = await Task.Run(() => App.Folio.ApproveProposed(batchId, session));
        _working = false; BusyOff();
        await ReloadHistory();
        if (result.approved == 0 && result.failed == 0)
            Say("No clear proposals here yet — process the batch first, or approve the remaining files one at a time.");
        else if (result.approved == 0)
            Say($"Nothing approved: {result.firstError}");
        else
        {
            var inReview = _rows.Count(r => r.State is "Proposed" or "MultipleCandidates" or "SensitiveHold"
                or "NeedsOcr" or "Unreadable" or "Pending" or "Processing" or "Abstained" or "Duplicate");
            Say($"Approved {result.approved} — imported and attached to their proposed requirements (evidence kinds guessed from file names; correct any in the evidence detail)."
                + (result.failed > 0 ? $" Failures: {result.firstError} " : "")
                + (inReview > 0 ? $" {inReview} still need your review (multiple, sensitive, unclear)." : "")
                + " Press Open Requirements → to see the attachments on each requirement.");
        }
    }

    private async Task<string?> PickFolder()
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl == null) return null;
        var folders = await tl.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { AllowMultiple = false, Title = "Choose folder" });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(_sourceDir)) { Say("Choose a documents folder first."); return; }
        if (string.IsNullOrWhiteSpace(_outputDir)) { Say("Choose an output folder first — never the source folder."); return; }
        if (_working) { Say("Already working — press Cancel run to stop."); return; }
        _working = true; BusyOn("Scanning the folder… hashing every file to recognise unchanged content next time.");
        var (res, id) = (await Task.Run(() => App.Folio.ScanAndQueue(App.Session!, _sourceDir, _outputDir)));
        _working = false; BusyOff();
        if (id == null) { Say(res.Error); return; }
        _batchId = id;
        await LoadBatchList();
        if (_batchIds.Contains(_batchId.Value)) _batchPicker.SelectedIndex = _batchIds.IndexOf(_batchId.Value);
        await ReloadHistory();
        Say("Scan complete. Press Process and Sort when ready — processing is cancellable and resumable.");
    }

    private async Task ProcessAsync()
    {
        if (_batchId == null) { Say("Scan a folder first."); return; }
        if (_working) { Say("Already working — press Cancel run to stop."); return; }
        _cts = new CancellationTokenSource();
        _working = true;
        var total = _rows.Count;
        var done = 0;
        Say("Processing… one file at a time; a problem isolates that file only.");
        BusyOn($"Preparing to process {total} files…");
        var session = App.Session!;
        var batchId = _batchId.Value;
        await Task.Run(() =>
        {
            var progress = new Progress<(string relPath, string state)>(t => Dispatcher.UIThread.Post(() =>
            {
                done++;
                BusyText($"Processing {done} of {total} — {t.relPath}");
                var r = _rows.FirstOrDefault(x => x.File == t.relPath);
                if (r != null)
                {
                    var i = _rows.IndexOf(r);
                    _rows[i] = r with { State = t.state, Detail = "" };
                    _grid.SelectedItem = _rows[i];
                    if (_grid.Columns.Count > 0) _grid.ScrollIntoView(_rows[i], _grid.Columns[0]);
                }
            }));
            App.Folio.ProcessBatch(batchId, session, progress, _cts.Token);
        });
        _working = false; BusyOff();
        await ReloadHistory();
    }

    private void BusyOn(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _busy.IsVisible = true;
            _busyText.IsVisible = true;
            _busyText.Text = text;
        });
    }

    private void BusyText(string text) => Dispatcher.UIThread.Post(() => _busyText.Text = text);

    private void BusyOff()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _busy.IsVisible = false;
            _busyText.IsVisible = false;
        });
    }

    private void CancelRun() { _cts?.Cancel(); Say("Cancel requested — finishing the current file. Already-processed files keep their state."); }

    private async Task ApproveSelectedAsync()
    {
        if (_grid.SelectedItem is not Row row) { Say("Select a file in the list first."); return; }
        if (_batchId == null) { Say("Scan a folder first."); return; }
        var dlg = new FolioApproveDialog(row.ItemId);
        await dlg.ShowDialog(App.Main!);
        await ReloadHistory();
        if (dlg.Approved) Say("Approved — links are created as proposed, exactly like a manual attach; review stays with the manager.");
    }

    private async Task PublishAsync()
    {
        if (_batchId == null) { Say("Nothing to publish yet — scan, then process, then approve files."); return; }
        var (res, manifest) = App.Folio.PublishFolio(_batchId.Value, App.Session!);
        Say(res.Ok ? $"Folio published. Manifest: {manifest}" : res.Error);
        await ReloadHistory();
    }

    private void OpenOutput()
    {
        if (string.IsNullOrWhiteSpace(_outputDir)) { Say("Choose an output folder first."); return; }
        try { Process.Start(new ProcessStartInfo("/usr/bin/open", $"\"{_outputDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Say($"Could not open the folder: {ex.Message}"); }
    }

    private void Say(string message) => Dispatcher.UIThread.Post(() => _summary.Text = message);

    private async Task ReloadHistory()
    {
        if (_batchId == null) return;
        var batch = await App.Db.FolioBatches.AsNoTracking().Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == _batchId);
        if (batch == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            _rows.Clear();
            foreach (var i in batch.Items)
                _rows.Add(new Row(i.SourceRelativePath, i.State.ToString(), DetailOf(i), i.Id, batch.Id));
            var props = batch.Items.Count(i => i.State == FolioItemState.Proposed);
            var mult = batch.Items.Count(i => i.State == FolioItemState.MultipleCandidates);
            var sens = batch.Items.Count(i => i.State == FolioItemState.SensitiveHold);
            var ocr = batch.Items.Count(i => i.State == FolioItemState.NeedsOcr);
            var done = batch.Items.Count(i => i.State is FolioItemState.Approved or FolioItemState.Published);
            _summary.Text = $"Batch {batch.State}: {done}/{batch.Items.Count} attached so far. Queue: {props} clear proposals — {mult} multiple choice — {sens} sensitive — {ocr} awaiting OCR. Skipped at scan: {batch.SkippedCount}." + (props > 0 ? " Approve the clear proposals to attach them, then review the rest one by one." : "");
            _bulkApprove.Content = props > 0 ? $"Approve {props} proposed…" : "Approve proposed…";
        });
        await Task.CompletedTask;
    }

    private static string DetailOf(FolioItem i)
    {
        if (!string.IsNullOrEmpty(i.ErrorText)) return i.ErrorText;
        try
        {
            var props = JsonSerializer.Deserialize<List<FolioSuggestion>>(i.ProposalsJson) ?? [];
            if (props.Count > 0 && i.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates)
                return "→ " + string.Join(", ", props.Take(3).Select(p => $"{p.Ref} ({p.Score})"));
        }
        catch { /* detail is best-effort */ }
        return i.ParserUsed.Length > 0 && PageCountOf(i) > 0 ? $"parsed via {i.ParserUsed}, {i.PageCount} pages" : "";
    }

    private static int PageCountOf(FolioItem i) => i.PageCount;
}

/// <summary>Review dialog for ONE folio file: choose title, kind and destination
/// requirements. Opens pre-filtered to the classifier's top picks; the search
/// narrows across the whole corpus. Sensitive items demand the explicit
/// patient-data attestation before they can be attached at all.</summary>
public partial class FolioApproveDialog : Window
{
    public bool Approved { get; private set; }
    private readonly ObservableCollection<PickRow> _candidates = new();
    private readonly ListBox _list = new() { Height = 300, SelectionMode = SelectionMode.Multiple };
    private readonly TextBox _title = new();
    private readonly TextBox _kind = new() { Text = "PolicyOrSop" };
    private readonly TextBox _search = new() { Watermark = "Search every requirement by ref or words" };
    private readonly CheckBox _patient = new() { Content = "Contains patient information" };
    private readonly TextBox _attestation = new() { Watermark = "Required when the file contains patient data: what was redacted, who reviewed" };
    private readonly Guid _itemId;
    private List<FolioCandidate> _all = new();

    private sealed record PickRow(Guid Id, string Ref, string RefKind, string Title, string Label);

    public FolioApproveDialog(Guid itemId)
    {
        _itemId = itemId;
        Title = "Approve destinations";
        Width = 840; Height = 740;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var sp = new StackPanel { Margin = new(20), Spacing = 10 };
        sp.Children.Add(new TextBlock { Classes = { "h3" }, Text = "Where does this document belong?" });

        var searchBtn = new Button { Content = "Search" };
        searchBtn.Click += (_, _) => Refilter(_search.Text ?? "");
        sp.Children.Add(Ui.Row(8, Ui.FormField("Search", _search), searchBtn));

        _list.ItemsSource = _candidates;
        _list.DisplayMemberBinding = new Avalonia.Data.Binding("Label");
        Ui.Ax(_list, "Requirement destinations", "Select one or more — tick as many as the document supports");
        sp.Children.Add(_list);
        sp.Children.Add(new TextBlock
        {
            Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Br("Brush.Muted"),
            Text = "★ marks the destinations proposed by deterministic matching. A document may genuinely support several requirements.",
        });

        sp.Children.Add(Ui.FormField("Evidence title", _title));
        sp.Children.Add(Ui.FormField("Kind — PolicyOrSop, Certificate, Record, Register, TrainingRecord, PhotoOrScan, Other", _kind));
        sp.Children.Add(_patient);
        sp.Children.Add(Ui.FormField("Redaction attestation", _attestation));

        var approveBtn = new Button { Content = "Approve and attach", Classes = { "primary" } };
        var closeBtn = new Button { Content = "Close" };
        approveBtn.Click += (_, _) => ApproveNow();
        closeBtn.Click += (_, _) => Close();
        sp.Children.Add(Ui.Row(8, approveBtn, closeBtn));

        Content = new ScrollViewer { Content = sp };
        AttachedToVisualTree += (_, _) => LoadAll();
    }

    private void LoadAll()
    {
        try
        {
            var item = App.Db.FolioItems.AsNoTracking().First(i => i.Id == _itemId);
            var props = JsonSerializer.Deserialize<List<FolioSuggestion>>(item.ProposalsJson) ?? [];
            _all = App.Folio.CandidatesForDialog(props);
            _title.Text = DefaultTitleFrom(item.SourceRelativePath);
            Refilter("");
            // Preselect the top proposal when the classifier was confident.
            var top = props.FirstOrDefault();
            var pre = _candidates.FirstOrDefault(p => top != null && p.Id == top.RequirementId);
            if (pre != null && props[0].Score >= 10) _list.SelectedItems?.Add(pre);
        }
        catch (Exception ex)
        {
            _all = App.Folio.CandidatesForDialog(null);
            Refilter("");
            _attestation.Watermark = $"Note: {ex.Message}";
        }
    }

    private static string DefaultTitleFrom(string relPath) => FolioWorkspace.DefaultTitleFrom(relPath);

    private void Refilter(string query)
    {
        var q = (query ?? "").Trim();
        _candidates.Clear();
        var filtered = _all.Where(c =>
            q.Length == 0 ||
            c.Ref.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var c in filtered.Take(120))
            _candidates.Add(new PickRow(c.Id, c.Ref, c.RefKind, c.Title, $"{c.Ref}  {c.Title}   [{c.RefKind}]"));
        if (filtered.Count > 120) _candidates.Add(new PickRow(Guid.Empty, "…", "…", $"{filtered.Count - 120} more — narrow the search", $"{filtered.Count - 120} more — narrow the search"));
    }

    private void ApproveNow()
    {
        var picks = (_list.SelectedItems?.Cast<PickRow>().Where(p => p.Id != Guid.Empty) ?? []).ToList();
        if (picks.Count == 0) { Close(); return; }
        var approvedJson = JsonSerializer.Serialize(picks.Select(p => new FolioSuggestion(p.Id, p.Ref, p.RefKind, p.Title, 0, "user-approved", FolioClassifier.Stage1Model, FolioClassifier.Stage1ModelVersion)));
        var (res, _) = App.Folio.ApproveItem(_itemId, App.Session!, _title.Text.Trim(), _kind.Text.Trim(), approvedJson, _patient.IsChecked == true, _attestation.Text ?? "");
        if (res.Ok) { Approved = true; Close(); }
        else
        {
            _attestation.Watermark = res.Error; // the refusal surfaces inside the dialog
            _attestation.Focus();
        }
    }
}