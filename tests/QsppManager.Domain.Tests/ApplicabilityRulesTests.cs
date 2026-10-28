using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using Xunit;

namespace QsppManager.Domain.Tests;

public class ApplicabilityRulesTests
{
    [Theory]
    [InlineData(ApplicabilityHint.Always, ServiceScopeFlag.None, Applicability.Applicable)]
    [InlineData(ApplicabilityHint.IfCompounding, ServiceScopeFlag.ComplexCompounding, Applicability.Applicable)]
    [InlineData(ApplicabilityHint.IfCompounding, ServiceScopeFlag.Prescribing, Applicability.NotApplicable)]
    [InlineData(ApplicabilityHint.IfSterile, ServiceScopeFlag.ComplexCompounding, Applicability.NotApplicable)]
    [InlineData(ApplicabilityHint.IfSterile, ServiceScopeFlag.SterileCompounding, Applicability.Applicable)]
    [InlineData(ApplicabilityHint.IfPrescribing, ServiceScopeFlag.Prescribing, Applicability.Applicable)]
    [InlineData(ApplicabilityHint.IfIndirect, ServiceScopeFlag.IndirectSupply, Applicability.Applicable)]
    public void Suggest_ByHintAndScope(ApplicabilityHint hint, ServiceScopeFlag offered, Applicability expected)
    {
        var sug = ApplicabilityRules.Suggest(hint, offered);
        Assert.NotNull(sug);
        var (value, _) = sug!.Value;
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Suggest_ConditionalWithNoServicesSet_ReturnsNull()
    {
        Assert.Null(ApplicabilityRules.Suggest(ApplicabilityHint.IfCompounding, ServiceScopeFlag.None));
    }

    [Fact]
    public void Suggest_UnknownHint_ReturnsNull()
    {
        Assert.Null(ApplicabilityRules.Suggest(ApplicabilityHint.Unknown, ServiceScopeFlag.ComplexCompounding));
    }
}