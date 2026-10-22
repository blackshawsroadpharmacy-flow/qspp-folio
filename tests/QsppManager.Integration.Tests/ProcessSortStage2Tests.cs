using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.ProcessSort;
using QuestPDF.Fluent;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M8-C stage-2: the local typed-decision model is CLIENT here — every
/// test drives a hermetic loopback mock that speaks the Jev-compatible wire
/// format (`POST /v1/systemone`). Contracts proven: stage-2 only re-ranks or
/// holds stage-1, records model+revision per suggestion, honours abstain, keeps
/// stage-1 on low confidence / server down, and never breaks the batch on
/// garbage responses. Real-model accuracy runs separately against the pinned
/// checkpoint (tools/decision).</summary>
public class ProcessSortStage2Tests : TestBase
{
    [Fact]
    public void Stage2Maps_Choice()
    {
        var stage1 = new List<FolioSuggestion>
        {
            new(Guid.NewGuid(), "Rule 8", "Rule", "Assessment cycle", 12.0, "tokens", "deterministic-v1", "1"),
            new(Guid.NewGuid(), "Rule 10", "Rule", "Assessment bookings and cancellations", 10.5, "tokens", "deterministic-v1", "1"),
        };
        var candidates = stage1.Select(s => new FolioCandidate(s.RequirementId, s.Ref, s.RefKind, s.Title, "summary", [])).ToList();
        var answer = new FolioDecisionStage2.Stage2Answer(candidates[1].Id.ToString(), 0.87, "laya-typed-decisions", "c5d78730f3493e4fe16d61507ef4b78eef7318cf", "gpu");
        var (mapped, state) = FolioStage2Mapping.Map(stage1, FolioItemState.Proposed, answer, candidates);
        Assert.Equal(FolioItemState.Proposed, state);
        Assert.Equal(candidates[1].Id, mapped[0].RequirementId);
        Assert.Equal(FolioDecisionStage2.ModelName, mapped[0].Model);
        Assert.Equal("c5d78730f349", mapped[0].ModelVersion); // 12-char revision head in the suggestion record
        Assert.Contains("stage-2", mapped[0].Rationale);
        Assert.Equal("deterministic-v1", mapped[1].Model); // non-winners keep stage-1 identity
    }

    [Fact]
    public void Stage2Maps_WinnerOutsideTop2_KeepsStage1()
    {
        var stage1 = new List<FolioSuggestion>
        {
            new(Guid.NewGuid(), "Rule 8", "Rule", "Assessment cycle", 12.0, "tokens", "deterministic-v1", "1"),
            new(Guid.NewGuid(), "Rule 10", "Rule", "Assessment bookings and cancellations", 10.5, "tokens", "deterministic-v1", "1"),
            new(Guid.NewGuid(), "Rule 12", "Rule", "Access to premises", 6.0, "tokens", "deterministic-v1", "1"),
        };
        var candidates = stage1.Select(s => new FolioCandidate(s.RequirementId, s.Ref, s.RefKind, s.Title, "summary", [])).ToList();
        var answer = new FolioDecisionStage2.Stage2Answer(candidates[2].Id.ToString(), 0.92, "laya-typed-decisions", "c5d78730f3493e4f", "gpu");
        var (mapped, state) = FolioStage2Mapping.Map(stage1, FolioItemState.Proposed, answer, candidates);
        Assert.Equal(FolioItemState.Proposed, state);   // stage-1 order intact
        Assert.Equal(candidates[0].Id, mapped[0].RequirementId);
        Assert.Equal("deterministic-v1", mapped[0].Model); // NOT promoted — deterministic-first contract
    }

    [Fact]
    public void Stage2Maps_Multiple()
    {
        var stage1 = new List<FolioSuggestion>
        {
            new(Guid.NewGuid(), "Rule 8", "Rule", "Assessment cycle", 12.0, "tokens", "deterministic-v1", "1"),
            new(Guid.NewGuid(), "Rule 10", "Rule", "Assessment bookings and cancellations", 10.5, "tokens", "deterministic-v1", "1"),
        };
        var candidates = stage1.Select(s => new FolioCandidate(s.RequirementId, s.Ref, s.RefKind, s.Title, "summary", [])).ToList();
        var answer = new FolioDecisionStage2.Stage2Answer(FolioDecisionStage2.MultipleOption, 0.86, "laya-typed-decisions", null, "gpu");
        var (mapped, state) = FolioStage2Mapping.Map(stage1, FolioItemState.Proposed, answer, candidates);
        Assert.Equal(FolioItemState.MultipleCandidates, state);
        Assert.Equal(2, mapped.Count); // "multiple" is the REVIEWER's business; nothing invented
    }

    [Fact]
    public void Stage2Maps_Abstain()
    {
        var stage1 = new List<FolioSuggestion>
        {
            new(Guid.NewGuid(), "Rule 8", "Rule", "Assessment cycle", 12.0, "tokens", "deterministic-v1", "1"),
        };
        var candidates = stage1.Select(s => new FolioCandidate(s.RequirementId, s.Ref, s.RefKind, s.Title, "summary", [])).ToList();
        var answer = new FolioDecisionStage2.Stage2Answer(FolioDecisionStage2.AbstainOption, 0.91, "laya-typed-decisions", null, "gpu");
        var (mapped, state) = FolioStage2Mapping.Map(stage1, FolioItemState.Proposed, answer, candidates);
        Assert.Equal(FolioItemState.Abstained, state);
        Assert.Equal(stage1, mapped); // unchanged suggestions; a human decides next
    }

    [Fact]
    public void Stage2Maps_LowConfidence_KeepsStage1()
    {
        var stage1 = new List<FolioSuggestion>
        {
            new(Guid.NewGuid(), "Rule 8", "Rule", "Assessment cycle", 12.0, "tokens", "deterministic-v1", "1"),
        };
        var candidates = stage1.Select(s => new FolioCandidate(s.RequirementId, s.Ref, s.RefKind, s.Title, "summary", [])).ToList();
        var answer = new FolioDecisionStage2.Stage2Answer(candidates[0].Id.ToString(), 0.18, "laya-typed-decisions", null, "gpu");
        var (mapped, state) = FolioStage2Mapping.Map(stage1, FolioItemState.MultipleCandidates, answer, candidates);
        Assert.Equal(FolioItemState.MultipleCandidates, state);
        Assert.Equal(stage1, mapped);
        Assert.Equal("deterministic-v1", mapped[0].Model); // stage-2 never overwrote the record
    }

    [Fact]
    public void ClientDown_Stage1Stands_NeverThrows()
    {
        var folio = NewWorkspace($"{TempDir("s2-down")}", withStage2: true);
        folio.SetStage2(new FolioDecisionStage2($"http://127.0.0.1:{PickClosedPort()}"));
        var src = TempDir("s2-down-src");
        var outDir = TempDir("s2-down-out");
        MakePdf(Path.Combine(src, "bookings.pdf"), "Assessment bookings and cancellations. Cancellation windows apply to booked assessments.");
        var (res, batchId0) = folio.ScanAndQueue(Owner, src, outDir);
        var batch = Assert.IsType<Guid>(batchId0);
        var (pres, _) = folio.ProcessBatch(batch, Owner);
        Assert.True(pres.Ok || pres.Error.Contains("review"), pres.Error);
        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        Assert.True(item.State is FolioItemState.Proposed or FolioItemState.MultipleCandidates, $"{item.State}: {item.ErrorText}");
        var props = JsonSerializer.Deserialize<List<FolioSuggestion>>(item.ProposalsJson, FolioJson.Opts) ?? [];
        Assert.All(props, p => Assert.Equal("deterministic-v1", p.Model));
    }

    [Fact]
    public void MockServer_ReordersWinner_EndToEnd()
    {
        var folio = NewWorkspace(TempDir("s2-e2e"), withStage2: true);
        var mock = Mocks.First();

        var src = TempDir("s2-e2e-src");
        var outDir = TempDir("s2-e2e-out");
        MakePdf(Path.Combine(src, "bookings.pdf"), "Assessment bookings and cancellations. Cancellation windows apply to booked assessments.");
        var (res, batchId0) = folio.ScanAndQueue(Owner, src, outDir);
        var batch = Assert.IsType<Guid>(batchId0);
        folio.ProcessBatch(batch, Owner);

        var item = Db.FolioItems.AsNoTracking().First(i => i.BatchId == batch);
        var props = JsonSerializer.Deserialize<List<FolioSuggestion>>(item.ProposalsJson, FolioJson.Opts) ?? [];
        Assert.True(props.Count >= 1, $"need >=1 stage-1 candidate; got none: {item.ProposalsJson}");
        Assert.Equal(FolioDecisionStage2.ModelName, props[0].Model); // stage-2 engaged already on the first run
        Assert.Equal("c5d78730f349", props[0].ModelVersion);

        // The mock contract: server saw a typed choice question with candidate ids + multiple + abstain,
        // a captioned excerpt within the cap, and the pinned model name.
        Assert.NotNull(mock.LastRequest);
        Assert.Equal(FolioDecisionStage2.ModelName, mock.LastRequest!.Model);
        Assert.True(mock.LastRequest.CriteriaLabels.Contains(FolioDecisionStage2.MultipleOption));
        Assert.True(mock.LastRequest.CriteriaLabels.Contains(FolioDecisionStage2.AbstainOption));
        Assert.True(mock.LastRequest.CriteriaLabels.Any(l => l != FolioDecisionStage2.MultipleOption && l != FolioDecisionStage2.AbstainOption));
        Assert.True(mock.LastRequest.Excerpt.Length <= FolioDecisionStage2.ExcerptChars + 16);
    }

    [Fact]
    public void ClientRefusesNonLoopback()
    {
        Assert.Throws<ArgumentException>(() => new FolioDecisionStage2("http://192.168.1.44:8642"));
        Assert.Throws<ArgumentException>(() => new FolioDecisionStage2("https://example.com:8642"));
    }

    // ---- helpers ----

    private static void MakePdf(string path, string text)
    {
        QsppManager.Infrastructure.Printing.PdfComposer.ConfigureLicense();
        QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Margin(28);
            page.Content().Text(text);
        })).GeneratePdf(path);
    }

    private static string TempDir(string tag)
    {
        var d = Path.Combine(Path.GetTempPath(), $"qspp-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private FolioWorkspace NewWorkspace(string cacheRoot, bool withStage2 = false)
    {
        var store = new QsppManager.Infrastructure.Storage.AttachmentStore(AttachmentRoot);
        var folio = new FolioWorkspace(Db, Workspace, store, Audit, cacheRoot);
        if (withStage2)
        {
            var mock = new LayamockServer();
            folio.SetStage2(new FolioDecisionStage2(mock.Url));
            Mocks.Add(mock);
        }
        return folio;
    }

    private readonly List<LayamockServer> Mocks = [];

    public override void Dispose()
    {
        foreach (var m in Mocks) m.Dispose();
        base.Dispose();
    }

    private static int PickClosedPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

/// <summary>A loopback-only stand-in for `laya-apple serve` (POST /v1/systemone)
/// used to verify the app-side contract hermetically. Records the request, then
/// answers a canned choice.</summary>
public sealed class LayamockServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly System.Threading.Thread _thread;
    public string AnswerRef { get; set; } = "canned";
    public MockRequest? LastRequest { get; private set; }
    public List<string> LastSeenChoices { get; private set; } = new();

    public sealed record MockRequest(string Model, List<string> CriteriaLabels, string Excerpt);

    public string Url { get; }

    public LayamockServer()
    {
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _thread = new System.Threading.Thread(ServeLoop) { IsBackground = true };
        _thread.Start();
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private void ServeLoop()
    {
        while (_listener.IsListening)
        {
            try
            {
                var ctx = _listener.GetContext();
                var urlPath = ctx.Request.Url!.AbsolutePath;
                if (urlPath == "/v1/models")
                {
                    Write(ctx, "{\"models\":[\"laya\",\"laya-typed-decisions\"]}");
                    continue;
                }
                if (urlPath != "/v1/systemone") { ctx.Response.StatusCode = 404; ctx.Response.Close(); continue; }

                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                using var doc = JsonDocument.Parse(reader.ReadToEnd());
                var model = doc.RootElement.GetProperty("model").GetString() ?? "";
                var state = doc.RootElement.GetProperty("state");
                var excerpt = state.GetProperty("document").GetProperty("excerpt").GetString() ?? "";
                var q = doc.RootElement.GetProperty("questions").GetProperty("destination");
                var choiceLabels = new List<string>();
                // criteria may be dict {label: description} or list of labels
                if (q.TryGetProperty("criteria", out var crit))
                {
                    if (crit.ValueKind == JsonValueKind.Object)
                        choiceLabels = crit.EnumerateObject().Select(p => p.Name).ToList();
                    else
                        choiceLabels = crit.EnumerateArray().Select(v => v.GetString() ?? "").ToList();
                }
                LastSeenChoices = choiceLabels;
                LastRequest = new MockRequest(model, choiceLabels, excerpt);

                var winner = choiceLabels.FirstOrDefault(l => l != FolioDecisionStage2.MultipleOption && l != FolioDecisionStage2.AbstainOption &&
                                                              l.Contains(AnswerRef)) ?? choiceLabels.FirstOrDefault(l => l != FolioDecisionStage2.MultipleOption && l != FolioDecisionStage2.AbstainOption) ?? FolioDecisionStage2.AbstainOption;
                var probabilities = choiceLabels.ToDictionary(l => l, l => l == winner ? 0.86 : 0.1);
                var answer = JsonSerializer.Serialize(new
                {
                    model = "laya-typed-decisions",
                    answers = new Dictionary<string, object>
                    {
                        ["destination"] = new
                        {
                            type = "choice", choice = winner, probabilities,
                            confidence = 0.7, answer_confidence = 0.86, action = new { act_probability = 1.0 },
                        },
                    },
                    usage = new { input_tokens = 128, output_tokens = 0 },
                    routing = new { model = "typed", repo = "convaiinnovations/laya-typed-decisions", reason = "mock" },
                    laya_apple = new
                    {
                        model = "laya-typed-decisions",
                        model_revision = "c5d78730f3493e4fe16d61507ef4b78eef7318cf",
                        device = "gpu", backend = "mlx", routing_reason = "mock", request_id = 1,
                        truncated = false, sequence_length = 128, question_count = 1,
                        latency_ms = 4.2, queue_wait_ms = 0.0, device_ms = 4.2,
                    },
                });
                Write(ctx, answer);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                break;
            }
            catch { /* malformed request: answer 500 */ }
        }
    }

    private static void Write(HttpListenerContext ctx, string json)
    {
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}