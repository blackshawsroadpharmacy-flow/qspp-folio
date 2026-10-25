using QsppManager.Domain.Services;
using Xunit;

namespace QsppManager.Domain.Tests;

/// <summary>Natural reference order for the Requirements list (owner brief): numeric runs
/// compare by value so "Rule 10" follows "Rule 2"; dotted subclauses compare part by part;
/// guide refs order by section, subclause, then task letter.</summary>
public class RefOrderTests
{
    private static void Ascending(params string[] refs)
    {
        for (var i = 0; i < refs.Length - 1; i++)
        {
            Assert.True(RefOrder.Compare(refs[i], refs[i + 1]) < 0, $"expected '{refs[i]}' < '{refs[i + 1]}'");
            Assert.True(RefOrder.Compare(refs[i + 1], refs[i]) > 0, $"expected '{refs[i]}' > '{refs[i + 1]}'");
        }
    }

    [Fact]
    public void RuleReferencesSortNumerically() => Ascending("Rule 2", "Rule 10", "Rule 11", "Rule 19", "Rule 20");

    [Fact]
    public void ReadinessReferencesSortNumerically() => Ascending("Hit-2", "Hit-10", "Hit-11");

    [Fact]
    public void RemoteRoundReferencesSortNumerically() => Ascending("REM-02", "REM-10", "REM-19");

    [Fact]
    public void TermsReferencesSortNumerically() => Ascending("T&C 3", "T&C 16");

    [Fact]
    public void StandardSubclauseRefsSortByPart() => Ascending("2.2.3.2", "2.2.3.10", "2.2.4");

    [Fact]
    public void GuideRefsOrderBySectionThenSubclauseThenLetter() =>
        Ascending("UAG 1.2 1.1.2.7 task a", "UAG 1.2 1.1.2.7 task b", "UAG 1.2 1.1.2.10 task a", "UAG 1.3 2.0.0.1 task a");

    [Fact]
    public void FullSortProducesNaturalSequence()
    {
        var refs = new[] { "Rule 10", "Rule 1", "Hit-10", "Rule 2", "Hit-2", "T&C 16", "T&C 3", "REM-10", "REM-02", "2.2.3.10", "2.2.3.2" };
        var sorted = refs.Order(RefOrder.Comparer).ToArray();
        Assert.Equal(
            new[] { "2.2.3.2", "2.2.3.10", "Hit-2", "Hit-10", "REM-02", "REM-10", "Rule 1", "Rule 2", "Rule 10", "T&C 3", "T&C 16" },
            sorted);
    }

    [Fact]
    public void EqualAndNullCasesAreStable()
    {
        Assert.Equal(0, RefOrder.Compare("Rule 5", "Rule 5"));
        Assert.Equal(0, RefOrder.Compare("", ""));
        Assert.True(RefOrder.Compare(null, "Rule 1") < 0);
        Assert.True(RefOrder.Compare("Rule 1", null) > 0);
        Assert.Equal(0, RefOrder.Compare(null, null));
    }
}