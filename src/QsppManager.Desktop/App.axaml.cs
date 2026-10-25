using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using QsppManager.Desktop.ViewModels;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.ProcessSort;
using QsppManager.Infrastructure.Runtime;
using QsppManager.Infrastructure.Seed;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Serilog;
using QsppManager.Desktop.Views;
using QsppManager.Domain;

namespace QsppManager.Desktop;

public partial class App : Avalonia.Application
{
    public static string RepoRoot { get; private set; } = "";
    public static QsppDbContext Db { get; private set; } = null!;
    public static AttachmentStore Store { get; private set; } = null!;
    public static AuditService Audit { get; private set; } = null!;
    public static AuthService Auth { get; private set; } = null!;
    public static SeedImporter Seeds { get; private set; } = null!;
    public static QsppWorkspace Workspace { get; private set; } = null!;
    public static QsppManager.Infrastructure.ProcessSort.FolioWorkspace Folio { get; private set; } = null!;
    public static CupsPrintService Printer { get; private set; } = null!;
    public static PdfComposer Composer { get; private set; } = null!;
    public static CorpusSearch Search { get; private set; } = null!;
    public static DocumentTextIndex TextIndex { get; private set; } = null!;
    public static bool DemoMode { get; private set; }
    public static AppConfig Config { get; private set; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsMacOS())
        {
            // The bold macOS app menu is exported from the Application-level NativeMenu.
            // XAML attachment is ignored by the 12.1.4 mac exporter while a window-level
            // NativeMenu is present, so build the app menu in code BEFORE platform setup.
            var menu = new NativeMenu();
            menu.Items.Add(WithGesture("About QSPP Manager", null, MenuAbout));
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(WithGesture("Settings…", new Avalonia.Input.KeyGesture(Avalonia.Input.Key.OemComma, Avalonia.Input.KeyModifiers.Meta), MenuSettings));
            // Hide/Show All/Quit are appended automatically by Avalonia's standard app-menu block.
            NativeMenu.SetMenu(this, menu);
        }
    }

    private static NativeMenuItem WithGesture(string header, Avalonia.Input.KeyGesture? gesture, System.EventHandler e)
    {
        var mi = new NativeMenuItem(header);
        if (gesture is { } g) { mi.Gesture = g; }
        mi.Click += e;
        return mi;
    }

    private void MenuAbout(object? sender, EventArgs e) => (Main as MainWindow)?.ShowAbout();

    public override void OnFrameworkInitializationCompleted()
    {
        var (dataRoot, migrated, migrationWarning) = AppData.MigrateLegacyOnFirstRun();
        AppData.SessionRoot = dataRoot;
        AppData.EnsureAll();
        RestoreStagedBackupIfAny();
        RepoRoot = FindRepoRoot();
        DisplayTitleSources.RepoRootHint = RepoRoot;   // display titles load from the same seed location
        DemoMode = Environment.GetEnvironmentVariable("QSPP_DEMO") == "1";

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(AppData.LogsDir, "app-.log"), rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .CreateLogger();
        if (migrationWarning != null) Log.Warning("{Warn}", migrationWarning);

        // A UI-thread exception should not take the whole session down: log it and keep running.
        // (⌘, previously crashed the app via a latent SettingsView cast bug — caught this way too.)
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            try { Log.Error(e.Exception, "UI-thread exception caught; app keeps running"); } catch { }
        };

        Config = AppConfig.Load();

        var options = new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={AppData.DatabasePath(DemoMode)}")
            .Options;
        Db = new QsppDbContext(options);
        DatabaseBootstrapper.Prepare(Db);   // fresh → migrations create; legacy EnsureCreated → stamped then migrated

        var fontsDir = Path.Combine(RepoRoot, "assets", "fonts");
        if (!Directory.Exists(fontsDir))
            fontsDir = Path.Combine(AppContext.BaseDirectory, "..", "Resources", "assets", "fonts"); // bundled in the .app
        if (!Directory.Exists(fontsDir))
            fontsDir = AppData.Root; // installed app: fonts copied to appdata at packaging time
        PdfComposer.ConfigureLicense();

        Store = new AttachmentStore(AppData.AttachmentsDir);
        Audit = new AuditService(Db);
        Auth = new AuthService(Db, Audit);
        Config.PackRoot = string.IsNullOrWhiteSpace(Config.PackRoot) ? null : Config.PackRoot;
        SeedImporter.PackRootOverride = Config.PackRoot;
        Seeds = new SeedImporter(Db, Store, Audit, RepoRoot);
        Search = new CorpusSearch(Db);
        TextIndex = new DocumentTextIndex(Db, Store, Search, AppData.Ensure("document-text"));
        Workspace = new QsppWorkspace(Db, Store, Audit, Search);
        Folio = new FolioWorkspace(Db, Workspace, Store, Audit, AppData.Ensure("folio-cache"));
        // Stage-2 connects only if a LOCAL decision server is up on loopback; a
        // missing/down server leaves deterministic stage-1 exactly as before.
        try { Folio.SetStage2(new FolioDecisionStage2()); } catch { /* non-loopback configured: refuse quietly */ }
        Printer = new CupsPrintService(Db, Audit);
        Composer = new PdfComposer(fontsDir);

        ApplyAppearance(Config.Appearance);

        try
        {
            Seeds.ImportAll();
            // Fresh full-text index over whatever the import (re)built — cheap at corpus scale.
            Search.EnsureIndex();
            Log.Information("FTS index rebuilt: {Rows} rows", Search.RebuildIndex());
            Log.Information("Document full-text reindexed from cache: {Docs} docs", TextIndex.ReindexCached());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "seed import partially failed");
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.FontsDiagnostic)
            {
                // --fonts: report what actually resolves, then exit (3B).
                ApplyAppearance(Program.RenderTheme ?? "light");
                FontsDiagnostic.Run();
                base.OnFrameworkInitializationCompleted();
                desktop.Shutdown();
                return;
            }
            if (Program.AssertTodayLayout)
            {
                // --assert-today: hero layout assertions at 100/130/150% text scale (3A).
                ApplyAppearance(Program.RenderTheme ?? "light");
                var scale = Program.TextScale;
                TodayLayoutAssert.Run(desktop, scale);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            if (Program.RenderOffscreen is { } renderPath)
            {
                // Off-screen verification render: the window is the lifetime's main window so
                // closing it ends the process; the capture tick sets the exit code first.
                var page = Program.RenderPage;
                // Deterministic render theme (arg wins; default light) BEFORE any brush resolves.
                ApplyAppearance(Program.RenderTheme ?? "light");
                desktop.MainWindow = page is { } p && p is not (ShellPage.Loading or ShellPage.Login or ShellPage.Onboarding)
                    ? BuildShellWindow(p, renderPath)
                    : BuildOffscreenWindow(renderPath);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            desktop.MainWindow = new MainWindow();
            Main = desktop.MainWindow;
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ==================== app menu (Application-level NativeMenu, macOS bold menu) ====================

    private void MenuSettings(object? sender, EventArgs e) => (Main as MainWindow)?.OpenSettings();

    private void MenuQuit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    /// <summary>Off-screen render of a signed-in shell screen (the 10 destinations): the
    /// same chrome as MainWindow.axaml (sidebar, toolbar, page host, text-scale wrapper)
    /// composed in code, without window behaviours. The page is built exactly as the app
    /// builds it, against the scratch data dir; --hold keeps the window for the OS capture.</summary>
    internal static Window BuildShellWindow(ShellPage page, string path)
    {
        var window = new Window
        {
            Title = "Shell offscreen render",
            Width = Program.RenderWidth,
            Height = Program.RenderHeight,
            ShowActivated = false,
            ShowInTaskbar = false,
            SystemDecorations = Avalonia.Controls.WindowDecorations.None,
            Background = Helpers.Brush("Brush.Paper"),
        };

        try
        {
            App.Session = new UserSession(Guid.Empty, "owner", "Jordan Vella", UserRole.OwnerAdmin);  // neutral render persona: chip/greeting must never bake in a real person
            App.Config.SidebarCollapsed = false;   // renders show the expanded sidebar, deterministic
            var shell = new ShellViewModel();
            shell.SignIn(App.Session);             // lands on Today; recurring occurrences generated (scratch DB)
            if (page != ShellPage.Today) shell.Navigate(page);

            // ---- sidebar (mirrors MainWindow.axaml: header, three groups, pinned Settings)
            Button NavRow(ShellViewModel s, NavItem item, bool isSettings)
            {
                static string HueClass(ShellPage p) => p switch
                {
                    ShellPage.Today => "hueOchre",
                    ShellPage.Requirements => "hueForest",
                    ShellPage.Documents => "hueTeal",
                    ShellPage.Tasks => "hueClay",
                    ShellPage.Registers => "hueUmber",
                    ShellPage.People => "hueBerry",
                    ShellPage.Assessments => "hueViolet",
                    ShellPage.PrintCentre or ShellPage.Settings => "hueStone",
                    _ => "",
                };
                var b = new Button
                {
                    Classes = { "navRow" }, Height = 30, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Command = s.NavigateCommand, CommandParameter = item.Page,
                };
                if (HueClass(item.Page) is { } hc && hc != "") b.Classes.Add(hc);  // sidebar icon hue (3D)
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
                // No inline GlyphBrush: the navRow hue styles (App.axaml) must govern — the
                // destination hue at 85% when unselected, 100% with its tint when selected.
                g.Children.Add(new LucideGlyph { Glyph = item.Icon, GlyphSize = 16, StrokeThickness = 1.5 });
                g.Children.Add(new TextBlock { Text = item.Label });
                var label = (TextBlock)g.Children[1];
                Grid.SetColumn(label, 1);
                label.VerticalAlignment = VerticalAlignment.Center;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                b.Content = g;
                void Sync()
                {
                    var active = isSettings ? s.IsSettingsActive : item.IsActive;
                    if (active) b.Classes.Add("sel"); else b.Classes.Remove("sel");
                }
                item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NavItem.IsActive)) Sync(); };
                s.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ShellViewModel.IsSettingsActive)) Sync(); };
                Sync();
                return b;
            }

            // 3F #2: identical header block on the code-built window — two lines, untrimmed
            // (Avalonia renders trimming as one line), then 'QSPP Manager' beneath, fixed height.
            var sidebarHeader = new StackPanel { Margin = new(18, 40, 16, 4), Height = 54 };
            sidebarHeader.Children.Add(new TextBlock
            {
                Classes = { "headline" }, Text = shell.PharmacyName,
                TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                VerticalAlignment = VerticalAlignment.Top,
            });
            sidebarHeader.Children.Add(new TextBlock { Classes = { "footnote" }, Text = "QSPP Manager" });

            var groupPanel = new StackPanel { Margin = new(8, 6, 8, 8) };
            foreach (var group in shell.NavGroups)
            {
                groupPanel.Children.Add(new TextBlock { Classes = { "eyebrow" }, Text = group.Label, Margin = new(10, 10, 0, 5) });
                foreach (var item in group.Items)
                    groupPanel.Children.Add(NavRow(shell, item, isSettings: false));
            }

            var settingsPinned = new Border
            {
                BorderBrush = Helpers.Brush("Brush.Divider"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(8, 4, 8, 10),
                Padding = new Thickness(0, 6, 0, 0),
                Child = NavRow(shell, shell.SettingsItem, isSettings: true),
            };

            var sidebarGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            sidebarGrid.Children.Add(sidebarHeader);
            var sidebarScroll = new ScrollViewer { Content = groupPanel };
            Grid.SetRow(sidebarScroll, 1);
            sidebarGrid.Children.Add(sidebarScroll);
            Grid.SetRow(settingsPinned, 2);
            sidebarGrid.Children.Add(settingsPinned);

            var sidebar = new Border
            {
                Width = 232,
                Background = Helpers.Brush("Brush.Sidebar"),
                BorderBrush = Helpers.Brush("Brush.Divider"),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = sidebarGrid,
            };

            // ---- toolbar (opaque paper + 1px divider; toggle, filter, commands, drag strip, user chip)
            var toolbarGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,10,Auto,10,Auto,*,Auto,12"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 14, 0),
            };
            toolbarGrid.Children.Add(new Button
            {
                Classes = { "ghost" }, Height = 28, Width = 30, Padding = new Thickness(4, 0),
                Content = new LucideGlyph { Glyph = "panel-left-close", GlyphSize = 17, GlyphBrush = Helpers.Brush("Brush.Ink"), StrokeThickness = 1.5 },
            });
            var search = new TextBox
            {
                Width = 220, Height = 28, FontSize = 12.5,
                PlaceholderText = "Filter",
            };
            ToolTip.SetTip(search, "Filter the current screen (⌘F)");
            search.Classes.Add("searchfield");   // sage fill, no border (3F #3)
            Grid.SetColumn(search, 2);
            toolbarGrid.Children.Add(search);
            var palette = new Button
            {
                Classes = { "ghost" }, Height = 26, Padding = new Thickness(6, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "Commands", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center },
                        new Border
                        {
                            Classes = { "keycap" }, VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock { Text = "⌘K" },
                        },
                    },
                },
            };
            Grid.SetColumn(palette, 4);
            toolbarGrid.Children.Add(palette);
            var drag = new Border { Background = Brushes.Transparent };
            Grid.SetColumn(drag, 5);
            toolbarGrid.Children.Add(drag);
            var chip = new Button
            {
                Classes = { "ghost" }, Height = 28, Padding = new Thickness(2, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Content = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new(12),
                    Background = Helpers.Brush("Brush.SelBg"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Child = new TextBlock
                    {
                        Text = shell.Initials, FontSize = 9, FontWeight = FontWeight.SemiBold,
                        Foreground = Helpers.Brush("Brush.Ink"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            };
            Grid.SetColumn(chip, 6);
            toolbarGrid.Children.Add(chip);

            var toolbar = new Border
            {
                Height = 52,
                Background = Helpers.Brush("Brush.Paper"),
                BorderBrush = Helpers.Brush("Brush.Divider"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = toolbarGrid,
            };

            // ---- page host with the app's text-scale wrapper
            var host = new ContentControl
            {
                Background = Helpers.Brush("Brush.Paper"),
                Content = new TextScaleContent { Child = shell.CurrentPage is Control p ? p : null },
            };

            var right = new Grid { RowDefinitions = new RowDefinitions("52,*") };
            right.Children.Add(toolbar);
            Grid.SetRow(host, 1);
            right.Children.Add(host);

            var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            root.Children.Add(sidebar);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            window.Content = root;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shell render setup failed for {Page}", page);
            Console.Error.WriteLine($"render failed: {ex.Message}");
            Environment.ExitCode = 1;
        }

        // Hold open for the external capture; the process is killed from outside.
        var kick = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        kick.Tick += (_, _) =>
        {
            kick.Stop();
            Log.Information("Shell render hold: {Page} at {W}x{H}", page, window.Width, window.Height);
        };
        kick.Start();
        return window;
    }

    /// <summary>Today redesign verification (24 Oct): build the Today screen against the real
    /// workspace (read-only), render it through a never-activated composited window, save a
    /// PNG at the requested path, then close the window (which ends the process). Nothing is
    /// written to production tables.</summary>
    private static Window BuildOffscreenWindow(string path)
    {
        var window = new Window
        {
            Title = Program.RenderPage switch
            {
                ShellPage.Onboarding => "Onboarding offscreen render",
                ShellPage.Login => "Login offscreen render",
                _ => "Today offscreen render",
            },
            Width = Program.RenderWidth,
            Height = Program.RenderHeight,
            ShowActivated = false,
            ShowInTaskbar = false,
            SystemDecorations = Avalonia.Controls.WindowDecorations.None,
        };

        TodayScreen? screen = null;
        Control? root = null;
        try
        {
            App.Session = new UserSession(Guid.Empty, "owner", "Jordan Vella", UserRole.OwnerAdmin);  // neutral render persona: chip/greeting must never bake in a real person
            var shell = new ShellViewModel();
            if (Program.RenderPage is ShellPage.Onboarding or ShellPage.Login)
            {
                // First-run surfaces: build the real views exactly as ShellViewModel.Navigate
                // does, so first-run renders match what a real user sees on a fresh data dir.
                root = Program.RenderPage == ShellPage.Onboarding
                    ? new OnboardingView(shell)
                    : new LoginView(shell);
            }
            else
            {
                var vm = new TodayViewModel(shell, null!);
                screen = new TodayScreen(vm);
                vm.Refresh();
                screen.PostRefresh(vm);
                root = screen.Root;
            }
            window.Content = new Border
            {
                Background = Helpers.Brush("Brush.Paper") ?? Avalonia.Media.Brushes.White,
                Child = root,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Offscreen render setup failed");
            Console.Error.WriteLine($"render failed: {ex.Message}");
            Environment.ExitCode = 1;
        }

        var kick = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        kick.Tick += (_, _) =>
        {
            kick.Stop();
            if (root is null) { window.Close(); return; }
            if (Program.Hold)
            {
                // Hold open for an external OS capture (screencapture -l); the process is
                // killed from outside. Log the window dimensions once for the record.
                Log.Information("Offscreen hold: content {C} root {R}", window.Content is Border b2 ? b2.Bounds.Size : default, root.Bounds.Size);
                return;
            }
            try
            {
                // Force a full layout pass before capture; render the CONTENT root (the
                // window surface itself composites as transparent → black in PNG).
                var content = (Border)window.Content!;
                content.Background = Helpers.Brush("Brush.Paper");
                window.UpdateLayout();
                Log.Information("Offscreen bounds: window content {Content} root {Root}", content.Bounds.Size, root.Bounds.Size);
                root.InvalidateVisual();
                var scale = window.RenderScaling;
                var size = new PixelSize((int)(content.Bounds.Width * scale), (int)(content.Bounds.Height * scale));
                using var bmp = new RenderTargetBitmap(size, new Vector(scale, scale));
                bmp.Render(root);
                using (var fs = File.Create(path))
                    bmp.Save(fs);
                Log.Information("Offscreen render written: {Path}", path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Offscreen render failed");
                Console.Error.WriteLine($"render failed: {ex.Message}");
                if (Environment.ExitCode == 0) Environment.ExitCode = 1;
            }
            window.Close();
        };
        kick.Start();
        return window;
    }

    public static void ApplyAppearance(string mode)
    {
        var app = Current;
        if (app is null) return;
        app.RequestedThemeVariant = mode switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>If a verified restore left files in appsdata/restore-staging, promote them
    /// (database + attachments) into the live data dir BEFORE the database opens. Runs once;
    /// the staging dir is removed after promotion.</summary>
    private static void RestoreStagedBackupIfAny()
    {
        var staging = AppData.RestoreStaging();
        var stagedDb = Path.Combine(staging, "qspp.db");
        if (!File.Exists(stagedDb)) return;
        var liveDb = AppData.DatabasePath(App.DemoMode);
        File.Copy(stagedDb, liveDb, overwrite: true);
        var stagedAtt = Path.Combine(staging, "attachments");
        if (Directory.Exists(stagedAtt))
        {
            var attDir = AppData.AttachmentsDir;
            foreach (var f in Directory.EnumerateFiles(stagedAtt, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(attDir, Path.GetRelativePath(stagedAtt, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
            }
        }
        try { Directory.Delete(staging, recursive: true); } catch { }
        Console.WriteLine($"[qspp] restored backup promoted into {AppData.Root}");
    }

    /// <summary>Signed-in session; null until sign-in. Roles enforced in workspace services.</summary>
    public static UserSession? Session { get; set; }
    public static Window? Main { get; set; }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "seed")) && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
        var nearExe = Path.Combine(AppContext.BaseDirectory, "seed");
        if (Directory.Exists(nearExe)) return AppContext.BaseDirectory;
        // packaged .app: Resources sits at Contents/Resources (Contents/Resources/seed + /docs)
        var resources = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources"));
        if (Directory.Exists(Path.Combine(resources, "seed"))) return resources;
        return AppContext.BaseDirectory;
    }
}