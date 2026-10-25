using System.Collections.Generic;

namespace QsppManager.Domain.Services;

/// <summary>Natural reference order for requirement reference codes. Number runs compare
/// numerically, so "Rule 10" follows "Rule 2" and "Hit-10" follows "Hit-2" instead of
/// falling between the small numbers; text runs compare case-insensitively; digits order
/// before text at a run boundary; an ordinal tie-break keeps numerically-equal refs
/// ("REM-2" vs "REM-02") in one deterministic order.</summary>
public static class RefOrder
{
    public static readonly IComparer<string> Comparer = Comparer<string>.Create(Compare);

    /// <summary>Compare two refs run by run. At each position both sides either carry a
    /// digit run (compared by value, leading zeros ignored) or a text run (compared
    /// ignoring case). A digit run on only one side orders first. Runs split on the
    /// digit/text boundary, so dotted subclauses compare part by part ("2.2.3.2" before
    /// "2.2.3.10") and trailing task letters compare lexically after their subclause.</summary>
    public static int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            var digitA = char.IsDigit(a[i]);
            var digitB = char.IsDigit(b[j]);
            if (digitA != digitB) return digitA ? -1 : 1;
            if (digitA)
            {
                var runA = Digits(a, ref i);
                var runB = Digits(b, ref j);
                var numeric = CompareNumeric(runA, runB);
                if (numeric != 0) return numeric;
            }
            else
            {
                var runA = Text(a, ref i);
                var runB = Text(b, ref j);
                var text = string.Compare(runA, runB, StringComparison.OrdinalIgnoreCase);
                if (text != 0) return text;
            }
        }
        // One side ran out: the shorter prefix first; the ordinal tie-break keeps
        // numerically-equal refs deterministic instead of flipping between runs.
        var prefix = (a.Length - i).CompareTo(b.Length - j);
        return prefix != 0 ? prefix : string.CompareOrdinal(a[i..], b[j..]);
    }

    private static string Digits(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return s[start..i];
    }

    private static string Text(string s, ref int i)
    {
        var start = i;
        while (i < s.Length && !char.IsDigit(s[i])) i++;
        return s[start..i];
    }

    private static int CompareNumeric(string a, string b)
    {
        var digitsA = a.TrimStart('0');
        var digitsB = b.TrimStart('0');
        if (digitsA.Length != digitsB.Length) return digitsA.Length.CompareTo(digitsB.Length);
        return string.CompareOrdinal(digitsA, digitsB);
    }
}