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
    private readonly TextBlock _status = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _jobs = new() { Height = 220, AutoGenerateColumns = false, IsReadOnly = true };
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
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 14, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Print centre" });
        sp.Children.Add(new TextBlock
        {
            Classes = { "lead" }, TextWrapping = TextWrapping.Wrap,
            Text = "Print the current requirement checklist as a working paper document: blank, pre-filled with expected evidence, or completed with the current evidence register. Rendering is A4, greyscale-safe; paper output depends on the printer — a spooler acceptance does not guarantee the pages printed.",
        });

        sp.Children.Add(new TextBlock { Text = "Document", Classes = { "h3" } });
        sp.Children.Add(_req);
        sp.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "Variant:", VerticalAlignment = VerticalAlignment.Center }, _variant } });

        sp.Children.Add(new TextBlock { Text = "Printer", Classes = { "h3" } });
        var printerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        printerRow.Children.Add(_printer);
        printerRow.Children.Add(new Button { Content = "Refresh printers", Height = 32 }.With(_ => _.Click += (_, _) => { LoadPrinters(); _status.Text = ""; }));
        printerRow.Children.Add(_duplex);
        printerRow.Children.Add(new StackPanel { Children = { new TextBlock { Classes = { "label" }, Text = "COPIES" }, _copies } });
        sp.Children.Add(printerRow);
        sp.Children.Add(_status);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Button { Content = "Render + preview", Classes = { "primary" }, Height = 36 }.With(_ => _.Click += async (_, _) => await RenderAsync()));
        row.Children.Add(new Button { Content = "Send to printer", Height = 36 }.With(_ => _.Click += (_, _) => PrintLast()));
        row.Children.Add(new Button { Content = "Open PDF externally", Height = 36 }.With(_ => _.Click += (_, _) =>
        {
            if (File.Exists(_lastRenderedPdf)) Helpers.OpenWithSystemViewer(_lastRenderedPdf);
            else _status.Text = "Render the document first.";
        }));
        sp.Children.Add(row);

        sp.Children.Add(new TextBlock { Text = "Recent print jobs", Classes = { "h3" }, Margin = new(0, 8, 0, 0) });
        _jobs.Columns.Add(new DataGridTextColumn { Header = "When", Binding = new Avalonia.Data.Binding("When"), Width = new DataGridLength(150) });
        _jobs.Columns.Add(new DataGridTextColumn { Header = "Document", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(300) });
        _jobs.Columns.Add(new DataGridTextColumn { Header = "Printer", Binding = new Avalonia.Data.Binding("Printer"), Width = new DataGridLength(130) });
        _jobs.Columns.Add(new DataGridTextColumn { Header = "Copies", Binding = new Avalonia.Data.Binding("Copies"), Width = new DataGridLength(70) });
        _jobs.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(120) });
        _jobs.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new Avalonia.Data.Binding("Detail"), Width = new DataGridLength(280) });
        _jobs.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new Avalonia.Data.Binding("Id"), Width = new DataGridLength(280) });
        _jobs.ItemsSource = JobRows();
        sp.Children.Add(_jobs);
        sp.Children.Add(new Button { Content = "Print again (selected job, same PDF)", Height = 32 }.With(_ => _.Click += (_, _) => ReprintSelected()));
        sp.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Text = "Print again re-submits the previously rendered PDF with the recorded settings. Failed submissions are never retried automatically, because paper would come out twice if the first job actually printed. Printer presets, batch printing and binder templates land in Milestone 3." });

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
        _req.ItemsSource = _rows.Select(r => $"{r.Ref} — {r.Title}").ToList();
        _req.SelectedIndex = _rows.Count > 0 ? 0 : -1;
    }

    private void LoadPrinters()
    {
        var printers = App.Printer.DiscoverPrinters();
        var names = printers.Select(p => p.Name).ToList();
        _printer.ItemsSource = names;
        _printer.SelectedIndex = names.Count > 0 ? 0 : -1;
        _status.Text = printers.Count == 0
            ? "No printers visible to CUPS (lpstat). You can still render and preview the PDF, or save it and print from another device."
            : $"{printers.Count} printer(s) found via CUPS." + (printers.FirstOrDefault(p => p.IsDefault) is { } def ? $" Default: {def.Name}." : "");
    }

    private async Task RenderAsync()
    {
        if (_rows.Count == 0) { _status.Text = "No requirements imported yet — print needs a requirement to reference."; return; }
        var row = _rows.ElementAtOrDefault(Math.Max(_req.SelectedIndex, 0));
        if (row == null) { _status.Text = "Choose a requirement first."; return; }
        var detail = App.Workspace.RequirementDetail(row.Id, App.Session);
        if (detail == null) { _status.Text = "Requirement details could not be loaded."; return; }

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
        if (detail.Requirement.AnchorApproximate)
            anchorNote += " · line anchor approximate (provisional)";

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
            _status.Text = "Rendered. The preview window shows the generated pages; 'Send to printer' submits the same file through CUPS.";
            LoadJobs();
            ShowPreview(App.Printer.RecentJobs(30).First(j => j.Id == _lastRenderedJob));
        }
        catch (Exception ex)
        {
            _status.Text = $"Rendering failed: {ex.Message}. Nothing was queued and no state was changed.";
        }
    }

    private void PrintLast()
    {
        if (_lastRenderedJob == Guid.Empty || !File.Exists(_lastRenderedPdf))
        { _status.Text = "Render a document first."; return; }
        var printer = _printer.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(printer)) { _status.Text = "Choose a printer (none visible — refresh)."; return; }
        var outcome = App.Printer.SubmitToPrinter(_lastRenderedPdf, printer, (int)_copies.Value, _duplex.IsChecked == true);
        var state = outcome.Ok ? PrintJobState.SentToPrinter : PrintJobState.Failed;
        App.Printer.UpdateJobState(_lastRenderedJob, state, outcome.detail, "").GetAwaiter().GetResult();
        _status.Text = outcome.detail;
        LoadJobs();
    }

    private void ReprintSelected()
    {
        if (_jobs.SelectedItem is not JobRow row) { _status.Text = "Select a job row."; return; }
        var pdf = Path.Combine(AppData.PdfDir, $"job-{row.Id}.pdf");
        if (!File.Exists(pdf)) { _status.Text = "The rendered PDF for this job is gone (cleaned up). Render again instead."; return; }
        var printer = _printer.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(printer)) { _status.Text = "Choose a printer."; return; }
        var outcome = App.Printer.SubmitToPrinter(pdf, printer, (int)_copies.Value, _duplex.IsChecked == true);
        var job = App.Db.PrintJobs.FirstOrDefault(j => j.Id == row.Id);
        if (job != null)
            App.Printer.UpdateJobState(row.Id, outcome.Ok ? PrintJobState.SentToPrinter : PrintJobState.Failed, outcome.detail + " (re-print)", "").GetAwaiter().GetResult();
        _status.Text = outcome.detail;
        LoadJobs();
    }

    private void LoadJobs()
    {
        var current = _jobs.ItemsSource;
        _jobs.ItemsSource = JobRows();
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
            _status.Text = $"Preview could not open: {ex.Message}. Use 'Open PDF externally'.";
        }
    }
}