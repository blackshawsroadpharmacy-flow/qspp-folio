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

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selftest"))
            return QsppManager.Infrastructure.Services.TodaySelftest.Run(Console.Out);

        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--render-offscreen") RenderOffscreen = args[i + 1];
        Hold = args.Contains("--hold");
        // Optional window size for off-screen renders (phase-4 verification matrix).
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--width" && int.TryParse(args[i + 1], out var w)) RenderWidth = w;
            if (args[i] == "--height" && int.TryParse(args[i + 1], out var h)) RenderHeight = h;
            if (args[i] == "--page" && Enum.TryParse<ShellPage>(args[i + 1], true, out var p)) RenderPage = p;
            if (args[i] == "--theme" && args[i + 1] is "light" or "dark") RenderTheme = args[i + 1];
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
            bootstrap.Fatal(ex, "Hugh's QSPP Manager terminated unexpectedly");
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