global using Serilog;
using Avalonia;

namespace QsppManager.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var bootstrap = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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