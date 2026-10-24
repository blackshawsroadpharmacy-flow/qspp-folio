using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;   // M7.2: ScrollBarVisibility for the AutoGrow helper
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Application;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

/// <summary>The flagship requirements workspace: corpus list on the left, working detail on
/// the right. Detail = Overview / Evidence / Tasks / Notes tabs. State transitions and role
/// checks live in QsppWorkspace; this view only collects intent and shows results.</summary>
public partial class RequirementsView : UserControl, IPageSearchFocus
{
    private readonly ShellViewModel _shell;
    private readonly ObservableCollection<RequirementItem> _items = [];
    private RequirementDetail? _detail;
    private DataGrid? _evidenceGrid;
    private NoticeHost? _resultBar;
    private TextBox? _notesBox;
    private string? _currentDetailId;

    public RequirementsView(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        LoadList();
        ToolTip.SetTip(BulkAssign, "One click marks you (the signed-in user) as the owner of every requirement that has none. Audit-logged as a single entry.");
        BulkAssign.Click += (_, _) =>
        {
            if (App.Session is not { ReadOnly: false } session) return;
            var changed = App.Workspace.AssignAllOwnersToUser(session.UserId, session);
            LoadList();
            ShowResult(changed > 0 ? $"Owner recorded on {changed} requirement{(changed == 1 ? "" : "s")}." :
                "No unassigned requirements left.");
            if (changed > 0 && Guid.TryParse(_currentDetailId, out var claimed)) LoadDetail(claimed);
        };
        FilterBox.TextChanged += (_, _) => LoadList();
        ReqList.SelectionChanged += (_, _) =>
        {
            if (ReqList.SelectedItem is RequirementItem r)
                LoadDetail(r.Id);
        };
        var first = _items.FirstOrDefault(i => i.RefKind == RefKind.Hitlist) ?? _items.FirstOrDefault();
        if (first != null) ReqList.SelectedItem = first;
    }

    /// <summary>Palette/search deep link: select a requirement by id.</summary>
    public void OpenById(Guid id)
    {
        if (_items.All(i => i.Id != id)) LoadList();
        var row = _items.FirstOrDefault(i => i.Id == id);
        if (row != null) ReqList.SelectedItem = row;
    }

    /// <summary>Command-palette / ⌘F hook: focus the filter and route a preset filter.</summary>
    public void FocusSearch()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }
    public void SetFilter(string text) => FilterBox.Text = text;

    private void LoadList()
    {
        _items.Clear();
        var filter = FilterBox.Text?.Trim() ?? "";
        var rows = App.Workspace.RequirementRows(App.Session);
        foreach (var r in rows)
        {
            if (filter.Length > 0 &&
                !(r.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.Ref.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;
            _items.Add(new RequirementItem(r));
        }
        ReqList.ItemsSource = _items;
        CountLabel.Text = $"{_items.Count} requirement{(_items.Count == 1 ? "" : "s")} in the imported corpus";
        var unassigned = rows.Count(r => r.OwnerUnassigned);
        BulkAssign.IsVisible = App.Session is { ReadOnly: false } && unassigned > 0;
        if (BulkAssign.IsVisible) BulkAssign.Content = $"Assign {unassigned} unassigned to me";
    }

    private void LoadDetail(Guid id)
    {
        try
        {
            _detail = App.Workspace.RequirementDetail(id, App.Session);
            if (_detail == null) { Detail.Content = null; ToastWindow.Show($"RequirementDetail returned null for id {id}", App.Main, error: true); return; }
            _currentDetailId = id.ToString();
            Detail.Content = BuildDetail();
        }
        catch (Exception ex)
        {
            ToastWindow.Show("DETAIL BUILD ERROR: " + ex.Message, App.Main, error: true);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowResultSafe("DETAIL BUILD ERROR: " + ex.Message));
        }
    }

    private void ShowResultSafe(string text)
    {
                if (_resultBar != null) _resultBar.Show(NoticeTone.Info, text);
    }

    // ---------------- detail construction ----------------
    private Control BuildDetail()
    {
        var d = _detail!;
        var sp = new StackPanel { Margin = new(28, 24), Spacing = 14, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };

        sp.Children.Add(Header(d));
        sp.Children.Add(StatusRow(d));

        var tabs = new TabControl();
        var overview = new StackPanel { Spacing = 12, Margin = new(4) };
        overview.Children.Add(OverviewBody(d));
        var evidence = new StackPanel { Spacing = 12, Margin = new(4) };
        evidence.Children.Add(EvidenceBody(d, out _evidenceGrid));
        var tasks = new StackPanel { Spacing = 12, Margin = new(4) };
        tasks.Children.Add(TasksBody(d));
        var notes = new StackPanel { Spacing = 12, Margin = new(4) };
        notes.Children.Add(NotesBody(d));

        tabs.Items.Add(new TabItem { Header = "Requirement", Content = Pad(overview) });
        tabs.Items.Add(new TabItem { Header = $"Evidence ({d.Links.Count})", Content = Pad(evidence) });
        tabs.Items.Add(new TabItem { Header = $"Tasks ({d.Tasks.Count(t => t.State == TaskState.Open)})", Content = Pad(tasks) });
        tabs.Items.Add(new TabItem { Header = "Notes & issues", Content = Pad(notes) });
        sp.Children.Add(tabs);

        _resultBar = new NoticeHost();
        sp.Children.Insert(2, _resultBar);   // under the status row, above the tabs

        return new ScrollViewer { Content = sp };
    }

    // M7.2: content first gets the room the window affords; the outer page ScrollViewer
    // handles the true overflow, so inner clipping only appears on extreme lengths.
    private static ScrollViewer Pad(StackPanel p) => new() { Content = p, MaxHeight = 900 };

    /// <summary>M7.2: autosize contract for the detail-page text areas — grow with content
    /// (expand and contract), scroll only past the MaxHeight cap. ScrollBarVisibility is an
    /// attached property here.</summary>
    private static TextBox AutoGrow(TextBox b) { ScrollViewer.SetVerticalScrollBarVisibility(b, ScrollBarVisibility.Auto); return b; }

    private Control Header(RequirementDetail d)
    {
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(new TextBlock { Text = d.Requirement.Ref, Classes = { "mono" }, Foreground = Ui.Br("Brush.Muted") });
        sp.Children.Add(new TextBlock { Text = d.Requirement.Title,Classes = { "h2" }, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(new TextBlock
        {
            Classes = { "small" },
            TextWrapping = TextWrapping.Wrap,
            Text = $"Source: {d.SourceDocument?.DisplayName ?? d.Requirement.SourceDocFile} v{d.Requirement.SourceVersion}" +
                   (d.Requirement.AnchorPage is { } pg
                       ? $" · PDF p{pg}{(d.Requirement.AnchorVerified ? " (verified)" : " (unverified — locator search only)")}"
                       : d.Requirement.AnchorApproximate
                           ? $" · anchor approx. lines {d.Requirement.AnchorLines} (provisional)"
                           : $" · anchor lines {d.Requirement.AnchorLines}") +
                   $" · type {d.Requirement.RefKind}",
        });
        return sp;
    }

    private Control StatusRow(RequirementDetail d)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Chip(ReadinessLabel(d.State.Readiness), d.State.Readiness switch
        {
            RequirementReadiness.ManagerVerified or RequirementReadiness.AssessorAccepted => "ok",
            RequirementReadiness.ReadyForReview => "review",
            RequirementReadiness.NotStarted => "neutral",   // stone, hollow dot (3D)
            RequirementReadiness.InProgress => "warn",
            _ => "warn",
        }));
        row.Children.Add(Ui.Chip(ApplicabilityLabel(d.State.Applicability), d.State.Applicability switch
        {
            Applicability.NotApplicable => "neutral",
            _ => "info",   // applicable = quiet stone, not a success hue (3D)
        }));
        if (d.State.OwnerUnassigned)
        {
            // Quiet state: muted text + explanation on hover + one click to claim (M6 feedback).
            var unassigned = new TextBlock
            {
                Text = "No owner yet", Classes = { "small" },
                VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85,
            };
            ToolTip.SetTip(unassigned,
                "Nothing recorded: nobody currently owns gathering this evidence. " +
                "Use 'Assign to me' to claim it. Assessors read ownership as accountability for the evidence.");
            row.Children.Add(unassigned);
            row.Children.Add(new TextBlock { Text = "·", Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            var owner = new TextBlock { Text = $"Owner: {d.OwnerName}", VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(owner, "The person accountable for collecting and verifying this evidence.");
            row.Children.Add(owner);
        }
        if (App.Session is { } session && !session.ReadOnly)
        {
            var assign = new Button { Content = "Assign to me", Height = 28, HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(assign, "Record you as the owner of this requirement (audit-logged).");
            assign.Click += (_, _) =>
            {
                var res = App.Workspace.AssignOwner(d.Requirement.Id, session.UserId, session);
                ShowResultSafe(res.Ok ? "Owner assigned to you." : res.Error);
                Reload();
            };
            row.Children.Add(assign);
        }
        Ui.WrapGaps(row);
        return row;
    }

    private Control OverviewBody(RequirementDetail d)
    {
        var st = new StackPanel { Spacing = 10 };
        st.Children.Add(new TextBlock { Text = "What the source requires (verbatim excerpt)", Classes = { "label" } });
        // M7.2 + premium-polish 3.7: the verbatim excerpt is a quote — selectable serif
        // text with a 2px green rule, no input box; it grows with content and scrolls
        // past the 480pt cap.
        st.Children.Add(string.IsNullOrWhiteSpace(d.Requirement.WordingExcerpt)
            ? new TextBlock
            {
                Text = "(no verbatim excerpt was imported for this requirement — open the source document from the Library)",
                Classes = { "callout" }, TextWrapping = TextWrapping.Wrap,
                Margin = new(2, 6, 0, 2),
            }
            : Ui.QuoteBlock(d.Requirement.WordingExcerpt, maxHeight: 480));
        // 3F #7: hide "Plain English" when it merely repeats the verbatim quote.
        var excerpt = d.Requirement.WordingExcerpt ?? "";
        var plain = d.Requirement.PlainSummary ?? "";
        var dup = plain.Trim().Length > 0 &&
                  (excerpt.Trim().Contains(plain.Trim(), StringComparison.OrdinalIgnoreCase) ||
                   Similar(excerpt, plain));
        if (!string.IsNullOrWhiteSpace(plain) && !dup)
            st.Children.Add(new TextBlock { Text = "Plain English: " + plain, TextWrapping = TextWrapping.Wrap });

        if (d.Requirement.EvidenceExamples.Count > 0)
        {
            st.Children.Add(new TextBlock { Text = "Expected evidence (from the source — examples unless marked required)", Classes = { "label" } });
            foreach (var e in d.Requirement.EvidenceExamples)
                st.Children.Add(new TextBlock { Text = (e.Mandatory ? "• required: " : "• example: ") + e.Text, TextWrapping = TextWrapping.Wrap });
        }

        st.Children.Add(new TextBlock { Text = "Applicability", Classes = { "h3" } });
        var reason = AutoGrow(new TextBox { Text = d.State.ApplicabilityReason, Watermark = "Reason for the decision (assessors and future staff need it)", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 44, MaxHeight = 280 });
        var combo = new ComboBox { Width = 190, ItemsSource = new[] { "— choose —", "Applicable", "Not applicable", "Unknown" }, SelectedIndex = (int)d.State.Applicability + 1 };
        var apply = new Button { Content = "Record decision", Height = 32 };
        apply.Click += (_, _) =>
        {
            var idx = combo.SelectedIndex - 1;
            if (idx < 0 || idx > 2) { ShowInfo("Choose Applicable, Not applicable or Unknown before recording a decision."); return; }
            var res = App.Workspace.SetApplicability(d.Requirement.Id, (Applicability)idx, reason.Text ?? "", App.Session!);
            ShowResult(res.Ok ? "Applicability recorded." : res.Error);
            Reload();
        };
        st.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { combo, apply } });
        st.Children.Add(Ui.FormField("Reason for the decision", reason));

        // 3F #7: one caption only ("Next action"); the label repeats it otherwise.
        st.Children.Add(new TextBlock { Text = "Next action", Classes = { "h3" } });
        var next = AutoGrow(new TextBox { Text = d.State.NextAction, Watermark = "What must happen next, and why it matters", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 44, MaxHeight = 280 });
        st.Children.Add(next);
        var savenext = new Button { Content = "Save next action", Height = 32 };
        savenext.Click += (_, _) => { var r = App.Workspace.SetNextAction(d.Requirement.Id, next.Text ?? "", App.Session!); ShowResult(r.Ok ? "Next action saved." : r.Error); Reload(); };
        st.Children.Add(savenext);

        st.Children.Add(new TextBlock { Text = "Move readiness", Classes = { "h3" } });
        var rowB = new WrapPanel();
        rowB.Children.Add(ReadinessBtn("Mark in progress", RequirementReadiness.InProgress, d));
        rowB.Children.Add(ReadinessBtn("Ready for manager review", RequirementReadiness.ReadyForReview, d));
        rowB.Children.Add(ReadinessBtn("Manager verifies", RequirementReadiness.ManagerVerified, d, "verify"));
        rowB.Children.Add(ReadinessBtn("Record assessor outcome", RequirementReadiness.AssessorAccepted, d, "verify"));
        Ui.WrapGaps(rowB);
        st.Children.Add(rowB);
        st.Children.Add(new TextBlock
        {
            Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Readiness is per requirement: Not started → In progress → Ready for manager review → Manager verified. 'Assessor outcome' is only recorded when an external outcome exists (report or letter reference)."
        });

        return st;
    }

    /// <summary>Loose containment test for the Plain-English duplicate check: true when
    /// the plain summary also appears inside the verbatim excerpt (>=80% shared words).</summary>
    private static bool Similar(string excerpt, string plain)
    {
        var words = plain.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 5) return false;
        var shared = words.Count(w => excerpt.Contains(w, StringComparison.OrdinalIgnoreCase));
        return shared >= words.Length * 0.8;
    }

    private Button ReadinessBtn(string label, RequirementReadiness target, RequirementDetail d, string classes = "")
    {
        var b = new Button { Content = label, Height = 32 };
        foreach (var cls in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(cls);
        b.Click += async (_, _) =>
        {
            string? ext = "";
            if (target == RequirementReadiness.AssessorAccepted)
            {
                ext = await Dialogs.Prompt(this, "Assessor outcome reference", "Report / letter reference (required — only record an outcome that actually arrived)");
                if (string.IsNullOrWhiteSpace(ext)) return;
            }
            var r = App.Workspace.SetReadiness(d.Requirement.Id, target, App.Session!, ext);
            ShowResult(r.Ok ? "Readiness updated." : r.Error);
            Reload();
        };
        return b;
    }

    private Control EvidenceBody(RequirementDetail d, out DataGrid grid)
    {
        var st = new StackPanel { Spacing = 10 };
        st.Children.Add(new TextBlock { Text = "Linked evidence", Classes = { "h3" } });

        grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true,
            // Proportion rule (M7.2): the grid grows with the actual number of links up to
            // the page's working height — an empty or one-row grid no longer leaves a
            // 180 px dead void between the heading and the actions.
            Height = Math.Min(180, 46 + 32 * Math.Max(1, d.Links.Count)),
            ItemsSource = d.Links.Select(l => new
            {
                l.State,
                l.EvidenceTitle,
                l.ReviewState,
                l.FileName,
                EffectiveLabel = (l.EffectiveFrom?.ToString("dd MMM yyyy") ?? "—") + (l.EffectiveTo is null ? "" : " → " + l.EffectiveTo?.ToString("dd MMM yyyy")),
                PatientDataLabel = l.ContainsPatientData ? "Flagged" : "",
                l.EvidenceItemId,
            }).ToList(),
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Link state", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Evidence", Binding = new Avalonia.Data.Binding("EvidenceTitle"), Width = new DataGridLength(250) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Review", Binding = new Avalonia.Data.Binding("ReviewState"), Width = new DataGridLength(130) });
        grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Avalonia.Data.Binding("FileName"), Width = new DataGridLength(180) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Effective period", Binding = new Avalonia.Data.Binding("EffectiveLabel"), Width = new DataGridLength(170) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Patient data", Binding = new Avalonia.Data.Binding("PatientDataLabel"), Width = new DataGridLength(100) });
        if (d.Links.Count == 0)
            st.Children.Add(new TextBlock
            {
                Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
                Text = "No evidence linked yet — attach the first document with the green action below.",
            });
        else
            st.Children.Add(grid);

        // Actions WRAP (never clip): this row overflowed the window edge — the fifth
        // button ("Needs rework") was invisible until the window was widened, the audit's
        // reported defect. Colour language: primary green = attach (the creating action);
        // green outline = the positive verify claim; amber outline = corrective rework.
        var row = new WrapPanel();
        row.Children.Add(PrimaryBtn("Attach and link a document", async (_, _) => await AttachAndLinkAsync(d)));
        row.Children.Add(new Button { Content = "Link existing evidence", Height = 32 }.With(_ => _.Click += async (_, _) => await LinkExistingAsync(d)));
        row.Children.Add(new Button { Content = "Submit selected for review", Height = 32 }.With(_ => _.Click += (_, _) => ReviewEvidenceSelected(EvidenceReviewState.ReadyForReview)));
        row.Children.Add(new Button { Content = "Manager verifies selected", Height = 32, Classes = { "verify" } }.With(_ => _.Click += (_, _) => ReviewEvidenceSelected(EvidenceReviewState.ManagerVerified)));
        row.Children.Add(new Button { Content = "Needs rework", Height = 32, Classes = { "caution" } }.With(_ => _.Click += (_, _) => ReviewEvidenceSelected(EvidenceReviewState.NeedsRework)));
        Ui.WrapGaps(row);
        st.Children.Add(row);
        st.Children.Add(new TextBlock
        {
            Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "One record may support several requirements — set the link rationale per link. Uploading a file never sets compliance: links are Proposed until the evidence is manager-verified." 
        });
        return st;
    }

    private async Task AttachAndLinkAsync(RequirementDetail d)
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl == null) return;
        var files = await tl.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { AllowMultiple = false, Title = "Attach an evidence document" });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path)) { ShowInfo("The chosen file has no local path — copy it somewhere on disk first."); return; }

        var dlg = new EvidenceDialog(this);
        var ok = await dlg.ShowDialog<bool>(App.Main);
        if (!ok) return;

        var (evidResult, evidItem) = App.Workspace.ImportEvidence(path, dlg.TitleText, dlg.Kind, dlg.EffectiveFrom, dlg.EffectiveTo, dlg.HasPatientData, dlg.PatientDataAttestation, App.Session!);
        if (!evidResult.Ok || evidItem is null) { ShowResult(evidResult.Error); return; }
        var link = App.Workspace.LinkEvidence(evidItem.Id, d.Requirement.Id, dlg.Scope, dlg.EffectiveFrom, dlg.EffectiveTo, dlg.Rationale, App.Session!);
        ShowResult(link.Ok ? "Attached and linked. The link is Proposed until the evidence is manager-verified." : link.Error);
        Reload();
    }

    private async Task LinkExistingAsync(RequirementDetail d)
    {
        var dlg = new EvidencePickerDialog(this);
        var item = await dlg.ShowDialog<EvidenceItem?>(App.Main!);
        if (item is null) return;
        var rationale = await Dialogs.Prompt(this, "Rationale (required)", "Why does this evidence support this requirement?");
        if (string.IsNullOrWhiteSpace(rationale)) return;
        var res = App.Workspace.LinkEvidence(item.Id, d.Requirement.Id, "", item.EffectiveFrom, item.EffectiveTo, rationale, App.Session!);
        ShowResult(res.Ok ? "Linked." : res.Error);
        Reload();
    }

    private async void ReviewEvidenceSelected(EvidenceReviewState target)
    {
        if (_evidenceGrid?.SelectedItem is not EvidenceLinkRow row || row.EvidenceItemId == Guid.Empty)
        { ShowResult("Select an evidence row first."); return; }
        string note = "";
        if (target == EvidenceReviewState.ManagerVerified)
        {
            note = await Dialogs.Prompt(this, "Verification note (optional)", "Note saved with the verification") ?? "";
        }
        var res = App.Workspace.SetEvidenceReview(row.EvidenceItemId, target, App.Session!, note);
        ShowResult(res.Ok ? "Review state updated." : res.Error);
        Reload();
    }

    private Control TasksBody(RequirementDetail d)
    {
        var st = new StackPanel { Spacing = 10 };
        st.Children.Add(new TextBlock { Text = "Tasks for this requirement", Classes = { "h3" } });
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, Height = 170,
            ItemsSource = d.Tasks.Select(t => new
            {
                t.Title,
                Priority = t.Priority.ToString(),
                DueLabel = t.DueDate?.ToString("dd MMM yyyy") ?? "",
                State = t.State.ToString(),
                t.Details,
                t.Id,
            }).ToList(),
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(260) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Avalonia.Data.Binding("Priority"), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Due", Binding = new Avalonia.Data.Binding("DueLabel"), Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(80) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Details", Binding = new Avalonia.Data.Binding("Details"), Width = new DataGridLength(220) });
        st.Children.Add(grid);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(PrimaryBtn("Create task", (_, _) => new TaskDialog(this, d.Requirement.Id).ShowDialog(App.Main)));
        row.Children.Add(new Button { Content = "Complete selected", Height = 32 }.With(_ => _.Click += (_, _) =>
        {
            if (grid.SelectedItem is not null && grid.SelectedItem.GetType().GetProperty("Id") is { } p && p.GetValue(grid.SelectedItem) is Guid tid)
                new CompleteTaskDialog(this, tid).ShowDialog(App.Main);
            else ShowInfo("Select a task to complete.");
        }));
        st.Children.Add(row);
        return st;
    }

    private Control NotesBody(RequirementDetail d)
    {
        var st = new StackPanel { Spacing = 10 };
        st.Children.Add(new TextBlock { Text = "Notes", Classes = { "h3" } });
        _notesBox = AutoGrow(new TextBox { Text = d.State.Notes, AcceptsReturn = true, MinHeight = 80, MaxHeight = 340, TextWrapping = TextWrapping.Wrap, Watermark = "Working notes visible to the team" });
        st.Children.Add(_notesBox);
        var save = new Button { Content = "Save notes", Height = 32 };
        save.Click += (_, _) =>
        {
            var state = _detail!.State;
            state.Notes = _notesBox.Text ?? "";
            state.UpdatedAtUtc = DateTime.UtcNow;
            App.Db.SaveChanges();
            App.Audit.Append(App.Session?.DisplayName ?? "unknown", App.Session?.UserId, "requirement.notes", "requirement", d.Requirement.Id.ToString(), "notes updated");
            ShowResult("Notes saved.");
        };
        st.Children.Add(save);

        if (d.RelatedIssues.Count > 0)
        {
            st.Children.Add(new TextBlock { Text = "Interpretation issues affecting this requirement", Classes = { "h3" } });
            foreach (var issue in d.RelatedIssues)
            {
                var box = new Border { Classes = { "issuebox" } };
                var isp = new StackPanel { Spacing = 5 };
                isp.Children.Add(new TextBlock { Text = issue.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                isp.Children.Add(new TextBlock { Classes = { "small" }, Text = "Conservative planning: " + issue.ConservativePlanning, TextWrapping = TextWrapping.Wrap });
                foreach (var s in ParseSources(issue.SourcesJson))
                    isp.Children.Add(new TextBlock { Classes = { "small" }, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, Text = $"{s.reference} — “{s.text}” ({s.file}{(s.approxLines == "-" ? "" : ", approx. lines " + s.approxLines)})" });
                isp.Children.Add(new TextBlock { Classes = { "small" }, Text = $"Status: {issue.Status} · retrieved {issue.RetrievedDate} · pack {issue.PackVersion}" });
                var captured = issue;
                isp.Children.Add(new Button { Content = "Acknowledge (manager role required)", Height = 30 }.With(_ => _.Click += (_, _) =>
                {
                    var r = App.Workspace.AcknowledgeIssue(captured.Id, App.Session!);
                    ShowResult(r.Ok ? "Acknowledgement recorded for the current interpretation issue." : r.Error);
                }));
                box.Child = isp;
                st.Children.Add(box);
            }
        }

        if (d.SourceDocument != null)
        {
            st.Children.Add(new TextBlock { Text = "Source document", Classes = { "h3" } });
            st.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Text = $"{d.SourceDocument.DisplayName} — {d.SourceDocument.FileName} · v{d.SourceDocument.SourceVersion} · sha256 {d.SourceDocument.Sha256Hex[..12]}… · {d.SourceDocument.ProvenanceNote}" });
            var open = new Button { Content = "Open PDF via system viewer", Height = 30 };
            open.Click += (_, _) => Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(d.SourceDocument.StoredRelativePath));
            st.Children.Add(open);
        }

        return st;
    }

    // ---------------- helpers ----------------
    private void Reload()
    {
        LoadList();
        if (_currentDetailId is { } keep && Guid.TryParse(keep, out var g))
        {
            var row = _items.FirstOrDefault(i => i.Id == g);
            if (row != null) ReqList.SelectedItem = row;
            LoadDetail(g);
        }
    }

    private void ShowResult(string text)
    {
                if (_resultBar != null) _resultBar.Show(NoticeTone.Info, text);
    }
    private void ShowInfo(string text) => ShowResult(text);

    private static string ReadinessLabel(RequirementReadiness r) => r switch
    {
        RequirementReadiness.NotStarted => "Not started",
        RequirementReadiness.InProgress => "In progress",
        RequirementReadiness.ReadyForReview => "Ready for review",
        RequirementReadiness.ManagerVerified => "Manager verified",
        RequirementReadiness.AssessorAccepted => "Assessor outcome recorded",
        _ => r.ToString(),
    };
    private static string ApplicabilityLabel(Applicability a) => a switch
    {
        Applicability.Applicable => "Applicable",
        Applicability.NotApplicable => "Not applicable",
        _ => "Unknown",
    };

    private static Button PrimaryBtn(string label, EventHandler<RoutedEventArgs> onClick)
    {
        var b = new Button { Content = label, Classes = { "primary" }, Height = 32 };
        b.Click += onClick;
        return b;
    }

    private static List<(string reference, string text, string file, string approxLines)> ParseSources(string json)
    {
        try
        {
            var arr = JsonSerializer.Deserialize<JsonElement>(json);
            var list = new List<(string, string, string, string)>();
            foreach (var e in arr.EnumerateArray())
                list.Add((
                    e.TryGetProperty("reference", out var a) ? a.GetString() ?? "" : "",
                    e.TryGetProperty("text", out var b) ? b.GetString() ?? "" : "",
                    e.TryGetProperty("file", out var c) ? c.GetString() ?? "" : "",
                    e.TryGetProperty("approxLines", out var d) ? d.GetString() ?? "" : ""));
            return list;
        }
        catch { return []; }
    }
}

/// <summary>Left-list row model.</summary>
public record RequirementItem
{
    public Guid Id { get; init; }
    public RefKind RefKind { get; init; }
    public string Ref { get; init; } = "";
    public string Title { get; init; } = "";
    public string ReadinessLabel { get; init; } = "";
    public string OwnerNote { get; init; } = "";
    public Applicability Applicability { get; init; }

    public RequirementItem(RequirementRow r)
    {
        Id = r.Id; RefKind = r.RefKind; Ref = r.Ref; Title = r.Title;
        ReadinessLabel = r.Readiness switch
        {
            RequirementReadiness.InProgress => "in progress",
            RequirementReadiness.ReadyForReview => "ready for review",
            RequirementReadiness.ManagerVerified => "verified",
            RequirementReadiness.AssessorAccepted => "assessor outcome recorded",
            _ => "not started",
        };
        // Quiet list: rows carry work, not identity — ownership is a detail-page fact
        // (chip + hover there). Only nonzero counts render, so rows stay scannable.
        var ev = r.EvidenceCount > 0 ? $"{r.EvidenceCount} evidence" : "";
        var tk = r.OpenTaskCount > 0 ? $"{r.OpenTaskCount} open task{(r.OpenTaskCount == 1 ? "" : "s")}" : "";
        OwnerNote = string.Join(" · ", new[] { ev, tk }.Where(s => s.Length > 0));
        Applicability = r.Applicability;
    }
}
