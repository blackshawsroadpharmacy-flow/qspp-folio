using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.ProcessSort;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M8 heavy tier (MinerU 4, opt-in per batch): the local wrapper, the
/// server-down→Vision fallback, the honest non-fallback for real parse failures,
/// the engine-keyed cache separation, and the markdown page-splitting. The REAL
/// MinerU install has been benchmarked against this exact contract (milestones,
/// 24 Oct); these tests run through a FAKE CLI so they stay hermetic and green on
/// machines without the 2 GB model set. Vision fallback legs are gated on the
/// qspp-ocr helper being present, like the other OCR tests.</summary>
public class ProcessSortHeavyOcrTests : TestBase
{
    private readonly string _srcDir;
    private readonly string _outDir;
    private readonly string _cacheDir;
    private readonly FolioWorkspace _folio;
    private static readonly string? OcrFixtures = FindFixtureDir();
    private static readonly bool HelperPresent = FolioOcr.LocateHelper() is { };

    public ProcessSortHeavyOcrTests()
    {
        _srcDir = Path.Combine(Path.GetTempPath(), $"heavy-src-{Guid.NewGuid():N}");
        _outDir = Path.Combine(Path.GetTempPath(), $"heavy-out-{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(), $"heavy-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_srcDir);
        Directory.CreateDirectory(_cacheDir);
        var store = new AttachmentStore(Path.Combine(Path.GetTempPath(), $"heavy-att-{Guid.NewGuid():N}"));
        _folio = new FolioWorkspace(Db, Workspace, store, Audit, _cacheDir);
    }

    public override void Dispose()
    {
        try { Directory.Delete(_srcDir, true); } catch { }
        try { Directory.Delete(_outDir, true); } catch { }
        try { Directory.Delete(_cacheDir, true); } catch { }
        base.Dispose();
    }

    private static string? FindFixtureDir()
    {
        var dir = Path.GetFullPath(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != Path.GetPathRoot(dir); i++)
        {
            var candidate = Path.Combine(dir, "tests", "fixtures", "ocr");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    private static string WriteCli(string dir, string name, string lines, string? jsonSidecar = null)
    {
        var cli = Path.Combine(dir, name);
        var script = $"#!/bin/sh\n{lines}";
        if (jsonSidecar is { })
        {
            var sidecar = Path.Combine(dir, $"{name}.json");
            File.WriteAllText(sidecar, jsonSidecar);
            script = $"#!/bin/sh\ncat '{sidecar}'\n{lines}";
        }
        File.WriteAllText(cli, script);
        File.SetUnixFileMode(cli, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
        return cli;
    }

    // wire shape helper; anonymous serializer emits the exact camelCase the MinerU CLI prints
    private static string MineruJson(string markdown) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            parse = new { sha256 = "abc123", short_id = "abc123", tier = "standard", status = "done" },
            content = new { sha256 = "abc123", short_id = "abc123", tier = "standard", format = "markdown", content = markdown },
        });

    // ---------- wrapper-level ----------

    [Fact]
    public void SplitPages_ParsesMineruPageMarkers()
    {
        var md = "<!-- page 1 of 2 -->\n\nRow one\n\n<!-- page 2 of 2 -->\n\nRow two";
        var pages = FolioHeavyOcr.SplitPages(md);
        Assert.Equal(2, pages.Count);
        Assert.Contains("Row one", pages[0]);
        Assert.Contains("Row two", pages[1]);
        // a document without markers still becomes one page record
        Assert.Single(FolioHeavyOcr.SplitPages("No markers here"));
        // blank output stays pageless (the engine caller treats empty as failure)
        Assert.Empty(FolioHeavyOcr.SplitPages("   "));
    }

    [Fact]
    public void FakeMineruCannedParse_PipelinesAndCachesByEngineKey()
    {
        var md = "<!-- page 1 of 1 -->\n\n## Register: Cleaning and Maintenance Schedule\n\n<table><tr><td>Area</td><td>Daily</td></tr></table>";
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            parse = new { sha256 = "abc123", short_id = "abc123", tier = "standard", status = "done" },
            content = new { sha256 = "abc123", short_id = "abc123", tier = "standard", format = "markdown", content = md },
        });
        var cli = WriteCli(_cacheDir, "mineru-ok", "", jsonSidecar: json);
        _folio.SetHeavyOcr(true, cli);
        // use a REAL image fixture so the scanner's signature guard admits it; the
        // mime sniff routes it to the OCR tier like any scan
        File.Copy(Path.Combine(OcrFixtures!, "register.png"), Path.Combine(_srcDir, "register.png"));
        var (res, batchId) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        var (pres, _) = _folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok || pres.Error.Contains("review"), pres.Error);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.Equal(FolioHeavyOcr.EngineKey, item.ParserUsed);
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates or FolioItemState.Abstained or FolioItemState.NeedsOcr,
            $"fake parse text ran the pipeline: got {item.State}");
        var extractor = new FolioExtractor(_cacheDir, new FolioHeavyOcr(cli));
        Assert.True(extractor.IsCached(item.SourceSha256, FolioHeavyOcr.EngineKey),
            $"expected folio-{item.SourceSha256[..Math.Min(24, item.SourceSha256.Length)]}-mineru-4-standard.json under the cache dir");
    }

    [Fact]
    public void HeavyServerDown_FallsBackToVision_ParserRecordedHonest()
    {
        if (!HelperPresent) return; // Vision helper needed for the fallback leg
        var cli = WriteCli(_cacheDir, "mineru-down",
            "echo \"Error: Local mineru server is not running. Run 'mineru server start'.\" >&2\nexit 1\n");
        _folio.SetHeavyOcr(true, cli);
        File.Copy(Path.Combine(OcrFixtures!, "register.png"), Path.Combine(_srcDir, "register.png"));
        var (res, batchId) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        var (pres, _) = _folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok || pres.Error.Contains("review"), pres.Error);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.Equal(FolioOcr.EngineKey, item.ParserUsed); // fell back to Vision, honestly recorded
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
            $"Vision fallback must classify, not stall: {item.State}");
    }

    [Fact]
    public void HeavyRealParseFailure_DoesNotClaimVision()
    {
        var cli = WriteCli(_cacheDir, "mineru-crash",
            "echo \"Model failure: layout engine crashed\" >&2\nexit 3\n");
        _folio.SetHeavyOcr(true, cli);
        File.Copy(Path.Combine(OcrFixtures!, "register.png"), Path.Combine(_srcDir, "register.png"));
        var (res, batchId) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        _folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        // a REAL crash stays honest: the human sees the heavy-tier error; the parser
        // field never pretends Vision produced the text
        Assert.Equal(FolioHeavyOcr.EngineKey, item.ParserUsed);
        Assert.True(item.ErrorText.Contains("layout engine crashed"), item.ErrorText);
        Assert.Null(item.EvidenceItemId);
    }

    [Fact]
    public void HeavyEnabledWithoutUsableCli_EngineStaysHonest()
    {
        if (HelperPresent)
        {
            _folio.SetHeavyOcr(true, "/nonexistent/mineru");
            Assert.Equal("on-device, Apple Vision, no download", _folio.OcrMode);
        }
        else
        {
            _folio.SetHeavyOcr(true, "/nonexistent/mineru");
            Assert.Equal("queued honestly (no OCR engine is installed on this Mac)", _folio.OcrMode);
        }
        Assert.False(_folio.HeavyOcrReady);
    }

    [Fact]
    public void HeavyExitSilent_QueueStayHonest_NoEvidenceInvented()
    {
        var cli = WriteCli(_cacheDir, "mineru-silent", "exit 9\n");
        _folio.SetHeavyOcr(true, cli);
        File.Copy(Path.Combine(OcrFixtures!, "register.png"), Path.Combine(_srcDir, "register.png"));
        var (res, batchId) = _folio.ScanAndQueue(Owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        _folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.True(item.State is FolioItemState.NeedsOcr or FolioItemState.Abstained,
            $"queue stays honest under engine anomalies: {item.State}");
        Assert.Null(item.EvidenceItemId);
    }
}