using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaGroup = Avalonia.Media.GeometryGroup;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace QsppManager.Desktop;

/// <summary>Mac shell contracts. Views implement these so the toolbar/menu shortcuts
/// can route to the right control on the current page.</summary>
public interface IPageSearchFocus { void FocusSearch(); void SetFilter(string text); }

public interface IPagePrintRequest { void OpenPrintRequest(); }

/// <summary>The shared component kit for the macOS design system. Every code-built view
/// builds from these factories so spacing, type, colour and motion stay uniform.
/// Brushes are captured at build time from the active theme — the shell rebuilds
/// pages on Helpers.ThemeChanged.</summary>
public static class Ui
{
    public const double IconSizeDefault = 16;

    // ---------- text ----------
    public static TextBlock H1(string t) => new() { Classes = { "h1" }, Text = t };
    public static TextBlock H2(string t) => new() { Classes = { "h2" }, Text = t };
    public static TextBlock H3(string t) => new() { Classes = { "h3" }, Text = t };
    public static TextBlock Lead(string t) => new() { Classes = { "lead" }, Text = t, TextWrapping = TextWrapping.Wrap, MaxWidth = 900, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    public static TextBlock Small(string t) => new() { Classes = { "small" }, Text = t, TextWrapping = TextWrapping.Wrap };
    public static TextBlock Label(string t) => new() { Classes = { "label" }, Text = t };
    public static TextBlock Para(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, MaxWidth = 820, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    public static TextBlock Mono(string t) => new() { Classes = { "mono" }, Text = t };

    // ---------- scaffolding ----------
    /// <summary>Standard page scaffold: scrollable, left-aligned, max width 1140.</summary>
    public static ScrollViewer Page(params Control[] children)
    {
        var sp = new StackPanel { Margin = new(36, 30, 36, 44), Spacing = 14, MaxWidth = 1140, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var c in children) sp.Children.Add(c);
        return new ScrollViewer { Content = sp };
    }

    /// <summary>Stack aligned vertically (spacing applied).</summary>
    public static StackPanel Col(double spacing, params Control[] c)
    {
        var sp = new StackPanel { Spacing = spacing };
        foreach (var x in c) sp.Children.Add(x);
        return sp;
    }

    /// <summary>Stack aligned horizontally (spacing applied).</summary>
    public static StackPanel Row(double spacing, params Control[] c)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var x in c) sp.Children.Add(x);
        return sp;
    }

    public static Control Spacer(double h) => new Border { Height = h };

    /// <summary>Rounded surface card with hairline border.</summary>
    public static Border Card(Control? content, double vPad = 16, double hPad = 18, double radius = 9) => new()
    {
        Padding = new(hPad, vPad),
        CornerRadius = new(radius),
        Background = Br("Brush.Surface"),
        BorderBrush = Br("Brush.Divider"),
        BorderThickness = new(1),
        Child = content,
    };

    /// <summary>Card with a heading row (title + optional trailing content) above a body.</summary>
    public static Border Section(string title, Control body, Control? trailing = null, double vPad = 16)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(new TextBlock { Classes = { "h3" }, Text = title, VerticalAlignment = VerticalAlignment.Center });
        if (trailing != null) head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { trailing } });
        var bodySp = new StackPanel { Spacing = 12 };
        bodySp.Children.Add(head);
        bodySp.Children.Add(body);
        return Card(bodySp, vPad);
    }

    /// <summary>Single place that attaches automation names/help so voice control and
    /// screen readers see meaningful labels on every primary control.</summary>
    internal static void Ax(Control c, string name, string? help = null)
    {
        c.SetValue(AutomationProperties.NameProperty, name);
        if (!string.IsNullOrEmpty(help)) c.SetValue(AutomationProperties.HelpTextProperty, help);
    }

    /// <summary>Empty state: icon, title, hint and an optional action.</summary>
    public static Control EmptyState(string iconName, string title, string hint, Control? action = null)
    {
        var col = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 430 };
        col.Children.Add(new Border
        {
            Width = 46, Height = 46, CornerRadius = new(23),
            Background = Br("Brush.Sage"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Lucide(iconName, 20, Br("Brush.Green"), center: true),
        });
        col.Children.Add(new TextBlock { Classes = { "h3" }, Text = title, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        col.Children.Add(new TextBlock
        {
            Classes = { "small" }, Text = hint, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (action != null) col.Children.Add(action);
        return new Border
        {
            Padding = new(24, 34), CornerRadius = new(9),
            Background = Br("Brush.Surface"), BorderBrush = Br("Brush.Divider"), BorderThickness = new(1),
            Child = col,
        };
    }

    // ---------- buttons ----------
    public static Button Btn(string text,
         System.Windows.Input.ICommand? command = null, string classes = "", int minWidth = 0, string? axName = null, string? axHelp = null)
    {
        var b = new Button { Content = text };
        if (command != null) b.Command = command;
        if (minWidth > 0) b.MinWidth = minWidth;
        foreach (var cls in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(cls);
        Ax(b, axName ?? text, axHelp);
        return b;
    }

    public static Button PrimaryBtn(string text, System.Windows.Input.ICommand? command = null, int minWidth = 0, string? axName = null, string? axHelpText = null)
        => Btn(text, command, "primary", minWidth, axName, axHelpText);

    public static Button GhostBtn(string text, System.Windows.Input.ICommand? command = null, int minWidth = 0, string? axName = null, string? axHelpText = null)
        => Btn(text, command, "ghost", minWidth, axName, axHelpText);

    public static Button DangerBtn(string text, System.Windows.Input.ICommand? command = null, int minWidth = 0, string? axName = null, string? axHelpText = null)
        => Btn(text, command, "destructive", minWidth, axName, axHelpText);

    /// <summary>Button rows WRAP instead of clipping: a horizontal StackPanel pushes its
    /// fifth button past the window edge where it becomes invisible-until-resized (found
    /// in the audit: the requirement evidence actions). WrapPanel keeps every action
    /// visible at any window width; buttons land on a second line when needed. Avalonia's
    /// pinned WrapPanel has no Spacing, so the gap is a per-button margin (right + bottom)
    /// — edges keep the visual rhythm of a spaced row and the wrapped second line.</summary>
    public static Control BtnRow(params Button[] buttons)
    {
        var wp = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var x in buttons)
        {
            x.Margin = new(0, 0, 8, 8);
            wp.Children.Add(x);
        }
        return wp;
    }

    /// <summary>Apply the same wrap-gap rhythm to a hand-built WrapPanel of buttons.</summary>
    public static void WrapGaps(WrapPanel row)
    {
        foreach (var c in row.Children) if (c is Control ct) ct.Margin = new(0, 0, 8, 8);
    }

    // ---------- form ----------
    public static Control Labeled(string label, Control field)
    {
        var sp = new StackPanel { Spacing = 5 };
        sp.Children.Add(Ui.Label(label));
        sp.Children.Add(field);
        return sp;
    }

    /// <summary>A small-caps label above its field with breathing room. Use wherever a
    /// labelled input is built by hand — a bare `StackPanel { labels + field }` has no
    /// spacing and reads cramped (the mistake seen across v1 dialogs); every labelled
    /// field in the app must go through this so the rhythm stays consistent.</summary>
    public static StackPanel FormField(string label, Control field)
    {
        var sp = new StackPanel { Spacing = 6 };
        sp.Children.Add(Ui.Label(label));
        sp.Children.Add(field);
        return sp;
    }

    public static Grid FormGrid(int labelWidth, params (string label, Control field)[] rows)
    {
        var g = new Avalonia.Controls.Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*"),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rows.Length))),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i].label.Length > 0)
            {
                var lbl = new TextBlock { Classes = { "label" }, Text = rows[i].label, Margin = new(0, 7, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                Avalonia.Controls.Grid.SetRow(lbl, i);
                g.Children.Add(lbl);
            }
            Avalonia.Controls.Grid.SetRow(rows[i].field, i);
            Avalonia.Controls.Grid.SetColumn(rows[i].field, 1);
            g.Children.Add(rows[i].field);
        }
        return g;
    }

    // ---------- chips / status ----------
    /// <summary>Tone chips carry their meaning in text as well as colour.</summary>
    public static Border Chip(string text, string tone = "neutral", bool mono = false)
    {
        var (bg, fg) = Tone(tone);
        var tb = new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold,
            Foreground = fg, TextWrapping = TextWrapping.NoWrap,
        };
        if (mono) tb.FontFamily = new FontFamily(FontFamilies.Mono);
        return new Border
        {
            CornerRadius = new(5), Padding = new(8, 3),
            Background = bg, BorderThickness = new(0),
            Child = tb,
        };
    }

    private static (IBrush? bg, IBrush? fg) Tone(string tone) => tone switch
    {
        "ok" => (Br("Brush.OkBg"), Br("Brush.Green")),
        "open" => (Br("Brush.OkBg"), Br("Brush.Green")),
        "warn" => (Br("Brush.Sage"), Br("Brush.Caution")),
        "bad" => (Br("Brush.DangerBg"), Br("Brush.Alert")),
        "info" => (Br("Brush.Sage"), Br("Brush.Muted")),
        _ => (Br("Brush.Sage"), Br("Brush.Ink")),
    };

    // ---------- data grids ----------
    public static Avalonia.Controls.DataGrid Grid(int rowHeight = 36) => new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, RowHeight = rowHeight,
        HeadersVisibility = Avalonia.Controls.DataGridHeadersVisibility.Column,
        SelectionMode = Avalonia.Controls.DataGridSelectionMode.Single,
    };

    public static Avalonia.Controls.DataGridTextColumn Col(string header, string path, double width) =>
        new() { Header = header, Binding = new Binding(path), Width = new DataGridLength(width) };

    public static Avalonia.Controls.DataGridTemplateColumn MonoCol(string header, string path, double width) =>
        new()
        {
            Header = header,
            Width = new DataGridLength(width),
            CellTemplate = new FuncDataTemplate<object>((_, _) =>
            {
                var tb = new TextBlock { Classes = { "monoSm" }, VerticalAlignment = VerticalAlignment.Center };
                tb.Bind(TextBlock.TextProperty, new Binding(path));
                return tb as Control;
            }, true),
        };

    public static Avalonia.Controls.DataGridTemplateColumn WrapCol(string header, string path, double width) =>
        new()
        {
            Header = header,
            Width = new DataGridLength(width),
            CellTemplate = new FuncDataTemplate<object>((_, _) =>
            {
                var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                tb.Bind(TextBlock.TextProperty, new Binding(path));
                return tb as Control;
            }, true),
        };

    // ---------- fonts / icons ----------
    public static class FontFamilies
    {
        public const string Mono = "JetBrains Mono, SF Mono, Menlo, Consolas, monospace";
    }

    /// <summary>Lucide icon rendered from the vendored SVG (ISC-licensed) as stroke
    /// geometry — no external SVG package needed.</summary>
    public static Control Lucide(string name, double size = IconSizeDefault, IBrush? stroke = null, bool center = false)
    {
        return new AvaloniaPath
        {
            Data = Shape(name),
            Stretch = Stretch.Uniform,
            Stroke = stroke ?? Br("Brush.Ink"),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = null,
            Width = size, Height = size,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = center ? VerticalAlignment.Center : VerticalAlignment.Top,
        };
    }


    public static Geometry? Shape(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return LucideIconCache.Get(name); }
        catch { return null; }
    }

    public static IBrush? Br(string key) => Helpers.Brush(key);

    /// <summary>Small two-column fact row for the About dialog (label / value).</summary>
    public static Control InfoLine(string label, string value)
    {
        var grid = new AvaloniaGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*,12") };
        var lbl = new TextBlock
        {
            Text = label,
            Classes = { "label" },
            VerticalAlignment = VerticalAlignment.Center,
        };
        var val = new TextBlock
        {
            Text = value,
            Classes = { "monoSm" },
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AvaloniaGrid.SetColumn(lbl, 0);
        AvaloniaGrid.SetColumn(val, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(val);
        return new Border
        {
            Background = Br("Brush.Surface"),
            BorderBrush = Br("Brush.Divider"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 8),
            Child = grid,
        };
    }

    private sealed class LucideIconCache
    {
        private static readonly ConcurrentDictionary<string, Geometry> Cache = new();

        public static Geometry Get(string name) => Cache.GetOrAdd(name, n2 => ParseShape(ReadIconInternal(n2)));

        /// <summary>Combine every geometry element into one group that the Path control
        /// stretches uniformly into its own width/height box.</summary>
        internal static Geometry ParseShape(string xml)
        {
            var geoms = ParseGeoms(xml);
            if (geoms.Count == 0) return new AvaloniaGroup();
            if (geoms.Count == 1) return geoms[0];
            var group = new AvaloniaGroup();
            foreach (var geom in geoms) group.Children.Add(geom);
            return group;
        }

        private static string ReadIconInternal(string name)
        {
            try
            {
                using var s = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://QsppManager.Desktop/Assets/Icons/{name}.svg"));
                using var sr = new StreamReader(s);
                return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[icon] {name}: missing or unreadable ({ex.Message})");
                return "";
            }
        }

        internal static List<Avalonia.Media.Geometry> ParseGeoms(string xml)
        {
            var geoms = new List<Avalonia.Media.Geometry>();
            foreach (Match m in PathRegex.Matches(xml))
                Add(geoms, m.Groups[1].Value);

            foreach (Match m in CircleRegex.Matches(xml))
            {
                if (!TryD(m.Groups[1].Value, out var cx) || !TryD(m.Groups[2].Value, out var cy) || !TryD(m.Groups[3].Value, out var r)) continue;
                Add(geoms, $"M {cx - r} {cy} A {r} {r} 0 1 0 {cx + r} {cy} A {r} {r} 0 1 0 {cx - r} {cy} Z");
            }

            foreach (Match m in LineRegex.Matches(xml))
                Add(geoms, $"M {m.Groups[1].Value} {m.Groups[2].Value} L {m.Groups[3].Value} {m.Groups[4].Value}");

            foreach (Match m in RectRegex.Matches(xml))
            {
                if (!TryD(m.Groups[1].Value, out var w) || !TryD(m.Groups[2].Value, out var h) || !TryD(m.Groups[3].Value, out var x) || !TryD(m.Groups[4].Value, out var y)) continue;
                Add(geoms, $"M {x} {y} H {x + w} V {y + h} H {x} Z");
            }

            foreach (Match m in PolyRegex.Matches(xml))
            {
                var sb = new StringBuilder("M ");
                var first = true;
                foreach (var pair in Regex.Split(m.Groups[1].Value.Trim(), "\\s+"))
                {
                    if (!Regex.IsMatch(pair, "^[0-9.,-]+$")) continue;
                    var xy = pair.Split(',');
                    if (xy.Length < 2) continue;
                    sb.Append(first ? xy[0] : $" L {xy[0]}");
                    sb.Append($" {xy[1]}");
                    first = false;
                }
                if (!first) Add(geoms, sb.ToString());
            }
            return geoms;
        }

        private static bool TryD(string raw, out double value) =>
            double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

        private static void Add(List<Avalonia.Media.Geometry> geoms, string svgPathData)
        {
            try { geoms.Add(Avalonia.Media.StreamGeometry.Parse(svgPathData)); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[icon] bad path data skipped: {ex.Message}");
            }
        }

        private static readonly Regex PathRegex = new("<path[^>]*d=\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex CircleRegex = new("<circle[^>]*cx=\"([^\"]+)\"[^>]*cy=\"([^\"]+)\"[^>]*r=\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex LineRegex = new("<line[^>]*x1=\"([^\"]+)\"[^>]*y1=\"([^\"]+)\"[^>]*x2=\"([^\"]+)\"[^>]*y2=\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex RectRegex = new("<rect[^>]*width=\"([^\"]+)\"[^>]*height=\"([^\"]+)\"[^>]*x=\"([^\"]+)\"[^>]*y=\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex PolyRegex = new("<poly(?:line|gon)[^>]*points=\"([^\"]+)\"", RegexOptions.Compiled);
    }
}

/// <summary>Fluent extension for inline configuration, used across the code-built views.</summary>
internal static class ControlExtensions
{
    public static T With<T>(this T control, Action<T> configure) where T : Control
    {
        configure(control);
        return control;
    }
}

/// <summary>XAML-usable Lucide icon glyph: sidebar and toolbar icons bind the brand
/// stroke colour with DynamicResource so appearance changes repaint correctly.
/// (Top-level class — the XAML compiler cannot resolve nested types.)</summary>
public class LucideGlyph : Control
{
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<LucideGlyph, string>("Glyph", "");

    public static readonly StyledProperty<double> GlyphSizeProperty =
        AvaloniaProperty.Register<LucideGlyph, double>("GlyphSize", 16);

    public static readonly StyledProperty<IBrush?> GlyphBrushProperty =
        AvaloniaProperty.Register<LucideGlyph, IBrush?>("GlyphBrush");

    public string Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double GlyphSize { get => GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public IBrush? GlyphBrush { get => GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(GlyphSize, GlyphSize);

    public override void Render(DrawingContext context)
    {
        var geo = Ui.Shape(Glyph);
        if (geo is null || GlyphBrush is null) return;
        var scale = GlyphSize / 24;
        using (context.PushTransform(new Matrix(scale, 0, 0, scale, (GlyphSize - 24 * scale) / 2, (GlyphSize - 24 * scale) / 2)))
        {
            context.DrawGeometry(null, new Pen(GlyphBrush, 2, null, PenLineCap.Round, PenLineJoin.Round), geo);
        }
    }
}
