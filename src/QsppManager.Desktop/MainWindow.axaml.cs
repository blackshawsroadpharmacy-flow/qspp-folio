using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _shell;
        KeyDown += OnKey;
        Helpers.TextScaleChanged += _ => FontSize = 14 * Helpers.TextScale;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.K when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _shell.TogglePalette();
                e.Handled = true;
                break;
            case Key.Escape when _shell.PaletteOpen:
                _shell.ClosePalette();
                e.Handled = true;
                break;
            case Key.Return when _shell.PaletteOpen && _shell.SelectedSearchResult != null:
                _shell.OpenResult(_shell.SelectedSearchResult);
                e.Handled = true;
                break;
        }
    }
}