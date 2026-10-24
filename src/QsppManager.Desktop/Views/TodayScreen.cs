using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Desktop.Views.Today;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Desktop.Views;

/// <summary>The redesigned Today screen (24 Oct), built in code against the tested planning
/// layer. Sections: greeting, hero deadline panel, accreditation position, the year ahead,
/// renewals ledger, this week / review inbox, activity. Token colours only; the only filled
/// panel is the hero; the acid accent appears only in the hero's run line.</summary>
public class TodayScreen
{
    private const int LedgerPeek = 8;
    private readonly TodayViewModel _vm;

    // VM-owned dynamic values are pushed in PostRefresh; these are the touched controls.
    private TextBlock _eyebrow = null!;
    private TextBlock _greeting = null!;
    private Grid _heroGrid = null!;
    private Border _heroFull = null!;
    private StackPanel _heroEmpty = null!;
    private TextBlock _heroNumeral = null!;
    private Panel? _heroNumeralRow;
    private TextBlock _heroDaysWord = null!;
    private TextBlock _heroDateLine = null!;
    private TextBlock _heroTitle = null!;
    private TextBlock _heroBody = null!;
    private TextBlock _heroProvenance = null!;
    private TextBlock _heroConsequence = null!;
    private Border _planTag = null!;
    private TextBlock _afterLine = null!;
    private RunLine _run = null!;
    private StackPanel _positionPanel = null!;
    private StackPanel _positionBlock = null!;
    private Panel _roundBarHost = null!;
    private TextBlock _roundLead = null!;
    private TextBlock _roundNext = null!;
    private TextBlock _yearRange = null!;
    private YearAheadStrip _strip = null!;
    private TextBlock _ledgerEmpty = null!;
    private readonly Dictionary<string, StackPanel> _bucketPanels = [];
    private readonly Dictionary<string, Button> _showAllButtons = [];
    private readonly Dictionary<string, StackPanel> _bucketBlocks = [];
    private readonly HashSet<string> _fullBuckets = [];
    private Grid _twoCol = null!;
    private StackPanel _weekPanel = null!;
    private StackPanel _inboxPanel = null!;
    private TextBlock _weekEmpty = null!;
    private TextBlock _inboxEmpty = null!;
    private StackPanel _activityBody = null!;
    private TextBlock _activityCount = null!;

    public TodayScreen(TodayViewModel vm)
    {
        _vm = vm;
        Root = Build(vm);
        // Self-contained responsive rule: works in the TodayView host AND in
        // off-screen renders (which bypass TodayView entirely).
        Root.SizeChanged += (_, e) => OnWidthChanged(e.NewSize.Width);
    }

    public Control Root { get; }

    // ---------------------------------------------------------------- build

    private Control Build(TodayViewModel vm)
    {
        var scroll = new ScrollViewer { Padding = new(0), Background = null };
        var outer = new StackPanel
        {
            Margin = new(32, 24, 32, 48),
            MaxWidth = 1240,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 30,
        };
        scroll.Content = outer;

        // Header
        var header = new StackPanel { Spacing = 4 };
        _eyebrow = new TextBlock { Classes = { "eyebrow" } };
        _greeting = new TextBlock { Classes = { "h1" } };
        header.Children.Add(_eyebrow);
        header.Children.Add(_greeting);
        outer.Children.Add(header);

        // Hero + position (7/5, collapses below 980)
        _heroGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("7*,24,5*") };
        _heroFull = BuildHeroFull(vm);
        Grid.SetColumn(_heroFull, 0);
        _heroEmpty = BuildHeroEmpty(vm);
        Grid.SetColumn(_heroEmpty, 0);
        var positionBlock = BuildPosition(vm);
        _positionBlock = positionBlock;
        Grid.SetColumn(positionBlock, 2);
        _heroGrid.Children.Add(_heroFull);
        _heroGrid.Children.Add(_heroEmpty);
        _heroEmpty.IsVisible = false;
        _heroGrid.Children.Add(positionBlock);
        outer.Children.Add(_heroGrid);

        // The year ahead
        var yearAhead = BuildYearAhead(vm);
        outer.Children.Add(yearAhead);

        // Renewals
        outer.Children.Add(BuildRenewals(vm));

        // This week / review inbox (two column, collapses below 980)
        _twoCol = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        var week = BuildWeek(vm);
        var inbox = BuildInbox(vm);
        Grid.SetColumn(week, 0);
        Grid.SetColumn(inbox, 2);
        _twoCol.Children.Add(week);
        _twoCol.Children.Add(inbox);
        outer.Children.Add(_twoCol);

        // Activity
        outer.Children.Add(BuildActivity(vm));

        OnWidthChanged(1200);
        return scroll;
    }

    /// <summary>--assert-today hook (3A): the numerals row, date line and title bounds
    /// after a layout pass. The layout assertions in TodayLayoutAssert read these.</summary>
    internal (Rect Numeral, Rect Date, Rect Title) HeroQaRects()
    {
        var numeral = _heroNumeralRow?.Bounds ?? _heroNumeral.Bounds;
        return (numeral, _heroDateLine.Bounds, _heroTitle.Bounds);
    }

    private Border BuildHeroFull(TodayViewModel vm)
    {
        Cursor? hand = new Cursor(StandardCursorType.Hand);
        var body = new StackPanel { Spacing = 10 };

        var numeralRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Top,
        };
        // 3A: numeral + days live in a row sized by the numeral's line height (the
        // display.hero style), no negative margins anywhere; the date line sits 14 below.
        _heroNumeralRow = numeralRow;
        _heroNumeral = new TextBlock { Classes = { "displayHero" }, VerticalAlignment = VerticalAlignment.Bottom };
        _heroDaysWord = new TextBlock { Classes = { "heroDays" }, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 9) };
        numeralRow.Children.Add(_heroNumeral);
        numeralRow.Children.Add(_heroDaysWord);

        _heroDateLine = new TextBlock { Classes = { "heroDate" }, Margin = new(0, 14, 0, 0) };
        _heroTitle = new TextBlock { Classes = { "heroTitle" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 0) };
        _heroBody = new TextBlock { Classes = { "heroMeta" }, TextWrapping = TextWrapping.Wrap };

        _run = new RunLine { Height = 26, Margin = new(0, 12, 4, 2) };   // labels under the line (3F #8)

        var provRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _heroProvenance = new TextBlock { Classes = { "heroMeta" }, FontSize = 11.5 };
        _heroConsequence = new TextBlock { Classes = { "heroMeta" }, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        _planTag = new Border
        {
            CornerRadius = new(3),
            Padding = new(6, 1.5),
            Background = TodayInk.Br("Brush.HeroDiv"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Classes = { "eyebrow" }, FontSize = 9, Foreground = TodayInk.Br("Brush.HeroFg"), Text = "PLANNING ONLY" },
        };
        provRow.Children.Add(_heroProvenance);
        provRow.Children.Add(_planTag);

        _afterLine = new TextBlock { Classes = { "heroMeta" }, FontSize = 12, Margin = new(0, 6, 0, 0) };

        body.Children.Add(numeralRow);
        body.Children.Add(_heroDateLine);
        body.Children.Add(_heroTitle);
        body.Children.Add(_heroBody);
        body.Children.Add(_run);
        body.Children.Add(provRow);
        body.Children.Add(_heroConsequence);   // 3F #8: the consequence on its own line
        body.Children.Add(_afterLine);

        var panel = new Border
        {
            CornerRadius = new(10),
            Padding = new(26, 22, 26, 24),
            Background = TodayInk.Br("Brush.HeroBg"),
            Child = body,
            Cursor = hand,
        };

        // The whole hero opens the Assessments page; its automation name carries the countdown.
        panel.PointerReleased += (_, _) => vm.OpenHeroCommand.Execute(null);
        return panel;
    }

    /// <summary>3F #8 display formatter: "Source: PPA notice (ppaonline.com.au) · Confirmed".
    /// Pure display — the underlying provenance strings are untouched.</summary>
    private static string ProvenanceLine(QsppManager.Domain.Services.DeadlineInfo f)
    {
        var src = f.Source ?? "";
        string label = src switch
        {
            string s when s.StartsWith("PPA") => "PPA notice",
            string s when s.StartsWith("Assessment provider") => "Assessment provider",
            string s when s.StartsWith("T&C") => "Terms and conditions",
            string s when s.StartsWith("Program Rules") => "Program rules",
            string s when s.StartsWith("internal") => "Internal planning",
            string s when s.StartsWith("ISSUE-") => "Interpretation issue",
            _ => "",
        };
        string? host = null;
        var m = System.Text.RegularExpressions.Regex.Match(src, @"\(([a-z0-9.\-]+\.[a-z]{2,})\)");
        if (m.Success) host = m.Groups[1].Value;
        if (label == "")
        {
            // Fallback: first sentence-ish fragment of the raw source, trimmed.
            var cut = src.IndexOfAny(['(', '—', ':']);
            label = cut > 0 ? src[..cut].Trim() : (src.Length > 50 ? src[..50].TrimEnd() + "…" : src.Trim());
        }
        var status = f.PlanningOnly ? "Planning only" : "Confirmed";
        return "Source: " + label + (host is { } ? " (" + host + ")" : "") + " · " + status;
    }

    private static StackPanel BuildHeroEmpty(TodayViewModel vm)
    {
        var p = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
        p.Children.Add(Ui.H2("No QSPP deadline recorded"));
        p.Children.Add(new TextBlock
        {
            Classes = { "lead" },
            Text = "When the PPA notice or a program date is recorded, the countdown appears here.",
            MaxWidth = 520,
        });
        p.Children.Add(new Button { Content = "Open Assessments", Command = vm.OpenHeroCommand });
        return p;
    }

    private StackPanel BuildPosition(TodayViewModel vm)
    {
        var p = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        // eyebrow stays caps (caps are reserved for eyebrows, 3C)
        p.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = "ACCREDITATION POSITION" });
        _positionPanel = new StackPanel { Spacing = 0 };
        p.Children.Add(_positionPanel);

        p.Children.Add(new Border { Height = 1, Background = TodayInk.Br("Brush.Divider"), Margin = new(0, 14, 0, 12) });
        p.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = "SURVEILLANCE ROUND \u00B7 QCPP PORTAL" });
        _roundLead = new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        _roundBarHost = new Panel { Height = 3, Margin = new(0, 8, 0, 2) };
        _roundNext = new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
        p.Children.Add(_roundLead);
        p.Children.Add(BuildRoundLegend());
        p.Children.Add(_roundBarHost);
        p.Children.Add(_roundNext);
        return p;
    }

    private static Control BuildRoundLegend()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var green = TodayInk.Br("Brush.Green");
        var tint = TodayInk.Br("Brush.SelBg");
        var ink = TodayInk.Br("Brush.Ink");
        void Item(string text, IBrush chip)
        {
            row.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children =
                {
                    new Border { Width = 12, Height = 3, CornerRadius = new(1), Background = chip, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = text, FontSize = 10.5, Foreground = TodayInk.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center },
                },
            });
        }
        Item("with evidence", green);
        Item("awaiting evidence", tint);
        Item("verified", ink);
        return row;
    }

    private StackPanel BuildYearAhead(TodayViewModel vm)
    {
        var p = new StackPanel { Spacing = 10 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        head.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = "THE YEAR AHEAD" });
        _yearRange = new TextBlock { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(_yearRange);
        p.Children.Add(head);
        _strip = new YearAheadStrip { Height = 64, OpenCommand = vm.OpenLedgerCommand };
        p.Children.Add(_strip);
        p.Children.Add(new TextBlock
        {
            Classes = { "small" },
            Text = "\u25CF people \u00B7 \u25A0 equipment \u00B7 \u25C6 evidence \u00B7 \u25B3 program \u00B7 tall bar = featured deadline. Marks are clickable and open the record.",
            TextWrapping = TextWrapping.Wrap,
        });
        return p;
    }

    private Control BuildRenewals(TodayViewModel vm)
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = "RENEWALS, RECALIBRATIONS AND EXPIRIES" });
        _ledgerEmpty = new TextBlock
        {
            Classes = { "lead" },
            Text = "No renewals, recalibrations or expiries are recorded. Add dates under Registers, People and Documents.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 4, 0, 0),
        };
        p.Children.Add(_ledgerEmpty);

        _bucketPanels["b0"] = new StackPanel { Spacing = 2 };
        _bucketPanels["b1"] = new StackPanel { Spacing = 2 };
        _bucketPanels["b2"] = new StackPanel { Spacing = 2 };
        _showAllButtons["b0"] = GhostShowAll("b0");
        _showAllButtons["b1"] = GhostShowAll("b1");
        _showAllButtons["b2"] = GhostShowAll("b2");

        p.Children.Add(BucketBlock("NEXT 30 DAYS", "b0"));
        p.Children.Add(BucketBlock("31 TO 90 DAYS", "b1"));
        var later = BucketBlock("LATER THIS CYCLE", "b2");
        p.Children.Add(later);

        // Reference texts await their own table (proposal held); until approved the ledger
        // shows the honest quiet line. "Add one" opens Registers, where the table will live.
        var refRow = new Button
        {
            Classes = { "ghost" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new(0, 4),
            Content = new TextBlock { Text = "No reference texts (AMH, APF, eMIMS, eTG) recorded. Add one \u2192", FontSize = 12 },
            Command = vm.OpenRegistersCommand,
        };
        p.Children.Add(refRow);
        return p;
    }

    private Button GhostShowAll(string bucket)
    {
        var b = new Button
        {
            Classes = { "ghost" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new(0, 2),
            FontSize = 12,
        };
        b.Click += (_, _) =>
        {
            if (!_fullBuckets.Add(bucket)) _fullBuckets.Remove(bucket);
            var rows = bucket switch
            { "b0" => _vm.Bucket30, "b1" => _vm.Bucket90, _ => _vm.BucketLater };
            RefreshBucket(_vm, bucket, rows);
        };
        return b;
    }

    private StackPanel BucketBlock(string title, string bucket)
    {
        var block = new StackPanel { Spacing = 4 };
        block.Children.Add(new TextBlock { Classes = { "label" }, Text = title });
        block.Children.Add(_bucketPanels[bucket]);
        _showAllButtons[bucket].IsVisible = false;
        block.Children.Add(_showAllButtons[bucket]);
        _bucketBlocks[bucket] = block;
        return block;
    }

    private Control BuildWeek(TodayViewModel vm)
    {
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = "THIS WEEK" });
        _weekEmpty = new TextBlock { Classes = { "small" }, Text = "Nothing due this week.", Margin = new(0, 8, 0, 0) };
        _weekPanel = new StackPanel { Spacing = 2 };
        p.Children.Add(_weekPanel);
        p.Children.Add(_weekEmpty);
        return p;
    }

    private Control BuildInbox(TodayViewModel vm)
    {
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = "REVIEW INBOX" });
        _inboxEmpty = new TextBlock { Classes = { "small" }, Text = "The review inbox is clear.", Margin = new(0, 8, 0, 0) };
        _inboxPanel = new StackPanel { Spacing = 2 };
        p.Children.Add(_inboxPanel);
        p.Children.Add(_inboxEmpty);
        return p;
    }

    private Control BuildActivity(TodayViewModel vm)
    {
        _activityBody = new StackPanel { Spacing = 6, Margin = new(0, 12, 0, 0) };
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        headerRow.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = "ACTIVITY" });
        _activityCount = new TextBlock { Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center };
        headerRow.Children.Add(_activityCount);
        // 3F #8: Activity becomes a text disclosure, not a box that looks like a combobox.
        var toggle = new Avalonia.Controls.Button
        {
            Classes = { "ghost" }, Padding = new(0), Height = 24,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        var chevronPath = new Avalonia.Controls.Shapes.Path
        {
            Stroke = TodayInk.Br("Brush.Muted"), StrokeThickness = 1.5,
            Data = Avalonia.Media.Geometry.Parse("M 4,7 L 8,11 L 12,7"),
            Width = 14, Height = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        void SyncToggle()
        {
            // chevron alone: _activityCount stays in headerRow (double-parenting crash).
            toggle.Content = chevronPath;
        }
        toggle.Click += (_, _) =>
        {
            _activityBody.IsVisible = !_activityBody.IsVisible;
            chevronPath.Data = Avalonia.Media.Geometry.Parse(
                _activityBody.IsVisible ? "M 4,7 L 8,11 L 12,7" : "M 4,11 L 8,7 L 12,11");  // down = open
            SyncToggle();
        };
        headerRow.Children.Add(toggle);
        var host = new Avalonia.Controls.StackPanel { Spacing = 4 };
        host.Children.Add(headerRow);
        _activityBody.IsVisible = false;
        host.Children.Add(_activityBody);
        SyncToggle();
        return host;
    }

    // ---------------------------------------------------------------- populate

    /// <summary>Pushes the freshly-refreshed VM values into the built controls. Re-entrant:
    /// called on every VM.Refresh (30 s / midnight / after any record change).</summary>
    public void PostRefresh(TodayViewModel vm)
    {
        _eyebrow.Text = vm.Eyebrow;
        _greeting.Text = vm.Greeting;

        var brief = vm.Brief;
        if (brief.Featured is { } f && brief.FeaturedTone is { } tone)
        {
            _heroFull.IsVisible = true;
            _heroEmpty.IsVisible = false;
            var numeral = tone.State == TodayPlanning.Tone.Overdue
                ? Math.Abs(tone.DaysRemaining).ToString()
                : tone.DaysRemaining > 0 ? tone.DaysRemaining.ToString() : tone.Word;
            _heroNumeral.Text = numeral;
            _heroDaysWord.Text = tone.State switch
            {
                TodayPlanning.Tone.Overdue => "days overdue",
                _ when tone.DaysRemaining == 0 => "due today",
                _ => "days",
            };
            _heroDateLine.Text = vm.DeadlineFullDate;
            _heroTitle.Text = vm.DeadlineTitle;
            _heroBody.Text = vm.DeadlineBody;
            _heroProvenance.Text = ProvenanceLine(f);   // 3F #8 display formatter
            _heroConsequence.Text = f.Explanation;
            _planTag.IsVisible = f.PlanningOnly;
            _afterLine.Text = vm.AfterThis;
            Ui.Ax(_heroFull, vm.HeroAutomationName, "Opens the Assessments page");
            _run.Start = vm.RunStart;
            _run.Deadline = vm.DeadlineDate;
            _run.Today = TodayPlanning.MelbourneToday(DateTime.UtcNow);
            _run.Milestones.Clear();
            foreach (var m in brief.Milestones) _run.Milestones.Add(m);
            _run.InvalidateVisual();
        }
        else
        {
            _heroFull.IsVisible = false;
            _heroEmpty.IsVisible = true;
        }

        // Position rows
        _positionPanel.Children.Clear();
        foreach (var row in vm.Position)
            _positionPanel.Children.Add(LeaderRow.Build(row.Label, row.Count, row.Command, row.Label == "Overdue tasks" && row.Count != "0"));
        var (items, withEvidence, inProgress, verified) = vm.RoundCounts;
        _roundLead.Text = vm.RoundLead;
        _roundNext.Text = vm.RoundNextEvent;
        _roundBarHost.Children.Clear();
        var hasBar = items > 0;
        _roundBarHost.IsVisible = hasBar;   // 3F #8: no orphan dash when the round is empty
        _roundBarHost.Height = hasBar ? 3 : 0;
        _roundBarHost.Margin = hasBar ? new(0, 8, 0, 2) : new(0);
        if (hasBar) _roundBarHost.Children.Add(SegmentedBar.Build(items, withEvidence, inProgress, verified));

        // Year ahead
        _yearRange.Text = vm.YearAheadRange;
        _strip.Window = brief.YearAhead;
        _strip.Today = TodayPlanning.MelbourneToday(DateTime.UtcNow);
        _strip.Marks.Clear();
        foreach (var m in vm.Marks) _strip.Marks.Add(m);
        _strip.InvalidateVisual();

        // Renewals ledger
        RefreshBucket(vm, "b0", vm.Bucket30);
        RefreshBucket(vm, "b1", vm.Bucket90);
        RefreshBucket(vm, "b2", vm.BucketLater);
        var anyRows = vm.Bucket30.Count + vm.Bucket90.Count + vm.BucketLater.Count;
        _ledgerEmpty.IsVisible = anyRows == 0;
        // A group takes room; when it is quiet it steps aside unless the whole ledger is.
        _bucketBlocks["b0"].IsVisible = vm.Bucket30.Count > 0;
        _bucketBlocks["b1"].IsVisible = vm.Bucket90.Count > 0;
        _bucketBlocks["b2"].IsVisible = vm.BucketLater.Count > 0;

        // This week + inbox
        _weekPanel.Children.Clear();
        foreach (var t in vm.ThisWeek) _weekPanel.Children.Add(TaskRow(t, vm));
        _weekEmpty.IsVisible = vm.ThisWeek.Count == 0;
        _inboxPanel.Children.Clear();
        foreach (var e in vm.Inbox) _inboxPanel.Children.Add(InboxRow(e, vm));
        _inboxEmpty.IsVisible = vm.Inbox.Count == 0;

        // Activity
        _activityCount.Text = $"{vm.Activity.Count} recent";
        _activityBody.Children.Clear();
        foreach (var a in vm.Activity) _activityBody.Children.Add(ActivityRow(a, vm));
    }

    private void RefreshBucket(TodayViewModel vm, string bucket, IReadOnlyList<TodayViewModel.LedgerRow> rows)
    {
        var panel = _bucketPanels[bucket];
        panel.Children.Clear();
        var visible = _fullBuckets.Contains(bucket) ? rows : rows.Take(LedgerPeek).ToList();
        foreach (var r in visible) panel.Children.Add(LedgerRowView(r, vm));
        var btn = _showAllButtons[bucket];
        btn.Content = _fullBuckets.Contains(bucket) ? "Show recent 8" : $"Show all {rows.Count}";
        btn.IsVisible = rows.Count > LedgerPeek;
    }

    private static Control LedgerRowView(TodayViewModel.LedgerRow r, TodayViewModel vm)
    {
        var glyph = r.CategoryKey switch
        {
            "people" => "\u25CF",
            "equipment" => "\u25A0",
            "documents" => "\u25C6",
            _ => "\u25B3",
        };
        var overInk = r.Overdue ? TodayInk.Br("Brush.Alert") : TodayInk.Br("Brush.Ink");

        var inner = new Avalonia.Controls.Grid
        {
            ColumnDefinitions = new ColumnDefinitions("20,*,110,92,70,90,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var shape = new TextBlock { Text = glyph, FontSize = 10, Foreground = overInk, VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(shape);

        var nameCol = new StackPanel { Spacing = 1 };
        var name = new TextBlock { Text = r.Item, FontSize = 13.5, Foreground = overInk, TextTrimming = TextTrimming.CharacterEllipsis };
        nameCol.Children.Add(name);
        if (r.Caption.Length > 0)
            nameCol.Children.Add(new TextBlock { Text = r.Caption, FontSize = 11, Foreground = TodayInk.Br("Brush.Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(nameCol, 1);
        inner.Children.Add(nameCol);

        var action = new TextBlock { Text = r.Action, FontSize = 11.5, Foreground = TodayInk.Br("Brush.Muted"), TextAlignment = TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(action, 2);
        inner.Children.Add(action);

        var date = new TextBlock { Text = r.DateLabel, Classes = { "monoDate" }, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(date, 3);
        inner.Children.Add(date);

        var days = new TextBlock
        {
            Text = r.DaysLabel,
            Classes = { "monoSm" },
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = r.Overdue ? TodayInk.Br("Brush.Alert") : TodayInk.Br("Brush.Muted"),
        };
        Grid.SetColumn(days, 4);
        inner.Children.Add(days);

        if (r.Owner.Length > 0)
        {
            var owner = new TextBlock { Text = r.Owner, FontSize = 11, Foreground = TodayInk.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
            Grid.SetColumn(owner, 5);
            inner.Children.Add(owner);
        }

        if (r.Chip.Length > 0)
        {
            var chip = Ui.Chip(r.Chip, r.ChipTone);
            Grid.SetColumn(chip, 6);
            inner.Children.Add(chip);
        }

        var b = new Button { Classes = { "ledger" }, Content = inner, Command = vm.OpenLedgerCommand, CommandParameter = r };
        Ui.Ax(b, $"{r.Item}: {r.Action}, {r.DateLabel}{(r.DaysLabel.Length > 0 ? ", " + r.DaysLabel : "")}");
        return b;
    }

    private static Control TaskRow(TodayViewModel.TaskRow t, TodayViewModel vm)
    {
        var overdue = t.DueLabel.EndsWith("overdue", StringComparison.Ordinal);
        var col = overdue ? TodayInk.Br("Brush.Alert") : TodayInk.Br("Brush.Ink");
        var nameCol = new StackPanel { Spacing = 2 };
        nameCol.Children.Add(new TextBlock { Text = t.Title, FontSize = 13.5, Foreground = col, TextTrimming = TextTrimming.CharacterEllipsis });
        nameCol.Children.Add(new TextBlock { Text = t.MetaLine, FontSize = 11, Foreground = TodayInk.Br("Brush.Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
        var due = new TextBlock { Text = t.DueLabel, Classes = { "monoSm" }, FontSize = 11, Foreground = overdue ? TodayInk.Br("Brush.Alert") : TodayInk.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center };
        var inner = new Avalonia.Controls.Grid
        {
            ColumnDefinitions = new ColumnDefinitions("12,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        inner.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new(2), Background = overdue ? TodayInk.Br("Brush.Alert") : TodayInk.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center });
        var nameHost = nameCol;
        Grid.SetColumn(nameHost, 1);
        inner.Children.Add(nameHost);
        Grid.SetColumn(due, 2);
        inner.Children.Add(due);
        var b = new Button
        {
            Classes = { "ledger" },
            Content = inner,
            Command = vm.OpenTaskCommand,
        };
        Ui.Ax(b, $"{t.Title}, due {t.DueLabel}");
        return b;
    }

    private static Control InboxRow(string e, TodayViewModel vm)
    {
        var b = new Button
        {
            Classes = { "ledger" },
            Content = new TextBlock { Classes = { "small" }, Text = e, TextTrimming = TextTrimming.CharacterEllipsis },
            Command = vm.OpenInboxCommand,
        };
        Ui.Ax(b, e);
        return b;
    }

    private static Control ActivityRow(TodayViewModel.ActivityRow a, TodayViewModel vm)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var line = new TextBlock { Classes = { "small" }, Text = a.Line, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(line);
        if (!string.IsNullOrEmpty(a.RevealPath))
        {
            var reveal = new Button { Classes = { "ghost" }, Padding = new(4, 0), FontSize = 11, Content = new TextBlock { Text = "reveal \u2192", FontSize = 11 } };
            reveal.Command = vm.RevealCommand;
            reveal.CommandParameter = a.RevealPath;
            Ui.Ax(reveal, $"Reveal {a.RawTooltip} in Finder");
            row.Children.Add(reveal);
        }
        Ui.Ax(row, a.Line);
        return row;
    }

    // ---------------------------------------------------------------- responsive

    public void OnWidthChanged(double contentWidth)
    {
        var single = contentWidth < 980;
        _heroGrid.ColumnDefinitions = single ? ColumnDefinitions.Parse("*") : ColumnDefinitions.Parse("7*,24,5*");
        _heroGrid.RowDefinitions = single ? new RowDefinitions("*,Auto") : new RowDefinitions("*");
        // Hero panels share row 0 in both modes; the position block drops to row 1 when single.
        Grid.SetRow(_heroFull, 0);
        Grid.SetRow(_heroEmpty, 0);
        Grid.SetColumn(_positionBlock, 0);
        Grid.SetRow(_positionBlock, single ? 1 : 0);
        if (single) Grid.SetColumn(_positionBlock, 0);
        else Grid.SetColumn(_positionBlock, 2);
        _positionBlock.Margin = single ? new(0, 24, 0, 0) : new(0);
        _twoCol.ColumnDefinitions = single ? new ColumnDefinitions("*") : new ColumnDefinitions("*,24,*");
        _twoCol.RowDefinitions = single ? new RowDefinitions("*,Auto") : new RowDefinitions("*");
        for (var i = 0; i < _twoCol.Children.Count; i++)
        {
            var child = _twoCol.Children[i];
            Grid.SetRow(child, single ? i : 0);
            Grid.SetColumn(child, 0);
            child.Margin = single && i > 0 ? new(0, 24, 0, 0) : new(0);
            if (!single) Grid.SetColumn(child, i == 0 ? 0 : 2);
        }
    }
}