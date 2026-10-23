using QsppManager.Domain.Entities;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>The opt-in heavy OCR tier: MinerU (pinned local install) parsing through
/// its LOCAL doclib server. The wrapper never starts the server itself and never
/// touches the network: parse requests go to the loopback daemon. When the daemon
/// is off, the result fails with an explicit "server not running" marker and the
/// pipeline falls back to Apple Vision instead — never a silent mis-file and never
/// a stall. On-device privacy wording applies unchanged (nothing is transmitted
/// off-device; the daemon binds loopback by default — verified 24 Oct).</summary>
public sealed class FolioHeavyOcr(string mineruCli) : FolioOcrEngine
{
    /// <summary>Stable engine key — "4" is the pinned CLI line (>=4.0,<5), "standard"
    /// the benchmarked, user-approved tier (2.0 GB models, Apache-2.0-based).</summary>
    public const string EngineKey = "mineru-4-standard";
    /// <summary>Fallback trigger: the exact class of failure that means "the local
    /// server is off", matched case-insensitively in the exit output.</summary>
    public const string ServerDownMarker = "server is not running";

    public override string EngineKeyName => EngineKey;

    public override string UnavailableReason =>
        "The MinerU heavy tier is not installed on this machine (tools/ocr-heavy venv — user-approved install).";

    public static string? LocateCli()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("QSPP_MINERU_CLI"),
            // dev: the pinned venv inside the repo
            Path.Combine(AppContext.BaseDirectory.Split("/bin/")[0], "tools", "ocr-heavy", ".venv", "bin", "mineru"),
        };
        foreach (var candidate in candidates.Where(c => !string.IsNullOrEmpty(c)))
        {
            var p = Path.GetFullPath(candidate!);
            if (File.Exists(p)) return p;
        }
        // packaged app/dev fallback: walk up from the binaries for the repo venv
        var dir = Path.GetFullPath(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != Path.GetPathRoot(dir); i++)
        {
            var candidate = Path.Combine(dir, "tools", "ocr-heavy", ".venv", "bin", "mineru");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    /// <summary>Whole-file heavy parse (PDF or image). MinerU itself decides page
    /// layout; the output markdown's page markers become per-page records with no
    /// invented confidence (0.0 — none is measured; display-only anyway).</summary>
    public override FolioOcr.OcrResult Ocr(string fullPath, int? page, int dpi, CancellationToken ct = default)
    {
        if (!File.Exists(mineruCli)) return Unavailable;
        var isPdf = Path.GetExtension(fullPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var pageArg = isPdf ? "--pages all " : "";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = mineruCli,
            Arguments = $"parse \"{fullPath}\" --tier standard {pageArg}--json --force",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        try { proc.Start(); }
        catch (Exception ex) { return new(false, $"heavy tier could not start: {ex.Message}", EngineKey, 0, []); }
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        const int timeoutMs = 600_000; // heavy tier is slow by design; 10 min ceiling per file
        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already dead */ }
            return new(false, $"heavy tier timed out after 600 s ({Path.GetFileName(fullPath)}).", EngineKey, 0, []);
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        var exit = proc.ExitCode;
        if (exit != 0)
        {
            var msg = string.IsNullOrWhiteSpace(stderr) ? $"heavy tier exit {exit}." : stderr.Trim();
            if (msg.Contains(ServerDownMarker, StringComparison.OrdinalIgnoreCase))
                msg = "the heavy tier's local server is not running (scripts/serve-mineru.sh starts it) — falls back to Apple Vision.";
            return new(false, msg, EngineKey, 0, []);
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<MineruOutput>(stdout, FolioJson.Opts);
            var markdown = parsed?.Content?.Content ?? "";
            if (markdown.Length == 0)
                return new(false, "heavy tier returned no text (likely an unreadable or blank document).", EngineKey, 0, []);
            return new(true, "", EngineKey, SplitPages(markdown).Count,
                SplitPages(markdown).Select((t, i) => new FolioOcr.OcrPage(i + 1, t, 0.0)).ToList());
        }
        catch (Exception ex)
        {
            return new(false, $"heavy tier output unreadable: {ex.Message}", EngineKey, 0, []);
        }
    }

    /// <summary>Split MinerU markdown on its page-marker comments: &lt;!-- page N of M --&gt;
    /// — each marker opens a page whose content runs to the next marker (or the end).</summary>
    public static List<string> SplitPages(string markdown)
    {
        var pages = new List<string>();
        var marker = "<!-- page ";
        var idx = 0;
        while (true)
        {
            var start = markdown.IndexOf(marker, idx, StringComparison.Ordinal);
            if (start < 0) break;
            var contentStart = markdown.IndexOf("-->", start, StringComparison.Ordinal);
            if (contentStart < 0) break;
            contentStart += 3;
            var next = markdown.IndexOf(marker, contentStart, StringComparison.Ordinal);
            var text = (next < 0 ? markdown[contentStart..] : markdown[contentStart..next]).Trim();
            if (text.Length > 0) pages.Add(text);
            idx = next < 0 ? markdown.Length : next;
            if (next < 0) break;
        }
        if (pages.Count == 0 && markdown.Trim().Length > 0) pages.Add(markdown.Trim());
        return pages;
    }

    private sealed record MineruOutput(ParseInfo? Parse, MineruContent? Content);
    private sealed record MineruContent(string Content, string Format);
    private sealed record ParseInfo(string Sha256, string ShortId, string Tier, string Status);
}