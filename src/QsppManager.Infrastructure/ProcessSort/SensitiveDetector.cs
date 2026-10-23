using System.Text.RegularExpressions;

namespace QsppManager.Infrastructure.ProcessSort;

/// <summary>Heuristic gate for private information in document text: patient
/// identifiers, personnel records and security credentials are routed to
/// sensitive review BEFORE the item can be published. Heuristics are a
/// detection aid — they are NOT a redaction guarantee, and the app says so.
///
/// Tuned against the pharmacy's real corpus (52 hold rows, 24 Oct): the bare-word
/// rules caught POLICIES ABOUT the topics — an MHR security policy, the IT access
/// policy, a WorkCover registration letter, an assessment guide that merely LISTS
/// rosters and performance forms. Tune rules (see milestones, 24 Oct):
///   • credential content is held only when an actual VALUE-SHAPED assignment is
///     present ("password: <token>", "passphrase = <token>", "SECRET: <token>") —
///     security policies explaining password rules pass, with or without a
///     policy-shaped title; bare mentions alone never hold;
///   • personnel words hold RECORDS (CVs, contracts, warnings, wage agreements —
///     anything not policy-shaped), but a POLICY-SHAPED document (filename carries
///     policy/procedure/sop/guide/template/checklist/kit/plan/to-do/manual) that
///     merely talks ABOUT rosters or payroll passes — its own review step still
///     sees everything. Known tradeoff, documented: a real personnel record whose
///     filename is policy-shaped ("Induction Checklist - filled for SM") would pass
///     the gate; approval review remains the final human guard;
///   • Medicare-adjacent numbers, the patient/confidential marker and DOB-paired dates
///     are UNCHANGED (they carried no false positives in the real corpus; widening the
///     confidential word to "confidentiality" was tried and REVERTED — policies ABOUT
///     confidential handling tripped it).</summary>
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

    /// <summary>An actual credential VALUE: keyword, colon or equals, then a token —
    /// the shape a real secret takes, and the shape a policy description never does.</summary>
    [GeneratedRegex(@"(?i)\b(password|passphrase|api[- ]?key|secret|credential)\b\s*[:=]\s*\S{6,}")]
    private static partial Regex CredentialValue();

    /// <summary>A document ABOUT rules (policy/procedure/guide/template/checklist/kit/
    /// plan/to-do/manual, judged by its own FILE NAME) is evidence, not a record.
    /// Boundaries are letter-based, not word-based: "Template_FT-and-PT" must still
    /// count (underscore is a word character, so \b would miss it).</summary>
    [GeneratedRegex(@"(?i)(?<![A-Za-z])(policy|policies|procedure|procedures|sop|sops|guide|guides|template|templates|checklist|checklists|kit|kits|plan|plans|to[\s-]?do|manual)(?![A-Za-z])")]
    private static partial Regex PolicyShapeWords();

    [GeneratedRegex(@"\b\d{2}[./]\d{2}[./]\d{2,4}\b.*\b(dob|date of birth|birth date)\b|\b(dob|date of birth)\b.*\b\d{2}[./]\d{2}[./]\d{2,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DobPaired();

    /// <summary>Returns human-readable reasons; empty list = no sensitive signal found.
    /// The first 6000 characters of text plus the file name are scanned — long
    /// documents still surface their patient/HR content early (which is where it lives).</summary>
    public static List<string> Detect(string text, string fileName)
    {
        var reasons = new List<string>();
        text ??= "";
        var hay = fileName + "\n" + (text.Length > 6000 ? text[..6000] : text);
        var policyShapedName = PolicyShapeWords().IsMatch(fileName ?? "");

        if (MedicareNumberShape().IsMatch(hay) && MedicareWords().IsMatch(hay))
            reasons.Add("a Medicare-number-shaped identifier appears near Medicare wording");
        if (ConfidentialPatient().IsMatch(hay))
            reasons.Add("patient/confidential marker (clinical confidentiality)");
        if (PersonnelWords().IsMatch(hay) && !policyShapedName)
            reasons.Add("personnel/workforce content (rosters, payroll, performance)");
        // a SECURITY POLICY passes without a value; any document (shaped or not) that
        // carries an actual value-shaped assignment is held
        if (CredentialWords().IsMatch(hay) && CredentialValue().IsMatch(hay))
            reasons.Add("credential value content (a password/passphrase/key with an assigned value)");
        if (DobPaired().IsMatch(hay))
            reasons.Add("a date of birth is paired with a date-of-birth label");
        return reasons;
    }
}