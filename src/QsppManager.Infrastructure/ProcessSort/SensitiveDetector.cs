using System.Text.RegularExpressions;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Heuristic gate for private information in document text: patient
/// identifiers, personnel records and security credentials are routed to
/// sensitive review BEFORE the item can be published. Heuristics are a
/// detection aid — they are NOT a redaction guarantee, and the app says so.</summary>
public static partial class SensitiveDetector
{
    [GeneratedRegex(@"\bmedicare\b|\bmedicare card\b|\bMRN\b|\bmedical record number\b|\bconcessional card\b", RegexOptions.IgnoreCase)]
    private static partial Regex MedicareWords();

    [GeneratedRegex(@"\b\d{4}[\s-]?\d{4}[\s-]?\d{1}\b")]
    private static partial Regex MedicareNumberShape();

    [GeneratedRegex(@"\bconf(?:idential)?\b.*\b(patient|resident|consumer|clinical|pharmacotherapy)\b|\b(patient|resident|consumer|clinical|pharmacotherapy)\b.*\bconf(?:idential)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConfidentialPatient();

    [GeneratedRegex(@"\b(payroll|salary|performance review|disciplinary|roster(?!\s*note)|shift times)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PersonnelWords();

    [GeneratedRegex(@"\b(password|passphrase|api[- ]?key|secret|credentials?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialWords();

    [GeneratedRegex(@"\b\d{2}[./]\d{2}[./]\d{2,4}\b.*\b(dob|date of birth|birth date)\b|\b(dob|date of birth)\b.*\b\d{2}[./]\d{2}[./]\d{2,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DobPaired();

    /// <summary>Returns human-readable reasons; empty list = no sensitive signal found.</summary>
    public static List<string> Detect(string text, string fileName)
    {
        var reasons = new List<string>();
        text ??= "";
        var hay = fileName + "\n" + (text.Length > 6000 ? text[..6000] : text);

        if (MedicareNumberShape().IsMatch(hay) && MedicareWords().IsMatch(hay))
            reasons.Add("a Medicare-number-shaped identifier appears near Medicare wording");
        if (ConfidentialPatient().IsMatch(hay))
            reasons.Add("patient/confidential marker (clinical confidentiality)");
        if (PersonnelWords().IsMatch(hay))
            reasons.Add("personnel/workforce content (rosters, payroll, performance)");
        if (CredentialWords().IsMatch(hay))
            reasons.Add("possible credential/password content");
        if (DobPaired().IsMatch(hay))
            reasons.Add("a date of birth is paired with a date-of-birth label");
        return reasons;
    }
}