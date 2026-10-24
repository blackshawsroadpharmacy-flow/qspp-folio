using Avalonia.Media;

namespace QsppManager.Desktop;

/// <summary>--fonts diagnostic (phase 3 v2, 3B): for every Font.* token, print the family
/// that actually wins (proven by membership in SystemFonts — TryGetGlyphTypeface is too
/// lenient to be trusted — or by a real FormattedText render for embedded families), then
/// measure the digits 0-9 via glyph metrics: equal raw advances → tabular; ink below the
/// baseline → descending (old-style) digits. lnum/tnum themselves are applied as style
/// features and confirmed visually in the render set.</summary>
internal static class FontsDiagnostic
{
    /// <summary>The family stacks to verify, exactly as App.axaml declares them.</summary>
    private static readonly (string Token, string Stack)[] Tokens =
    {
        ("Font.Ui", "$Default, .AppleSystemUIFont, SF Pro Text, SF Pro, Inter, Host Grotesk, Helvetica Neue, Helvetica, Arial"),
        ("Font.SansDisplay", "$Default, .AppleSystemUIFont, SF Pro Display, SF Pro, Inter, Helvetica Neue, Helvetica"),
        ("Font.Serif", "avares://Newsreader-Regular (Newsreader), Georgia, Hoefler Text, Times New Roman"),
        ("Font.SerifDisplay", "avares://Newsreader-Light (Newsreader Light), Newsreader Display, Newsreader"),
        ("Font.SerifDisplayLight", "avares://Newsreader-Light (Newsreader Light), Newsreader Display Light, Newsreader"),
        ("Font.SerifMedium", "avares://Newsreader-Medium (Newsreader Medium), Newsreader"),
        ("Font.SerifItalic", "avares://Newsreader-Italic (Newsreader Italic), Newsreader"),
        ("Font.Mono", "avares://JetBrainsMono (JetBrains Mono), SF Mono, Menlo"),
        ("Font.MonoMedium", "avares://JetBrainsMono-Medium (JetBrains Mono Medium), SF Mono, Menlo"),
        ("Font.Brand", "avares://HostGrotesk (Host Grotesk), SF Pro Text, Helvetica Neue"),
    };

    public static void Run()
    {
        Console.WriteLine("== --fonts diagnostic (resolved via SystemFonts membership + real renders) ==");
        foreach (var (token, stack) in Tokens)
        {
            var winner = WinnerOf(stack);
            Console.WriteLine($"{token,-24} stack: {stack}");
            Console.WriteLine($"{"",-24} resolved: {winner?.Name ?? "(nothing resolves — fallback face in use)"}");
            if (winner is not null && !token.StartsWith("Font.Mono"))
                FigureProbe(winner);
        }

        Console.WriteLine("\n== SF Pro candidate families actually installed ==");
        foreach (var name in new[] { ".AppleSystemUIFont", "SF Pro Text", "SF Pro Display", "SF Pro", "Inter", "Helvetica Neue", "Arial" })
        {
            var hit = FindSystemFamily(name);
            Console.WriteLine($"{name,-20} -> " + (hit is { } f ? f.Name + " (installed)" : "(not installed)"));
        }

        Console.WriteLine($"\nSystemFonts total: {FontManager.Current.SystemFonts.Count}");
        Console.WriteLine("Embedded families probed by real render:");
        EmbeddedProbe("JetBrains Mono");
        EmbeddedProbe("JetBrains Mono Medium");
        EmbeddedProbe("Newsreader");
        EmbeddedProbe("Newsreader Light");
        EmbeddedProbe("Newsreader Medium");
        EmbeddedProbe("Newsreader Italic");
        EmbeddedProbe("Host Grotesk");

        Console.WriteLine("\n== FontFeature API surface (Avalonia " + typeof(FontFamily).Assembly.GetName().Version + ") ==");
        var fft = typeof(FontFeature);
        Console.WriteLine("  FontFeature fields: " + string.Join(", ", fft.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static).Select(f => f.FieldType.Name + " " + f.Name)));
        Console.WriteLine("  FontFeature props:  " + string.Join(", ", fft.GetProperties().Select(pi => pi.PropertyType.Name + " " + pi.Name)));
        Console.WriteLine("  FontFeature ctors:  " + string.Join(", ", fft.GetConstructors().Select(c => "(" + string.Join(", ", c.GetParameters().Select(pp => pp.ParameterType.Name + " " + pp.Name)) + ")")));
        try
        {
            var parse = fft.GetMethods().Where(m => m.Name == "Parse" && m.IsStatic).FirstOrDefault();
            if (parse is { } pm)
            {
                var parsed = pm.Invoke(null, new object?[] { "tnum" });
                Console.WriteLine("  FontFeature.Parse(\"tnum\") -> " + parsed);
            }
            else
                Console.WriteLine("  FontFeature.Parse: (absent)");
        }
        catch (Exception ex) { Console.WriteLine("  FontFeature.Parse FAILED: " + ex.GetBaseException().Message); }
        Console.WriteLine("  TextElement feature methods: " + string.Join(", ", typeof(Avalonia.Controls.Documents.TextElement).GetMethods().Where(m => m.Name.Contains("FontFeatures")).Select(m => m.Name)));

        Console.WriteLine("\n== Style tokens in the live theme (declared in App.axaml) ==");
        foreach (var (cls, fam) in new[]
        {
            ("displayHero", "Font.SerifDisplayLight 80pt"),
            ("title.page / h1", "Font.Serif 30pt"),
            ("title.section / h2", "Font.SansDisplay 17pt"),
            ("eyebrow / label / ui", "Font.Ui"),
            ("stat / quote", "Font.Serif"),
            ("code", "Font.Mono"),
        })
            Console.WriteLine($"  {cls,-22} → {fam}");
    }

    private static FontFamily? WinnerOf(string stack)
    {
        foreach (var part in stack.Split(','))
        {
            var name = part.Trim();
            if (name == "" || name == "monospace") continue;
            if (name.StartsWith("avares://"))
            {
                // Embedded font URI — not in SystemFonts; the EmbeddedProbe render is proof.
                var paren = name.IndexOf('(');
                if (paren < 0) continue;
                var listed = name[(paren + 1)..].TrimEnd(')');
                var fam = EmbeddedProbe(listed);
                if (fam is not null) return fam;
                continue;
            }
            var f = FindSystemFamily(name);
            if (f is not null) return f;
        }
        return null;
    }

    private static FontFamily? FindSystemFamily(string name)
    {
        if (name == "$Default")
            return FontFamily.Default;
        foreach (var f in FontManager.Current.SystemFonts)
            if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return f;
        return null;
    }

    /// <summary>Proof an embedded family loads: render 0-9 with it and report the width.</summary>
    private static FontFamily? EmbeddedProbe(string familyName)
    {
        try
        {
            var tf = new Typeface(new FontFamily("avares://QsppManager.Desktop/Assets/Fonts/#" + familyName));
            var ft = new FormattedText("0123456789", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 20, Brushes.Black);
            var w = ft.WidthIncludingTrailingWhitespace;
            if (w <= 0)
            {
                Console.WriteLine($"  embedded {familyName,-22} renders empty (not loaded)");
                return null;
            }
            Console.WriteLine($"  embedded {familyName,-22} OK: 10 digits = {w:F1}px at 20pt");
            return tf.FontFamily;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  embedded {familyName,-22} FAILED: {ex.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>Figure probe on the winner: raw glyph metrics for 0-9 — equal advances
    /// mean the face's default digits are already tabular; Height − YBearing &gt; 0.5
    /// means digits dip below the baseline (old-style figures needing lnum).</summary>
    private static void FigureProbe(FontFamily family)
    {
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var gt))
        {
            Console.WriteLine($"{"",-24} figure probe: no glyph typeface");
            return;
        }
        var cmap = gt.CharacterToGlyphMap;
        var advances = new System.Collections.Generic.List<double>();
        var descend = false;
        for (var c = '0'; c <= '9'; c++)
        {
            if (!cmap.TryGetGlyph(c, out var glyph)) continue;
            if (!gt.TryGetGlyphMetrics(glyph, out var m)) continue;
            advances.Add(m.XBearing + m.Width);
            if (m.YBearing < -0.5) descend = true;
        }
        advances.Sort();
        var tabular = advances.Count == 10 && advances.Max() - advances.Min() < 0.5;
        Console.WriteLine($"{"",-24} digits raw: {(tabular ? "tabular" : "proportional")}, " +
            (descend ? "descending (lnum needed)" : "lining by default (lnum harmless)"));
    }
}