using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Reactive;
using QsppManager.Desktop.ViewModels;
using QsppManager.Infrastructure.Runtime;
using QsppManager.Infrastructure.Seed;

namespace QsppManager.Desktop;

/// <summary>Mac-first shell window: extended client area (traffic lights over the
/// sidebar strip), collapsible sidebar, contextual toolbar, full native menu bar,
/// command palette overlay. ⌘W hides (close ≠ quit); dock click reopens.</summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell = new();
    private bool _autoCollapsed;   // narrow-window auto-collapse came from the size watcher

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _shell;

        // ---- palette opens → focus its search box
        _shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.PaletteOpen) && _shell.PaletteOpen)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    PaletteSearch.Focus();
                    PaletteSearch.SelectAll();
                }, Avalonia.Threading.DispatcherPriority.Input);
            if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
                RebuildPageHost();
            if (e.PropertyName == nameof(ShellViewModel.CurrentPage) && ToolbarSearch.Text?.Length > 0)
                ToolbarSearch.Text = "";   // new page: the old filter no longer applies
        };

        RebuildPageHost();

        // ---- toolbar search routes into the current page's filter box
        ToolbarSearch.TextChanged += (_, _) =>
        {
            if (_shell.CurrentPage is IPageSearchFocus page)
                page.SetFilter(ToolbarSearch.Text ?? "");
        };

        // ---- drag strip (empty toolbar stretch) moves the window
        ToolbarDragStrip.PointerPressed += (_, e) =>
        {
            try { BeginMoveDrag(e); } catch { }
        };

        PaletteBackdrop.PointerPressed += (_, _) => _shell.ClosePalette();
        PaletteResults.DoubleTapped += (_, _) => OpenSelectedResult();

        KeyDown += OnFallbackKey;

        Closing += OnWindowClosing;
        SizeChanged += OnSizeChangedAutoCollapse;
        PropertyChanged += OnWindowPropertyChanged;   // watches TransparencyLevel
        WireReopen();

        WireTextScale();
        if (OperatingSystem.IsMacOS()) BuildNativeMenu();
    }

    // ==================== keys ====================

    private void OnFallbackKey(object? sender, KeyEventArgs e)
    {
        var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var acc = meta || ctrl;

        switch (e.Key)
        {
            case Key.K when acc: _shell.TogglePalette(); e.Handled = true; break;
            case Key.F when acc: FocusSearchTarget(); e.Handled = true; break;
            case Key.P when meta: _shell.RequestPrint(); e.Handled = true; break;
            case Key.OemComma when acc: _shell.Navigate(ShellPage.Settings); e.Handled = true; break;
            case Key.W when meta: Close(); e.Handled = true; break;
            case Key.Q when meta: ShutdownApp(); e.Handled = true; break;
            case Key.M when meta: WindowState = WindowState.Minimized; e.Handled = true; break;
            case Key.S when meta && e.KeyModifiers.HasFlag(KeyModifiers.Alt): _shell.ToggleSidebar(); e.Handled = true; break;
            case Key.D1 when meta: _shell.Navigate(ShellPage.Today); e.Handled = true; break;
            case Key.D2 when meta: _shell.Navigate(ShellPage.Requirements); e.Handled = true; break;
            case Key.D3 when meta: _shell.Navigate(ShellPage.Documents); e.Handled = true; break;
            case Key.D4 when meta: _shell.Navigate(ShellPage.Tasks); e.Handled = true; break;
            case Key.D5 when meta: _shell.Navigate(ShellPage.Registers); e.Handled = true; break;
            case Key.D6 when meta: _shell.Navigate(ShellPage.People); e.Handled = true; break;
            case Key.D7 when meta: _shell.Navigate(ShellPage.Assessments); e.Handled = true; break;
            case Key.D8 when meta: _shell.Navigate(ShellPage.Library); e.Handled = true; break;
            case Key.D9 when meta: _shell.Navigate(ShellPage.PrintCentre); e.Handled = true; break;
            case Key.D0 when meta: _shell.Navigate(ShellPage.Settings); e.Handled = true; break;
            case Key.Escape when _shell.PaletteOpen: _shell.ClosePalette(); e.Handled = true; break;
            case Key.Return when _shell.PaletteOpen: OpenSelectedResult(); e.Handled = true; break;
        }
    }

    private void OpenSelectedResult()
    {
        var row = _shell.SelectedSearchResult ?? _shell.SearchResults.FirstOrDefault();
        if (row != null) _shell.OpenResult(row);
        else _shell.ClosePalette();
    }

    /// <summary>⌘F: focus the toolbar search when the page supports a filter,
    /// otherwise open the command palette.</summary>
    private void FocusSearchTarget()
    {
        if (_shell.CurrentPage is IPageSearchFocus)
        {
            ToolbarSearch.Focus();
            ToolbarSearch.SelectAll();
        }
        else
        {
            _shell.TogglePalette();
        }
    }

    // ==================== text scale ====================

    private void WireTextScale()
    {
        Helpers.SetTextScale(Math.Clamp(App.Config.TextScale, 1.0, 2.0));
        Helpers.TextScaleChanged += _ => { FontSize = 14 * Helpers.TextScale; }; // inherited fallback; layout scale lives on PageHost

        // Theme change (menu toggle or OS system appearance): code-built pages capture
        // brushes at build time → rebuild the visible page so it re-resolves tokens.
        Helpers.ThemeChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(RebuildPageHost);
        ActualThemeVariantChanged += (_, _) => Helpers.FireThemeChanged();
        FontSize = 14 * Helpers.TextScale;
    }

    /// <summary>Page instance is single-owned: build the scale wrapper here, never
    /// via a ContentTemplate (double-parent race during re-templating). When the
    /// wrapper is replaced, unparent the old child FIRST — a control still parented
    /// throws "The Control already has a parent" on the next wrap (logged 24 Oct
    /// from a double theme-change; app survived, page rebuild did not).</summary>
    private TextScaleContent? _pageWrapper;
    private void RebuildPageHost()
    {
        if (_pageWrapper?.Child is not null) _pageWrapper.Child = null;
        PageHost.Content = _pageWrapper = _shell.CurrentPage is Control page ? new TextScaleContent { Child = page } : null;
    }

    // ==================== window lifecycle ====================

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // macOS: closing the window hides it (app stays alive, dock click reopens).
        // Quit is the app menu / ⌘Q. Other platforms keep normal close semantics.
        if (!OperatingSystem.IsMacOS()) return;
        e.Cancel = true;
        Hide();
    }

    private void WireReopen()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IActivatableLifetime activation) return;
        activation.Activated += (_, args) =>
        {
            if (args.Kind == Avalonia.Controls.ApplicationLifetimes.ActivationKind.Reopen)
                Avalonia.Threading.Dispatcher.UIThread.Post(ReopenFromDock);
        };
    }

    private void ReopenFromDock()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Narrow windows auto-collapse the sidebar; wide windows auto-restore
    /// it (only after an auto-collapse — the user's manual choice stays sticky).</summary>
    private void OnSizeChangedAutoCollapse(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0) return;
        var w = e.NewSize.Width;
        if (w < 1020 && !_shell.SidebarCollapsed)
        {
            _shell.ToggleSidebar();
            _autoCollapsed = true;
        }
        else if (w >= 1120 && _autoCollapsed && _shell.SidebarCollapsed)
        {
            _shell.ToggleSidebar();
            _autoCollapsed = false;
        }
    }

    /// <summary>When the achieved transparency level changes, solid fallback surfaces
    /// replace the translucent chrome (no acrylic → no floating-glass look).</summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name != "ActualTransparencyLevel") return;
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        var acrylic = ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur;
        Sidebar.Background = acrylic
            ? new SolidColorBrush(Color.FromArgb(0xB8, 0xE9, 0xED, 0xDF))
            : Helpers.Brush("Brush.Sidebar");
        ToolbarRow.Background = acrylic ? Brushes.Transparent : Helpers.Brush("Brush.Surface");
    }

    // ==================== native menu ====================

    private void BuildNativeMenu()
    {
        var appMenu = new NativeMenu();
        appMenu.Items.Add(Mi("About Hugh's QSPP Manager", ShowAbout));
        appMenu.Items.Add(Sep());
        appMenu.Items.Add(Mi("Settings…", Key.OemComma, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Settings)));
        appMenu.Items.Add(Sep());
        appMenu.Items.Add(Mi("Hide Hugh's QSPP Manager", Key.H, KeyModifiers.Meta, Hide));
        appMenu.Items.Add(Sep());
        appMenu.Items.Add(Mi("Quit Hugh's QSPP Manager", Key.Q, KeyModifiers.Meta, ShutdownApp));

        var fileMenu = new NativeMenu();
        fileMenu.Items.Add(Mi("Print…", Key.P, KeyModifiers.Meta, _shell.RequestPrint));
        fileMenu.Items.Add(Sep());
        fileMenu.Items.Add(Mi("Close Window", Key.W, KeyModifiers.Meta, Close));

        var editMenu = new NativeMenu();
        editMenu.Items.Add(Mi("Cut", Key.X, KeyModifiers.Meta, EditCut));
        editMenu.Items.Add(Mi("Copy", Key.C, KeyModifiers.Meta, EditCopy));
        editMenu.Items.Add(Mi("Paste", Key.V, KeyModifiers.Meta, EditPaste));
        editMenu.Items.Add(Sep());
        editMenu.Items.Add(Mi("Select All", Key.A, KeyModifiers.Meta, EditSelectAll));

        var viewMenu = new NativeMenu();
        viewMenu.Items.Add(Mi("Toggle Sidebar", Key.S, KeyModifiers.Meta | KeyModifiers.Alt, _shell.ToggleSidebar));
        viewMenu.Items.Add(Sep());
        viewMenu.Items.Add(Mi("Command Palette", Key.K, KeyModifiers.Meta, _shell.TogglePalette));
        viewMenu.Items.Add(Mi("Search Current Screen", Key.F, KeyModifiers.Meta, FocusSearchTarget));
        viewMenu.Items.Add(Sep());
        viewMenu.Items.Add(Mi("Go to Today ⌘1", Key.D1, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Today)));
        viewMenu.Items.Add(Mi("Go to Requirements ⌘2", Key.D2, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Requirements)));
        viewMenu.Items.Add(Mi("Go to Evidence & documents ⌘3", Key.D3, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Documents)));
        viewMenu.Items.Add(Mi("Go to Tasks & calendar ⌘4", Key.D4, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Tasks)));
        viewMenu.Items.Add(Mi("Go to Registers ⌘5", Key.D5, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Registers)));
        viewMenu.Items.Add(Mi("Go to People & training ⌘6", Key.D6, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.People)));
        viewMenu.Items.Add(Mi("Go to Assessments ⌘7", Key.D7, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Assessments)));
        viewMenu.Items.Add(Mi("Go to Library ⌘8", Key.D8, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Library)));
        viewMenu.Items.Add(Mi("Go to Print centre ⌘9", Key.D9, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.PrintCentre)));
        viewMenu.Items.Add(Mi("Go to Settings ⌘0", Key.D0, KeyModifiers.Meta, () => _shell.Navigate(ShellPage.Settings)));
        viewMenu.Items.Add(Sep());
        viewMenu.Items.Add(Mi("Appearance: Use System Appearance", null, null, () => ApplyMenuAppearance("system"), App.Config.Appearance == "system"));
        viewMenu.Items.Add(Mi("Appearance: Light", null, null, () => ApplyMenuAppearance("light"), App.Config.Appearance == "light"));
        viewMenu.Items.Add(Mi("Appearance: Dark", null, null, () => ApplyMenuAppearance("dark"), App.Config.Appearance == "dark"));
        viewMenu.Items.Add(Mi("Text Size: Regular (100%)", null, null, () => ApplyMenuTextScale(1.0), Math.Abs(App.Config.TextScale - 1.0) < 0.01));
        viewMenu.Items.Add(Mi("Text Size: Larger (115%)", null, null, () => ApplyMenuTextScale(1.15), Math.Abs(App.Config.TextScale - 1.15) < 0.01));
        viewMenu.Items.Add(Mi("Text Size: Largest (130%)", null, null, () => ApplyMenuTextScale(1.3), Math.Abs(App.Config.TextScale - 1.3) < 0.01));

        var windowMenu = new NativeMenu();
        windowMenu.Items.Add(Mi("Minimize", Key.M, KeyModifiers.Meta, () => WindowState = WindowState.Minimized));
        windowMenu.Items.Add(Mi("Zoom", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        windowMenu.Items.Add(Sep());
        windowMenu.Items.Add(Mi("Bring All to Front", BringAllWindowsToFront));

        var helpMenu = new NativeMenu();
        helpMenu.Items.Add(Mi("Open Data Folder in Finder", () => Helpers.RevealInFileManager(AppData.Root)));
        helpMenu.Items.Add(Mi("View App Logs in Finder", () => Helpers.RevealInFileManager(AppData.LogsDir)));
        helpMenu.Items.Add(Sep());
        helpMenu.Items.Add(Mi("Go to Library", () => _shell.Navigate(ShellPage.Library)));

        var root = new NativeMenu();
        root.Items.Add(new NativeMenuItem("Hugh's QSPP Manager") { Menu = appMenu });
        root.Items.Add(new NativeMenuItem("File") { Menu = fileMenu });
        root.Items.Add(new NativeMenuItem("Edit") { Menu = editMenu });
        root.Items.Add(new NativeMenuItem("View") { Menu = viewMenu });
        root.Items.Add(new NativeMenuItem("Window") { Menu = windowMenu });
        root.Items.Add(new NativeMenuItem("Help") { Menu = helpMenu });

        NativeMenu.SetMenu(this, root);
    }

    private NativeMenuItem Mi(string header, Action action, bool? isChecked = null)
    {
        var mi = new NativeMenuItem(header) { Command = new RelayCommand(_ => action()) };
        if (isChecked == true) mi.IsChecked = true;
        return mi;
    }

    private NativeMenuItem Mi(string header, Key? key, KeyModifiers? mods, Action action, bool? isChecked = null)
    {
        var mi = new NativeMenuItem(header)
        {
            Gesture = key is not null && mods is not null ? new KeyGesture(key.Value, mods.Value) : null,
            Command = new RelayCommand(_ => action()),
        };
        if (isChecked == true) mi.IsChecked = true;
        return mi;
    }

    private static NativeMenuItemSeparator Sep() => new();

    private void ApplyMenuAppearance(string mode)
    {
        App.Config.Appearance = mode;
        App.Config.Save();
        App.ApplyAppearance(mode);
        Helpers.FireThemeChanged();
        BuildNativeMenu(); // refresh checkmarks
    }

    private void ApplyMenuTextScale(double scale)
    {
        App.Config.TextScale = scale;
        App.Config.Save();
        Helpers.SetTextScale(scale);
        BuildNativeMenu();
    }

    // ==================== editing commands (focused TextBox) ====================

    private TextBox? FocusedTextBox()
    {
        var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
        return focusManager?.GetFocusedElement() as TextBox;
    }

    private async void EditCopy()
    {
        var tb = FocusedTextBox();
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (tb == null || clip == null) return;
        var text = tb.SelectedText;
        if (!string.IsNullOrEmpty(text)) await clip.SetTextAsync(text);
    }

    private async void EditCut()
    {
        var tb = FocusedTextBox();
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (tb == null || clip == null) return;
        var text = tb.SelectedText;
        if (string.IsNullOrEmpty(text)) return;
        await clip.SetTextAsync(text);
        ReplaceSelection(tb, "");
    }

    private async void EditPaste()
    {
        var tb = FocusedTextBox();
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (tb == null || clip == null) return;
        try
        {
            var text = await clip.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text)) ReplaceSelection(tb, text);
        }
        catch { /* clipboard unavailable — ignore */ }
    }

    private static void ReplaceSelection(TextBox tb, string text)
    {
        var start = Math.Min(tb.SelectionStart, tb.SelectionEnd);
        var end = Math.Max(tb.SelectionStart, tb.SelectionEnd);
        var s =tb.Text ?? "";
        var insert = start >= 0 && end > start ? s[..start] + text + s[end..]
                   : start >= 0 ? s[..start] + text + s[start..]
                   : s + text;
        tb.Text = insert;
        var caret = (start >= 0 && end > start ? start : start >= 0 ? start : s.Length) + text.Length;
        tb.CaretIndex = Math.Clamp(caret, 0, tb.Text?.Length ?? 0);
    }

    private void EditSelectAll()
    {
        FocusedTextBox()?.SelectAll();
    }

    private void BringAllWindowsToFront()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var w in desktop.Windows)
            {
                w.Show();
            }
        Activate();
    }

    private void ShutdownApp()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // ==================== about ====================

    /// <summary>Decode an embedded (avares://) image asset, downscaled to `width` px.</summary>
    private static Avalonia.Media.Imaging.Bitmap LoadEmbeddedImage(string uri, int width = 256)
    {
        using var s = Avalonia.Platform.AssetLoader.Open(new Uri(uri));
        return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(s, width);
    }

    private void ShowAbout()
    {
        var win = new Window
        {
            Title = "About Hugh's QSPP Manager",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            SystemDecorations = Avalonia.Controls.WindowDecorations.Full,
        };

        // Avatar mark: circular crop of the client logo with the brand-green keyline.
        var avatar = new Avalonia.Controls.Image
        {
            Source = LoadEmbeddedImage("avares://QsppManager.Desktop/Assets/Art/HughLogo.png"),
            Width = 64, Height = 64,
        };
        var avatarRing = new Avalonia.Controls.Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32),
            ClipToBounds = true, BorderBrush = Helpers.Brush("Brush.Green"), BorderThickness = new Thickness(2),
            Child = avatar,
        };

        var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(avatarRing);
        var headerText = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock
        {
            Text = "Hugh's QSPP Manager",
            FontWeight = FontWeight.SemiBold,
            FontSize = 21,
            Margin = new Thickness(0, 0, 0, 4),
        });
        var ver = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0";
        headerText.Children.Add(new TextBlock
        {
            Text = $"Version {ver} · M5 (macOS refinement)",
            FontSize = 11.5,
            Foreground = Helpers.Brush("Brush.Muted"),
        });
        header.Children.Add(headerText);

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(header);
        stack.Children.Add(Ui.InfoLine("Data folder", AppData.Root));
        stack.Children.Add(Ui.InfoLine("Sources pack", SeedImporter.PackRoot() ?? "(pack not found — set it in Settings)"));
        stack.Children.Add(new TextBlock
        {
            Text = "Offline-first. No cloud upload. Audit history is append-only.",
            FontSize = 11.5, Foreground = Helpers.Brush("Brush.Muted"),
            Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Hugh's QSPP Manager is an internal compliance workspace for the Victorian Quality Service Pharmacy Program (QSPP). Program documents belong to the Pharmacy Guild of Australia; requirements shown here are working aids, not the source text.",
            FontSize = 11.5, Foreground = Helpers.Brush("Brush.Muted"),
            Margin = new Thickness(0, 8, 0, 16), TextWrapping = TextWrapping.Wrap,
        });
        var closeBtn = new Button { Content = "Close", Classes = { "primary" } };
        closeBtn.Click += (_, _) => win.Close();
        stack.Children.Add(closeBtn);

        win.Content = new Border { Background = Helpers.Brush("Brush.Paper"), Child = stack };
        win.ShowDialog(this);
    }
}

/// <summary>Scales page content (not shell chrome) for enlarged-text support.</summary>
public class TextScaleContent : Avalonia.Controls.LayoutTransformControl
{
    private readonly ScaleTransform _scale = new(1, 1);

    public TextScaleContent()
    {
        LayoutTransform = _scale;
        Helpers.TextScaleChanged += Apply;
        Apply(Helpers.TextScale);
    }

    private void Apply(double s)
    {
        var clamped = Math.Clamp(s, 1.0, 2.0);
        _scale.ScaleX = clamped;
        _scale.ScaleY = clamped;
    }
}