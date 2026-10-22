using System.Net.Http.Json;
using System.Text.Json;
using QsppManager.Domain.Entities;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Stage-2 decision service: a local, loopback-only typed-decision model
/// (Laya checkpoint `convaiinnovations/laya-typed-decisions` served by `laya-apple
/// serve`, pinned 1.7.0, MLX GPU path). Stage-2 NEVER sees an OCR dump from us:
/// it receives the document title, a ≤1.2k-char excerpt and the stage-1 candidate
/// descriptions, and answers ONE typed `choice` question with a fixed option
/// order — a candidate id, `multiple`, or `abstain`. It only re-ranks or holds
/// stage-1 output; suggestions never acquire review trust, and every answer
/// records the exact model + revision so the review queue shows who proposed
/// what. If the server is down, slow, or unsure, the batch keeps stage-1.</summary>
public sealed class FolioDecisionStage2
{
    /// <summary>The checkpoint name asked for in every request (Jev-compatible API).</summary>
    public const string ModelName = "laya-typed-decisions";

    /// <summary>Loopback pin — the client refuses to talk to anything else.</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:8642";

    /// <summary>Excerpt cap per the plan: concise, relevant context, never an OCR dump.</summary>
    public const int ExcerptChars = 1200;

    public const string MultipleOption = "multiple";
    public const string AbstainOption = "abstain";

    private readonly HttpClient _http;
    public string? LastFailure { get; private set; }

    public FolioDecisionStage2(string? baseUrl = null)
    {
        var url = baseUrl ?? Environment.GetEnvironmentVariable("QSPP_LAYA_URL") ?? DefaultBaseUrl;
        if (!url.StartsWith("http://127.0.0.1:") && !url.StartsWith("http://localhost:"))
            throw new ArgumentException("Stage-2 must bind to loopback only; refusing non-loopback URL.");
        _http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>Cheap probe: the server lists its models. Loopback, 1s budget.</summary>
    public bool Available()
    {
        try
        {
            using var probe = _http.GetAsync("/v1/models");
            if (!probe.Wait(1000) || !probe.Result.IsSuccessStatusCode)
            {
                LastFailure = "stage-2 server did not answer /v1/models";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LastFailure = ex.Message;
            return false;
        }
    }

    public sealed record Stage2Answer(string Choice, double AnswerConfidence, string Model, string? ModelRevision, string Device)
    {
        public string Identifier => ModelRevision is { } r ? $"{ModelName}@{r[..Math.Min(12, r.Length)]}" : ModelName;
    }

    /// <summary>One typed `choice` question over the stage-1 shortlist (fixed order)
    /// plus `multiple`/`abstain`. Returns null on any failure — the caller keeps
    /// stage-1 untouched.</summary>
    public Stage2Answer? Decide(string title, string excerpt, List<FolioCandidate> candidates)
    {
        var state = new
        {
            document = new { title, excerpt = Truncate(excerpt, ExcerptChars) },
            task = "pick the QSPP evidence destination this document belongs in",
            namespaces = "requirements carry a RefKind (Standard, UAG, Policy, Rule, Surveillance); the same number can exist in two namespaces and is NOT interchangeable",
        };
        var criteria = new Dictionary<string, string>();
        foreach (var c in candidates)
            criteria[c.Id.ToString()] = $"{c.RefKind} {c.Ref} — {Truncate(c.Title, 90)}. {Truncate(c.Summary, 140)}";
        criteria[MultipleOption] = "several of these destinations genuinely fit; the reviewer will pick them";
        criteria[AbstainOption] = "no listed destination fits well from this excerpt";

        var request = new
        {
            model = ModelName,
            state,
            questions = new Dictionary<string, object>
            {
                ["destination"] = new
                {
                    type = "choice",
                    instructions = "Given the document excerpt and the candidate list in order, choose the single best destination label.",
                    criteria,
                },
            },
        };

        try
        {
            using var content = JsonContent.Create(request);
            using var response = _http.PostAsync("/v1/systemone", content).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                LastFailure = $"stage-2 returned {(int)response.StatusCode}";
                return null;
            }
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return Parse(body);
        }
        catch (Exception ex)
        {
            LastFailure = ex.Message;
            return null;
        }
    }

    private static Stage2Answer? Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("answers", out var answers) ||
            !answers.TryGetProperty("destination", out var a) ||
            !a.TryGetProperty("choice", out var choiceEl))
            return null;

        var choice = choiceEl.GetString() ?? AbstainOption;
        var confidence = a.TryGetProperty("answer_confidence", out var ac) && ac.ValueKind == JsonValueKind.Number
            ? ac.GetDouble() : 0.0;
        string model = ModelName, revision = "";
        var device = "gpu";
        if (doc.RootElement.TryGetProperty("laya_apple", out var la))
        {
            if (la.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) model = m.GetString() ?? ModelName;
            if (la.TryGetProperty("model_revision", out var r) && r.ValueKind == JsonValueKind.String) revision = r.GetString() ?? "";
            if (la.TryGetProperty("device", out var d) && d.ValueKind == JsonValueKind.String) device = d.GetString() ?? device;
        }
        else if (doc.RootElement.TryGetProperty("routing", out var rt) &&
                 rt.TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.String)
        {
            revision = repo.GetString() ?? "";
        }
        return new(choice, confidence, model, revision, device);
    }

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max].TrimEnd());
}

/// <summary>Maps a stage-2 answer onto stage-1 output. Pure function so tests can
/// drive every branch at once: choice → ordered; multiple → several kept; abstain
/// or low confidence → stage-1 stands. Never invents suggestions, never changes
/// scores, never touches review trust.</summary>
public static class FolioStage2Mapping
{
    // Measured on the pinned checkpoint (bench_process_sort.py, this machine):
    // destination-choice accuracy ≈71% at answer_confidence 0.36-0.61 — the raw
    // confidence barely separates right from wrong below ~0.6. Stage-2 therefore
    // only REORDERS when it is fairly confident AND the winner is already inside
    // stage-1's top-2; overriding into abstain/multiple needs ≥0.80 (rare).
    public const double MinReorderConfidence = 0.60;
    public const double MinOverrideConfidence = 0.80;
    public const int WinnerMustBeTop = 2;

    /// <summary>Returns the possibly re-ordered suggestion list plus the final state.</summary>
    public static (List<FolioSuggestion> suggestions, FolioItemState state) Map(
        List<FolioSuggestion> stage1, FolioItemState stage1State, FolioDecisionStage2.Stage2Answer answer,
        List<FolioCandidate> candidates)
    {
        var winner = answer.Choice switch
        {
            FolioDecisionStage2.AbstainOption => null,
            FolioDecisionStage2.MultipleOption => null,
            _ when Guid.TryParse(answer.Choice, out _) => candidates.FirstOrDefault(c => c.Id.ToString() == answer.Choice),
            _ => null,
        };
        var markStage2 = (FolioSuggestion s) =>
        {
            var model = answer.Identifier.Contains("laya-typed-decisions") || answer.Model == FolioDecisionStage2.ModelName
                ? FolioDecisionStage2.ModelName
                : $"{answer.Model}";
            var version = string.IsNullOrEmpty(answer.ModelRevision) ? "" : answer.ModelRevision[..Math.Min(12, answer.ModelRevision.Length)];
            return s with { Model = model, ModelVersion = version == "" ? s.ModelVersion : version, Rationale = $"stage-2 pick (answer_confidence {answer.AnswerConfidence:0.00})" };
        };

        if (winner != null && answer.AnswerConfidence >= MinReorderConfidence)
        {
            var idx = stage1.FindIndex(s => s.RequirementId == winner.Id);
            if (idx >= 0 && idx < WinnerMustBeTop)
            {
                var reordered = new List<FolioSuggestion> { markStage2(stage1[idx]) };
                reordered.AddRange(stage1.Where((_, i) => i != idx));
                return (reordered, stage1State == FolioItemState.MultipleCandidates && answer.AnswerConfidence < 0.80
                    ? FolioItemState.MultipleCandidates
                    : FolioItemState.Proposed);
            }
        }
        if (answer.Choice == FolioDecisionStage2.MultipleOption && answer.AnswerConfidence >= MinOverrideConfidence
            && stage1.Count > 1)
            return (stage1, FolioItemState.MultipleCandidates);
        if (answer.Choice == FolioDecisionStage2.AbstainOption && answer.AnswerConfidence >= MinOverrideConfidence
            && stage1State != FolioItemState.Abstained)
            // stage-1 proposed something the model could not place — keep BOTH
            // honest: suggestions stay, state says a decision is wanted.
            return (stage1, FolioItemState.Abstained);
        return (stage1, stage1State); // unsure, low confidence, or unknown label: stage-1 stands
    }
}