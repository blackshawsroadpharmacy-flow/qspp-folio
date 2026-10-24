using Serilog;
using Avalonia;

namespace QsppManager.Desktop;

internal static class Program
{
    /// <summary>Set when launched with --render-offscreen &lt;path&gt;: bootstrap, render the
    /// Today screen to the PNG at that path, and exit (sibling mechanic, 1:1). With --hold the
    /// window stays open so the OS compositor can be captured externally.</summary>
    public static string? RenderOffscreen { get; private set; }
    public static bool Hold { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selftest"))
            return QsppManager.Infrastructure.Services.TodaySelftest.Run(Console.Out);

        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--render-offscreen") RenderOffscreen = args[i + 1];
        Hold = args.Contains("--hold");

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