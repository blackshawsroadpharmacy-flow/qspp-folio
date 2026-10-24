using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var (dataRoot, migrated, migrationWarning) = AppData.MigrateLegacyOnFirstRun();
        AppData.SessionRoot = dataRoot;
        AppData.EnsureAll();
        RestoreStagedBackupIfAny();
        RepoRoot = FindRepoRoot();
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
            if (Program.RenderOffscreen is { } renderPath)
            {
                // Off-screen verification render: the window is the lifetime's main window so
                // closing it ends the process; the capture tick sets the exit code first.
                desktop.MainWindow = BuildOffscreenWindow(renderPath);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            desktop.MainWindow = new MainWindow();
            Main = desktop.MainWindow;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Today redesign verification (24 Oct): build the Today screen against the real
    /// workspace (read-only), render it through a never-activated composited window, save a
    /// PNG at the requested path, then close the window (which ends the process). Nothing is
    /// written to production tables.</summary>
    private static Window BuildOffscreenWindow(string path)
    {
        var window = new Window
        {
            Title = "Today offscreen render",
            Width = 1280,
            Height = 940,
            ShowActivated = false,
            ShowInTaskbar = false,
            SystemDecorations = Avalonia.Controls.WindowDecorations.None,
        };

        TodayScreen? screen = null;
        try
        {
            // Verification-only knob: QSPP_RENDER_THEME=dark renders the dark dictionary.
            if (Environment.GetEnvironmentVariable("QSPP_RENDER_THEME") is { } t && t.Length > 0)
                Current.RequestedThemeVariant = t == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            App.Session = new UserSession(Guid.Empty, "owner", "Owner", UserRole.OwnerAdmin);
            var vm = new TodayViewModel(new ShellViewModel(), null!);
            screen = new TodayScreen(vm);
            vm.Refresh();
            screen.PostRefresh(vm);
            window.Content = new Border
            {
                Background = Helpers.Brush("Brush.Paper") ?? Avalonia.Media.Brushes.White,
                Child = screen.Root,
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
            if (screen is null) { window.Close(); return; }
            if (Program.Hold)
            {
                // Hold open for an external OS capture (screencapture -l); the process is
                // killed from outside. Log the window dimensions once for the record.
                Log.Information("Offscreen hold: content {C} root {R}", window.Content is Border b2 ? b2.Bounds.Size : default, screen.Root.Bounds.Size);
                return;
            }
            try
            {
                // Force a full layout pass before capture; render the CONTENT root (the
                // window surface itself composites as transparent → black in PNG).
                var content = (Border)window.Content!;
                content.Background = Helpers.Brush("Brush.Paper");
                window.UpdateLayout();
                var root = screen.Root;
                Log.Information("Offscreen bounds: window content {Content} root {Root}", content.Bounds.Size, root.Bounds.Size);
                root.InvalidateVisual();
                var scale = window.RenderScaling;
                var size = new PixelSize((int)(1280 * scale), (int)(940 * scale));
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