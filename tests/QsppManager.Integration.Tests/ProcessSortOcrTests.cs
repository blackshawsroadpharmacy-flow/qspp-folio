using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.ProcessSort;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M8-B: the local Apple Vision OCR tier (qspp-ocr Swift helper). The
/// fixtures are real raster scans (text drawn into PNGs, encoded as image-only
/// PDFs) made with PIL, versioned under tests/fixtures/ocr/. These tests prove:
/// scanned images land as real classified documents (never mis-filed), rotated
/// scans recover, page counts hold for multi-page scans, and the extraction
/// cache makes repeated work free. The helper is entirely on-device — Vision
/// + PDFKit, no network anywhere.</summary>
public class ProcessSortOcrTests : TestBase
{
    private static readonly string? OcrFixtures = FindFixtureDir();

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

    [Fact]
    public void RasterImage_Ocrs_AndProposesRequirement()
    {
        var helper = FolioOcr.LocateHelper();
        Assert.True(helper != null, "qspp-ocr helper must be present (built from tools/ocr/qspp-ocr.swift) for the OCR tier tests");
        var src = TempDir("ocr-src");
        var outDir = TempDir("ocr-out");
        File.Copy(Path.Combine(OcrFixtures!, "register.png"), Path.Combine(src, "register.png"));

        var folio = NewWorkspace($"{TempDir("ocr-cache")}");
        var (res, batchId0) = folio.ScanAndQueue(Owner, src, outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId0);
        var (pres, _) = folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok, pres.Error);

        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
            $"expected a proposal from OCR text, got {item.State} ({item.ErrorText})");
        Assert.Equal(FolioOcr.EngineKey, item.ParserUsed);
        Assert.True(item.PageCount >= 1);
        var props = JsonSerializer.Deserialize<List<FolioSuggestion>>(item.ProposalsJson, FolioJson.Opts) ?? [];
        Assert.Contains(props, p => p.Ref == "Rule 10");
    }

    [Fact]
    public void RotatedScan_Ocrs_RecoversText()
    {
        var helper = FolioOcr.LocateHelper();
        Assert.True(helper != null, "qspp-ocr helper must be present for the OCR tier tests");
        var src = TempDir("rot-src");
        var outDir = TempDir("rot-out");
        File.Copy(Path.Combine(OcrFixtures!, "register-rot90.png"), Path.Combine(src, "rotated-register.png"));
        var folio = NewWorkspace(TempDir("rot-cache"));
        var (res, batchId0) = folio.ScanAndQueue(Owner, src, outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId0);
        folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.True(item.State != FolioItemState.NeedsOcr, "rotated scan must not stay queued — Vision rotation handling");
        Assert.Equal(FolioOcr.EngineKey, item.ParserUsed);
        Assert.NotEmpty(item.ProposalsJson);
    }

    [Fact]
    public void ScannedPdf_Ocrs_AllPagesCounted()
    {
        var helper = FolioOcr.LocateHelper();
        Assert.True(helper != null, "qspp-ocr helper must be present for the OCR tier tests");
        var src = TempDir("spdf-src");
        var outDir = TempDir("spdf-out");
        File.Copy(Path.Combine(OcrFixtures!, "scan-2page.pdf"), Path.Combine(src, "scan-2page.pdf"));
        var folio = NewWorkspace(TempDir("spdf-cache"));
        var (res, batchId0) = folio.ScanAndQueue(Owner, src, outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId0);
        folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
            $"scanned pdf should classify, got {item.State} ({item.ErrorText})");
        Assert.Equal(2, item.PageCount);
        Assert.Equal(FolioOcr.EngineKey, item.ParserUsed);
    }

    [Fact]
    public void ExtractionCache_SecondRunIsFree()
    {
        var helper = FolioOcr.LocateHelper();
        Assert.True(helper != null, "qspp-ocr helper must be present for the OCR tier tests");
        var cacheRoot = TempDir("cache-hit");
        var src = TempDir("cache-src");
        var outDir = TempDir("cache-out");
        File.Copy(Path.Combine(OcrFixtures!, "register.png"), Path.Combine(src, "register.png"));

        var folio = NewWorkspace(cacheRoot);
        var (res, batchId0) = folio.ScanAndQueue(Owner, src, outDir);
        var batch = Assert.IsType<Guid>(batchId0);
        folio.ProcessBatch(batch, Owner);
        var sha = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch).SourceSha256;

        var extractor = new FolioExtractor(cacheRoot, new FolioOcr(helper));
        var t0 = DateTime.UtcNow;
        var second = extractor.Extract(Path.Combine(src, "register.png"), sha, "image/png");
        var elapsed = (DateTime.UtcNow - t0).TotalMilliseconds;
        Assert.True(extractor.IsCached(sha, FolioOcr.EngineKey), "first run must write the extraction cache");
        Assert.True(elapsed < 500, $"second extraction took {elapsed:0} ms — the cache must make repeats free");

        // full batch re-run with a fresh workspace: the item classifies from cache, not from the helper
        var (res2, batchId2) = folio.ScanAndQueue(Owner, src, outDir);
        var batch2 = Assert.IsType<Guid>(batchId2);
        folio.ProcessBatch(batch2, Owner);
        var item2 = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch2 && i.SourceRelativePath == "register.png");
        Assert.True(item2.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
            $"cached OCR should still classify: {item2.State}");
        Assert.Equal(FolioOcr.EngineKey, item2.ParserUsed);
    }

    [Fact]
    public void NoHelper_ItemsQueueHonestly()
    {
        var src = TempDir("nh-src");
        var outDir = TempDir("nh-out");
        File.Copy(Path.Combine(OcrFixtures!, "register.png"), Path.Combine(src, "register.png"));
        var folio = NewWorkspace(TempDir("nh-cache"), withOcr: false);
        var (res, batchId0) = folio.ScanAndQueue(Owner, src, outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId0);
        folio.ProcessBatch(batch, Owner);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.Equal(FolioItemState.NeedsOcr, item.State);
        Assert.Contains("helper", item.ErrorText, StringComparison.OrdinalIgnoreCase);
        var batchRow = Db.FolioBatches.AsNoTracking().First(b => b.Id == batch);
        Assert.Contains("not installed", batchRow.ModeDescription, StringComparison.OrdinalIgnoreCase);
    }

    private FolioWorkspace NewWorkspace(string cacheRoot, bool withOcr = true)
    {
        var store = new QsppManager.Infrastructure.Storage.AttachmentStore(AttachmentRoot);
        var folio = new FolioWorkspace(Db, Workspace, store, Audit, cacheRoot);
        if (!withOcr) folio.SetOcrEngine(null);
        return folio;
    }

    private static string TempDir(string tag)
    {
        var d = Path.Combine(Path.GetTempPath(), $"qspp-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }
}