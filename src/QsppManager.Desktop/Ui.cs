using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
    /// <summary>ShellPage → (eyebrow, icon, hue-name) per the v2 destination table (3D).</summary>
    public static readonly System.Collections.Generic.Dictionary<ViewModels.ShellPage, (string Eyebrow, string Icon, string HueBrush, string TileBrush)> HueTable =
        new()
        {
            { ViewModels.ShellPage.Today,       ("Focus",    "house",          "Brush.Ochre",    "Brush.OchreTint"    ) },
            { ViewModels.ShellPage.Requirements,("Prepare",  "list-checks",    "Brush.Green",    "Brush.SelBg"        ) },
            { ViewModels.ShellPage.Documents,   ("Evidence", "files",          "Brush.Teal",     "Brush.TealTint"     ) },
            { ViewModels.ShellPage.Tasks,       ("Plan",     "calendar-days",  "Brush.Clay",     "Brush.ClayTint"     ) },
            { ViewModels.ShellPage.Registers,   ("Govern",   "clipboard-list", "Brush.Umber",    "Brush.UmberTint"    ) },
            { ViewModels.ShellPage.People,      ("People",   "users",      "Brush.Berry",    "Brush.BerryTint"    ) },
            { ViewModels.ShellPage.Assessments, ("Assess",   "clipboard-check", "Brush.Violet", "Brush.VioletTint") },
            { ViewModels.ShellPage.Library,     ("Library",  "book-open",  "Brush.Ink",      "Brush.SelBg"        ) },
            { ViewModels.ShellPage.PrintCentre, ("Print",    "printer",    "Brush.Stone",    "Brush.StoneTint"    ) },
            { ViewModels.ShellPage.Settings,    ("Setup",    "settings",   "Brush.Stone",    "Brush.StoneTint"    ) },
        };

    /// <summary>Standard page header for a destination: hue tile + eyebrow above the serif
    /// title, then the lead. The titles mirror the sidebar labels exactly.</summary>
    public static Control PageTitle(ViewModels.ShellPage page, string lead, string? explainer = null) =>
        page switch
        {
            ViewModels.ShellPage.Documents   => PageHeader("Evidence & documents", lead, explainer, page),
            ViewModels.ShellPage.Tasks       => PageHeader("Tasks & calendar", lead, explainer, page),
            ViewModels.ShellPage.People      => PageHeader("People & training", lead, explainer, page),
            ViewModels.ShellPage.PrintCentre => PageHeader("Print centre", lead, explainer, page),
            ViewModels.ShellPage.Requirements=> PageHeader("Requirements", lead, explainer, page),
            ViewModels.ShellPage.Registers   => PageHeader("Registers", lead, explainer, page),
            ViewModels.ShellPage.Assessments => PageHeader("Assessments", lead, explainer, page),
            ViewModels.ShellPage.Library     => PageHeader("Library", lead, explainer, page),
            ViewModels.ShellPage.Settings    => PageHeader("Settings", lead, explainer, page),
            _                                => PageHeader(page.ToString(), lead, explainer, page),
        };

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
    public static Control TitleLarge(string t) => new TextBlock { Classes = { "titleLarge" }, Text = t };
    public static Control Title2(string t) => new TextBlock { Classes = { "title2" }, Text = t };
    public static Control Callout(string t, double? maxWidth = null) => new TextBlock { Classes = { "callout" }, Text = t, MaxWidth = maxWidth ?? double.NaN };
    public static Control Footnote(string t) => new TextBlock { Classes = { "footnote" }, Text = t };

    // ---------- page header (premium-polish 3.9) ----------
    /// <summary>Page header v2 (3D #2): 32pt rounded tile with the destination icon in its
    /// hue, the page eyebrow in the hue above the serif title, then one short callout lead
    /// (90 characters at most) and the ⓘ popover. One tile per page (v2 brief).</summary>
    public static Control PageHeader(string title, string lead, string? explainer = null,
        ViewModels.ShellPage? page = null)
    {
        var spage = page ?? default;
        var hasTile = page is { } p2 && HueTable.ContainsKey(p2);
        var row = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions(hasTile ? "Auto,*,Auto" : "*,Auto"),
            ColumnSpacing = 12,
        };
        var textCol = new StackPanel { Spacing = 6 };
        Control? infoButton = null;   // built later when an explainer exists
        if (hasTile)
        {
            // Destination tile + hue eyebrow (3D places 1-2), one tile per page.
            var h = HueTable[spage];
            var eyebrowRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var eyebrow = new TextBlock
            {
                Classes = { "eyebrow" },
                Text = h.Eyebrow.ToUpperInvariant(),
                Foreground = Br(h.HueBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            eyebrowRow.Children.Add(eyebrow);
            infoButton = new Button
            {
                Classes = { "ghost" }, Width = 22, Height = 22, Padding = new(0),
                VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 2, 0, 0),
            };
            eyebrowRow.Children.Add(infoButton);
            textCol.Children.Add(eyebrowRow);
        }
        textCol.Children.Add(new TextBlock { Classes = { "titleLarge" }, Text = title });
        if (!string.IsNullOrWhiteSpace(lead))
            textCol.Children.Add(new TextBlock { Classes = { "callout" }, Text = lead, TextWrapping = TextWrapping.Wrap });
        var textColumn = hasTile ? 1 : 0;
        AvaloniaGrid.SetColumn(textCol, textColumn);
        row.Children.Add(textCol);
        if (hasTile)
        {
            var h = HueTable[spage];
            var tile = new Border
            {
                Width = 32, Height = 32, CornerRadius = new(8),
                Background = Br(h.TileBrush),
                Child = Lucide(h.Icon, 16, Br(h.HueBrush), center: true),
                VerticalAlignment = VerticalAlignment.Top,
            };
            AvaloniaGrid.SetColumn(tile, 0);
            row.Children.Insert(0, tile);
        }

        if (!string.IsNullOrWhiteSpace(explainer))
        {
            var button = (Button)(infoButton ?? new Button
            {
                Classes = { "ghost" }, Width = 24, Height = 24, Padding = new(0),
                VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0),
            });
            Ui.Ax(button, "About this screen");
            var popup = new Popup
            {
                Placement = PlacementMode.Bottom,
                PlacementTarget = button,
                Child = new Border
                {
                    Background = Br("Brush.Surface"), BorderBrush = Br("Brush.ControlBorder"),
                    BorderThickness = new(1), CornerRadius = new(9), Padding = new(14, 12),
                    MaxWidth = 440,
                    Child = new ScrollViewer
                    {
                        MaxHeight = 340,
                        Content = new TextBlock { Classes = { "callout" }, Text = explainer, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
                    },
                },
            };
            button.Content = new Panel { Children = { Lucide("info", 15, Br("Brush.Muted"), center: true), popup } };
            button.Click += (_, _) => { if (popup is { } p) p.IsOpen = !p.IsOpen; };
            if (infoButton is null)
            {
                // No tile: ⓘ rides the trailing grid column after the title text.
                AvaloniaGrid.SetColumn(button, 1);
                row.Children.Add(button);
            }
            // With a tile the ⓘ already sits beside the eyebrow; nothing else to do.
        }
        return row;
    }

    // ---------- quiet ⓘ popover (PageHeader pattern, extracted) ----------
    /// <summary>The ⓘ that opens one explainer popover: hover shows a tooltip, click shows
    /// a bounded card. Reused by page headers, the "Plain title" footnote and detail rows.</summary>
    public static Control InfoDot(string text, string toolTip = "About this")
    {
        var button = new Button
        {
            Classes = { "ghost" }, Width = 20, Height = 20, Padding = new(0),
            VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 2, 0, 0),
        };
        Ax(button, toolTip, text);
        var popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = button,
            Child = new Border
            {
                Background = Br("Brush.Surface"), BorderBrush = Br("Brush.ControlBorder"),
                BorderThickness = new(1), CornerRadius = new(9), Padding = new(14, 12),
                MaxWidth = 440,
                Child = new ScrollViewer
                {
                    MaxHeight = 340,
                    Content = new TextBlock { Classes = { "callout" }, Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
                },
            },
        };
        button.Content = new Panel { Children = { Lucide("info", 14, Br("Brush.Muted"), center: true), popup } };
        button.Click += (_, _) => popup.IsOpen = !popup.IsOpen;
        return button;
    }

    // ---------- verbatim quote (premium-polish 3.7) ----------
    /// <summary>Read-only source wording: selectable serif text, 2px green rule on the
    /// left, 14px left padding, no box. Replaces read-only TextBoxes that looked editable.
    /// maxHeight caps tall passages (M7.2 autosize contract: grows with content, then
    /// scrolls inside the cap).</summary>
    public static Control QuoteBlock(string text, double? maxHeight = null)
    {
        var quote = new SelectableTextBlock { Classes = { "quote" }, Text = text };
        // SelectableTextBlock measures unbounded inside a ScrollViewer (never wraps); an
        // explicit wrap + width cap restores the wrap behaviour everywhere.
        quote.TextWrapping = TextWrapping.Wrap;
        quote.MaxWidth = 860;
        if (maxHeight is { } cap)
        {
            var scroll = new ScrollViewer
            {
                MaxHeight = cap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = quote,
            };
            return new Border
            {
                BorderBrush = Br("Brush.Green"),
                BorderThickness = new(2, 0, 0, 0),
                Padding = new(14, 2, 8, 2),
                Child = scroll,
            };
        }
        return new Border
        {
            BorderBrush = Br("Brush.Green"),
            BorderThickness = new(2, 0, 0, 0),
            Padding = new(14, 2, 0, 2),
            Child = quote,
        };
    }

    // ---------- grouped settings rows (3E #1, macOS System Settings style) ----------
    /// <summary>A surface group box (radius 10, 1px Divider) over 40pt rows separated by
    /// inset 1px rules. Left label in body, right control. A footnote (callout) rides under
    /// the box. Label/optional suffix supported: pass "(optional)" via the row text.</summary>
    public static Control GroupedRows((Control? Label, Control Field)[] rows, string? footnote = null)
    {
        var box = new Border
        {
            CornerRadius = new(10),
            Background = Br("Brush.Surface"),
            BorderBrush = Br("Brush.Divider"),
            BorderThickness = new(1),
            Padding = new(0, 4),
        };
        var col = new StackPanel { Spacing = 0 };
        for (var i = 0; i < rows.Length; i++)
        {
            var (label, field) = rows[i];
            var row = new AvaloniaGrid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Height = 44,
                Margin = new(14, 0),
            };
            if (label is not null)
            {
                AvaloniaGrid.SetColumn(label, 0);
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);
            }
            field.VerticalAlignment = VerticalAlignment.Center;
            AvaloniaGrid.SetColumn(field, 1);
            row.Children.Add(field);
            col.Children.Add(row);
            if (i < rows.Length - 1)
                col.Children.Add(new Border { Height = 1, Background = Br("Brush.Divider"), Margin = new(14, 0, 14, 0) });
        }
        box.Child = col;
        if (footnote is null) return box;
        return new StackPanel { Spacing = 6, Children =
        {
            box,
            new TextBlock { Classes = { "callout" }, Text = footnote, TextWrapping = TextWrapping.Wrap },
        } };
    }

    /// <summary>A 40pt row with the label on the left and an extra line under the box's rows
    /// (used by list-style rows like the deadlines two-column list).</summary>
    public static Control GroupedList(Control content, string? footnote = null)
    {
        var box = new Border
        {
            CornerRadius = new(10),
            Background = Br("Brush.Surface"),
            BorderBrush = Br("Brush.Divider"),
            BorderThickness = new(1),
            Padding = new(14, 8),
            Child = content,
        };
        if (footnote is null) return box;
        return new StackPanel { Spacing = 6, Children =
        {
            box,
            new TextBlock { Classes = { "callout" }, Text = footnote, TextWrapping = TextWrapping.Wrap },
        } };
    }

    /// <summary>Sentence-case label with a lighter "(optional)" suffix (3C).</summary>
    public static Control OptionalLabel(string text, bool optional) =>
        new TextBlock
        {
            Classes = { "body" },
            Inlines = { new Avalonia.Controls.Documents.Run(text), new Avalonia.Controls.Documents.Run(optional ? " (optional)" : "")
                { FontWeight = FontWeight.Normal, Foreground = Br("Brush.Muted") } },
            VerticalAlignment = VerticalAlignment.Center,
        };

    // ---------- scaffolding ----------
    /// <summary>Standard page scaffold: scrollable, content capped at 760pt and centred
    /// (premium-polish 3.11); side gutters 32pt. List-and-detail pages build their own
    /// full-width root instead.</summary>
    public static ScrollViewer Page(params Control[] children)
    {
        var sp = new StackPanel { Margin = new(32, 30, 32, 44), Spacing = 14, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Center };
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

    /// <summary>Empty state (premium-polish 3.8): one short block at most 120pt tall —
    /// a bare 20pt icon (no tinted circle behind it), one headline line, one callout
    /// line, one optional quiet action. No large white box, no card chrome.</summary>
    public static Control EmptyState(string iconName, string title, string hint, Control? action = null, string? hueBrush = null)
    {
        // 3E #4: top-aligned under their header, never centred in a void; 20pt icon in the
        // page hue; the whole block stays at most 120pt tall.
        var col = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top, MaxWidth = 430 };
        col.Children.Add(Lucide(iconName, 20, Br(hueBrush ?? "Brush.Stone")));
        col.Children.Add(new TextBlock { Classes = { "headline" }, Text = title });
        col.Children.Add(new TextBlock
        {
            Classes = { "callout" }, Text = hint,
            TextWrapping = TextWrapping.Wrap, MaxWidth = 420,
        });
        if (action != null)
        {
            action.Margin = new(0, 8, 0, 0);
            col.Children.Add(action);
        }
        return new Border
        {
            Padding = new(12, 14),
            MaxHeight = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
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

    // ---------- chips / status (3D #4: tint bg, hue text, leading dot) ----------
    /// <summary>Status chips carry their meaning in text as well as colour; the dot
    /// communicates state to colour-blind users. Status hues per the v2 table: verified
    /// Forest, in progress Ochre, ready for review Violet, overdue/blocked Red, planning
    /// and unknown Stone (hollow).</summary>
    public static Border Chip(string text, string tone = "neutral", bool mono = false)
    {
        var hue = Tone(tone);              // "Brush.Forest" | "Brush.Ochre" | ...
        var hollow = tone is "neutral" or "none";
        var tb = new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeight.Medium,
            Foreground = Br(hue), TextWrapping = TextWrapping.NoWrap,
        };
        if (mono) tb.FontFamily = new FontFamily(FontFamilies.Mono);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var dot = new Border
        {
            Width = 6, Height = 6, CornerRadius = new(3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (hollow)
        {
            dot.Background = Brushes.Transparent;
            dot.BorderBrush = Br(hue);
            dot.BorderThickness = new(1.2);
        }
        else
        {
            dot.Background = Br(hue);
        }
        panel.Children.Add(dot);
        panel.Children.Add(tb);
        return new Border
        {
            CornerRadius = new(5), Padding = new(8, 3),
            Background = Br(HueTint(hue)),
            BorderThickness = new(0),
            Child = panel,
        };
    }

    private static string Tone(string tone) => tone switch
    {
        // 11pt chip text on the SelBg tint needs 4.5:1 in dark too — GreenSoft is the
        // chip-safe green (light: identical to Green; dark: #4BBA8E). Buttons keep Green.
        "ok" or "open" => "Brush.GreenSoft", // verified/in-progress families split below
        "review" => "Brush.Violet",
        "warn" => "Brush.Ochre",
        "bad" => "Brush.Red",
        "info" => "Brush.Stone",
        _ => "Brush.Stone",
    };

    /// <summary>The hue brush key a status tone renders as — public so quiet list rows
    /// (bare 6pt status dots, no chip tint) share the same hue language as chips.</summary>
    public static string ToneBrushKey(string tone) => Tone(tone);

    private static string HueTint(string hueBrush) => hueBrush switch
    {
        "Brush.Green" or "Brush.GreenSoft" => "Brush.SelBg",
        "Brush.Teal" => "Brush.TealTint",
        "Brush.Clay" => "Brush.ClayTint",
        "Brush.Berry" => "Brush.BerryTint",
        "Brush.Umber" => "Brush.UmberTint",
        "Brush.Ochre" => "Brush.OchreTint",
        "Brush.Violet" => "Brush.VioletTint",
        "Brush.Stone" => "Brush.StoneTint",
        "Brush.Red" => "Brush.RedTint",
        _ => "Brush.SelBg",
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
        /// <summary>Editorial serif for page titles, detail titles, verbatim quotes and the
        /// Today hero numeral. New York first (modern macOS), Georgia fallback.</summary>
        public const string Serif = "New York, Georgia, Hoefler Text, Times New Roman";
        public const string Ui = "SF Pro Text, SF Pro, Host Grotesk, Helvetica Neue, Helvetica, Arial";
    }

    /// <summary>Lucide icon rendered from the vendored SVG (ISC-licensed) as stroke
    /// geometry — no external SVG package needed.</summary>
    public static Control Lucide(string name, double size = IconSizeDefault, IBrush? stroke = null, bool center = false, double strokeThickness = 2)
    {
        return new AvaloniaPath
        {
            Data = Shape(name),
            Stretch = Stretch.Uniform,
            Stroke = stroke ?? Br("Brush.Ink"),
            StrokeThickness = strokeThickness,
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

    /// <summary>Styled optional date field (premium-polish 3.10): d MMM yyyy, blank
    /// allowed and shown as "Not set" through the watermark. Replaces the cramped
    /// day/month/year triplet fields.</summary>
    public static CalendarDatePicker DateField(DateTime? value = null) => new()
    {
        SelectedDateFormat = Avalonia.Controls.CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "d MMM yyyy",
        Watermark = "Not set",
        SelectedDate = value,
        Width = 180,
    };

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

public enum NoticeTone { Ok, Error, Info }

/// <summary>Slim result banner pinned to the top of the page content (premium-polish
/// 3.4): an icon, the message and a close button. Success notices fade after 6 seconds
/// (180ms opacity fade, skipped under reduced motion); errors stay until dismissed and
/// offer "Copy details" when the view supplies detail text. Replaces the eight
/// read-only "Result messages appear here" TextBox strips; message text is unchanged.</summary>
public sealed class NoticeHost : Border
{
    private readonly Panel _iconHost = new();
    private readonly TextBlock _message = new()
    {
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13,
    };
    private readonly Button _copy;
    private readonly Button _close;
    private readonly StackPanel _copyRow;
    private DispatcherTimer? _fadeTimer;
    private string? _details;

    public NoticeHost()
    {
        IsVisible = false;
        CornerRadius = new(6);
        Padding = new(10, 7);
        _copy = Ui.Btn("Copy details", null, "ghost", axName: "Copy details");
        _copy.FontSize = 12;
        _copy.Padding = new(6, 0);
        _copy.Height = 22;
        _copy.Click += async (_, _) =>
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null && _details is { } d) await clip.SetTextAsync(d);
        };

        _close = new Button
        {
            Classes = { "ghost" }, Width = 22, Height = 22, Padding = new(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Ui.Ax(_close, "Dismiss message");
        _close.Click += (_, _) => { StopFade(); IsVisible = false; };

        _copyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,10,Auto,6,Auto") };
        Grid.SetColumn(_iconHost, 0);
        Grid.SetColumn(_message, 2);
        Grid.SetColumn(_copyRow, 4);
        _copyRow.Children.Add(_copy);
        Grid.SetColumn(_close, 6);
        grid.Children.Add(_iconHost);
        grid.Children.Add(_message);
        grid.Children.Add(_copyRow);
        grid.Children.Add(_close);
        Child = grid;
    }

    /// <summary>Convenience for the common outcome shape: one message on success,
    /// another (with detail) on failure. Message text unchanged from the old strips.</summary>
    public void Show(bool ok, string okMessage, string? errorDetails = null)
        => Show(ok ? NoticeTone.Ok : NoticeTone.Error, ok ? okMessage : (errorDetails ?? okMessage));

    /// <summary>Show a message. The view keeps producing the same text; only the
    /// container changed. details (error path) becomes the Copy-details payload.</summary>
    public void Show(NoticeTone tone, string message, string? details = null)
    {
        StopFade();
        _details = details;
        _message.Text = message;
        var (icon, iconBrush, bg, fg) = tone switch
        {
            NoticeTone.Ok => ("check-circle-2", Ui.Br("Brush.Green"), Ui.Br("Brush.OkBg"), Ui.Br("Brush.Ink")),
            NoticeTone.Error => ("alert-triangle", Ui.Br("Brush.Alert"), Ui.Br("Brush.DangerBg"), Ui.Br("Brush.Ink")),
            _ => ("info", Ui.Br("Brush.Muted"), Ui.Br("Brush.Sage"), Ui.Br("Brush.Ink")),
        };
        _iconHost.Children.Clear();
        _iconHost.Children.Add(Ui.Lucide(icon, 15, iconBrush, center: true));
        _iconHost.VerticalAlignment = VerticalAlignment.Center;
        _iconHost.Width = 16;
        _message.Foreground = fg;
        Background = bg;
        _copyRow.IsVisible = tone == NoticeTone.Error && !string.IsNullOrEmpty(details);
        Opacity = 1;
        IsVisible = true;

        if (tone == NoticeTone.Ok)
        {
            // Success fades after 6s (180ms), skipped under reduced motion.
            HoldThenFade(6.0);
        }
    }

    public void Clear()
    {
        StopFade();
        IsVisible = false;
    }

    private void HoldThenFade(double holdSeconds)
    {
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(holdSeconds) };
        _fadeTimer.Tick += (_, _) =>
        {
            _fadeTimer?.Stop();
            _fadeTimer = null;
            if (App.Config.ReduceMotion)
            {
                IsVisible = false;
                Opacity = 1;
                return;
            }
            // 30ms steps across 180ms: a quiet opacity fade without the 12 animation API.
            var step = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            var elapsed = 0;
            step.Tick += (_, _) =>
            {
                elapsed += 30;
                if (elapsed >= 180) { step.Stop(); IsVisible = false; Opacity = 1; }
                else Opacity = 1.0 - (elapsed / 180.0);
            };
            step.Start();
        };
        _fadeTimer.Start();
    }

    private void StopFade()
    {
        if (_fadeTimer is { } t)
        {
            t.Stop();
            _fadeTimer = null;
        }
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

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LucideGlyph, double>("StrokeThickness", 2.0);

    public static readonly StyledProperty<IBrush?> GlyphBrushProperty =
        AvaloniaProperty.Register<LucideGlyph, IBrush?>("GlyphBrush");

    public string Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double GlyphSize { get => GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public IBrush? GlyphBrush { get => GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(GlyphSize, GlyphSize);

    public override void Render(DrawingContext context)
    {
        var geo = Ui.Shape(Glyph);
        if (geo is null || GlyphBrush is null) return;
        var scale = GlyphSize / 24;
        using (context.PushTransform(new Matrix(scale, 0, 0, scale, (GlyphSize - 24 * scale) / 2, (GlyphSize - 24 * scale) / 2)))
        {
            context.DrawGeometry(null, new Pen(GlyphBrush, StrokeThickness, null, PenLineCap.Round, PenLineJoin.Round), geo);
        }
    }
}
