using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Application;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Runtime;

namespace QsppManager.Desktop.Views;

/// <summary>Milestone 1 print path laid out like the macOS print panel (§5): settings in a
/// 360pt left column, the current render as a live file card on the right. Render → preview
/// (in-app images stay deferred, DD-20: the exact file opens in the system viewer) → submit
/// via CUPS lp. States: Prepared / Sent to printer / Failed. The spooler accepting a job is
/// NOT proof of paper.</summary>
public partial class PrintCentreView : UserControl
{
    private const string NoPrinterItem = "No printers found \u00B7 Refresh";

    private readonly ShellViewModel _shell;
    private readonly ComboBox _req = new() { MinWidth = 320 };
    private readonly ComboBox _variant = new() { ItemsSource = new[] { "completed", "prefilled", "blank" }, SelectedIndex = 0 };
    private readonly ComboBox _printer = new() { MinWidth = 200 };
    private readonly CheckBox _duplex = new() { Content = "Duplex (long edge)", FontSize = 13 };
    private readonly NoticeHost _resultBar = new();
    private readonly DataGrid _jobs = Ui.Grid().With(_ => _.MaxHeight = 220);
    private TableHost _jobsHost = null!;
    private readonly TextBlock _printerLine = new() { Classes = { "small" }, Foreground = Ui.Br("Brush.Muted"), TextWrapping = TextWrapping.Wrap };
    private TextBlock _copiesValue = null!;
    private Button _sendBtn = null!;
    private StackPanel _previewBody = null!;
    private bool _suppressPrinterSel;
    private int _copies = 1;
    private List<RequirementRow> _rows = [];
    private Guid _lastRenderedJob = Guid.Empty;
    private string _lastRenderedPdf = "";

    public PrintCentreView(ShellViewModel shell)
    {
        _shell = shell;
        Content = Build();
        LoadRows();
        LoadPrinters();
        LoadJobs();
        UpdateSendEnabled();
    }

    private Control Build()
    {
        // ---- primary interactive controls get automation names (voice control / screen readers) ----
        Ui.Ax(_req, "Requirement to print", "The requirement whose checklist is rendered");
        Ui.Ax(_variant, "Checklist variant", "Completed, pre-filled or blank");
        Ui.Ax(_printer, "Printer", "CUPS printer that receives the print submission. The 'Refresh' entry re-scans CUPS.");
        Ui.Ax(_jobs, "Recent print jobs", "Select a job row to print it again with the same PDF");

        var sp = new StackPanel { Margin = new(32, 28), Spacing = 12, MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.PrintCentre, "Print the current requirement checklist as a working paper document.", "Variants: blank, pre-filled with expected evidence, or completed with the current evidence register. Rendering is A4, greyscale-safe; paper output depends on the printer; a spooler acceptance does not guarantee the pages printed."));
        sp.Children.Add(_resultBar);

        // ---- print-panel layout (§5): settings column + file preview card ----
        var settings = new StackPanel { Spacing = 12 };
        settings.Children.Add(Ui.FormField("Document", _req));
        settings.Children.Add(Ui.FormField("Variant", _variant));
        settings.Children.Add(Ui.FormField("Printer", PrinterRow()));
        settings.Children.Add(Ui.FormField("Copies", CopiesStepper()));
        settings.Children.Add(_duplex);
        settings.Children.Add(Ui.Spacer(6));
        var actions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        var renderBtn = Ui.PrimaryBtn("Render + preview").With(b => { b.Height = 32; b.Click += async (_, _) => await RenderAsync(); });
        _sendBtn = Ui.Btn("Send to printer").With(b => { b.Height = 32; b.Click += (_, _) => PrintLast(); });
        var openBtn = Ui.GhostBtn("Open PDF externally").With(b => { b.Height = 32; b.Click += (_, _) =>
        {
            if (File.Exists(_lastRenderedPdf)) Helpers.OpenWithSystemViewer(_lastRenderedPdf);
            else _resultBar.Show(NoticeTone.Info, "Render the document first.");
        }; });
        actions.Children.Add(renderBtn);
        actions.Children.Add(_sendBtn);
        actions.Children.Add(openBtn);
        settings.Children.Add(actions);
        var settingsCard = Ui.Card(settings, vPad: 16, hPad: 18);
        settingsCard.MinHeight = 420;

        _previewBody = new StackPanel { Spacing = 10 };
        ResetPreview();
        var previewCard = Ui.Card(_previewBody, vPad: 16, hPad: 18);

        var panel = new Avalonia.Controls.Grid
        {
            ColumnDefinitions = new ColumnDefinitions("360,8,*"),
            Children = { settingsCard, new GridSplitter { Width = 8, Background = Brushes.Transparent }, previewCard },
        };
        Avalonia.Controls.Grid.SetColumn(settingsCard, 0);
        Avalonia.Controls.Grid.SetColumn(panel.Children[1], 1);
        Avalonia.Controls.Grid.SetColumn(previewCard, 2);
        sp.Children.Add(panel);

        // ---- remote assessment round pack (M7): its own small section ----
        // One button renders the whole surveillance round as a response pack: every REM
        // item with its verbatim ask, recorded response and linked evidence, ready to
        // email or print for the portal round. Independent of the Document picker above;
        // "Send to printer" and "Open PDF externally" above work on whatever rendered last.
        sp.Children.Add(Ui.Section("Remote assessment round pack", Ui.Col(10,
            Ui.Btn("Render round pack + preview").With(b =>
            {
                b.Height = 32;
                b.Click += async (_, _) => await RenderRoundPackAsync();
            }),
            Ui.Small("Composes every REM evidence request from the current surveillance round into one dated PDF: verbatim wording, expected evidence, recorded notes and next actions, linked evidence with review states, and a sign-off block."))));

        // ---- recent print jobs: rows under a cap; a one-line empty state, never a void ----
        _jobs.Columns.Add(Ui.Col("When", "When", 150));
        _jobs.Columns.Add(Ui.Col("Document", "Title", 300));
        _jobs.Columns.Add(Ui.Col("Printer", "Printer", 130));
        _jobs.Columns.Add(Ui.Col("Copies", "Copies", 70));
        _jobs.Columns.Add(Ui.Col("State", "State", 120));
        _jobs.Columns.Add(Ui.Col("Detail", "Detail", 280));
        _jobs.Columns.Add(Ui.Col("Id", "Id", 280));
        var reprintStrip = new StackPanel
        {
            IsVisible = false,
            Children =
            {
                Ui.Btn("Print again (selected job, same PDF)").With(b => { b.Height = 30; b.Click += (_, _) => ReprintSelected(); }),
            },
        };
        _jobs.SelectionChanged += (_, _) =>
            reprintStrip.IsVisible = _jobs.SelectedItem is not null;
        var jobsScroll = new ScrollViewer { Content = _jobs, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var jobsEmpty = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(4, 6), IsVisible = false,
            Children =
            {
                new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Ui.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Classes = { "small" }, Foreground = Ui.Br("Brush.Muted"), Text = "No print jobs yet. Render + preview records the first one.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var jobsPanel = new Panel();
        jobsPanel.Children.Add(jobsScroll);
        jobsPanel.Children.Add(jobsEmpty);
        _jobsHost = new TableHost(jobsScroll, jobsEmpty, jobsPanel);
        sp.Children.Add(Ui.Section("Recent print jobs", Ui.Col(10,
            _jobsHost.Panel,
            Ui.Small("Print again re-submits the previously rendered PDF with the recorded settings. Failed submissions are never retried automatically, because paper would come out twice if the first job actually printed."))));

        return new ScrollViewer { Content = sp };
    }

    /// <summary>The printer row keeps the picker honest when CUPS shows nothing: the picker
    /// itself carries the "No printers found · Refresh" entry, and selecting it re-scans.</summary>
    private Control PrinterRow()
    {
        _printer.SelectionChanged += (_, _) =>
        {
            if (_suppressPrinterSel) return;
            if (ReferenceEquals(_printer.SelectedItem, NoPrinterItem)) LoadPrinters();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_printer);
        row.Children.Add(_printerLine);
        _printerLine.VerticalAlignment = VerticalAlignment.Center;
        return row;
    }

    /// <summary>Copies stepper styled to tokens: minus / value / plus on the app's quiet
    /// buttons (owner brief — the plain NumericUpDown looked like a foreign control).</summary>
    private Control CopiesStepper()
    {
        _copiesValue = new TextBlock { Text = "1", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, MinWidth = 18, TextAlignment = TextAlignment.Center };
        var minus = Ui.GhostBtn("\u2212").With(b =>
        {
            b.Height = 26;
            b.Click += (_, _) => { if (_copies > 1) { _copies--; SyncCopies(); } };
        });
        var plus = Ui.GhostBtn("+").With(b =>
        {
            b.Height = 26;
            b.Click += (_, _) => { if (_copies < 20) { _copies++; SyncCopies(); } };
        });
        var card = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        card.Children.Add(minus);
        card.Children.Add(_copiesValue);
        card.Children.Add(plus);
        card.Children.Add(new TextBlock { Text = "of up to 20", FontSize = 11, Foreground = Ui.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new(2, 0, 0, 0) });
        return card;
    }

    private void SyncCopies() => _copiesValue.Text = _copies.ToString();

    private record JobRow(Guid Id, string When, string Title, string Printer, string Copies, string State, string Detail);

    private List<JobRow> JobRows()
    {
        var jobs = App.Printer.RecentJobs(30);
        _lastRenderedJob = jobs.FirstOrDefault(j => j.State == PrintJobState.Prepared)?.Id ?? _lastRenderedJob;
        _lastRenderedPdf = jobs.FirstOrDefault(j => j.State == PrintJobState.Prepared)?.PdfRelativePath is { } rp
            ? Path.Combine(AppData.PdfDir, rp) : _lastRenderedPdf;
        return jobs.Select(j => new JobRow(j.Id, j.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy h:mm tt"), j.Title,
            j.PrinterName, j.Copies.ToString(), j.State.ToString(), j.ResultDetail)).ToList();
    }

    private void LoadRows()
    {
        _rows = App.Workspace.RequirementRows(App.Session);
        // Display titles lead the picker (owner item 15): real TextBlock pairs so the ref
        // renders muted mono AFTER the title — no visible label starts with a code.
        _req.ItemsSource = _rows.Select(r => (object)Ui.RequirementLabel(r.DisplayTitleText, r.Ref)).ToList();
        _req.SelectedIndex = _rows.Count > 0 ? 0 : -1;
    }

    private void LoadPrinters()
    {
        _suppressPrinterSel = true;
        try
        {
            var printers = App.Printer.DiscoverPrinters();
            var names = printers.Select(p => p.Name).ToList();
            _printer.ItemsSource = names.Count > 0 ? names : [NoPrinterItem];
            _printer.SelectedIndex = 0;
            _printerLine.Text = printers.Count == 0
                ? "Rendering and preview work without a printer; the PDF can be saved for another device."
                : (printers.FirstOrDefault(p => p.IsDefault) is { } def ? $"Default: {def.Name}." : "");
        }
        finally
        {
            _suppressPrinterSel = false;
        }
    }

    /// <summary>"Send to printer" stays disabled until a rendered file exists (owner brief).</summary>
    private void UpdateSendEnabled()
    {
        if (_sendBtn == null) return;
        _sendBtn.IsEnabled = _lastRenderedJob != Guid.Empty && File.Exists(_lastRenderedPdf);
    }

    private void ResetPreview()
    {
        UpdateSendEnabled();
        if (_previewBody == null) return;
        _previewBody.Children.Clear();
        _previewBody.Children.Add(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Classes = { "h3" }, Text = "Preview" },
                Ui.Small("Nothing rendered yet in this session."),
            },
        });
    }

    private void ShowPreviewBody(PrintJob job)
    {
        UpdateSendEnabled();
        if (_previewBody == null) return;
        var pdf = Path.Combine(AppData.PdfDir, job.PdfRelativePath);
        _previewBody.Children.Clear();
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Classes = { "h3" }, Text = "Preview" });
        body.Children.Add(new TextBlock { Text = job.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(Ui.Row(6, Ui.Chip(job.State.ToString(), job.State == PrintJobState.Prepared ? "info" : job.State == PrintJobState.SentToPrinter ? "ok" : "bad")));
        body.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Small($"Rendered {job.CreatedAtUtc.ToLocalTime():dd MMM yyyy h:mm tt} · {job.Copies} cop{(job.Copies == 1 ? "y" : "ies")}{(job.Duplex ? " · duplex" : "")}"),
                Ui.Small(string.IsNullOrWhiteSpace(job.PrinterName) ? "Printer: none selected at render time" : $"Printer: {job.PrinterName}"),
                new TextBlock { Classes = { "monoSm" }, Foreground = Ui.Br("Brush.Muted"), Text = pdf, TextWrapping = TextWrapping.Wrap },
                Ui.Small("In-app page images stay deferred (DD-20); this exact file opens in the system viewer."),
            },
        });
        body.Children.Add(Ui.PrimaryBtn("Open PDF externally").With(b => { b.Height = 30; b.Click += (_, _) => Helpers.OpenWithSystemViewer(pdf); }));
        _previewBody.Children.Add(body);
    }

    private async Task RenderAsync()
    {
        if (_rows.Count == 0) { _resultBar.Show(NoticeTone.Info, "No requirements imported yet; print needs a requirement to reference."); return; }
        var row = _rows.ElementAtOrDefault(Math.Max(_req.SelectedIndex, 0));
        if (row == null) { _resultBar.Show(NoticeTone.Info, "Choose a requirement first."); return; }
        var detail = App.Workspace.RequirementDetail(row.Id, App.Session);
        if (detail == null) { _resultBar.Show(NoticeTone.Error, "Requirement details could not be loaded."); return; }

        var variant = _variant.SelectedItem as string ?? "completed";
        var evRows = variant == "prefilled"
            ? detail.Requirement.EvidenceExamples.Select(e => new PdfComposer.EvidenceRow("", e.Text, "example", "", "", "")).ToList()
            : detail.Links.Select(l => new PdfComposer.EvidenceRow(
                "", l.EvidenceTitle, l.Kind.ToString(), l.FileName,
                (l.EffectiveFrom?.ToString("dd MMM yyyy") ?? "") + (l.EffectiveTo is null ? "" : " → " + l.EffectiveTo?.ToString("dd MMM yyyy")),
                l.ReviewState.ToString())).ToList();
        var taskRows = detail.Tasks.Select(t => new PdfComposer.TasksRow(t.Title,
            detail.OwnerName, t.DueDate?.ToString("dd MMM yyyy") ?? "", t.State.ToString())).ToList();

        var anchorNote = detail.SourceDocument is null
            ? "source document not linked to this requirement"
            : $"{detail.SourceDocument.DisplayName} · sha256 {detail.SourceDocument.Sha256Hex[..12]}…";
        anchorNote += detail.Requirement.AnchorPage is { } pg
            ? $" · PDF p{pg}{(detail.Requirement.AnchorVerified ? " (verified)" : " (unverified)")}"
            : detail.Requirement.AnchorApproximate ? " · line anchor approximate (provisional)" : "";

        var printerName = _printer.SelectedItem as string ?? "";
        if (printerName == NoPrinterItem) printerName = "";
        var copies = _copies;
        var duplex = _duplex.IsChecked == true;

        var model = new PdfComposer.ChecklistModel(
            App.Workspace.EnsurePharmacy().Name,
            detail.Requirement.Ref, detail.Requirement.Title,
            detail.Requirement.PlainSummary, detail.Requirement.WordingExcerpt,
            detail.Requirement.SourceDocFile, detail.Requirement.SourceVersion, anchorNote,
            detail.State.Readiness, detail.State.Applicability, detail.OwnerName,
            detail.State.NextAction, evRows, taskRows, variant, DateTime.UtcNow);

        try
        {
            await Task.Run(() =>
            {
                var bytes = App.Composer.Compose(model);
                var job = new PrintJob
                {
                    DocumentKind = "RequirementChecklist",
                    Title = $"{detail.Requirement.Ref} · {variant}",
                    SubjectId = detail.Requirement.Id,
                    PrinterName = printerName,
                    Copies = copies,
                    Duplex = duplex,
                    State = PrintJobState.Prepared,
                    ActorUserId = App.Session?.UserId ?? Guid.Empty,
                };
                var rel = $"job-{job.Id}.pdf";
                job.PdfRelativePath = rel;
                File.WriteAllBytes(Path.Combine(AppData.PdfDir, rel), bytes);
                App.Printer.RecordJob(job).GetAwaiter().GetResult();
                _lastRenderedJob = job.Id;
                _lastRenderedPdf = Path.Combine(AppData.PdfDir, rel);
            });
            _resultBar.Show(NoticeTone.Ok, "Rendered. The generated file is on the right; 'Send to printer' submits it through CUPS.");
            LoadJobs();
            ShowPreviewBody(App.Printer.RecentJobs(30).First(j => j.Id == _lastRenderedJob));
        }
        catch (Exception ex)
        {
            _resultBar.Show(NoticeTone.Error, $"Rendering failed: {ex.Message}. Nothing was queued and no state was changed.");
        }
    }

    /// <summary>M7: render the whole surveillance round as one response-pack PDF.
    /// Read-only over the workspace (no requirement-state changes); records one print
    /// job with DocumentKind "RemoteRoundPack", then previews it.</summary>
    private async Task RenderRoundPackAsync()
    {
        if (App.Session == null) { _resultBar.Show(NoticeTone.Info, "Sign in first."); return; }
        var items = App.Workspace.SurveillanceRoundPackItems();
        if (items.Count == 0) { _resultBar.Show(NoticeTone.Info, "No surveillance (REM) requirements imported; nothing to pack."); return; }
        var round = App.Workspace.SurveillanceRound();
        var ph = App.Workspace.EnsurePharmacy();
        var summary = $"{round.Items} items · {round.WithEvidence} with evidence · {round.Verified} verified" +
            (round.NextEvent is null ? "" : $" · next event: {round.NextEvent}");
        var printerName = _printer.SelectedItem as string ?? "";   // UI-thread reads BEFORE Task.Run:
        if (printerName == NoPrinterItem) printerName = "";        // Avalonia controls are thread-affine.
        var copies = _copies;
        var duplex = _duplex.IsChecked == true;
        var model = new PdfComposer.RoundPackModel(
            ph.Name,
            "QCPP remote assessment portal — surveillance round",
            "vendor questionnaire pack (REM items)",
            summary,
            items, DateTime.UtcNow);

        try
        {
            await Task.Run(() =>
            {
                var bytes = App.Composer.ComposeRoundPack(model);
                var job = new PrintJob
                {
                    DocumentKind = "RemoteRoundPack",
                    Title = $"Remote assessment round pack · {items.Count} items",
                    PrinterName = printerName,
                    Copies = copies,
                    Duplex = duplex,
                    State = PrintJobState.Prepared,
                    ActorUserId = App.Session.UserId,
                };
                var rel = $"job-{job.Id}.pdf";
                job.PdfRelativePath = rel;
                File.WriteAllBytes(Path.Combine(AppData.PdfDir, rel), bytes);
                App.Printer.RecordJob(job).GetAwaiter().GetResult();
                _lastRenderedJob = job.Id;
                _lastRenderedPdf = Path.Combine(AppData.PdfDir, rel);
            });
            _resultBar.Show(NoticeTone.Ok, $"Round pack rendered: {items.Count} REM items in one PDF. 'Send to printer' submits it through CUPS; 'Open PDF externally' for emailing.");
            LoadJobs();
            ShowPreviewBody(App.Printer.RecentJobs(30).First(j => j.Id == _lastRenderedJob));
        }
        catch (Exception ex)
        {
            _resultBar.Show(NoticeTone.Error, $"Round pack rendering failed: {ex.Message}. Nothing was queued and no state was changed.");
        }
    }

    private void PrintLast()
    {
        if (_lastRenderedJob == Guid.Empty || !File.Exists(_lastRenderedPdf))
        { _resultBar.Show(NoticeTone.Info, "Render a document first."); return; }
        var printer = _printer.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(printer) || printer == NoPrinterItem) { _resultBar.Show(NoticeTone.Info, "Choose a printer (none visible, press refresh)."); return; }
        var outcome = App.Printer.SubmitToPrinter(_lastRenderedPdf, printer, _copies, _duplex.IsChecked == true);
        var state = outcome.Ok ? PrintJobState.SentToPrinter : PrintJobState.Failed;
        App.Printer.UpdateJobState(_lastRenderedJob, state, outcome.detail, "").GetAwaiter().GetResult();
        _resultBar.Show(outcome.Ok, outcome.detail);
        LoadJobs();
    }

    private void ReprintSelected()
    {
        if (_jobs.SelectedItem is not JobRow row) { _resultBar.Show(NoticeTone.Info, "Select a job row."); return; }
        var pdf = Path.Combine(AppData.PdfDir, $"job-{row.Id}.pdf");
        if (!File.Exists(pdf)) { _resultBar.Show(NoticeTone.Error, "The rendered PDF for this job is gone (cleaned up). Render again instead."); return; }
        var printer = _printer.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(printer) || printer == NoPrinterItem) { _resultBar.Show(NoticeTone.Info, "Choose a printer."); return; }
        var outcome = App.Printer.SubmitToPrinter(pdf, printer, _copies, _duplex.IsChecked == true);
        var job = App.Db.PrintJobs.FirstOrDefault(j => j.Id == row.Id);
        if (job != null)
            App.Printer.UpdateJobState(row.Id, outcome.Ok ? PrintJobState.SentToPrinter : PrintJobState.Failed, outcome.detail + " (re-print)", "").GetAwaiter().GetResult();
        _resultBar.Show(outcome.Ok, outcome.detail);
        LoadJobs();
    }

    private void LoadJobs()
    {
        _jobs.ItemsSource = JobRows();
        _jobsHost?.Refresh(_jobs.ItemsSource?.Cast<object>().Count() ?? 0);
        UpdateSendEnabled();
    }
    /// <summary>Table with an in-place empty state; the visible child tracks the row count
    /// (premium-polish 3.8 — Print centre recent jobs).</summary>
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