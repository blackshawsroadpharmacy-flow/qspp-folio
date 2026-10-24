using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;   // ScrollBarVisibility for the AutoGrow helper
using Avalonia.Input;                  // Key, KeyModifiers for the ⌘↩ save hook
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Application;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;     // StateMachines

namespace QsppManager.Desktop.Views;

/// <summary>The flagship requirements workspace: corpus list on the left, working detail on
/// the right. List = segmented views (By area · Remote round · Readiness list · Rules and
/// terms) with collapsible section groups; detail = stepper, owner token and tabs. State
/// transitions and role checks live in QsppWorkspace and StateMachines; this view only
/// collects intent and shows results. Display titles come from the read-only
/// RequirementDisplay layer.</summary>
public partial class RequirementsView : UserControl, IPageSearchFocus
{
    private readonly ShellViewModel _shell;
    private readonly ObservableCollection<ListEntry> _entries = [];
    private List<RequirementItem> _items = [];
    private RequirementDetail? _detail;
    private DataGrid? _evidenceGrid;
    private NoticeHost? _resultBar;
    private TextBox? _notesBox;
    private string? _currentDetailId;
    private RequirementItem? _selectedItem;
    private int _segment; // 0 by area, 1 remote round, 2 readiness list, 3 rules and terms
    /// <summary>Sections the viewer collapsed (key = header label). View-scoped: state
    /// persists for the session per viewer without touching the data model (behaviour lock).</summary>
    private static readonly HashSet<string> _collapsedSections = [];

    public RequirementsView(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        FilterBox.TextChanged += (_, _) => LoadList();
        SegArea.Click += (_, _) => SetSegment(0);
        SegRemote.Click += (_, _) => SetSegment(1);
        SegHitlist.Click += (_, _) => SetSegment(2);
        SegRules.Click += (_, _) => SetSegment(3);
        ReqList.SelectionChanged += (_, _) =>
        {
            if (ReqList.SelectedItem is ListLeafEntry { Leaf: { } leaf })
            {
                _selectedItem = leaf;
                LoadDetail(leaf.Id);
            }
            else if (ReqList.SelectedItem is ListHeaderEntry head)
                ToggleSection(head.Label);
        };
        LoadList();
        // First impression: the readiness list opens, so the page reads like a list of jobs,
        // and its first job is already selected so the detail side shows real work.
        // The render harness can pin a specific segment (--seg 0..3).
        SetSegment(Program.RenderSegment ?? 2);
        if (_selectedItem == null)   // first visible job of the active segment, so detail shows real work
        {
            var firstLead = _entries.OfType<ListLeafEntry>().FirstOrDefault()?.Leaf;
            if (firstLead != null)
            {
                _selectedItem = firstLead;
                ReqList.SelectedItem = _entries.OfType<ListLeafEntry>().First(e => e.Leaf == firstLead);
            }
        }
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
    }

    private void SetSegment(int i)
    {
        _segment = i;
        RefreshSegmentVisual();
        LoadList();
    }

    private void RefreshSegmentVisual()
    {
        foreach (var (b, i) in new[] { (SegArea, 0), (SegRemote, 1), (SegHitlist, 2), (SegRules, 3) })
            if (i == _segment) b.Classes.Add("on"); else b.Classes.Remove("on");
    }

    /// <summary>The segment a requirement belongs to, so deep links land in the right view.</summary>
    private static int SegmentFor(RefKind kind) => kind switch
    {
        RefKind.Hitlist => 2,
        RefKind.Surveillance => 1,
        RefKind.Rule or RefKind.Tc or RefKind.Policy or RefKind.Obligation or RefKind.Std => 3,
        _ => 0,
    };

    /// <summary>Palette/search deep link: pick the segment the requirement lives in, open
    /// its group, select it and show the detail.</summary>
    public void OpenById(Guid id)
    {
        var row = _items.FirstOrDefault(i => i.Id == id);
        if (row == null)
        {
            _items = App.Workspace.RequirementRows(App.Session)
                .Select(r => new RequirementItem(r)).ToList();
            row = _items.FirstOrDefault(i => i.Id == id);
        }
        if (row == null) return;
        SetSegment(SegmentFor(row.RefKind));
        foreach (var k in SegmentKeys(row)) _collapsedSections.Remove(k);  // group opens automatically
        _selectedItem = _items.FirstOrDefault(i => i.Id == id);
        RefillList();
        var entry = _entries.FirstOrDefault(e => e is ListLeafEntry l && l.Leaf!.Id == id);
        if (entry != null) ReqList.SelectedItem = entry;
        if (_selectedItem != null) LoadDetail(_selectedItem.Id);
    }

    /// <summary>Command-palette / ⌘F hook: focus the filter and select all.</summary>
    public void FocusSearch()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }
    public void SetFilter(string text) => FilterBox.Text = text;

    // ---------------- list construction ----------------

    private void LoadList()
    {
        var rows = App.Workspace.RequirementRows(App.Session);
        SegRemote.Content = $"Remote round ({rows.Count(r => r.RefKind == RefKind.Surveillance)})";
        SegHitlist.Content = $"Readiness list ({rows.Count(r => r.RefKind == RefKind.Hitlist)})";
        SegRules.Content = $"Rules and terms ({rows.Count(r => r.RefKind is not (RefKind.Hitlist or RefKind.Surveillance))})";
        var filter = FilterBox.Text?.Trim() ?? "";
        _items = rows
            .Select(r => new RequirementItem(r))
            .Where(i => filter.Length == 0
                        || i.DisplayTitleText.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || i.Ref.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || i.KindLabel.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        RefillList();
        var unassigned = rows.Count(r => r.OwnerUnassigned);
        BulkAssign.IsVisible = App.Session is { ReadOnly: false } && unassigned > 0;
        if (BulkAssign.IsVisible) BulkAssign.Content = $"Assign {unassigned} unassigned to me";
    }

    /// <summary>Section header labels this leaf sits under in the CURRENT segment; they
    /// double as collapse-state keys (stable labels across reloads) and auto-open keys.</summary>
    private List<string> SegmentKeys(RequirementItem i)
    {
        if (_segment == 1) return ["Remote round"];
        if (_segment == 2) return ["Readiness list"];
        if (_segment == 3)
            return [i.KindLabel];
        return [i.DomainLabel, $"{i.DomainLabel} · {i.Area}"];
    }

    private void RefillList()
    {
        var previous = _items.FirstOrDefault(i => i.Id == _selectedItem?.Id);
        _entries.Clear();
        switch (_segment)
        {
            case 0:
            {
                var domains = _items.Select(i => i.DomainLabel).Distinct().ToList();
                foreach (var dLabel in domains)
                {
                    var domain = dLabel;
                    AddHeader(domain, _items.Where(i => i.DomainLabel == domain).ToList(), indent: false);
                    if (!_collapsedSections.Contains(domain))
                    {
                        var areas = _items.Where(i => i.DomainLabel == domain).Select(i => i.Area).Distinct().ToList();
                        foreach (var area in areas)
                        {
                            var key = $"{domain} · {area}";
                            AddHeader(key, _items.Where(i => i.DomainLabel == domain && i.Area == area).ToList(), indent: true);
                            if (_collapsedSections.Contains(key)) continue;
                            foreach (var i in _items.Where(i => i.DomainLabel == domain && i.Area == area).OrderBy(SortRef))
                                AddLeaf(i);
                        }
                    }
                }
                break;
            }
            case 1:
            {
                AddHeader("Remote round", _items.Where(i => i.RefKind == RefKind.Surveillance).ToList(), indent: false);
                if (!_collapsedSections.Contains("Remote round"))
                    foreach (var i in _items.Where(i => i.RefKind == RefKind.Surveillance).OrderBy(SortRef))
                        AddLeaf(i);
                break;
            }
            case 2:
            {
                AddHeader("Readiness list", _items.Where(i => i.RefKind == RefKind.Hitlist).ToList(), indent: false);
                if (!_collapsedSections.Contains("Readiness list"))
                    foreach (var i in _items.Where(i => i.RefKind == RefKind.Hitlist).OrderBy(SortRef))
                        AddLeaf(i);
                break;
            }
            default:
            {
                var kinds = _items.Where(i => i.RefKind is not (RefKind.Hitlist or RefKind.Surveillance))
                    .Select(i => i.KindLabel).Distinct().ToList();
                foreach (var kind in kinds)
                {
                    var k = kind;
                    AddHeader(k, _items.Where(i => i.KindLabel == k).ToList(), indent: false);
                    if (_collapsedSections.Contains(k)) continue;
                    foreach (var i in _items.Where(i => i.KindLabel == k).OrderBy(SortRef))
                        AddLeaf(i);
                }
                break;
            }
        }
        if (_items.Count == 0)
            _entries.Add(new ListHeaderEntry
            {
                Label = "No requirements match this filter", Count = "", Collapsible = false, Chevron = "",
            });
        ReqList.ItemsSource = _entries;
        if (previous != null && _selectedItem?.Id == previous.Id)
        {
            var entry = _entries.FirstOrDefault(e => e is ListLeafEntry l && l.Leaf == previous);
            if (entry != null) ReqList.SelectedItem = entry; // keep selection; do not jump to a header
        }
        return;

        void AddHeader(string label, List<RequirementItem> members, bool indent)
        {
            var started = members.Count(m => m.Readiness is not RequirementReadiness.NotStarted);
            _entries.Add(new ListHeaderEntry
            {
                Label = label, Indented = indent, Collapsible = true,
                Collapsed = _collapsedSections.Contains(label),
                Chevron = _collapsedSections.Contains(label) ? "▸" : "▾",
                Count = members.Count == 0 ? "" : $"{started} of {members.Count} started",
            });
        }

        void AddLeaf(RequirementItem i)
        {
            _entries.Add(new ListLeafEntry
            {
                Leaf = i,
                Headline = i.DisplayTitleText,
                StatusWord = i.ReadinessWord,
                RefCode = i.Ref,
                DotBrush = Ui.Br(Ui.ToneBrushKey(i.Tone)),
            });
        }
    }

    /// <summary>Natural reference order: leading dotted digits sort numerically per part
    /// ("Rule 10" after "Rule 9"); anything else falls back to the plain string.</summary>
    private static string SortRef(RequirementItem i)
    {
        var head = new string(i.Ref.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (head.Length == 0) return "z|" + i.Ref;
        var padded = string.Join(".", head.Split('.').Select(p => p.Length == 0 ? "0" : p.PadLeft(3, '0')));
        return $"{padded}|{i.Ref}";
    }

    private void ToggleSection(string label)
    {
        if (!_collapsedSections.Remove(label)) _collapsedSections.Add(label);
        RefillList();
        if (_selectedItem is { } keep)
        {
            var it = _items.FirstOrDefault(i => i.Id == keep.Id);
            var entry = it == null ? null : _entries.FirstOrDefault(e => e is ListLeafEntry l && l.Leaf == it);
            if (entry != null) ReqList.SelectedItem = entry;
        }
    }

    // ---------------- detail construction ----------------

    private void LoadDetail(Guid id)
    {
        try
        {
            _detail = App.Workspace.RequirementDetail(id, App.Session);
            if (_detail == null) { Detail.Content = null; ToastWindow.Show($"Requirement detail not found for id {id}", App.Main, error: true); return; }
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

    private Control BuildDetail()
    {
        var d = _detail!;
        var sp = new StackPanel { Margin = new(28, 24, 28, 24), Spacing = 14, MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };

        sp.Children.Add(Header(d));
        sp.Children.Add(StatusRow(d));

        // Manual tab switcher instead of TabControl: Avalonia's TabControl measures tab
        // content unbounded, which breaks text wrap in long source citations. A plain
        // WrapPanel of pill buttons + a ContentControl keeps everything bounded.
        var tabRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var host = new ContentControl();
        var tabDefs = new (string Label, Func<StackPanel> Build)[]
        {
            ("Overview", () => OverviewPanel(d)),
            ($"Evidence ({d.Links.Count})", () => EvidencePanel(d)),
            ($"Tasks ({d.Tasks.Count(t => t.State == TaskState.Open)})", () => TasksPanel(d)),
            ("Notes & issues", () => NotesPanel(d)),
        };
        var tabButtons = new List<Button>();
        void ShowTab(int index)
        {
            host.Content = Pad(tabDefs[index].Build());
            for (var i = 0; i < tabButtons.Count; i++)
            {
                if (i == index) tabButtons[i].Classes.Add("on"); else tabButtons[i].Classes.Remove("on");
            }
        }
        for (var i = 0; i < tabDefs.Length; i++)
        {
            var index = i;
            var b = new Button { Content = tabDefs[i].Label, Height = 26 };
            b.Classes.Add("seg");
            b.Click += (_, _) => ShowTab(index);
            tabButtons.Add(b);
            tabRow.Children.Add(b);
        }
        ShowTab(0);
        sp.Children.Add(tabRow);
        sp.Children.Add(host);

        _resultBar = new NoticeHost();
        sp.Children.Insert(2, _resultBar);   // under the status row, above the tabs

        return new ScrollViewer
        {
            Content = sp,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private StackPanel OverviewPanel(RequirementDetail d)
    {
        var overview = new StackPanel { Spacing = 12, Margin = new(4) };
        overview.Children.Add(OverviewBody(d));
        return overview;
    }
    private StackPanel EvidencePanel(RequirementDetail d)
    {
        var evidence = new StackPanel { Spacing = 12, Margin = new(4) };
        evidence.Children.Add(EvidenceBody(d, out _evidenceGrid));
        return evidence;
    }
    private StackPanel TasksPanel(RequirementDetail d)
    {
        var tasks = new StackPanel { Spacing = 12, Margin = new(4) };
        tasks.Children.Add(TasksBody(d));
        return tasks;
    }
    private StackPanel NotesPanel(RequirementDetail d)
    {
        var notes = new StackPanel { Spacing = 12, Margin = new(4) };
        notes.Children.Add(NotesBody(d));
        return notes;
    }

    // M7.2: content first gets the room the window affords; the outer page ScrollViewer
    // handles the true overflow, so inner clipping only appears on extreme lengths.
    // Horizontal scroll must stay DISABLED on every layer: an enabled/hidden horizontal
    // axis measures children unbounded and they never wrap (the phase-3 RunLine trap).
    private static ScrollViewer Pad(StackPanel p) => new()
    {
        Content = p, MaxHeight = 900,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };

    /// <summary>M7.2: autosize contract for the detail-page text areas; grow with content,
    /// scroll only past the MaxHeight cap. ScrollBarVisibility is attached here.</summary>
    private static TextBox AutoGrow(TextBox b) { ScrollViewer.SetVerticalScrollBarVisibility(b, ScrollBarVisibility.Auto); return b; }

    /// <summary>Detail top: eyebrow = kind · area; then the display title in h2; editorial
    /// titles carry a "Plain title" footnote with an ⓘ so the official wording below is
    /// never mistaken for paraphrased source text.</summary>
    private Control Header(RequirementDetail d)
    {
        var t = d.Display;
        var sp = new StackPanel { Spacing = 5 };
        sp.Children.Add(new TextBlock
        {
            Text = $"{t.KindLabel} · {t.Area}".ToUpperInvariant(),
            Classes = { "eyebrow" }, Foreground = Ui.Br("Brush.Muted"),
        });
        sp.Children.Add(new TextBlock { Text = t.Title, Classes = { "title2" }, TextWrapping = TextWrapping.Wrap });
        if (t.Basis == DisplayTitleBasis.Editorial)
        {
            var plain = new StackPanel { Orientation = Orientation.Horizontal };
            plain.Children.Add(new TextBlock
            {
                Text = "Plain title", Classes = { "footnote" },
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Br("Brush.Muted"),
            });
            plain.Children.Add(Ui.InfoDot(
                "This shorter title is written by the pharmacy for reading speed. It adds nothing to, and replaces nothing in, the source: the official wording appears under 'What the source says', unaltered, and is what the assessor pack and printouts use.",
                "About the plain title"));
            plain.Spacing = 4;
            sp.Children.Add(plain);
        }
        return sp;
    }

    /// <summary>Status row: the four-step readiness stepper, the ONE next action beside it,
    /// everything else in the ⋯ menu; then the owner token on its own line underneath.
    /// Readiness moves only through the state machine in StateMachines.</summary>
    private Control StatusRow(RequirementDetail d)
    {
        var st = new StackPanel { Spacing = 10 };
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Stepper(d));
        if (NextAction(d) is { } next) row.Children.Add(next);
        if (MenuButton(d) is { } menu) row.Children.Add(menu);
        Ui.WrapGaps(row);
        st.Children.Add(row);
        st.Children.Add(OwnerToken(d));
        return st;
    }

    /// <summary>A single row: filled 12px dots with labels beside them for reached steps,
    /// hairline dots for later ones, 1px connector lines between.</summary>
    private static StackPanel Stepper(RequirementDetail d)
    {
        var current = d.State.Readiness switch
        {
            RequirementReadiness.NotStarted => 0,
            RequirementReadiness.InProgress => 1,
            RequirementReadiness.ReadyForReview => 2,
            RequirementReadiness.ManagerVerified or RequirementReadiness.AssessorAccepted => 3,
            _ => 0,
        };
        var names = new[] { "Not started", "In progress", "Ready for review", "Verified" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < 4; i++)
        {
            var dotBorder = new Border
            {
                Width = 12, Height = 12, CornerRadius = new(6),
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (i <= current)
            {
                dotBorder.Background = Ui.Br("Brush.Green");
                dotBorder.BorderThickness = new(0);
            }
            else
            {
                dotBorder.Background = Ui.Br("Brush.Paper");
                dotBorder.BorderBrush = Ui.Br("Brush.ControlBorder");
                dotBorder.BorderThickness = new(1.2);
            }
            var step = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            step.Children.Add(dotBorder);
            step.Children.Add(new TextBlock
            {
                Text = names[i], Classes = { "small" },
                Foreground = i <= current ? Ui.Br("Brush.Ink") : Ui.Br("Brush.Muted"),
                FontWeight = i == current ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(step);
            if (i < 3)
                row.Children.Add(new Border
                {
                    Height = 1, Background = Ui.Br("Brush.Divider"),
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 22, Margin = new(8, 0),
                });
        }
        return row;
    }

    /// <summary>Exactly one next-step button: the natural next transition from here.</summary>
    private Button? NextAction(RequirementDetail d)
    {
        var (current, next, label, cls) = (d.State.Readiness) switch
        {
            RequirementReadiness.NotStarted => (RequirementReadiness.NotStarted, RequirementReadiness.InProgress, "Mark in progress", "primary"),
            RequirementReadiness.InProgress => (RequirementReadiness.InProgress, RequirementReadiness.ReadyForReview, "Ready for manager review", "primary"),
            RequirementReadiness.ReadyForReview => (RequirementReadiness.ReadyForReview, RequirementReadiness.ManagerVerified, "Manager verifies", "verify"),
            RequirementReadiness.ManagerVerified => (RequirementReadiness.ManagerVerified, RequirementReadiness.AssessorAccepted, "Record assessor outcome", "verify"),
            _ => (RequirementReadiness.AssessorAccepted, RequirementReadiness.AssessorAccepted, "", ""),
        };
        if (label == "") return null;
        var b = new Button { Content = label, Height = 28 };
        b.Classes.Add(cls);
        ToolTip.SetTip(b, TransitionTip(next));
        if (!App.Session.ReadOnly && StateMachines.CanTransitionReadiness(current, next)
            && StateMachines.MayApplyReadiness(next, App.Session.Role))
            b.Click += (_, _) => TransitionTo(d, next);
        else
        {
            b.IsEnabled = false;
            ToolTip.SetTip(b, $"{label} needs the right role for this step ({ReadinessRoleHint(next)}).");
        }
        return b;
    }

    /// <summary>Transitions other than the natural next step (backing out, recording the
    /// assessor outcome early) — exactly the set the state machine allows from here.</summary>
    private Button? MenuButton(RequirementDetail d)
    {
        if (App.Session is not { ReadOnly: false }) return null;
        var others = Enum.GetValues<RequirementReadiness>()
            .Where(t => t != NextTarget(d.State.Readiness) && StateMachines.CanTransitionReadiness(d.State.Readiness, t))
            .ToList();
        if (others.Count == 0) return null;
        var flyout = new MenuFlyout();
        foreach (var t in others)
        {
            var mi = new MenuItem { Header = ReadinessLabel(t) };
            var target = t;
            mi.Click += (_, _) => TransitionTo(_detail!, target);
            flyout.Items.Add(mi);
        }
        var button = new Button { Content = "⋯", Height = 28 };
        ToolTip.SetTip(button, "Other movements the state machine allows from here");
        button.Flyout = flyout;
        return button;
    }

    private static RequirementReadiness NextTarget(RequirementReadiness cur) => cur switch
    {
        RequirementReadiness.NotStarted => RequirementReadiness.InProgress,
        RequirementReadiness.InProgress => RequirementReadiness.ReadyForReview,
        RequirementReadiness.ReadyForReview => RequirementReadiness.ManagerVerified,
        RequirementReadiness.ManagerVerified => RequirementReadiness.AssessorAccepted,
        _ => RequirementReadiness.AssessorAccepted,
    };

    private static string TransitionTip(RequirementReadiness target) => target switch
    {
        RequirementReadiness.InProgress => "Work has started on gathering the evidence.",
        RequirementReadiness.ReadyForReview => "The evidence is assembled and this hands it to the manager.",
        RequirementReadiness.ManagerVerified => "The manager has checked the evidence against the requirement.",
        RequirementReadiness.AssessorAccepted => "An external outcome (report or letter) has arrived.",
        _ => "",
    };

    private static string ReadinessRoleHint(RequirementReadiness target) => target switch
    {
        RequirementReadiness.ManagerVerified or RequirementReadiness.AssessorAccepted => "owner, admin or manager",
        _ => "owner, admin, manager or contributor",
    };

    private async void TransitionTo(RequirementDetail d, RequirementReadiness target)
    {
        if (App.Session is not { ReadOnly: false } session) return;
        string? ext = "";
        if (target == RequirementReadiness.AssessorAccepted)
        {
            ext = await Dialogs.Prompt(this, "Assessor outcome reference", "Report / letter reference (required; only record an outcome that actually arrived)");
            if (string.IsNullOrWhiteSpace(ext)) return;
        }
        var r = App.Workspace.SetReadiness(d.Requirement.Id, target, session, ext);
        ShowResult(r.Ok ? "Readiness updated." : r.Error);
        Reload();
    }

    /// <summary>Owner as a token: initials circle + name, "Change" opens a menu with
    /// 'Assign to me'. Unowned shows a hairline placeholder avatar with the same menu.
    /// No standalone button (the old row carried two buttons for one job).</summary>
    private Control OwnerToken(RequirementDetail d)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        var initials = string.IsNullOrWhiteSpace(d.OwnerName) ? "" : OwnerInitials(d.OwnerName);
        row.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new(12),
            Background = Ui.Br("Brush.Sage"),
            BorderBrush = Ui.Br(initials == "" ? "Brush.ControlBorder" : Ui.ToneBrushKey("ok")),
            BorderThickness = new(initials == "" ? 1.1 : 0),
            Child = new TextBlock
            {
                Text = initials == "" ? "·" : initials,
                FontSize = 10.5, FontWeight = FontWeight.Medium,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = initials == "" ? Ui.Br("Brush.Muted") : Ui.Br("Brush.Green"),
            },
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = initials == "" ? "No owner yet" : d.OwnerName,
            Classes = { "small" },
            Foreground = initials == "" ? Ui.Br("Brush.Muted") : Ui.Br("Brush.Ink"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        ToolTip.SetTip(row, initials == ""
            ? "Nothing recorded: nobody currently owns gathering this evidence. Assessors read ownership as accountability for the evidence."
            : "The person accountable for collecting and verifying this evidence.");
        if (App.Session is { } session && !session.ReadOnly)
        {
            var change = new Button { Content = "Change", Classes = { "textlink" }, VerticalAlignment = VerticalAlignment.Center };
            var flyout = new MenuFlyout();
            var claim = new MenuItem { Header = "Assign to me" };
            claim.Click += (_, _) =>
            {
                var res = App.Workspace.AssignOwner(d.Requirement.Id, session.UserId, session);
                ShowResultSafe(res.Ok ? "Owner assigned to you." : res.Error);
                Reload();
            };
            flyout.Items.Add(claim);
            change.Flyout = flyout;
            row.Children.Add(change);
        }
        return row;
    }

    private static string OwnerInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("", parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    // ---------------- overview tab ----------------

    private Control OverviewBody(RequirementDetail d)
    {
        var st = new StackPanel { Spacing = 10 };
        st.Children.Add(new TextBlock { Text = "What the source requires (verbatim excerpt)", Classes = { "label" } });
        st.Children.Add(string.IsNullOrWhiteSpace(d.Requirement.WordingExcerpt)
            ? new TextBlock
            {
                Text = "(no verbatim excerpt was imported for this requirement; open the source document from the Library)",
                Classes = { "callout" }, TextWrapping = TextWrapping.Wrap,
                Margin = new(2, 6, 0, 2),
            }
            : Ui.QuoteBlock(d.Requirement.WordingExcerpt, maxHeight: 480));
        // Footnote citation stays visible under the quote; provenance forensics go in the
        // expander below (master plan §4.6).
        var cite = $"Source: {d.SourceDocument?.DisplayName ?? d.Requirement.SourceDocFile} · version {d.Requirement.SourceVersion}";
        if (d.Requirement.AnchorPage is { } pgNo)
            cite += d.Requirement.AnchorVerified ? $" · page {pgNo} verified" : $" · page {pgNo} (unverified)";
        st.Children.Add(new TextBlock { Classes = { "footnote" }, Text = cite, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Br("Brush.Muted") });
        st.Children.Add(SourceDetailsDisclosure(d));   // provenance bits behind a collapsed expander

        // "In plain English" only when it says something: hidden when it merely repeats
        // the verbatim quote (3F #7 rule kept — paraphrase with the same words adds nothing).
        var excerpt = d.Requirement.WordingExcerpt ?? "";
        var plain = d.Requirement.PlainSummary ?? "";
        var dup = plain.Trim().Length > 0 &&
                  (excerpt.Trim().Contains(plain.Trim(), StringComparison.OrdinalIgnoreCase) ||
                   Similar(excerpt, plain));
        if (!string.IsNullOrWhiteSpace(plain) && !dup)
            st.Children.Add(new TextBlock { Text = "Plain English: " + plain, TextWrapping = TextWrapping.Wrap });

        if (d.Requirement.EvidenceExamples.Count > 0)
        {
            st.Children.Add(new TextBlock { Text = "Expected evidence (from the source; examples unless marked required)", Classes = { "label" } });
            foreach (var e in d.Requirement.EvidenceExamples)
                st.Children.Add(new TextBlock { Text = (e.Mandatory ? "• required: " : "• example: ") + e.Text, TextWrapping = TextWrapping.Wrap });
        }

        st.Children.Add(new TextBlock { Text = "Applies to us?", Classes = { "h3" } });
        st.Children.Add(ApplicabilityBlock(d));

        st.Children.Add(new TextBlock { Text = "Next action", Classes = { "h3" } });
        st.Children.Add(NextActionField(d));

        return st;
    }

    /// <summary>Provenance internals behind a collapsed expander; the citation above stays
    /// visible. Carries the Open PDF action (it belongs with the source it opens).</summary>
    private Control SourceDetailsDisclosure(RequirementDetail d)
    {
        var body = new StackPanel { Spacing = 6 };
        void Line(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                body.Children.Add(new TextBlock { Classes = { "footnote" }, Text = text, TextWrapping = TextWrapping.Wrap });
        }
        Line($"Source: {d.SourceDocument?.DisplayName ?? d.Requirement.SourceDocFile} · version {d.Requirement.SourceVersion}");
        if (d.Requirement.AnchorPage is { } p)
            Line(d.Requirement.AnchorVerified
                ? $"PDF page {p} (verified)"
                : $"PDF page {p} (unverified: located by text search, not checked against the printed page)");
        if (d.Requirement.AnchorLines is { } lines)
            Line(d.Requirement.AnchorApproximate
                ? $"anchor approximately lines {lines} (provisional)"
                : $"anchor lines {lines}");
        if (d.SourceDocument is { } sd)
        {
            if (sd.Sha256Hex is { } sha && sha.Length >= 12) Line($"sha256 {sha[..12]}…");
            if (!string.IsNullOrWhiteSpace(sd.ProvenanceNote)) Line($"provenance note: {sd.ProvenanceNote}");
            var open = new Button { Content = "Open PDF via system viewer", Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => Helpers.OpenWithSystemViewer(App.Store.AbsolutePath(sd.StoredRelativePath));
            body.Children.Add(open);
        }
        return new Expander
        {
            Header = new TextBlock { Text = "Source details", Classes = { "small" }, Foreground = Ui.Br("Brush.Muted") },
            IsExpanded = false, Content = body, Margin = new(0, 2, 0, 4),
        };
    }

    /// <summary>Loose containment test for the Plain-English duplicate check: true when
    /// the plain summary also appears inside the verbatim excerpt (≥80% shared words).</summary>
    private static bool Similar(string excerpt, string plain)
    {
        var words = plain.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 5) return false;
        var shared = words.Count(w => excerpt.Contains(w, StringComparison.OrdinalIgnoreCase));
        return shared >= words.Length * 0.8;
    }

    /// <summary>Applies to us? as three pill buttons + reason field + ONE Save. The pending
    /// selection is view-local until saved; reason-required validation stays in the
    /// workspace layer (it knows the rule, the button does not).</summary>
    private Control ApplicabilityBlock(RequirementDetail d)
    {
        var current = d.State.Applicability;
        Applicability? pending = null;
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var buttons = new List<(Applicability Value, Button Button)>();
        foreach (var (label, value) in new[] { ("Applies", Applicability.Applicable), ("Doesn't apply", Applicability.NotApplicable), ("Unsure", Applicability.Unknown) })
        {
            var b = new Button { Content = label, Height = 26 };
            b.Classes.Add("seg");
            if (value == current) b.Classes.Add("on");
            var captured = value;
            b.Click += (_, _) =>
            {
                pending = captured;
                foreach (var (v, btn) in buttons)
                {
                    if (v == captured) btn.Classes.Add("on"); else btn.Classes.Remove("on");
                }
            };
            buttons.Add((value, b));
            seg.Children.Add(b);
        }
        var reason = AutoGrow(new TextBox
        {
            Text = d.State.ApplicabilityReason,
            Watermark = "Reason for the decision (assessors and future staff need it)",
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 48, MaxHeight = 240, MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,   // Stretch+MaxWidth centres; fields must align to the headings
        });
        var save = PrimaryBtn("Save", (_, _) =>
        {
            if (App.Session is not { ReadOnly: false }) return;
            var chosen = pending ?? current;
            var r = App.Workspace.SetApplicability(d.Requirement.Id, chosen, reason.Text ?? "", App.Session!);
            ShowResult(r.Ok ? "Applicability recorded." : r.Error);
            if (r.Ok)
            {
                current = chosen; pending = null;
                d.State.Applicability = chosen;
                d.State.ApplicabilityReason = reason.Text ?? "";
                RefreshSegmentVisual();
                LoadList();
            }
        });
        save.Margin = new(0, 8, 0, 0);
        return new StackPanel { Spacing = 8, Children = { seg, reason, save } };
    }

    /// <summary>Next action: autosize text area saved on blur (no change = no write) and
    /// on ⌘↩. No Reload on save: the field stays where the reader left it.</summary>
    private Control NextActionField(RequirementDetail d)
    {
        var original = d.State.NextAction ?? "";
        var box = AutoGrow(new TextBox
        {
            Text = original, Watermark = "What must happen next, and why it matters",
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 48, MaxHeight = 220, MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,   // match the reason field's left edge
        });
        void Save()
        {
            if (box.Text == original) return;
            if (App.Session is not { ReadOnly: false } session) return;
            var r = App.Workspace.SetNextAction(d.Requirement.Id, box.Text ?? "", session);
            ShowResult(r.Ok ? "Next action saved." : r.Error);
            if (r.Ok)
            {
                original = box.Text ?? "";
                d.State.NextAction = original;
            }
        }
        box.LostFocus += (_, _) => Save();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return && (e.KeyModifiers & KeyModifiers.Meta) != 0) Save();
        };
        return box;
    }

    // ---------------- evidence tab ----------------

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
                Text = "No evidence linked yet; attach the first document with the green action below.",
            });
        else
            st.Children.Add(grid);

        // Actions WRAP (never clip): this row overflowed the window edge in an earlier
        // build; a wrapped row keeps every action visible. Colour language: primary green
        // = attach (the creating action); green outline = the positive verify claim;
        // amber outline = corrective rework.
        var row = new WrapPanel();
        row.Children.Add(PrimaryBtn("Attach and link a document", async (_, _) => await AttachAndLinkAsync(d)));
        row.Children.Add(new Button { Content = "Link existing evidence", Height = 32 }.With(_ => _.Click += async (_, _) => await LinkExistingAsync(d)));
        row.Children.Add(new Button { Content = "Submit selected for review", Height = 32 }.With(_ => _.Click += (_, _) => ReviewEvidenceSelected(EvidenceReviewState.ReadyForReview)));
        row.Children.Add(new Button { Content = "Manager verifies selected", Height = 32, Classes = { "verify" } }.With(_ => _.Click += (_, _) => ReviewEvidenceSelected(EvidenceReviewState.ManagerVerified)));
        row.Children.Add(new Button { Content = "Needs rework", Height = 32, Classes = { "caution" } }.With(_ => _.Click += (_, _) => ReviewEvidenceSelected(EvidenceReviewState.NeedsRework)));
        _evidenceGrid = grid;
        Ui.WrapGaps(row);
        st.Children.Add(row);
        st.Children.Add(new TextBlock
        {
            Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "One record may support several requirements; set the link rationale per link. Uploading a file never sets compliance: links are Proposed until the evidence is manager-verified.",
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
        if (string.IsNullOrEmpty(path)) { ShowInfo("The chosen file has no local path; copy it somewhere on disk first."); return; }

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

        return st;
    }

    // ---------------- helpers ----------------

    private void Reload()
    {
        LoadList();
        if (_currentDetailId is { } keep && Guid.TryParse(keep, out var g))
        {
            var it = _items.FirstOrDefault(i => i.Id == g);
            if (it != null)
            {
                foreach (var k in SegmentKeys(it)) _collapsedSections.Remove(k);
                _selectedItem = it;
                RefillList();
                var entry = _entries.FirstOrDefault(e => e is ListLeafEntry l && l.Leaf == it);
                if (entry != null) ReqList.SelectedItem = entry;
            }
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
                    e.TryGetProperty("approxLines", out var dd) ? dd.GetString() ?? "" : ""));
            return list;
        }
        catch { return []; }
    }
}

/// <summary>A flattened list entry: a collapsible section header or a requirement leaf.
/// The ListBox shows these two concrete types so the DataTemplates match by DataType.</summary>
public abstract class ListEntry;

/// <summary>Section header row: label, "x of y started" count, chevron, indentation.</summary>
public sealed class ListHeaderEntry : ListEntry
{
    public string Label { get; init; } = "";
    public string Count { get; init; } = "";
    public string Chevron { get; init; } = "▾";
    public bool Collapsed { get; init; }
    public bool Collapsible { get; init; } = true;
    public bool Indented { get; init; }
}

/// <summary>Requirement leaf row: headline first (the display title), then status dot +
/// word, then the reference code in mono. The item itself rides along for selection.</summary>
public sealed class ListLeafEntry : ListEntry
{
    public RequirementItem? Leaf { get; init; }
    public string Headline { get; init; } = "";
    public string StatusWord { get; init; } = "";
    public string RefCode { get; init; } = "";
    public IBrush DotBrush { get; init; } = Brushes.Transparent;
}

/// <summary>Left-list leaf model: everything the row renders plus readiness state for
/// grouping/counts. Title stays the raw source heading for search-by-code paths.</summary>
public record RequirementItem
{
    public Guid Id { get; init; }
    public RefKind RefKind { get; init; }
    public string Ref { get; init; } = "";
    public string Title { get; init; } = "";
    public string DisplayTitleText { get; init; } = "";
    public string SourceTitle { get; init; } = "";
    public string Area { get; init; } = "";
    public string KindLabel { get; init; } = "";
    public string DomainLabel { get; init; } = "";
    public RequirementReadiness Readiness { get; init; }
    public string ReadinessWord { get; init; } = "";
    public string Tone { get; init; } = "";
    public bool OwnerUnassigned { get; init; }
    public Applicability Applicability { get; init; }

    public RequirementItem(RequirementRow r)
    {
        Id = r.Id; RefKind = r.RefKind; Ref = r.Ref; Title = r.Title;
        DisplayTitleText = r.DisplayTitleText;
        Area = r.Area; KindLabel = r.KindLabel; DomainLabel = r.DomainLabel;
        Readiness = r.Readiness;
        // Colour and word come from the same place: the readiness machine drives the tone,
        // the tone drives the dot; applicability overrides only for "does not apply".
        if (r.Applicability == Applicability.NotApplicable) Tone = "neutral";
        else Tone = r.Readiness switch
        {
            RequirementReadiness.ManagerVerified or RequirementReadiness.AssessorAccepted => "ok",
            RequirementReadiness.ReadyForReview => "review",
            RequirementReadiness.InProgress => "warn",
            _ => "neutral",
        };
        ReadinessWord = r.Readiness switch
        {
            RequirementReadiness.InProgress => "in progress",
            RequirementReadiness.ReadyForReview => "ready for review",
            RequirementReadiness.ManagerVerified => "verified",
            RequirementReadiness.AssessorAccepted => "assessor outcome recorded",
            _ => "not started",
        };
        OwnerUnassigned = r.OwnerUnassigned;
        Applicability = r.Applicability;
    }
}