using QsppManager.Infrastructure.ProcessSort;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Stage-1 v2 mechanics, pinned at unit level (each uses a tiny synthetic
/// candidate list, so inverse-frequency weights are exact and deterministic). The
/// corpus-grounded outcomes (clears, clusters, never-invents) live in
/// ProcessSortWiderCorpusTests;</summary>
public class FolioClassifierV2Tests
{
    private static FolioCandidate C(string ref_, string kind, string title, string summary, params string[] examples) =>
        new(Guid.NewGuid(), ref_, kind, title, summary, examples);

    [Fact]
    public void Stemming_Unifies_PluralMismatches()
    {
        var cand = C("Rule 10", "Rule", "Assessment bookings", "Cancellation windows apply.", "Booking register");
        var clf = new FolioClassifier([cand]);
        // plural forms on the doc side must find the singular forms in the wording
        var s = clf.Classify("Cancellations recorded: windows for registration apply.", "mixed.pdf");
        Assert.NotEmpty(s);
        Assert.True(s[0].Score >= 6, $"plural doc scored {s[0].Score:F2} against singular wording");
    }

    [Fact]
    public void DistinctiveTokens_Outweigh_CorpusFillers()
    {
        // IDF only bites against a REALISTIC population: 30 candidates share the filler
        // wording ("pharmacy services delivery"), one carries a distinctive term. A doc
        // with mostly filler and one distinctive phrase must land on the distinctive one.
        var cands = new List<FolioCandidate>();
        for (var i = 0; i < 30; i++)
            cands.Add(C($"UAG 9.{i}", "Uag", $"Delivery of pharmacy services part {i}",
                "Pharmacy services delivered to the community.", "Pharmacy service delivery record"));
        cands.Add(C("REM-07", "Surveillance", "Cytotoxic waste disposal", "Cytotoxic waste is disposed of safely.",
            "Cytotoxic waste contract"));
        var clf = new FolioClassifier(cands);
        var doc = "Our pharmacy services include delivery of pharmacy services and handling of cytotoxic waste.";
        var s = clf.Classify(doc, "ops.docx");
        Assert.NotEmpty(s);
        Assert.Equal("REM-07", s[0].Ref); // the distinctive tokens win over the filler soup
    }

    [Fact]
    public void WeakCluster_IsOffered_NotAbstained()
    {
        // A family doc against a realistic population: twenty refs share the family
        // wording, so every family token is deflated and no member reaches the confident
        // floor — but the family IS the topic. The honest outcome is a cluster offer,
        // not silence.
        var cands = new List<FolioCandidate>();
        for (var i = 0; i < 20; i++)
            cands.Add(C($"Rule {i + 1}", "Rule", "Assessment bookings and cancellation windows",
                $"Assessment booking and cancellation window rules item {i}.", "Booking register"));
        for (var i = 0; i < 20; i++)
            cands.Add(C($"UAG 9.{i}", "Uag", "Delivery of pharmacy services", "Pharmacy services delivered.", ""));
        var clf = new FolioClassifier(cands);
        var s = clf.Classify("Cancellation confirmed for the booked assessment session.", "arrangements.pdf");
        Assert.True(s.Count >= 2, $"family expected a cluster, got {s.Count}: {string.Join(",", s.Select(x => x.Ref))}");
        Assert.All(s, x => Assert.True(x.Score < 6, "weak cluster means below the confident floor"));
        Assert.Contains("weak cluster", s[0].Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleWeakOrNoSignal_Abstains_Quietly()
    {
        var cands = new List<FolioCandidate>();
        for (var i = 0; i < 20; i++)
            cands.Add(C($"Rule {i + 1}", "Rule", "Assessment bookings and cancellation windows",
                $"Assessment booking and cancellation window rules item {i}.", "Booking register"));
        var clf = new FolioClassifier(cands);
        // one deflated family token, no phrase, no near-tied cluster worth offering
        var s = clf.Classify("Assessment paperwork somewhere in the archive box.", "box.pdf");
        Assert.Empty(s);
    }

    [Fact]
    public void TitleBigram_Phrase_BonusApplies()
    {
        var a = C("REM-05", "Surveillance", "Cold chain self-audit", "Strive for 5 fridge records.", "Cold chain log");
        var b = C("UAG 9.3", "Uag", "Cleaning premises", "Premises cleanliness schedule.", "Cleaning rota");
        var clf = new FolioClassifier([a, b]);
        var s = clf.Classify("Our cold chain is monitored daily and we keep the cold chain log.", "note.pdf");
        Assert.Equal("REM-05", s[0].Ref); // adjacent "cold chain" phrase beats scattered tokens
    }

    [Fact]
    public void FileName_Tokens_WeightAbove_Body()
    {
        var a = C("REM-09", "Surveillance", "IT loss recovery process", "Backup and recovery of clinic systems.", "IT recovery plan");
        var b = C("UAG 9.3", "Uag", "Cleaning premises", "Premises maintenance and cleaning records.", "Cleaning log");
        var clf = new FolioClassifier([a, b]);
        // filename carries the signal; the body is vague for both candidates
        var s = clf.Classify("Records and schedules kept as required.", "IT-recovery-plan-2026.pdf");
        Assert.NotEmpty(s);
        Assert.Equal("REM-09", s[0].Ref); // the file name is a label, weighted on top
    }
}