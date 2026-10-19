namespace QsppManager.Infrastructure.Runtime;

public static class AppData
{
    public static string Root =>
        Environment.GetEnvironmentVariable("QSPP_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QsppManager");

    public static string RestoreStaging() => Ensure("restore-staging");

    public static string DatabasePath(bool demo) => Path.Combine(Root, demo ? "qspp-demo.db" : "qspp.db");
    public static string SeedDirectory(string repoRoot) =>
        Path.Combine(repoRoot, "seed");
    public static string AttachmentsDir => Ensure("attachments");
    public static string PdfDir => Ensure("pdf");
    public static string BackupsDir => Ensure("backups");
    public static string LogsDir => Ensure("logs");

    public static string Ensure(string sub)
    {
        var p = Path.Combine(Root, sub);
        Directory.CreateDirectory(p);
        return p;
    }

    public static void EnsureAll() { Ensure("attachments"); Ensure("pdf"); Ensure("backups"); Ensure("logs"); }
}