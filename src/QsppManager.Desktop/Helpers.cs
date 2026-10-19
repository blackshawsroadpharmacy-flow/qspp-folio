using Avalonia.Controls;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop;

/// <summary>Small cross-cutting helpers. Printing/print-preflight lives in the
/// Infrastructure services; these are desktop-only conveniences.</summary>
public static class Helpers
{
    /// <summary>Open a managed file with the OS viewer. Used for source PDFs and as the
    /// preview fallback when in-app rendering is unavailable on a platform.</summary>
    public static void OpenWithSystemViewer(string absolutePath)
    {
        if (!File.Exists(absolutePath)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsLinux() ? "xdg-open" : OperatingSystem.IsMacOS() ? "open" : "cmd",
                Arguments = OperatingSystem.IsWindows() ? $"/c start \"\" \"{absolutePath}\"" : $"\"{absolutePath}\"",
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            ToastWindow.Show($"Could not open the system viewer: {ex.Message}", null, error: true);
        }
    }

    /// <summary>Text enlargement independent of density (spec §5.2 note). 1.0 = normal.</summary>
    public static double TextScale { get; private set; } = 1.0;
    public static event Action<double>? TextScaleChanged;

    /// <summary>Fetch an app-level brush resource (code-built views avoid XAML binding syntax).</summary>
    public static Avalonia.Media.IBrush? Brush(string key)
        => Avalonia.Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Avalonia.Media.IBrush b ? b : null;

    public static void SetTextScale(double scale)
    {
        TextScale = scale;
        TextScaleChanged?.Invoke(scale);
    }
}

/// <summary>Toast notifications: short-lived status windows. Errors stay longer.</summary>
public static class ToastWindow
{
    public static void Show(string message, Window? owner, bool error = false)
    {
        var win = new Window
        {
            Title = error ? "Attention required" : "Saved",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            
            Topmost = true,
        };
        var border = new Avalonia.Controls.Border
        {
            Padding = new(16),
            Background = error ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F7E8E3")) : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F5F5EB")),
            BorderBrush = error ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9A3F1F")) : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#163F35")),
            BorderThickness = new Avalonia.Thickness(2),
            Child = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
        };
        win.Content = border;
        if (owner != null) win.Show(owner);
        else win.Show();
        var closeAfter = error ? 8 : 4;
        var timer = new System.Timers.Timer(closeAfter * 1000) { AutoReset = false };
        timer.Elapsed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { try { win.Close(); } catch { } timer.Dispose(); });
        timer.Start();
    }
}