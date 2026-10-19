using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Infrastructure.Backup;
using QsppManager.Infrastructure.Runtime;

namespace QsppManager.Desktop.Views;

/// <summary>Assessments: the guided preparation journey. Every phase states what it
/// counts, reads its numbers live from the workspace, and never presents a count as
/// official progress or the app as the accrediting body. Booking/cancelling happens
/// with the Guild — the app only helps you not be surprised.</summary>
public partial class AssessmentsView : UserControl
{
    private readonly DataGrid _events = new() { Height = 140, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 32 };
    private readonly DataGrid _findings = new() { Height = 150, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 32 };

    public AssessmentsView(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 14, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Assessments" });
        sp.Children.Add(new TextBlock { Classes = { "lead" },
            Text = "The preparation journey from first reading to the cycle's end. QSPP accreditation is awarded by the Guild's assessment process — this screen helps you arrive ready and keep the records that show it. Counts are live database values; what to do next is stated plainly." });

        Refresh(sp, shell);
        BuildEvents(sp);
        LoadEvents();
        Content = new ScrollViewer { Content = sp };
    }

    private void BuildEvents(StackPanel sp)
    {
        sp.Children.Add(new TextBlock { Text = "Assessment events timeline & findings", Classes = { "h3" }, Margin = new(0, 10, 0, 0) });
        sp.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Record each planned assessment and the outcome when it actually arrives — an outcome can never be marked received without the report or letter reference. Findings carry corrective actions; a finding cannot close empty." });
        _events.Columns.Add(new DataGridTextColumn { Header = "Kind", Binding = new Avalonia.Data.Binding("Kind"), Width = new DataGridLength(130) });
        _events.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new Avalonia.Data.Binding("Date"), Width = new DataGridLength(110) });
        _events.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(140) });
        _events.Columns.Add(new DataGridTextColumn { Header = "Outcome reference", Binding = new Avalonia.Data.Binding("Outcome"), Width = new DataGridLength(260) });
        _events.Columns.Add(new DataGridTextColumn { Header = "Note", Binding = new Avalonia.Data.Binding("Note"), Width = new DataGridLength(400) });
        sp.Children.Add(_events);
        _events.SelectionChanged += (_, _) => LoadFindings();
        var evRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        evRow.Children.Add(new Button { Content = "New event…", Classes = { "primary" }, Height = 34 }.With(b => b.Click += async (_, _) =>
        {
            var kind = await Dialogs.Prompt(this, "New assessment event", "Onsite, MidCycle, ReAccreditation or Other", "Onsite");
            if (string.IsNullOrWhiteSpace(kind)) return;
            var dateText = await Dialogs.Prompt(this, "New assessment event", "Scheduled date (yyyy-MM-dd)");
            if (!DateOnly.TryParse(dateText, out var date)) { _eventsMessage.Text = "Date must be yyyy-MM-dd."; return; }
            var note = await Dialogs.Prompt(this, "New assessment event", "Note (provider, booking ref…)") ?? "";
            var res = App.Workspace.AddAssessmentEvent(kind.Trim(), date, note, App.Session!);
            _eventsMessage.Text = res.Ok ? "Event recorded on the timeline." : res.Error;
            LoadEvents();
        }));
        evRow.Children.Add(new Button { Content = "Mark completed", Height = 34 }.With(b => b.Click += (_, _) =>
        {
            if (_events.SelectedItem is not EvRow r) { _eventsMessage.Text = "Select an event first."; return; }
            var res = App.Workspace.SetAssessmentEventState(r.Id, "Completed", "", App.Session!);
            _eventsMessage.Text = res.Ok ? "Marked completed." : res.Error;
            LoadEvents();
        }));
        evRow.Children.Add(new Button { Content = "Outcome received…", Height = 34 }.With(b => b.Click += async (_, _) =>
        {
            if (_events.SelectedItem is not EvRow r) { _eventsMessage.Text = "Select an event first."; return; }
            var refText = await Dialogs.Prompt(this, "Outcome received", "Report or letter reference (required)");
            var res = App.Workspace.SetAssessmentEventState(r.Id, "OutcomeReceived", refText ?? "", App.Session!);
            _eventsMessage.Text = res.Ok ? "Outcome recorded with its reference — the closure evidence the program expects." : res.Error;
            LoadEvents();
        }));
        sp.Children.Add(evRow);

        sp.Children.Add(new TextBlock { Text = "Findings (selected event)", Classes = { "h3" }, Margin = new(0, 6, 0, 0) });
        _findings.Columns.Add(new DataGridTextColumn { Header = "Requirement", Binding = new Avalonia.Data.Binding("ReqRef"), Width = new DataGridLength(120) });
        _findings.Columns.Add(new DataGridTextColumn { Header = "Severity", Binding = new Avalonia.Data.Binding("Severity"), Width = new DataGridLength(100) });
        _findings.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new Avalonia.Data.Binding("Desc"), Width = new DataGridLength(430) });
        _findings.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(90) });
        _findings.Columns.Add(new DataGridTextColumn { Header = "Corrective action", Binding = new Avalonia.Data.Binding("Action"), Width = new DataGridLength(280) });
        sp.Children.Add(_findings);
        var fRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fRow.Children.Add(new Button { Content = "Record finding…", Height = 34 }.With(b => b.Click += async (_, _) =>
        {
            if (_events.SelectedItem is not EvRow ev) { _eventsMessage.Text = "Select an event first."; return; }
            var desc = await Dialogs.Prompt(this, "Record finding", "What was found");
            if (string.IsNullOrWhiteSpace(desc)) return;
            var reqRef = await Dialogs.Prompt(this, "Record finding", "Related requirement ref (optional, e.g. 2.2.3.8)") ?? "";
            var sev = await Dialogs.Prompt(this, "Record finding", "Severity (free text — the program defines no taxonomy)") ?? "";
            var res = App.Workspace.AddFinding(ev.Id, reqRef, desc, sev, App.Session!.UserId, App.Session);
            _eventsMessage.Text = res.Ok ? "Finding recorded — it stays open until a corrective action closes it." : res.Error;
            LoadEvents();
        }));
        fRow.Children.Add(new Button { Content = "Close finding…", Height = 34 }.With(b => b.Click += async (_, _) =>
        {
            if (_findings.SelectedItem is not FRow f) { _eventsMessage.Text = "Select a finding first."; return; }
            var action = await Dialogs.Prompt(this, "Close finding", "Corrective action (required to close)");
            var res = App.Workspace.SetFindingState(f.Id, "Closed", action ?? "", App.Session!);
            _eventsMessage.Text = res.Ok ? "Finding closed with its corrective action on record." : res.Error;
            LoadEvents();
        }));
        sp.Children.Add(fRow);
        _eventsMessage = new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
        sp.Children.Add(_eventsMessage);

        // ---- assessor pack ----
        sp.Children.Add(new TextBlock { Text = "Assessor pack export", Classes = { "h3" }, Margin = new(0, 10, 0, 0) });
        sp.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "One checklist per manager-verified/applicable requirement plus the verified evidence that passes the redaction review, with a tamper-evident manifest (per-file sha256). Patient-data evidence without a redaction attestation is excluded and the exclusion is recorded in the manifest — decide with the manager whether to redact and re-import instead." });
        sp.Children.Add(new Button
        {
            Content = "Export assessor pack…", Classes = { "primary" }, Height = 36, MinWidth = 200,
        }.With(b => b.Click += async (_, _) =>
        {
            var label = await Dialogs.Prompt(this, "Assessor pack", "Label for this export (optional)") ?? "";
            var svc = new AssessorPackService(App.Workspace, App.Db, App.Composer, App.Store, AppData.PackDir);
            var (ok, zipPath, error, reqCount, files, exclusions) = svc.Export(App.Session!, label);
            if (!ok) { _eventsMessage.Text = $"Assessor pack failed: {error}"; return; }
            var note = exclusions.Count > 0 ? $"\nREDACTION EXCLUSIONS ({exclusions.Count}):\n- " + string.Join("\n- ", exclusions.Take(6)) : "\nNo redaction exclusions.";
            _eventsMessage.Text = $"Pack exported: {zipPath}\n{reqCount} requirement checklist(s), {files} evidence file(s).{note}";
            Helpers.OpenWithSystemViewer(zipPath);
        }));
    }

    private TextBlock _eventsMessage = new();

    private void LoadEvents()
    {
        _events.ItemsSource = App.Workspace.AssessmentEvents().Select(e => new EvRow(
            e.Id, e.Kind, e.ScheduledDate.ToString("dd MMM yyyy"), e.State, e.OutcomeRef, e.Note)).ToList();
        LoadFindings();
    }

    private void LoadFindings()
    {
        if (_events.SelectedItem is EvRow ev)
            _findings.ItemsSource = App.Workspace.FindingsFor(ev.Id).Select(f => new FRow(
                f.Id, f.RequirementRef, f.Severity, f.Description, f.State, f.CorrectiveAction)).ToList();
        else
            _findings.ItemsSource = new List<FRow>();
    }

    public record EvRow(Guid Id, string Kind, string Date, string State, string Outcome, string Note);
    public record FRow(Guid Id, string ReqRef, string Severity, string Desc, string State, string Action);

    private void Refresh(StackPanel sp, ShellViewModel shell)
    {
        var j = App.Workspace.Journey();
        var noOnsite = j.OnsiteAssessmentDate is null;
        var noExpiry = j.AccreditationExpiryDate is null;

        // Diagnostic strip: the four numbers that get pharmacies caught out.
        sp.Children.Add(new Border
        {
            Padding = new(14, 10), Background = Helpers.Brush("Brush.Sage"),
            BorderBrush = Helpers.Brush("Brush.Divider"), BorderThickness = new(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 26, Children =
            {
                Mini("Overdue tasks", j.OverdueTasks.ToString()),
                Mini("Evidence awaiting review", j.EvidenceAwaitingReview.ToString()),
                Mini("Open interpretation issues", j.UnresolvedIssues.ToString()),
                Mini("Accreditation verified", $"{j.ManagerVerified} req."),
            } },
        });

        Phase(sp, shell, "1 — Understand the program",
            $"The corpus holds {j.RequirementsTotal} verified records from the Standard, UAG, Program Rules, T&Cs, policies and the readiness hitlist. {j.TotalIssues} interpretation questions are tracked; {j.UnresolvedIssues} remain open and {j.AcknowledgedIssues} are acknowledged.",
            "Read the Knowledge Hub and the UAG. Where sources disagree, the app uses the conservative reading for planning — settle them in the Library with a recorded resolution.",
            ("Open Library", ShellPage.Library));

        Phase(sp, shell, "2 — Decide applicability",
            $"{j.ApplicabilityDecided} of {j.RequirementsTotal} requirements have a recorded applicability decision (with the reason you gave).",
            "Work through Requirements and set Applicable / Not applicable with a reason for each — future staff and assessors will rely on it.",
            ("Open Requirements", ShellPage.Requirements));

        Phase(sp, shell, "3 — Assign owners",
            $"{j.OwnedCount} requirements have a named owner. Blank owners are how responsibilities silently vanish.",
            "Assign a person to every applicable requirement, and give each one a live 'next action'.",
            ("Open Requirements", ShellPage.Requirements));

        Phase(sp, shell, "4 — Gather evidence",
            $"{j.RequirementsWithEvidence} requirements currently have at least one active or proposed evidence link. {j.EvidenceAwaitingReview} evidence items are waiting for review — manager verification is what activates links.",
            "Attach documents in Evidence & documents, link each to the requirement it supports, and let the manager verify them.",
            ("Open Evidence & documents", ShellPage.Documents));

        Phase(sp, shell, "5 — Internal readiness",
            $"{j.ManagerVerified} requirements are manager-verified, {j.InProgress} in progress, {j.NotStarted} started but not advanced. The readiness report (M4) will print the full picture; the print centre already produces per-requirement evidence checklists.",
            "Run your own internal review before the Guild sees it: print checklists in the Print centre and walk the pharmacy.",
            ("Open Print centre", ShellPage.PrintCentre));

        if (noOnsite)
            Phase(sp, shell, "6 — Book the onsite assessment",
                "No assessment date recorded yet. Book with the assessment services provider (EY), then record the date under Settings — the app derives the conservative cancellation cutoff (10 business days, planning-only while the sources disagree).",
                "After booking, enter the date in Settings; deadlines appears on Today immediately.",
                ("Open Settings", ShellPage.Settings));
        else
            Phase(sp, shell, "6 — Onsite assessment booked",
                $"Assessment recorded for {j.OnsiteAssessmentDate:dd MMM yyyy}. The conservative cancellation cutoff and the planning-only mid-cycle window are on Today. Changes or cancellations are made with the provider; the 10-day fee window applies under their rules.",
                "Keep evidence current until the assessor arrives; the audit trail is your activity log.",
                ("Open Today", ShellPage.Today));

        if (noExpiry)
            Phase(sp, shell, "7 — After the assessment",
                "No accreditation expiry recorded. When the outcome arrives, record assessor-accepted status per requirement (with the report reference) under Requirements, and set the expiry + cycle start dates in Settings.",
                "The three-year cycle, mid-cycle review window and annual attestation dates follow from the dates you record.",
                ("Open Settings", ShellPage.Settings));
        else
            Phase(sp, shell, "7 — Cycle in progress",
                $"Accreditation expires {j.AccreditationExpiryDate:dd MMM yyyy}. Rule 5 requires re-accreditation assessment to occur within the 3 calendar months BEFORE expiry; Today shows the derived preparation date.",
                "Keep the cycle clocks honest: update Settings if dates change.",
                ("Open Today", ShellPage.Today));

        sp.Children.Add(new TextBlock { Classes = { "small" }, Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "Counts are live database queries — they never claim more than recorded, and the app never claims to award accreditation." });
    }

    private static Control Mini(string label, string value) => new StackPanel { Spacing = 2, Children =
    {
        new TextBlock { Classes = { "h2" }, Text = value },
        new TextBlock { Classes = { "small" }, Text = label },
    } };

    private static void Phase(StackPanel sp, ShellViewModel shell, string title, string status, string advice, (string label, ShellPage page) button)
    {
        sp.Children.Add(new Border
        {
            BorderBrush = Helpers.Brush("Brush.Divider"), BorderThickness = new(0, 0, 0, 1),
            Padding = new(0, 10),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new StackPanel { Spacing = 4, Children =
                    {
                        new TextBlock { Text = title, Classes = { "h3" } },
                        new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Classes = { "small" }, MaxWidth = 780 },
                        new TextBlock { Text = advice, TextWrapping = TextWrapping.Wrap, Classes = { "small" }, MaxWidth = 780, Opacity = 0.85 },
                    } },
                    new Button { Content = button.label, Height = 34, MinWidth = 150, VerticalAlignment = VerticalAlignment.Top,
                        Command = new SimpleNav(shell, button.page) },
                },
            },
        });
    }

    private sealed class SimpleNav(ShellViewModel shell, ShellPage page) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            var nav = shell.NavItems.FirstOrDefault(n => n.Page == page);
            if (nav != null) shell.SelectedNav = nav;
        }
    }
}