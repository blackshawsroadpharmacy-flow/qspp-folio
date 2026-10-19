using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

public class SourceCompareTests
{
    [Fact]
    public void Diff_ReportsChangedLines_AndIgnoresWhitespaceOnlyDifferences()
    {
        var a = "line one\nPatient-Reported Outcome Measures (PROMs)\nline three\nline four";
        var b = "line one\nPatient-reported Measures\nline three\nline four\nnew line five";
        var (summary, lines) = SourceCompare.Diff("UAG original", a, "UAG hub copy", b);
        Assert.Contains("3 differing line(s)", summary); // replaced pair = -1/+1, plus the added line
        Assert.Contains(lines, l => l.StartsWith("- ") && l.Contains("Outcome Measures"));
        Assert.Contains(lines, l => l.StartsWith("+ ") && l.Contains("Patient-reported Measures"));
        Assert.Contains(lines, l => l.StartsWith("+ ") && l.Contains("new line five"));
        // whitespace-only line difference must NOT appear as a change
        var (s2, l2) = SourceCompare.Diff("A", "same text\t", "B", "same text    ");
        Assert.Contains("0 differing line(s)", s2);
        Assert.DoesNotContain(l2, l => l.StartsWith('+') || l.StartsWith('-'));
    }
}