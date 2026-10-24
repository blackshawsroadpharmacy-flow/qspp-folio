using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.VisualTree;
using QsppManager.Desktop.ViewModels;
using QsppManager.Desktop.Views;

namespace QsppManager.Desktop;

/// <summary>--assert-today (phase 3 v2, 3A): build the signed-in Today shell at 100/130/150%
/// text scale and fail (exit 2) when the hero numeral still overlaps the date line, or the
/// date line overlaps the title. This overlap must not come back.</summary>
internal static class TodayLayoutAssert
{
    public static void Run(IClassicDesktopStyleApplicationLifetime desktop, double? overrideScale)
    {
        var scales = overrideScale is { } s ? new[] { s } : new[] { 1.0, 1.3, 1.5 };
        var failed = false;
        foreach (var scale in scales)
        {
            App.Config.TextScale = scale;
            Helpers.SetTextScale(scale);   // drives TextScaleContent's transform
            var window = App.BuildShellWindow(ShellPage.Today, "/tmp/assert-today-unused.png");
            Window w = window;
            try
            {
                // A window that is never shown never walks its content template —
                // show it briefly (ShowActivated=false, no decorations) so the
                // ContentPresenter materialises TodayView, then measure.
                w.Show();
                w.UpdateLayout();
                var scaled = w.GetVisualDescendants().OfType<TextScaleContent>()
                    .Select(t => (double)((ScaleTransform)t.LayoutTransform).ScaleX).DefaultIfEmpty(0).Max();
                if (Math.Abs(scaled - Math.Clamp(scale, 1.0, 2.0)) > 0.01)
                    throw new InvalidOperationException($"TextScaleContent is {scaled:P0}, expected {scale:P0}");
                var (numeral, date, title) = HeroBounds(w);
                var overlap1 = numeral.Bottom > date.Top + 0.5;
                var overlap2 = date.Bottom > title.Top + 0.5;
                Console.WriteLine(
                    $"scale {scale:P0}: hero numeral bottom {numeral.Bottom:F1} vs date top {date.Top:F1}; " +
                    $"date bottom {date.Bottom:F1} vs title top {title.Top:F1} -> " +
                    (overlap1 || overlap2 ? "FAIL" : "PASS"));
                if (overlap1 || overlap2) failed = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"scale {(double)scale:P0}: FAIL ({ex.GetType().Name}: {ex.Message})");
                failed = true;
            }
            finally
            {
                w.Close();
            }
        }
        Console.Out.Flush();
        Environment.Exit(failed ? 2 : 0);   // hard exit: nothing queued on the dispatcher
    }

    private static (Rect Numeral, Rect Date, Rect Title) HeroBounds(Control root)
    {
        // The shell hosts TodayView (UserControl) whose code-built screen exposes the bounds.
        foreach (var todayView in root.GetVisualDescendants().OfType<TodayView>())
        {
            var screen = todayView.Screen;
            return screen.HeroQaRects();
        }
        throw new InvalidOperationException("TodayView not found in the shell tree");
    }
}
