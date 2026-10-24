using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;

namespace QsppManager.Desktop.Views.Today;

/// <summary>Custom-drawn pieces of the redesigned Today screen (24 Oct). Everything reads its
/// colours from the theme tokens (no hex in views); rebuild on theme change re-creates the
/// page, so captured brushes stay valid.</summary>
public static class TodayInk
{
    public static IBrush Br(string key) => Ui.Br(key) ?? Brushes.Gray;
}

/// <summary>The hero's run line: a 1px track from the start anchor (or today when no anchor
/// is recorded — never a guessed start date) to the deadline, a filled segment up to today, a
/// 7pt dot for today, and small ticks for recorded milestones. The acid accent appears here
/// and nowhere else on the screen.</summary>
public class RunLine : Control
{
    public DateOnly? Start { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateOnly Today { get; set; }
    public List<(DateOnly Date, string Label, bool PlanningOnly)> Milestones { get; } = [];

    public override void Render(DrawingContext ctx)
    {
        if (Deadline is not { } deadline || !(deadline > Today)) return;
        var start = Start is { } s && s <= Today ? s : Today;
        var total = deadline.DayNumber - start.DayNumber;
        if (total <= 0) return;
        var toDeadline = deadline.DayNumber - Today.DayNumber;
        var todayX = (total - toDeadline) / (double)total;

        var y = Bounds.Height / 2 - 4;   // leaves room for the end labels under the line
        var track = new Pen(TodayInk.Br("Brush.HeroDiv").ToImmutable(), 1);
        ctx.DrawLine(track, new(0, y), new(Bounds.Width, y));

        // 3F #8: label the ends of the run line — start (or "today" when no anchor was
        // recorded) at the left, the deadline at the right.
        var heroMuted = TodayInk.Br("Brush.HeroMuted").ToImmutable();
        var au = new System.Globalization.CultureInfo("en-AU");
        var labelFace = new Typeface(new FontFamily(Ui.FontFamilies.Ui));
        FormattedText Ft(string t)
        {
            var ft = new FormattedText(t, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, labelFace, 9, heroMuted);
            return ft;
        }
        var leftLabel = Ft(Start is { } s0 ? s0.ToString("d MMM", au) : "today");
        var rightLabel = Ft(deadline.ToString("d MMM yyyy", au));
        var labelY = y + 10;
        ctx.DrawText(leftLabel, new(0, labelY));
        ctx.DrawText(rightLabel, new(Bounds.Width - rightLabel.Width, labelY));

        var acid = TodayInk.Br("Brush.Acid").ToImmutable();
        var fillEnd = todayX * Bounds.Width;
        if (fillEnd > 1)
            ctx.DrawLine(new Pen(acid, 2), new(0, y), new(fillEnd, y));

        // Milestone ticks: recorded events as solid marks, conservative-reading windows as hollow.
        foreach (var (date, _, planningOnly) in Milestones)
        {
            if (date <= start || date >= deadline) continue;
            var x = (date.DayNumber - start.DayNumber) / (double)total * Bounds.Width;
            var tickBrush = planningOnly ? TodayInk.Br("Brush.HeroMuted").ToImmutable() : acid;
            ctx.DrawRectangle(planningOnly ? null : tickBrush, new Pen(tickBrush, 1), new Rect(x - 2.5, y - 4, 5, 8), 1, 1);
        }

        ctx.DrawEllipse(acid, null, new(fillEnd, y), 3.5, 3.5);
    }
}

/// <summary>The 12-month "year ahead" strip: equal month columns, tracked-caps month names,
/// 1px baseline, a vertical hairline for today, and a small shape per dated obligation whose
/// FORM encodes its category (circle people, square equipment, diamond evidence, triangle
/// program, bar the featured deadline — drawn tallest). Marks are separate interactive
/// elements laid over the drawn rules (hover = its tooltip, click = opens the record); the
/// drawing itself carries everything else.</summary>
public class YearAheadStrip : Control
{
    public List<TodayViewModel.MonthMark> Marks { get; } = [];
    public List<(int Year, int Month)> Window { get; set; } = [];
    public DateOnly Today { get; set; }
    public System.Windows.Input.ICommand? OpenCommand { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        InvalidateVisual();
        return base.MeasureOverride(availableSize);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        foreach (var mark in Marks)
        {
            if (!HitRect(mark).Contains(pt.Position)) continue;
            if (OpenCommand?.CanExecute(null) == true)
            {
                // The strip hands the CATEGORY through a tag row; the record it opens matches.
                var row = new TodayViewModel.LedgerRow(mark.RecordId, "", mark.CategoryKey, "", "", "", "", "", "", "", "", false);
                OpenCommand.Execute(row);
            }
            e.Handled = true;
            return;
        }
    }

    /// <summary>Gives each mark a generous hit area (14pt square around its centre).</summary>
    private Rect HitRect(TodayViewModel.MonthMark mark)
    {
        var (x, y) = MarkPoint(mark);
        return new Rect(x - 9, y - 12, 18, 20);
    }

    private (double x, double y) MarkPoint(TodayViewModel.MonthMark mark)
    {
        var colW = Bounds.Width / Math.Max(1, Window.Count);
        var top = 26;                                  // month labels band
        var baseline = Bounds.Height - 2;              // the 1px baseline
        var y = baseline - (mark.Tall ? 34 : 12);
        return (colW * (mark.MonthIndex + mark.Fraction), y);
    }

    public override void Render(DrawingContext ctx)
    {
        if (Window.Count == 0) return;
        var colW = Bounds.Width / Window.Count;
        var au = new System.Globalization.CultureInfo("en-AU");
        var ink = TodayInk.Br("Brush.Ink").ToImmutable();
        var muted = TodayInk.Br("Brush.Muted").ToImmutable();
        var secondary = TodayInk.Br("Brush.Muted").ToImmutable();
        var baseline = Bounds.Height - 2;
        var top = 26;

        // Month labels: 10.5pt tracked caps, one per column (hair-space tracking in canvas text).
        for (var i = 0; i < Window.Count; i++)
        {
            var name = string.Join('\u2009', new DateTime(Window[i].Year, Window[i].Month, 1).ToString("MMM", au).ToUpperInvariant().ToCharArray());
            var ft = new FormattedText(name, au, FlowDirection.LeftToRight, new Typeface(new FontFamily(Ui.FontFamilies.Ui)), 10.5, muted);
            ctx.DrawText(ft, new((i + 0.5) * colW - ft.Width / 2, 2));
        }

        // Month hairlines + baseline.
        var hairline = new Pen(TodayInk.Br("Brush.Divider").ToImmutable(), 1);
        for (var i = 0; i <= Window.Count; i++)
            ctx.DrawLine(hairline, new(i * colW, top), new(i * colW, baseline));
        ctx.DrawLine(new Pen(TodayInk.Br("Brush.Ink").ToImmutable(), 1), new(0, baseline), new(Bounds.Width, baseline));

        // Today hairline.
        var tIdx = Window.IndexOf((Today.Year, Today.Month));
        if (tIdx >= 0)
        {
            var tFrac = (Today.Day - 1 + 0.5) / DateTime.DaysInMonth(Today.Year, Today.Month);
            var tx = (tIdx + tFrac) * colW;
            ctx.DrawLine(new Pen(TodayInk.Br("Brush.Green").ToImmutable(), 1), new(tx, top), new(tx, baseline));
        }

        // Marks: shape encodes category, colour secondary; the featured deadline is a tall bar.
        // 3F #8: a LONE mark gets a text label so it never floats unexplained.
        var loneMark = Marks.Count == 1 ? Marks[0] : null;
        foreach (var mark in Marks)
        {
            if (loneMark == mark)
            {
                var loneFt = new FormattedText(mark.Tooltip, au, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(Ui.FontFamilies.Ui)), 10, muted);
                var (lx, ly) = MarkPoint(mark);
                var lxClamped = lx + 8 + loneFt.Width <= Bounds.Width ? lx + 8 : Math.Max(2, lx - 8 - loneFt.Width);
                ctx.DrawText(loneFt, new(lxClamped, ly + 14));
            }
            var (x, y) = MarkPoint(mark);
            var b = mark.Tall ? 34 : 12;
            if (mark.Shape == "bar")
                ctx.DrawRectangle(ink, null, new Rect(x - 1.5, y, 3, b));
            else if (mark.Shape == "circle")
                ctx.DrawEllipse(secondary, null, new(x, y + b - 6), 4, 4);
            else if (mark.Shape == "square")
                ctx.DrawRectangle(secondary, null, new Rect(x - 4, y + b - 9, 8, 8), 1, 1);
            else if (mark.Shape == "diamond")
                ctx.DrawGeometry(secondary, null, DiamondGeometry(new(x, y + b - 12), 4, 12));
            else // triangle (program)
                ctx.DrawGeometry(secondary, null, TriangleGeometry(new(x, y + b - 12), 10));
        }
    }

    private static Geometry TriangleGeometry(Point top, double height)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new(top.X, top.Y), true);
            g.LineTo(new(top.X + height / 2, top.Y + height));
            g.LineTo(new(top.X - height / 2, top.Y + height));
            g.EndFigure(true);
        }
        return geo;
    }

    private static Geometry DiamondGeometry(Point top, double halfWidth, double height)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new(top.X, top.Y), true);
            g.LineTo(new(top.X + halfWidth, top.Y + height / 2));
            g.LineTo(new(top.X, top.Y + height));
            g.LineTo(new(top.X - halfWidth, top.Y + height / 2));
            g.EndFigure(true);
        }
        return geo;
    }
}

/// <summary>Segmented 1px bar for portal request counts (surveillance round). Each segment
/// also labelled in words beside the bar and in the row lead.</summary>
public static class SegmentedBar
{
    public static Control Build(int items, int withEvidence, int inProgress, int verified)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = 1, VerticalAlignment = VerticalAlignment.Center };
        var total = Math.Max(1, items);
        var green = TodayInk.Br("Brush.Green").ToImmutable();
        var tint = TodayInk.Br("Brush.SelBg").ToImmutable();
        void Add(int count, bool filled)
        {
            if (count <= 0) return;
            bar.Children.Add(new Border { Width = 26.0 * count / total, Height = 1, Background = filled ? green : tint });
        }
        Add(withEvidence, true);
        Add(inProgress, false);
        Add(verified, false);
        var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        wrap.Children.Add(bar);
        return wrap;
    }
}

/// <summary>Leader-dot ledger row factory (the accreditation position block). Label left in
/// 14pt, a hairline running out to the count, count right-aligned in 17pt serif. The row is
/// one button that opens the filtered records.</summary>
public static class LeaderRow
{
    public static Control Build(string label, string count, System.Windows.Input.ICommand command, bool emphasized = false)
    {
        var b = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new(0, 6),
            Background = null,
            BorderThickness = new(0),
            CornerRadius = new(4),
            Height = 34,
        };
        b.Classes.Add("ledger");
        var grid = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        var lbl = new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        if (emphasized) lbl.FontWeight = FontWeight.Medium;
        grid.Children.Add(lbl);
        var dots = new Border { Height = 1, Margin = new(10, 2), Background = TodayInk.Br("Brush.Divider"), VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Controls.Grid.SetColumn(dots, 1);
        grid.Children.Add(dots);
        var cnt = new TextBlock
        {
            Text = count, FontSize = 17, TextAlignment = TextAlignment.Right,
        };
        cnt.FontFamily = new FontFamily(Ui.FontFamilies.Serif);
        Avalonia.Controls.Grid.SetColumn(cnt, 2);
        grid.Children.Add(cnt);
        b.Content = grid;
        b.Command = command;
        Ui.Ax(b, label);
        return b;
    }
}

public static class UiFontAdditions
{
    public static TextBlock Serif(this TextBlock tb, double size)
    {
        tb.FontFamily = new FontFamily(Ui.FontFamilies.Serif);
        tb.FontSize = size;
        return tb;
    }
}