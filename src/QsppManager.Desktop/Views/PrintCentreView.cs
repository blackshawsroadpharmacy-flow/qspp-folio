using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Avalonia.Layout;
using QsppManager.Application;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Runtime;

namespace QsppManager.Desktop.Views;

/// <summary>Milestone 1 print path: render a real A4 checklist → preview (in-app images
/// with an external-viewer fallback) → submit via CUPS lp. States: Prepared / Sent to
/// printer / Failed. The spooler accepting a job is NOT proof of paper.</summary>
public partial class PrintCentreView : UserControl
{
    private readonly ShellViewModel _shell;
    private readonly ComboBox _req = new() { MinWidth = 360 };
    private readonly ComboBox _variant = new() { ItemsSource = new[] { "completed", "prefilled", "blank" }, SelectedIndex = 0, MinWidth = 160 };
    private readonly ComboBox _printer = new() { MinWidth = 240 };
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 110 };
    private readonly CheckBox _duplex = new() { Content = "Duplex (long edge)", FontSize = 13 };
    private readonly NoticeHost _resultBar = new();
    private readonly DataGrid _jobs = Ui.Grid().With(_ => _.Height = 220);
    private TableHost _jobsHost = null!;
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
    }

    private Control Build()
    {
        // ---- primary interactive controls get automation names (voice control / screen readers) ----
        Ui.Ax(_req, "Requirement to print", "The requirement whose checklist is rendered");
        Ui.Ax(_variant, "Checklist variant", "Completed, pre-filled or blank");
        Ui.Ax(_printer, "Printer", "CUPS printer that receives the print submission");
        Ui.Ax(_copies, "Number of copies");
        Ui.Ax(_duplex, "Duplex printing", "Print double-sided, long-edge binding");
        Ui.Ax(_jobs, "Recent print jobs", "Select a job row to print it again with the same PDF");

        var sp = new StackPanel { Margin = new(32, 28), Spacing = 14, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.PrintCentre, "Print the current requirement checklist as a working paper document.", "Variants: blank, pre-filled with expected evidence, or completed with the current evidence register. Rendering is A4, greyscale-safe; paper output depends on the printer — a spooler acceptance does not guarantee the pages printed."));

        // ---- document ----
        var variantRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "Variant:", VerticalAlignment = VerticalAlignment.Center }, _variant },
        };
        sp.Children.Add(Ui.Section("Document", Ui.Col(8, _req, variantRow)));

        // ---- printer ----
        var printerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        printerRow.Children.Add(_printer);
        printerRow.Children.Add(Ui.GhostBtn("Refresh printers").With(b =>
        {
            b.Height = 32;
                        b.Click += (_, _) => { LoadPrinters(); _resultBar.Clear(); };
        }));
        printerRow.Children.Add(_duplex);
        printerRow.Children.Add(Ui.FormField("Copies", _copies));
        sp.Children.Add(Ui.Section("Printer", Ui.Col(8, printerRow, _printerLine)));

        // ---- remote assessment round pack (M7) ----
        // One button renders the whole surveillance round as a response pack: every REM
        // item with its verbatim ask, recorded response and linked evidence, ready to
        // email or print for the portal round. Independent of the Document picker above;
        // "Send to printer" and "Open PDF externally" below work on whatever rendered last.
        sp.Children.Add(Ui.Section("Remote assessment round pack", Ui.Col(10,
            Ui.Btn("Render round pack + preview").With(b =>
            {
                b.Height = 32;
                b.Click += async (_, _) => await RenderRoundPackAsync();
            }),
            Ui.Small("Composes every REM evidence request from the current surveillance round into one dated PDF: verbatim wording, expected evidence, recorded notes and next actions, linked evidence with review states, and a sign-off block."))));

        sp.Children.Add(_resultBar);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.PrimaryBtn("Render + preview").With(b => { b.Height = 32; b.Click += async (_, _) => await RenderAsync(); }));
        row.Children.Add(Ui.Btn("Send to printer").With(b => { b.Height = 32; b.Click += (_, _) => PrintLast(); }));
        row.Children.Add(Ui.GhostBtn("Open PDF externally").With(b => { b.Height = 32; b.Click += (_, _) =>
        {
            if (File.Exists(_lastRenderedPdf)) Helpers.OpenWithSystemViewer(_lastRenderedPdf);
                        else _resultBar.Show(NoticeTone.Info, "Render the document first.");
        }; }));
        Ui.WrapGaps(row);
        sp.Children.Add(row);

        // ---- recent print jobs ----
        _jobs.Columns.Add(Ui.Col("When", "When", 150));
        _jobs.Columns.Add(Ui.Col("Document", "Title", 300));
        _jobs.Columns.Add(Ui.Col("Printer", "Printer", 130));
        _jobs.Columns.Add(Ui.Col("Copies", "Copies", 70));
        _jobs.Columns.Add(Ui.Col("State", "State", 120));
        _jobs.Columns.Add(Ui.Col("Detail", "Detail", 280));
        _jobs.Columns.Add(Ui.Col("Id", "Id", 280));
        _jobs.ItemsSource = JobRows();

        sp.Children.Add(Ui.Section("Recent print jobs", Ui.Col(10, _jobs,
            Ui.Btn("Print again (selected job, same PDF)").With(b => { b.Height = 32; b.Click += (_, _) => ReprintSelected(); }),
            Ui.Small("Print again re-submits the previously rendered PDF with the recorded settings. Failed submissions are never retried automatically, because paper would come out twice if the first job actually printed. Printer presets, batch printing and binder templates land in Milestone 3."))));

        return new ScrollViewer { Content = sp };
    }

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
        // Display titles lead the picker (human-readable jobs); the ref stays for precision.
        _req.ItemsSource = _rows.Select(r => $"{r.Ref} · {r.DisplayTitleText}").ToList();
        _req.SelectedIndex = _rows.Count > 0 ? 0 : -1;
    }

    private void LoadPrinters()
    {
        var printers = App.Printer.DiscoverPrinters();
        var names = printers.Select(p => p.Name).ToList();
        _printer.ItemsSource = names;
        _printer.SelectedIndex = names.Count > 0 ? 0 : -1;
        // 3E #3: standing information is an inline callout line, not a permanent banner.
        _printerLine.Text = printers.Count == 0
            ? "No printers visible to CUPS (lpstat). You can still render and preview the PDF, or save it and print from another device."
            : $"{printers.Count} found via CUPS." + (printers.FirstOrDefault(p => p.IsDefault) is { } def ? $" Default: {def.Name}." : "");
    }

    private readonly TextBlock _printerLine = new()
    {
        Classes = { "callout" }, TextWrapping = TextWrapping.Wrap,
        Text = "Looking for CUPS printers…",
    };

    private async Task RenderAsync()
    {
                if (_rows.Count == 0) { _resultBar.Show(NoticeTone.Info, "No requirements imported yet — print needs a requirement to reference."); return; }
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
                    Title = $"{detail.Requirement.Ref} — {variant}",
                    SubjectId = detail.Requirement.Id,
                    PrinterName = _printer.SelectedItem as string ?? "",
                    Copies = (int)_copies.Value,
                    Duplex = _duplex.IsChecked == true,
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
                        _resultBar.Show(NoticeTone.Ok, "Rendered. The preview window shows the generated pages; 'Send to printer' submits the same file through CUPS.");
            LoadJobs();
            ShowPreview(App.Printer.RecentJobs(30).First(j => j.Id == _lastRenderedJob));
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
                if (items.Count == 0) { _resultBar.Show(NoticeTone.Info, "No surveillance (REM) requirements imported — nothing to pack."); return; }
        var round = App.Workspace.SurveillanceRound();
        var ph = App.Workspace.EnsurePharmacy();
        var summary = $"{round.Items} items · {round.WithEvidence} with evidence · {round.Verified} verified" +
            (round.NextEvent is null ? "" : $" · next event: {round.NextEvent}");
        var printerName = _printer.SelectedItem as string ?? "";   // UI-thread reads BEFORE Task.Run:
        var copies = (int)_copies.Value;                           // Avalonia controls are thread-affine.
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
                    Title = $"Remote assessment round pack — {items.Count} items",
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
            ShowPreview(App.Printer.RecentJobs(30).First(j => j.Id == _lastRenderedJob));
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
                if (string.IsNullOrWhiteSpace(printer)) { _resultBar.Show(NoticeTone.Info, "Choose a printer (none visible — refresh)."); return; }
        var outcome = App.Printer.SubmitToPrinter(_lastRenderedPdf, printer, (int)_copies.Value, _duplex.IsChecked == true);
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
                if (string.IsNullOrWhiteSpace(printer)) { _resultBar.Show(NoticeTone.Info, "Choose a printer."); return; }
        var outcome = App.Printer.SubmitToPrinter(pdf, printer, (int)_copies.Value, _duplex.IsChecked == true);
        var job = App.Db.PrintJobs.FirstOrDefault(j => j.Id == row.Id);
        if (job != null)
            App.Printer.UpdateJobState(row.Id, outcome.Ok ? PrintJobState.SentToPrinter : PrintJobState.Failed, outcome.detail + " (re-print)", "").GetAwaiter().GetResult();
                _resultBar.Show(outcome.Ok, outcome.detail);
        LoadJobs();
    }

    private void LoadJobs()
    {
        var current = _jobs.ItemsSource;
        _jobs.ItemsSource = JobRows();
        _jobsHost?.Refresh(JobRows().Count);
    }

    private void ShowPreview(PrintJob job)
    {
        try
        {
            var pdf = Path.Combine(AppData.PdfDir, job.PdfRelativePath);
            var win = new PreviewWindow(pdf);
            win.Show(App.Main!);
        }
        catch (Exception ex)
        {
                        _resultBar.Show(NoticeTone.Error, $"Preview could not open: {ex.Message}. Use 'Open PDF externally'.");
        }
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