using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-17: opt-in tests show as SKIPPED when their gate environment variable is
/// absent, instead of passing silently through an early return. xunit reads Skip on the
/// attribute at discovery, so the attribute IS the gate.</summary>
public sealed class RealCorpusFactAttribute : FactAttribute
{
    public RealCorpusFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("QSPP_REAL_CORPUS") != "1")
            Skip = "Opt-in only: set QSPP_REAL_CORPUS=1 (and QSPP_DATA_DIR to an initialised data dir) to run against the real corpus";
    }
}

public sealed class DumpTitlesFactAttribute : FactAttribute
{
    public DumpTitlesFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("QSPP_DUMP_TITLES") != "1")
            Skip = "Opt-in only: set QSPP_DUMP_TITLES=1 to regenerate the display-title review table under docs/review/";
    }
}