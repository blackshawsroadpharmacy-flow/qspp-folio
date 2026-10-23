using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Seed;
using QsppManager.Infrastructure.ProcessSort;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using QuestPDF.Fluent;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M8 wider-corpus round: the REAL requirement corpus (the repo's seed files
/// are READ and copied into a temp repo root, so the tests see all ~450 requirements
/// and the REM items) exercised against harder document shapes — table layouts,
/// expired certificates, garbled text, blank scans, mixed-topic documents, and
/// sensitive data hidden in table cells. Stable behaviours are asserted here;
/// raster-quality noise stays in the milestone bench, not in this file.
/// Hermetic: temp SQLite DB + temp dirs; the real repo seed is only copied FROM.</summary>
public class ProcessSortWiderCorpusTests : IDisposable
{
    private readonly string _srcDir;
    private readonly string _outDir;
    private readonly string _cacheDir;
    private readonly string _tempRepo;
    private readonly string _dbPath;
    private readonly FolioWorkspace _folio;
    private readonly QsppDbContext _db;
    private readonly UserSession _owner;

    public ProcessSortWiderCorpusTests()
    {
        _tempRepo = Path.Combine(Path.GetTempPath(), $"qspp-wide-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRepo);
        var seedDir = FindRepoSeedDir() ?? throw new InvalidOperationException("repo seed/ not found");
        Directory.CreateDirectory(Path.Combine(_tempRepo, "seed"));
        foreach (var f in new[] { "requirements.json", "remote-assessment.json", "issues.json" })
            File.Copy(Path.Combine(seedDir, f), Path.Combine(_tempRepo, "seed", f));
        // a documents.json of "[]" disables the real corpus document import in tests
        File.WriteAllText(Path.Combine(_tempRepo, "seed", "documents.json"), "[]");

        _dbPath = Path.Combine(Path.GetTempPath(), $"qspp-wide-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options;
        _db = new QsppDbContext(options);
        _db.Database.EnsureCreated();

        var store = new AttachmentStore(Path.Combine(Path.GetTempPath(), $"qspp-wide-att-{Guid.NewGuid():N}"));
        var audit = new AuditService(_db);
        var seeds = new SeedImporter(_db, store, audit, _tempRepo);
        seeds.ImportAll();

        var workspace = new QsppWorkspace(_db, store, audit);
        var auth = new AuthService(_db, audit);
        var (r, user) = auth.CreateFirstUser("owner", "Wide Corpus Owner", "passphras8");
        Assert.True(r.Ok, r.Error);
        _owner = new UserSession(user!.Id, user.UserName, user.DisplayName, user.Role);

        _srcDir = Path.Combine(Path.GetTempPath(), $"wide-src-{Guid.NewGuid():N}");
        _outDir = Path.Combine(Path.GetTempPath(), $"wide-out-{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(Path.GetTempPath(), $"wide-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_srcDir);
        Directory.CreateDirectory(_cacheDir);
        _folio = new FolioWorkspace(_db, workspace, store, audit, _cacheDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { Directory.Delete(_tempRepo, true); } catch { }
        try { Directory.Delete(_srcDir, true); } catch { }
        try { Directory.Delete(_outDir, true); } catch { }
        try { Directory.Delete(_cacheDir, true); } catch { }
    }

    private static string? FindRepoSeedDir()
    {
        var dir = Path.GetFullPath(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != Path.GetPathRoot(dir); i++)
        {
            var candidate = Path.Combine(dir, "seed");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "requirements.json")))
                return candidate;
            dir = Path.GetDirectoryName(dir)!;
        }
        return null;
    }

    /// <summary>QuestPDF Community licences this repo (DD-9).</summary>
    private static void MakePdf(string path, string text)
    {
        QsppManager.Infrastructure.Printing.PdfComposer.ConfigureLicense();
        Document.Create(c => c.Page(p =>
        {
            p.Margin(28);
            p.Content().Text(text);
        })).GeneratePdf(path);
    }

    private static void MakeTablePdf(string path, params (string a, string b)[] rows)
    {
        QsppManager.Infrastructure.Printing.PdfComposer.ConfigureLicense();
        Document.Create(c => c.Page(p =>
        {
            p.Margin(30);
            p.Content().Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); });
                foreach (var (a, b) in rows)
                {
                    // a separator row gap so PdfPig still keeps cell words readable
                    t.Cell().Text(a);
                    t.Cell().Text(b);
                }
            });
        })).GeneratePdf(path);
    }

    private (FolioBatch batch, List<FolioItem> items) ScanProcess()
    {
        var (res, batchId) = _folio.ScanAndQueue(_owner, _srcDir, _outDir);
        Assert.True(res.Ok, res.Error);
        var batch = Assert.IsType<Guid>(batchId);
        var (pres, _) = _folio.ProcessBatch(batch, _owner);
        Assert.True(pres.Ok, pres.Error);
        var items = _db.FolioItems.AsNoTracking().Where(i => i.BatchId == batch)
            .OrderBy(i => i.SourceRelativePath).ToList();
        return (_db.FolioBatches.AsNoTracking().First(x => x.Id == batch), items);
    }

    private static System.Text.Json.JsonSerializerOptions JsonOpts = FolioJson.Opts;

    private static List<FolioSuggestion> Props(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<List<FolioSuggestion>>(json, JsonOpts) ?? [];

    [Fact]
    public void TableLayoutPdf_BookingsTable_OffersCluster_NeverInvents()
    {
        MakeTablePdf(Path.Combine(_srcDir, "bookings-desk-table.pdf"),
            ("Assessment bookings and cancellations", "recorded in the register"),
            ("Booked assessments", "cancellation window 10 business days applies"));
        var (batch, items) = ScanProcess();
        var item = Assert.Single(items);
        // WIDER-CORPUS FINDING (recorded in milestones): a generic bookings TABLE hits a whole
        // cluster of corpus refs (bookings rules, business continuity) — the real corpus makes
        // no single winner from generic wording alone. The honest pin: the pipeline never
        // invents a wrong single answer — it offers the cluster for human choice.
        var props = Props(item.ProposalsJson);
        Assert.NotEmpty(props);
        Assert.Contains(props, p => p.Title.Contains("bookings", StringComparison.OrdinalIgnoreCase));
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
            $"table layout got {item.State} — {item.ErrorText}");
    }

    [Fact]
    public void ExpiredCertificate_WordingStillWins_Rem06()
    {
        // Expiry-date detection is deliberately NOT built (see milestones, not-yet):
        // classification is wording-based on purpose — an expired certificate still
        // maps to REM-06 Insurance certificates and the human approves with dates in mind.
        MakePdf(Path.Combine(_srcDir, "indemnity-cert-2023.pdf"),
            "Professional indemnity and public liability insurance certificate. " +
            "Issued to Burke Road Compounding Pharmacy. Policy expiry: 31 March 2024 (lapsed, prior period). " +
            "Cover confirmed by the broker for work cover requirements.");
        var (batch, items) = ScanProcess();
        var item = Assert.Single(items);
        Assert.Equal(FolioItemState.Proposed, item.State);
        Assert.Contains(Props(item.ProposalsJson), p => p.Ref == "REM-06");
    }

    [Fact]
    public void GarbledNonword_Text_Abstains_NeverInvents()
    {
        MakePdf(Path.Combine(_srcDir, "corrupted-extract.pdf"),
            "xkqw zrtpl vbnm qwzxf jklp zxcvb mnopq ktfhr wqznb plkju zxcv nmkl hjkl zzzz qwz");
        var (batch, items) = ScanProcess();
        var item = Assert.Single(items);
        Assert.Equal(FolioItemState.Abstained, item.State);
        Assert.Null(item.EvidenceItemId);
        Assert.Equal("[]", item.ApprovedJson); // nothing invented, nothing approved
    }

    [Fact]
    public void BlankPdf_NeverInventedDestination_HonestState()
    {
        MakePdf(Path.Combine(_srcDir, "blank-scan.pdf"), " ");
        var (batch, items) = ScanProcess();
        var item = Assert.Single(items);
        Assert.True(item.State is FolioItemState.Abstained or FolioItemState.Unreadable or FolioItemState.NeedsOcr,
            $"blank must land on an honest state, got {item.State} ({item.ErrorText})");
        Assert.Null(item.EvidenceItemId);
    }

    [Fact]
    public void MixedTopics_TopTwoCompete_NoSilentSwallow()
    {
        MakePdf(Path.Combine(_srcDir, "mixed-sop-and-batch.pdf"),
            "Booking policy: assessment bookings and cancellations, cancellation windows apply. " +
            "Also our compounded potency testing SOP and potency testing register are attached.");
        var (batch, items) = ScanProcess();
        var item = Assert.Single(items);
        // Deterministic behaviour on two topics of comparable strength (the classifier-level
        // balance is pinned by MultipleSignals_SignalMultipleCandidates): the pipeline may
        // land Proposed or MultipleCandidates as corpus scores drift, but it must NEVER
        // silently swallow one of the two topics.
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
            $"two-topic doc got {item.State}");
        var props = Props(item.ProposalsJson);
        // family-level honesty (real corpus): one bookings-family ref AND one potency-family ref
        Assert.Contains(props, p => p.Title.Contains("Assessment bookings") || p.Title.Contains("bookings"));
        Assert.Contains(props, p => p.Title.Contains("potency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SensitiveDataInsideTable_StillHeldBack()
    {
        MakeTablePdf(Path.Combine(_srcDir, "medicare-claims-table.pdf"),
            ("Claim batch report", "October"), ("Patients processed", "31"),
            ("Medicare card numbers", "4111 1111 1111 1 recorded per line"));
        var (batch, items) = ScanProcess();
        var item = Assert.Single(items);
        Assert.Equal(FolioItemState.SensitiveHold, item.State);
        Assert.Null(item.EvidenceItemId);
    }

    [Fact]
    public void FailedFile_DoesNotStopBatch_RestClassified()
    {
        File.WriteAllText(Path.Combine(_srcDir, "fake.pdf"), "<html>not a pdf</html>");
        MakePdf(Path.Combine(_srcDir, "good-bookings.pdf"),
            "Assessment bookings and cancellations content. The cancellation window applies.");
        MakePdf(Path.Combine(_srcDir, "good-potency.pdf"),
            "Consistency and potency testing is recorded for every compounded batch.");
        var (batch, items) = ScanProcess();
        Assert.Equal(2, items.Count); // the HTML fake is skipped at scan and recorded on the batch, not as a failed item
        // both batch-mates still classify (proposed or offered as a choice) — failure isolation holds
        Assert.All(items, i =>
            Assert.True(i.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates,
                $"{i.SourceRelativePath} got {i.State}"));
        Assert.Contains("fake.pdf", batch.ScanErrorsJson);
    }
}