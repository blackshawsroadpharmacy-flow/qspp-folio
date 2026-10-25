using Serilog;
using Avalonia;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop;

internal static class Program
{
    /// <summary>Set when launched with --render-offscreen &lt;path&gt;: bootstrap, render the
    /// Today screen to the PNG at that path, and exit (sibling mechanic, 1:1). With --hold the
    /// window stays open so the OS compositor can be captured externally.</summary>
    public static string? RenderOffscreen { get; private set; }
    public static bool Hold { get; private set; }
    public static int RenderWidth { get; private set; } = 1280;
    public static int RenderHeight { get; private set; } = 940;
    /// <summary>--page &lt;name&gt;: render a signed-in shell screen (Today, Requirements,
    /// Documents, Tasks, Registers, People, Assessments, Library, PrintCentre, Settings)
    /// instead of the bare Today composition. Absent → the original Today-only render.</summary>
    public static ShellPage? RenderPage { get; private set; }
    /// <summary>--theme light|dark: deterministic render theme (arg wins over the old env
    /// knob; default light).</summary>
    public static string? RenderTheme { get; private set; }
    /// <summary>--fonts: print which family each type token actually resolved to, with a
    /// tabular/lining figure probe for the digits, and exit (phase 3 v2, 3B).</summary>
    public static bool FontsDiagnostic { get; private set; }
    /// <summary>--assert-today: run the Today hero layout assertions at 100/130/150% text
    /// scale and exit non-zero on any failure (phase 3 v2, 3A).</summary>
    public static bool AssertTodayLayout { get; private set; }
    /// <summary>--text-scale &lt;1.0|1.3|1.5&gt;: render / assert at a text scale.</summary>
    public static double? TextScale { get; private set; }
    /// <summary>--seg &lt;0..3&gt;: which Requirements segment to render (phase-4 verification).</summary>
    public static int? RenderSegment { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selftest"))
            return QsppManager.Infrastructure.Services.TodaySelftest.Run(Console.Out);

        FontsDiagnostic = args.Contains("--fonts");
        AssertTodayLayout = args.Contains("--assert-today");
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--text-scale" && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var s))
                TextScale = s;
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "--render-offscreen" && i + 1 < args.Length) RenderOffscreen = args[i + 1];
        Hold = args.Contains("--hold");
        // Optional window size for off-screen renders (phase-4 verification matrix).
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--width" && int.TryParse(args[i + 1], out var w)) RenderWidth = w;
            if (args[i] == "--height" && int.TryParse(args[i + 1], out var h)) RenderHeight = h;
            if (args[i] == "--page" && Enum.TryParse<ShellPage>(args[i + 1], true, out var p)) RenderPage = p;
            if (args[i] == "--theme" && args[i + 1] is "light" or "dark") RenderTheme = args[i + 1];
            if (args[i] == "--seg" && int.TryParse(args[i + 1], out var seg)) RenderSegment = seg;
        }

        var bootstrap = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();
        try
        {
            // The real macOS backend: headless compositing never produced frames for
            // RenderTargetBitmap, so the off-screen render path shows the window for a
            // moment (or holds it open with --hold) and captures the OS composition.
            if (RenderOffscreen is not null)
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            else
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return Environment.ExitCode != 0 ? Environment.ExitCode : 0;
        }
        catch (Exception ex)
        {
            bootstrap.Fatal(ex, "QSPP Manager terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // One AppBuilder per process; headless tests reuse it.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}