using System.Diagnostics;
using System.Text.Json;
using QsppManager.Domain.Entities;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Runs the local Apple Vision OCR worker (`qspp-ocr`, a compiled Swift
/// helper in tools/ocr/). The helper uses Vision + PDFKit only, entirely on-device;
/// no request ever touches a network interface. Bounded per run: wall-clock timeout,
/// no parallelism here (the batch processes one file at a time), and stdout JSON only.
/// The helper path is located at App.Data("ocr") if bundled with the packaged app,
/// else next to the running binary or the repo build/ directory — never downloaded.
/// This is the DEFAULT engine; the heavy tier (FolioHeavyOcr) is opt-in.</summary>
public sealed class FolioOcr(string helperPath) : FolioOcrEngine
{
    /// <summary>Stable engine/version key used in extraction-cache file names.</summary>
    public const string EngineKey = "apple-vision-2";

    public override string EngineKeyName => EngineKey;
    public override string UnavailableReason =>
        "The Apple Vision OCR helper (qspp-ocr) is not available on this machine. Bundled with the packaged app.";

    public static string? LocateHelper()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("QSPP_OCR_HELPER"),
            // packaged app: qspp-ocr sits beside the app binary
            Path.Combine(AppContext.BaseDirectory, "qspp-ocr"),
            "/Applications/Hugh's QSPP Manager.app/Contents/MacOS/qspp-ocr",
        };
        foreach (var c in candidates.Where(c => !string.IsNullOrEmpty(c)))
        {
            var p = Path.GetFullPath(c!);
            if (File.Exists(p)) return p;
        }
        // dev runs: walk up from the binaries looking for the repo's build/ helper
        var dir = Path.GetFullPath(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != Path.GetPathRoot(dir); i++)
        {
            var candidate = Path.Combine(dir, "build", "qspp-ocr");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    public sealed record OcrPage(int Page, string Text, double Confidence);
    public sealed record OcrResult(bool Ok, string Error, string EngineIdentifier, int PageCount, List<OcrPage> Pages)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string FullText => string.Join("\n", Pages.Select(p => p.Text).Where(t => t.Length > 0));
    }

    /// <summary>OCR for one PDF page or a whole image. wall-clock timeout keeps a
    /// pathological page from stalling the batch; the item is then queued honestly.</summary>
    public override FolioOcr.OcrResult Ocr(string fullPath, int? page, int dpi, CancellationToken ct = default)
    {
        if (!File.Exists(helperPath)) return Unavailable;
        var args = page is { } p ? $"--page {p} --dpi {dpi}" : $"--dpi {dpi}";
        var psi = new ProcessStartInfo
        {
            FileName = helperPath,
            Arguments = $"\"{fullPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = new Process { StartInfo = psi };
        try { proc.Start(); }
        catch (Exception ex) { return new(false, $"OCR helper could not start: {ex.Message}", EngineKey, 0, []); }
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        var timeoutMs = page is { } ? 90_000 : 240_000;
        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already dead */ }
            return new(false, $"OCR timed out after {(timeoutMs / 1000)} s ({Path.GetFileName(fullPath)}).", EngineKey, 0, []);
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        var exit = proc.ExitCode;
        if (exit is 2 or 3)
            return new(false, stderr.Trim(), EngineKey, 0, []);
        if (exit != 0)
            return new(false, string.IsNullOrWhiteSpace(stderr) ? $"OCR helper exit {exit}." : stderr.Trim(), EngineKey, 0, []);

        try
        {
            var parsed = JsonSerializer.Deserialize<OcrHelperOutput>(stdout, FolioJson.Opts);
            if (parsed == null) return new(false, "OCR helper returned unreadable output.", EngineKey, 0, []);
            return new(true, "", parsed.Engine, parsed.Pages.Count,
                parsed.Pages.Select(x => new OcrPage(x.Page, x.Text ?? "", x.Confidence)).ToList());
        }
        catch (Exception ex)
        {
            return new(false, $"OCR helper output unreadable: {ex.Message}", EngineKey, 0, []);
        }
    }

    private sealed record OcrHelperOutput(string Engine, string? OsVersion, List<OcrHelperPage> Pages);
    private sealed record OcrHelperPage(int Page, string? Text, double Confidence);
}