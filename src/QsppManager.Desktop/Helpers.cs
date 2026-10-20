using Avalonia.Controls;
using System.Diagnostics;
using Avalonia.Media;

namespace QsppManager.Desktop;

/// <summary>Small cross-cutting helpers. Printing/print-preflight lives in the
/// Infrastructure services; these are desktop-only conveniences.</summary>
public static class Helpers
{
    /// <summary>Open a managed file with the OS viewer. Used for source PDFs and as the
    /// preview fallback when in-app rendering is unavailable on a platform.
    /// Returns false when nothing could handle it.</summary>
    public static bool OpenWithSystemViewer(string absolutePath)
    {
        if (!File.Exists(absolutePath)) return false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsLinux() ? "xdg-open" : OperatingSystem.IsMacOS() ? "open" : "cmd",
                Arguments = OperatingSystem.IsWindows() ? $"/c start \"\" \"{absolutePath}\"" : $"\"{absolutePath}\"",
                UseShellExecute = true,
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.Show($"Could not open the system viewer: {ex.Message}", null, error: true);
            return false;
        }
    }

    /// <summary>Reveal a file (or folder) in the platform file manager. macOS uses
    /// `open -R`; Windows selects in Explorer; Linux opens the containing folder.</summary>
    public static bool RevealInFileManager(string absolutePath)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", ["-R", absolutePath]) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsWindows())
            {
                var exists = File.Exists(absolutePath);
                Process.Start(new ProcessStartInfo("explorer.exe",
                    exists ? $"/select,{absolutePath}" : $"/open,{Path.GetDirectoryName(absolutePath)}") { UseShellExecute = true });
            }
            else
            {
                var dir = File.Exists(absolutePath) ? Path.GetDirectoryName(absolutePath) : absolutePath;
                Process.Start(new ProcessStartInfo("xdg-open", [dir ?? "/"]) { UseShellExecute = true });
            }
            return true;
        }
        catch (Exception ex)
        {
            ToastWindow.Show($"Could not reveal in the file manager: {ex.Message}", null, error: true);
            return false;
        }
    }

    /// <summary>Text enlargement scale (1.0 = normal, up to 1.5). Drive via the shell's
    /// layout transform — Settings and the View menu set it.</summary>
    public static double TextScale { get; private set; } = 1.0;
    public static event Action<double>? TextScaleChanged;
    public static void SetTextScale(double scale)
    {
        TextScale = scale;
        TextScaleChanged?.Invoke(scale);
    }

    /// <summary>Fired after the OS appearance change so code-built views can rebuild
    /// (their cached controls capture brushes at build time).</summary>
    public static event Action? ThemeChanged;

    internal static void FireThemeChanged() => ThemeChanged?.Invoke();

    /// <summary>Fetch an app-level brush resource, resolved against the ACTIVE theme
    /// variant (theme dictionaries). Falls back to the default dictionary.
    /// Callers capture the brush at build time; rebuild on Helpers.ThemeChanged.</summary>
    public static IBrush? Brush(string key)
    {
        var app = Avalonia.Application.Current;
        if (app is null) return null;
        if (app.TryGetResource(key, app.ActualThemeVariant, out var themed) && themed is IBrush b) return b;
        if (app.Resources.TryGetValue(key, out var plain) && plain is IBrush b2) return b2;
        return null;
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
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Topmost = true,
            SystemDecorations = Avalonia.Controls.WindowDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent,
        };
        var border = new Avalonia.Controls.Border
        {
            Margin = new(14),
            Padding = new(14, 11),
            CornerRadius = new(10),
            Background = Helpers.Brush(error ? "Brush.DangerBg" : "Brush.SelBg"),
            BorderBrush = Helpers.Brush(error ? "Brush.Alert" : "Brush.Green"),
            BorderThickness = new(1),
            Child = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, MaxWidth = 360 },
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