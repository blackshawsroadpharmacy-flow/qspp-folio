using Xunit;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Integration.Tests;

/// <summary>Keeps the --selftest mechanic honest: the CLI sweep cannot silently rot while
/// the suite is green (Today redesign, 24 Oct).</summary>
public class TodaySelftestTests
{
    [Fact]
    public void Selftest_Sweep_Passes()
    {
        using var w = new StringWriter();
        var code = TodaySelftest.Run(w);
        var output = w.ToString();
        Assert.Equal(0, code);
        Assert.DoesNotContain("FAIL", output);
        Assert.Contains("all checks pass", output);
    }
}