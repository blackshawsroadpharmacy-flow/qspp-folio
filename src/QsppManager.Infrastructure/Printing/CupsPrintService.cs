using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Infrastructure.Printing;

/// <summary>CUPS print adapter (Linux). Renders are submitted with `lp`; discovery via
/// `lpstat -p -d`. Argument lists are passed as arrays (no shell interpolation). Spooler
/// acceptance is NOT proof of paper — the job state says "Sent to printer" and asks the
/// user to confirm at the device. Windows/macOS adapters are honest stubs until verified.</summary>
public class CupsPrintService(QsppDbContext db, AuditService audit)
{
    public record PrinterInfo(string Name, bool IsDefault);

    public List<PrinterInfo> DiscoverPrinters()
    {
        try
        {
            var psi = new ProcessStartInfo("lpstat", ["-p", "-d"]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("lpstat could not be started.");
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            List<PrinterInfo> list = new();
            foreach (var line in output.Split('\n'))
            {
                if (line.StartsWith("printer ", StringComparison.Ordinal))
                    list.Add(new PrinterInfo(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "", false));
                if (line.StartsWith("system default destination:", StringComparison.Ordinal))
                {
                    var def = line.Split(':', StringSplitOptions.RemoveEmptyEntries) is { Length: > 1 } parts ? parts[1].Trim() : "";
                    list = list.Select(x => x with { IsDefault = x.Name == def }).ToList();
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            // No CUPS/print system available — tell the truth.
            Console.Error.WriteLine($"printer discovery failed: {ex.Message}");
            return [];
        }
    }

    public record PrintOutcome(bool Ok, string detail);

    /// <summary>Submits an existing PDF to a printer. No automatic retries: a failed or
    /// uncertain submission may have consumed paper elsewhere; the user re-prints deliberately.</summary>
    public PrintOutcome SubmitToPrinter(string pdfPath, string printer, int copies, bool duplex, CancellationToken ct = default)
    {
        if (!File.Exists(pdfPath)) return new PrintOutcome(false, $"Rendered PDF is missing at {pdfPath}. This is an application error — please try again.");
        var known = DiscoverPrinters().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (known.Count > 0 && !known.Contains(printer))
            return new PrintOutcome(false, $"Printer '{printer}' is not visible to the print system right now. Check the device, then choose a printer again. Nothing was printed.");

        var args = new List<string> { "-d", printer, "-n", Math.Clamp(copies, 1, 50).ToString() };
        if (duplex) args.AddRange(["-o", "sides=two-sided-long-edge"]);
        args.Add("-o");
        args.Add("sides=one-sided");
        args.Add(pdfPath);
        _ = args; // replaced below: duplex option must be exclusive

        var finalArgs = new List<string> { "-d", printer, "-n", Math.Clamp(copies, 1, 50).ToString() };
        finalArgs.AddRange(duplex ? ["-o", "sides=two-sided-long-edge"] : ["-o", "sides=one-sided"]);
        finalArgs.Add(pdfPath);

        var psi = new ProcessStartInfo("lp", finalArgs) { RedirectStandardOutput = true, RedirectStandardError = true };
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("lp could not be started.");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            if (p.ExitCode == 0)
            {
                var jobId = ExtractRequestId(stdout);
                return new PrintOutcome(true, jobId is null ? stdout.Trim() : $"Spooler accepted job {jobId}. Confirm the pages came out of the device.");
            }
            return new PrintOutcome(false, $"The print system rejected the job (lp exit {p.ExitCode}). {stderr.Trim()} No automatic retry — resolve the printer issue and print again.");
        }
        catch (Exception ex)
        {
            return new PrintOutcome(false, $"Print submission failed: {ex.Message} Nothing was queued.");
        }

        static string? ExtractRequestId(string stdout)
        {
            var idx = stdout.IndexOf("request id is", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            return stdout[(idx + "request id is".Length)..].Trim().Split(' ').FirstOrDefault();
        }
    }

    public async Task<PrintJob> RecordJob(PrintJob job)
    {
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    public async Task UpdateJobState(Guid jobId, PrintJobState state, string detail, string spoolJobId)
    {
        var job = await db.PrintJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null) return;
        job.State = state;
        job.ResultDetail = detail[..Math.Min(detail.Length, 500)];
        job.SpoolJobId = spoolJobId;
        await db.SaveChangesAsync();
    }

    public List<PrintJob> RecentJobs(int count) =>
        db.PrintJobs.AsNoTracking().OrderByDescending(j => j.CreatedAtUtc).Take(count).ToList();
}