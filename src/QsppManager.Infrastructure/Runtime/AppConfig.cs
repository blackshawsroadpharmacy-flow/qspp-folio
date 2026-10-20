using System.Text.Json;

namespace QsppManager.Infrastructure.Runtime;

/// <summary>Small user preferences file beside the database (app-config.json):
/// reference-pack location override, appearance intent, text scale and the last
/// successfully used printer. Database-backed settings stay in SQLite; this file
/// only carries shell preferences that must exist before the shell is usable.</summary>
public sealed class AppConfig
{
    public string? PackRoot { get; set; }
    public string Appearance { get; set; } = "system"; // system | light | dark
    public double TextScale { get; set; } = 1.0;
    public string? LastPrinter { get; set; }
    public bool SidebarCollapsed { get; set; }
    public bool ReduceMotion { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public static string ConfigPath => System.IO.Path.Combine(AppData.Root, "app-config.json");

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    public static AppConfig Load()
    {
        try
        {
            if (!System.IO.File.Exists(ConfigPath)) return new AppConfig();
            var cfg = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(System.IO.File.ReadAllText(ConfigPath), JsonOpts);
            return cfg ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"app-config.json unreadable, using defaults: {ex.Message}");
            return new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppData.Root);
            System.IO.File.WriteAllText(ConfigPath, System.Text.Json.JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"app-config.json could not be saved: {ex.Message}");
        }
    }
}