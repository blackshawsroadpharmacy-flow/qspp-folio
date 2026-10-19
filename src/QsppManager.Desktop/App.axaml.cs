using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Printing;
using QsppManager.Infrastructure.Runtime;
using QsppManager.Infrastructure.Seed;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Serilog;

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
    public static CupsPrintService Printer { get; private set; } = null!;
    public static PdfComposer Composer { get; private set; } = null!;
    public static CorpusSearch Search { get; private set; } = null!;
    public static DocumentTextIndex TextIndex { get; private set; } = null!;
    public static bool DemoMode { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppData.EnsureAll();
        RestoreStagedBackupIfAny();
        RepoRoot = FindRepoRoot();
        DemoMode = Environment.GetEnvironmentVariable("QSPP_DEMO") == "1";

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(AppData.LogsDir, "app-.log"), rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .CreateLogger();

        var options = new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={AppData.DatabasePath(DemoMode)}")
            .Options;
        Db = new QsppDbContext(options);
        DatabaseBootstrapper.Prepare(Db);   // fresh → migrations create; legacy EnsureCreated → stamped then migrated

        var fontsDir = Path.Combine(RepoRoot, "assets", "fonts");
        if (!Directory.Exists(fontsDir))
            fontsDir = AppData.Root; // installed app: fonts copied to appdata at packaging time
        PdfComposer.ConfigureLicense();

        Store = new AttachmentStore(AppData.AttachmentsDir);
        Audit = new AuditService(Db);
        Auth = new AuthService(Db, Audit);
        Seeds = new SeedImporter(Db, Store, Audit, RepoRoot);
        Search = new CorpusSearch(Db);
        TextIndex = new DocumentTextIndex(Db, Store, Search, AppData.Ensure("document-text"));
        Workspace = new QsppWorkspace(Db, Store, Audit, Search);
        Printer = new CupsPrintService(Db, Audit);
        Composer = new PdfComposer(fontsDir);

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
            Main = desktop.MainWindow!;
            desktop.MainWindow = new MainWindow();
            Main = desktop.MainWindow;
        }
        base.OnFrameworkInitializationCompleted();
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
        // packaged/installed layout: seed bundled next to the executable instead
        var nearExe = Path.Combine(AppContext.BaseDirectory, "seed");
        if (Directory.Exists(nearExe)) return AppContext.BaseDirectory;
        return AppContext.BaseDirectory;
    }
}