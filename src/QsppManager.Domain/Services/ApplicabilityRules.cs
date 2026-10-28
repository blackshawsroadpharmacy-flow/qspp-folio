namespace QsppManager.Domain.Services;

/// <summary>Maps a requirement's applicability hint and the services a pharmacy says it offers to a
/// SUGGESTION. It never decides: the manager records the decision and its reason.</summary>
public static class ApplicabilityRules
{
    private const ServiceScopeFlag AnyCompounding =
        ServiceScopeFlag.SimpleCompounding | ServiceScopeFlag.ComplexCompounding | ServiceScopeFlag.SterileCompounding;

    public static (Applicability value, string reason)? Suggest(ApplicabilityHint hint, ServiceScopeFlag offered)
    {
        if (hint == ApplicabilityHint.Unknown) return null;
        if (hint == ApplicabilityHint.Always)
            return (Applicability.Applicable, "Applies to every pharmacy in the program.");
        if (offered == ServiceScopeFlag.None) return null;    // services not set yet: no conditional suggestion

        (bool has, string what) = hint switch
        {
            ApplicabilityHint.IfCompounding => ((offered & AnyCompounding) != 0, "compounding"),
            ApplicabilityHint.IfSterile => ((offered & ServiceScopeFlag.SterileCompounding) != 0, "sterile compounding"),
            ApplicabilityHint.IfPrescribing => ((offered & ServiceScopeFlag.Prescribing) != 0, "pharmacist prescribing"),
            ApplicabilityHint.IfIndirect => ((offered & ServiceScopeFlag.IndirectSupply) != 0, "indirect supply"),
            _ => (false, ""),
        };
        if (what == "") return null;
        return has
            ? (Applicability.Applicable, $"The pharmacy offers {what} (Settings, services).")
            : (Applicability.NotApplicable, $"The pharmacy has not said it offers {what} (Settings, services).");
    }
}