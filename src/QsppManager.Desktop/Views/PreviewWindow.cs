using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace QsppManager.Desktop.Views;

/// <summary>Print preview window. In-app PDF page rendering is deliberately deferred
/// (SkiaSharp native-version collision with the bundled renderer — DD-20). The preview
/// opens the exact file the print job submits in the system viewer: what you see is
/// what will print. In-app thumbnails return in M2 with a renderer sharing Avalonia's Skia.</summary>
public sealed class PreviewWindow : Window
{
    public PreviewWindow(string pdfPath)
    {
        Title = $"Print preview · {Path.GetFileName(pdfPath)}";
        Width = 700; Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock
        {
            Classes = { "lead" },
            TextWrapping = TextWrapping.Wrap,
            Text = "The preview opens the rendered PDF in your system viewer; this is the exact file the print job submits, so what you see is what will print. In-app page thumbnails arrive in Milestone 2 once the PDF renderer shares the app's bundled Skia version.",
        };
        var open = new Button { Content = "Open PDF externally", Classes = { "primary" }, MinWidth = 170 };
        open.Click += (_, _) => Helpers.OpenWithSystemViewer(pdfPath);
        var close = new Button { Content = "Close", MinWidth = 90 };
        close.Click += (_, _) => Close();
        var fileNote = new TextBlock
        {
            Classes = { "small" },
            TextWrapping = TextWrapping.Wrap,
            Text = $"Rendered file: {pdfPath}",
        };

        Content = new StackPanel
        {
            Margin = new(20), Spacing = 12,
            Children = { new TextBlock { Classes = { "h3" }, Text = "Preview" }, message, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { open, close } }, fileNote },
        };

        /* Open automatically — "Render + preview" is meant to make the page visible
           without another click. */
        try { Helpers.OpenWithSystemViewer(pdfPath); } catch { /* the button stays available */ }
    }
}