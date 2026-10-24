using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using Xunit;

namespace QsppManager.Domain.Tests;

/// <summary>Display-title rules (owner brief §4): verbatim wording where the source is
/// short and imperative; structured headings where unique; acronyms expanded in labels.</summary>
public class DisplayTitlesTests
{
    private static Requirement Req(RefKind kind, string title, string summary = "",
        string wording = "", DomainPart domain = DomainPart.Domain1, string extra = "{}")
        => new()
        {
            RefKind = kind, Ref = title, Title = title, PlainSummary = summary,
            WordingExcerpt = wording, Domain = domain, ExtraJson = extra,
        };

    [Fact]
    public void KeyTask_shortImperativeWording_isUsedVerbatim()
    {
        var r = Req(RefKind.UagKeyTask, "UAG 1.1 1.1.1.1 task c",
            wording: "c. Have a culture of safety and quality improvement.");
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.Equal("Have a culture of safety and quality improvement", t.Title);
        Assert.Equal(DisplayTitleBasis.Wording, t.Basis);
    }

    [Fact]
    public void KeyTask_longWording_isCappedAtWordBoundary()
    {
        var r = Req(RefKind.UagKeyTask, "UAG 1.1 1.1.1.1 task c",
            wording: "c. Establish and maintain systems for integrating care with other service providers involved in a patient’s care.");
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.True(t.Title.Length <= 70 + 2); // cap + ellipsis
        Assert.EndsWith(" …", t.Title);
        Assert.DoesNotContain("involved", t.Title[..^2]); // no partial word
        Assert.Equal(DisplayTitleBasis.Wording, t.Basis);
    }

    [Fact]
    public void KeyTask_subjectFirstFragment_fallsBackToSource()
    {
        var r = Req(RefKind.UagKeyTask, "UAG 4.2 1.2.2.1 task a",
            wording: "a. The details of all pharmacy owners, business partnerships, shareholder or unit holders’ agreements (as applicable).");
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.Equal(DisplayTitleBasis.Source, t.Basis);
        Assert.True(t.NeedsHumanTitle);
    }

    [Theory]
    [InlineData("d. A substitute decision-maker if a patient does not have the capacity to make decisions for themselves.")]
    [InlineData("g. Mechanism for providing feedback and contact details for the appropriate healthcare complaints authority.")]
    public void KeyTask_fragments_neverBecomeTitles(string wording)
    {
        var r = Req(RefKind.UagKeyTask, "x", wording: wording);
        Assert.False(RequirementDisplay.PhraseFromWording(wording));
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.Equal(DisplayTitleBasis.Source, t.Basis);
    }

    [Fact]
    public void UagSection_mapSuffixIsStripped_andNumberRemoved()
    {
        var r = Req(RefKind.Uag, "2.3 Safe and Appropriate Provision of Medicines, Medical Devices and Poisons — maps to standard clause 2.2.2");
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.Equal("Safe and appropriate provision of medicines, medical devices and poisons", t.Title);
        Assert.Equal(RequirementDisplay.Area(r), t.Area);
    }

    [Fact]
    public void Std_uniqueSubclauseTitle_isUsed()
    {
        var r = Req(RefKind.Std, "1.1.1 — clause 1.1 Clinical Governance",
            extra: """{"subclauseTitle":"Governance, leadership, and culture"}""");
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.Equal("Governance, leadership, and culture", t.Title);
        Assert.Equal(DisplayTitleBasis.Source, t.Basis);
    }

    [Fact]
    public void Std_duplicatedSubclauseTitle_keepsSourceTitle()
    {
        var a = Req(RefKind.Std, "1.2.5 — clause 1.2 Legal Obligations",
            extra: """{"subclauseTitle":"Safety and security"}""");
        var b = Req(RefKind.Std, "4.5.5 — clause 4.5 Site Requirements",
            extra: """{"subclauseTitle":"Safety and security"}""");
        var titles = RequirementDisplay.Resolve(new[] { a, b });
        Assert.All(titles, t => Assert.Equal(t.SourceTitle, t.Title));
        Assert.All(titles, t => Assert.True(t.NeedsHumanTitle));
    }

    [Fact]
    public void Rule_uniqueHeading_isUsed_duplicated_staysSource()
    {
        var unique = Req(RefKind.Rule, "Rule 1 — Pharmacy Conduct");
        var dup1 = Req(RefKind.Rule, "Rule 10 — QSPP Assessor");
        var dup2 = Req(RefKind.Rule, "Rule 20 — QSPP Assessor");
        var titles = RequirementDisplay.Resolve(new[] { unique, dup1, dup2 });
        Assert.Equal("Pharmacy conduct", titles[0].Title);
        Assert.Equal("Rule 10 — QSPP Assessor", titles[1].Title);
        Assert.Equal("Rule 20 — QSPP Assessor", titles[2].Title);
    }

    [Fact]
    public void Tc_clauseHeading_isStripped()
    {
        var r = Req(RefKind.Tc, "Clause 10: Change in ownership of pharmacy business");
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.Equal("Change in ownership of pharmacy business", t.Title);
        Assert.Equal("Program terms", t.Area);
    }

    [Fact]
    public void Policy_qsppAndPolicySuffix_removed()
    {
        var r = Req(RefKind.Policy, "QSPP Accreditation & Revocation Policy");
        var t = RequirementDisplay.Resolve(new[] { r }).Single();
        Assert.Equal("Accreditation & revocation", t.Title);
    }

    [Fact]
    public void Hitlist_shortSummary_isVerbatim_longOneNeedsEditorial()
    {
        var shortish = Req(RefKind.Hitlist, "Hit-List #9 P3", summary: "Batch records template for permitted batching runs.");
        var lengthy = Req(RefKind.Hitlist, "Hit-List #1 P1",
            summary: "Write/upgrade beyond-use dating (BUD) procedure: how BUDs are assigned and substantiated (APF/USP references).");
        var titles = RequirementDisplay.Resolve(new[] { shortish, lengthy });
        Assert.Equal("Batch records template for permitted batching runs", titles[0].Title);
        Assert.Equal("Hit-List #1 P1", titles[1].Title);
        Assert.True(titles[1].NeedsHumanTitle);
    }

    [Fact]
    public void Surveillance_andObligation_keepSourceTitles()
    {
        var s = Req(RefKind.Surveillance, "Vaccination procedure");
        var o = Req(RefKind.Obligation, "Fees and invoices: pay on strict 28-day terms");
        var titles = RequirementDisplay.Resolve(new[] { s, o });
        Assert.All(titles, t => Assert.Equal(t.SourceTitle, t.Title));
        Assert.All(titles, t => Assert.False(t.NeedsHumanTitle));
        Assert.Equal("Remote assessment", titles[0].KindLabel);
    }

    [Fact]
    public void Editorial_overridesWinAndAreFlagged()
    {
        var r = Req(RefKind.Hitlist, "Hit-List #13 P3", summary: "Potency testing scope: confirm testing covers the required matrix.");
        var t = RequirementDisplay.Resolve(new[] { r },
            editorial: new Dictionary<string, string> { [r.Id.ToString()] = "Potency testing scope" }).Single();
        Assert.Equal("Potency testing scope", t.Title);
        Assert.Equal(DisplayTitleBasis.Editorial, t.Basis);
        Assert.False(t.NeedsHumanTitle);
    }

    [Fact]
    public void KindLabels_expandAcronyms()
    {
        Assert.Equal("Readiness list", RequirementDisplay.KindLabel(Req(RefKind.Hitlist, "Hit-List #1 P1")));
        Assert.Equal("Program rule", RequirementDisplay.KindLabel(Req(RefKind.Rule, "Rule 1 — Pharmacy Conduct")));
        Assert.Equal("Key task", RequirementDisplay.KindLabel(Req(RefKind.UagKeyTask, "x")));
    }

    [Theory]
    [InlineData("UAG", "User Assessment Guide")]
    [InlineData("STD", "the Standard (AS 85000:2024)")]
    [InlineData("T&C", "Terms and Conditions")]
    [InlineData("REM", "Remote assessment")]
    [InlineData("P1", "Priority 1")]
    public void AcronymMap_expandsKnownAcronyms(string acronym, string expected)
    {
        Assert.True(AcronymMap.TryExpand(acronym, out var expansion));
        Assert.Equal(expected, expansion);
    }

    [Fact]
    public void AcronymMap_8CPA_isNotExpandedByMistake()
    {
        Assert.False(AcronymMap.TryExpand("8CPA", out _));
    }

    [Fact]
    public void ParseEditorialJson_readsIdMap()
    {
        var parsed = RequirementDisplay.ParseEditorialJson("""{"<id>":"Title"}""");
        Assert.Equal("Title", parsed["<id>"]);
    }
}