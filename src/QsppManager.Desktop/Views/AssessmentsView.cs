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
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 14, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.Assessments, "The preparation journey from first reading to the cycle's end.", "QSPP accreditation is awarded by the Guild's assessment process — this screen helps you arrive ready and keep the records that show it. Counts are live database values; what to do next is stated plainly."));
        sp.Children.Add(_resultBar);

        Refresh(sp, shell);
        BuildEvents(sp);
        LoadEvents();
        Content = new ScrollViewer { Content = sp };
    }

    private void BuildEvents(StackPanel sp)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Record each planned assessment and the outcome when it actually arrives — an outcome can never be marked received without the report or letter reference. Findings carry corrective actions; a finding cannot close empty." });
        _events.Columns.Add(Ui.Col("Kind", "Kind", 130));
        _events.Columns.Add(Ui.Col("Date", "Date", 110));
        _events.Columns.Add(Ui.Col("State", "State", 140));
        _events.Columns.Add(Ui.Col("Outcome reference", "Outcome", 260));
        _events.Columns.Add(Ui.Col("Note", "Note", 400));
        Ui.Ax(_events, "Assessment events", "Timeline of planned and completed accreditation assessment events");
        body.Children.Add(_events);
        _events.SelectionChanged += (_, _) => LoadFindings();
        var evRow = Ui.BtnRow(
            Ui.PrimaryBtn("New event…").With(b => b.Click += async (_, _) =>
            {
                var kind = await Dialogs.Prompt(this, "New assessment event", "Onsite, MidCycle, ReAccreditation or Other", "Onsite");
                if (string.IsNullOrWhiteSpace(kind)) return;
                var date = await Dialogs.PromptDate(this, "New assessment event", "Scheduled date", required: true);
                if (date is null) return;
                var note = await Dialogs.Prompt(this, "New assessment event", "Note (provider, booking ref…)") ?? "";
                var res = App.Workspace.AddAssessmentEvent(kind.Trim(), date.Value, note, App.Session!);
                                _resultBar.Show(res.Ok, "Event recorded on the timeline.", res.Error);
                LoadEvents();
            }),
            Ui.Btn("Mark completed").With(b => b.Click += (_, _) =>
            {
                                if (_events.SelectedItem is not EvRow r) { _resultBar.Show(NoticeTone.Info, "Select an event first."); return; }
                var res = App.Workspace.SetAssessmentEventState(r.Id, "Completed", "", App.Session!);
                                _resultBar.Show(res.Ok, "Marked completed.", res.Error);
                LoadEvents();
            }),
            Ui.Btn("Outcome received…").With(b => b.Click += async (_, _) =>
            {
                                if (_events.SelectedItem is not EvRow r) { _resultBar.Show(NoticeTone.Info, "Select an event first."); return; }
                var refText = await Dialogs.Prompt(this, "Outcome received", "Report or letter reference (required)");
                var res = App.Workspace.SetAssessmentEventState(r.Id, "OutcomeReceived", refText ?? "", App.Session!);
                                _resultBar.Show(res.Ok, "Outcome recorded with its reference — the closure evidence the program expects.", res.Error);
                LoadEvents();
            }));
        body.Children.Add(evRow);

        body.Children.Add(new TextBlock { Text = "Findings (selected event)", Classes = { "h3" } });
        _findings.Columns.Add(Ui.Col("Requirement", "ReqRef", 120));
        _findings.Columns.Add(Ui.Col("Severity", "Severity", 100));
        _findings.Columns.Add(Ui.Col("Description", "Desc", 430));
        _findings.Columns.Add(Ui.Col("State", "State", 90));
        _findings.Columns.Add(Ui.Col("Corrective action", "Action", 280));
        Ui.Ax(_findings, "Findings", "Assessment findings attached to the selected event");
        body.Children.Add(_findings);
        var fRow = Ui.BtnRow(
            Ui.PrimaryBtn("Record finding…").With(b => b.Click += async (_, _) =>
            {
                                if (_events.SelectedItem is not EvRow ev) { _resultBar.Show(NoticeTone.Info, "Select an event first."); return; }
                var desc = await Dialogs.Prompt(this, "Record finding", "What was found");
                if (string.IsNullOrWhiteSpace(desc)) return;
                var reqRef = await Dialogs.Prompt(this, "Record finding", "Related requirement ref (optional, e.g. 2.2.3.8)") ?? "";
                var sev = await Dialogs.Prompt(this, "Record finding", "Severity (free text — the program defines no taxonomy)") ?? "";
                var res = App.Workspace.AddFinding(ev.Id, reqRef, desc, sev, App.Session!.UserId, App.Session);
                                _resultBar.Show(res.Ok, "Finding recorded — it stays open until a corrective action closes it.", res.Error);
                LoadEvents();
            }),
            Ui.Btn("Close finding…").With(b => b.Click += async (_, _) =>
            {
                                if (_findings.SelectedItem is not FRow f) { _resultBar.Show(NoticeTone.Info, "Select a finding first."); return; }
                var action = await Dialogs.Prompt(this, "Close finding", "Corrective action (required to close)");
                var res = App.Workspace.SetFindingState(f.Id, "Closed", action ?? "", App.Session!);
                                _resultBar.Show(res.Ok, "Finding closed with its corrective action on record.", res.Error);
                LoadEvents();
            }));
        body.Children.Add(fRow);
        sp.Children.Add(Ui.Section("Assessment events timeline & findings", body));

        // ---- assessor pack ----
        var pack = new StackPanel { Spacing = 10 };
        pack.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "One checklist per manager-verified/applicable requirement plus the verified evidence that passes the redaction review, with a tamper-evident manifest (per-file sha256). Patient-data evidence without a redaction attestation is excluded and the exclusion is recorded in the manifest — decide with the manager whether to redact and re-import instead." });
        pack.Children.Add(Ui.PrimaryBtn("Export assessor pack…", minWidth: 200).With(b => b.Click += async (_, _) =>
        {
            var label = await Dialogs.Prompt(this, "Assessor pack", "Label for this export (optional)") ?? "";
            var svc = new AssessorPackService(App.Workspace, App.Db, App.Composer, App.Store, AppData.PackDir);
            var (ok, zipPath, error, reqCount, files, exclusions) = svc.Export(App.Session!, label);
                        if (!ok) { _resultBar.Show(NoticeTone.Error, $"Assessor pack failed: {error}"); return; }
            var note = exclusions.Count > 0 ? $"\nREDACTION EXCLUSIONS ({exclusions.Count}):\n- " + string.Join("\n- ", exclusions.Take(6)) : "\nNo redaction exclusions.";
                        _resultBar.Show(NoticeTone.Info, $"Pack exported: {zipPath}\n{reqCount} requirement checklist(s), {files} evidence file(s).{note}");
            Helpers.OpenWithSystemViewer(zipPath);
        }));
        sp.Children.Add(Ui.Section("Assessor pack export", pack));
    }

    private readonly NoticeHost _resultBar = new();

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
        _currentAssigned = false;   // current-phase marker resets per refresh

        // 3F #6: the numbers that get pharmacies caught out — stat counts in meaning hues
        // only when they need attention; zeros stay muted (3D #7).
        var metrics = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 34, Margin = new(0, 2, 0, 4) };
        metrics.Children.Add(StatCount("Overdue tasks", j.OverdueTasks, j.OverdueTasks > 0 ? "Brush.Red" : "Brush.Muted", j.OverdueTasks > 0 ? "" : "none"));
        metrics.Children.Add(StatCount("Awaiting review", j.EvidenceAwaitingReview, j.EvidenceAwaitingReview > 0 ? "Brush.Violet" : "Brush.Muted", j.EvidenceAwaitingReview > 0 ? "" : "none"));
        metrics.Children.Add(StatCount("Open interpretations", j.UnresolvedIssues, j.UnresolvedIssues > 0 ? "Brush.Ochre" : "Brush.Muted", j.UnresolvedIssues > 0 ? "" : "none"));
        metrics.Children.Add(StatCount("Accreditation verified", j.ManagerVerified, j.ManagerVerified > 0 ? "Brush.Green" : "Brush.Muted", j.ManagerVerified > 0 ? "" : "none"));
        sp.Children.Add(metrics);

        Phase(sp, shell, 1, "Understand the program", j.RequirementsTotal > 0,
            $"The corpus holds {j.RequirementsTotal} verified records from the Standard, UAG, Program Rules, T&Cs, policies and the readiness hitlist. {j.TotalIssues} interpretation questions are tracked; {j.UnresolvedIssues} remain open and {j.AcknowledgedIssues} are acknowledged.",
            "Read the Knowledge Hub and the UAG. Where sources disagree, the app uses the conservative reading for planning — settle them in the Library with a recorded resolution.",
            ("Open Library", ShellPage.Library));

        Phase(sp, shell, 2, "Decide applicability", j.ApplicabilityDecided >= j.RequirementsTotal,
            $"{j.ApplicabilityDecided} of {j.RequirementsTotal} requirements have a recorded applicability decision (with the reason you gave).",
            "Work through Requirements and set Applicable / Not applicable with a reason for each — future staff and assessors will rely on it.",
            ("Open Requirements", ShellPage.Requirements));

        Phase(sp, shell, 3, "Assign owners", j.OwnedCount >= j.ApplicabilityDecided && j.ApplicabilityDecided > 0,
            $"{j.OwnedCount} requirements have a named owner. Blank owners are how responsibilities silently vanish.",
            "Assign a person to every applicable requirement, and give each one a live 'next action'.",
            ("Open Requirements", ShellPage.Requirements));

        Phase(sp, shell, 4, "Gather evidence", j.ManagerVerified > 0 && j.EvidenceAwaitingReview == 0,
            $"{j.RequirementsWithEvidence} requirements currently have at least one active or proposed evidence link. {j.EvidenceAwaitingReview} evidence items are waiting for review — manager verification is what activates links.",
            "Attach documents in Evidence & documents, link each to the requirement it supports, and let the manager verify them.",
            ("Open Evidence & documents", ShellPage.Documents));

        Phase(sp, shell, 5, "Internal readiness", false,
            $"{j.ManagerVerified} requirements are manager-verified, {j.InProgress} in progress, {j.NotStarted} started but not advanced. The readiness report (M4) will print the full picture; the print centre already produces per-requirement evidence checklists.",
            "Run your own internal review before the Guild sees it: print checklists in the Print centre and walk the pharmacy.",
            ("Open Print centre", ShellPage.PrintCentre));

        if (noOnsite)
            Phase(sp, shell, 6, "Book the onsite assessment", false,
                "No assessment date recorded yet. Book with the assessment services provider (EY), then record the date under Settings — the app derives the conservative cancellation cutoff (10 business days, planning-only while the sources disagree).",
                "After booking, enter the date in Settings; deadlines appears on Today immediately.",
                ("Open Settings", ShellPage.Settings));
        else
            Phase(sp, shell, 6, "Onsite assessment booked", true,
                $"Assessment recorded for {j.OnsiteAssessmentDate:dd MMM yyyy}. The conservative cancellation cutoff and the planning-only mid-cycle window are on Today. Changes or cancellations are made with the provider; the 10-day fee window applies under their rules.",
                "Keep evidence current until the assessor arrives; the audit trail is your activity log.",
                ("Open Today", ShellPage.Today));

        if (noExpiry)
            Phase(sp, shell, 7, "After the assessment", false,
                "No accreditation expiry recorded. When the outcome arrives, record assessor-accepted status per requirement (with the report reference) under Requirements, and set the expiry + cycle start dates in Settings.",
                "The three-year cycle, mid-cycle review window and annual attestation dates follow from the dates you record.",
                ("Open Settings", ShellPage.Settings));
        else
            Phase(sp, shell, 7, "Cycle in progress", true,
                $"Accreditation expires {j.AccreditationExpiryDate:dd MMM yyyy}. Rule 5 requires re-accreditation assessment to occur within the 3 calendar months BEFORE expiry; Today shows the derived preparation date.",
                "Keep the cycle clocks honest: update Settings if dates change.",
                ("Open Today", ShellPage.Today));

        sp.Children.Add(new TextBlock { Classes = { "small" }, Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "Counts are live database queries — they never claim more than recorded, and the app never claims to award accreditation." });
    }

    /// <summary>Stat count for the metrics row (3C stat + 3D #7 meaning hue; "none" mutes).</summary>
    private static Control StatCount(string label, int value, string hueBrush, string none) =>
        new StackPanel { Spacing = 2, Children =
        {
            new TextBlock { Classes = { "stat" }, Text = value.ToString(), Foreground = Ui.Br(none == "none" ? "Brush.Muted" : hueBrush) },
            new TextBlock { Classes = { "callout" }, Text = label },
        } };

    private static bool _currentAssigned;

    /// <summary>One journey phase (3F #6): a 24pt step badge (done = forest fill with a
    /// check, current = violet ring with the number, later = stone hairline), a plain
    /// title without the "1 —" prefix, its fact line, the advice behind "More", and a
    /// chevron text link instead of a boxed button.</summary>
    private static void Phase(StackPanel sp, ShellViewModel shell, int num, string title, bool done,
        string status, string advice, (string label, ShellPage page) button)
    {
        string badgeState;
        if (!_currentAssigned)
        {
            badgeState = done ? "done" : "current";
            if (!done) _currentAssigned = true;
        }
        else
        {
            badgeState = "later";
        }

        var badge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new(12),
            VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 2, 0, 0),
        };
        if (badgeState == "done")
        {
            badge.Background = Ui.Br("Brush.Green");
            badge.Child = new Avalonia.Controls.Shapes.Path
            {
                Stroke = Ui.Br("Brush.Paper"), StrokeThickness = 2,
                Data = Avalonia.Media.Geometry.Parse("M 7,12.5 L 10.5,16 L 17,9.5"),
            };
        }
        else if (badgeState == "current")
        {
            badge.Background = Brushes.Transparent;
            badge.BorderBrush = Ui.Br("Brush.Violet");
            badge.BorderThickness = new(1.6);
            badge.Child = new TextBlock
            {
                Text = num.ToString(), FontSize = 11, FontWeight = FontWeight.Medium,
                Foreground = Ui.Br("Brush.Violet"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            badge.Background = Brushes.Transparent;
            badge.BorderBrush = Ui.Br("Brush.Divider");
            badge.BorderThickness = new(1);
            badge.Child = new TextBlock
            {
                Text = num.ToString(), FontSize = 11,
                Foreground = Ui.Br("Brush.Muted"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var fact = new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Classes = { "body" } };
        var adviceText = new TextBlock { Text = advice, TextWrapping = TextWrapping.Wrap, Classes = { "callout" }, IsVisible = false };
        var more = new Avalonia.Controls.Button
        {
            Classes = { "ghost" }, Padding = new(0), Height = 20,
            Content = new TextBlock { Text = "More", Classes = { "callout" }, Foreground = Ui.Br("Brush.Green") },
        };
        more.Click += (_, _) => adviceText.IsVisible = !adviceText.IsVisible;
        var link = new Avalonia.Controls.Button
        {
            Classes = { "ghost" }, Padding = new(0), Height = 24,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 2,
                Children =
                {
                    new TextBlock { Text = button.label, Classes = { "body" }, Foreground = Ui.Br("Brush.Green") },
                    new Avalonia.Controls.Shapes.Path
                    {
                        Stroke = Ui.Br("Brush.Green"), StrokeThickness = 1.6,
                        Data = Avalonia.Media.Geometry.Parse("M 7,4 L 11,8 L 7,12"),
                        Width = 14, Height = 16, VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        link.Command = new SimpleNav(shell, button.page);

        var textCol = new StackPanel { Spacing = 4 };
        textCol.Children.Add(new TextBlock
        {
            Text = title,
            Classes = { "h3" },
            Foreground = badgeState == "current" ? Ui.Br("Brush.Violet") : Ui.Br("Brush.Ink"),
        });
        textCol.Children.Add(fact);
        textCol.Children.Add(more);
        textCol.Children.Add(adviceText);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,220"),
            ColumnSpacing = 12,
        };
        grid.Children.Add(badge);
        grid.Children.Add(textCol);
        grid.Children.Add(link);
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(textCol, 1);
        Grid.SetColumn(link, 2);
        link.VerticalAlignment = VerticalAlignment.Top;

        sp.Children.Add(new Border
        {
            BorderBrush = Ui.Br("Brush.Divider"), BorderThickness = new(0, 0, 0, 1),
            Padding = new(0, 10),
            Child = grid,
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