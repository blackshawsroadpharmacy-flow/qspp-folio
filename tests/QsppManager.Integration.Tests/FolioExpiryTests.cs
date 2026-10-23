using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.ProcessSort;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Expiry detection: keyword-adjacent, same-line, conservative. Formats across
/// the shapes Australian pharmacy documents actually print; every negative case (no
/// keyword, keyword without date, "from" without an end, garbled text, implausible
/// years) yields NO detection rather than a guess. Statuses are computed, never stored.</summary>
public class FolioExpiryTests
{
    [Theory]
    [InlineData("Professional indemnity insurance. This policy expires 30 June 2026.", "2026-06-30")]
    [InlineData("Licence renewal date: 31 Mar 2024 (lapsed).", "2024-03-31")]
    [InlineData("Valid until 30/06/2027", "2027-06-30")]
    [InlineData("Next due 2026-11-15 per the schedule", "2026-11-15")]
    [InlineData("Certificate valid until 30.06.2026 under the program rules", "2026-06-30")]
    [InlineData("CPRS training expires 1st August 2027", "2027-08-01")]
    [InlineData("Policy is not valid after 3/31/2025", "2025-03-31")]
    [InlineData("AHPRA registration expiry: 31-Dec-2026", "2026-12-31")]
    public void Detect_ParsesKeywordAdjacentDates(string line, string iso)
    {
        var guess = FolioExpiry.Detect(line);
        Assert.NotNull(guess);
        Assert.Equal(DateOnly.Parse(iso), guess!.ExpiresOn);
        Assert.Equal(line.Trim(), guess.Hint);
    }

    [Fact]
    public void Detect_Range_PrefersTheBindingEnd()
    {
        var guess = FolioExpiry.Detect("Policy valid from 1 July 2024 to 30 June 2025.");
        Assert.NotNull(guess);
        Assert.Equal(new DateOnly(2025, 6, 30), guess!.ExpiresOn);
    }

    [Fact]
    public void Detect_FromWithoutEnd_YieldsNoGuess()
    {
        Assert.Null(FolioExpiry.Detect("The policy is valid from 1 July 2024 and reviewed annually."));
    }

    [Fact]
    public void Detect_DateWithoutKeyword_YieldsNoGuess()
    {
        Assert.Null(FolioExpiry.Detect("The booking register was updated on 15 October 2026 by the pharmacist in charge."));
    }

    [Theory]
    [InlineData("Licence expired — see the register for the current status.")]
    [InlineData("expiry date: [unreadable scan region]")]
    [InlineData("This policy expires at the end of the agreed term.")]
    [InlineData("garbled text xx9/9//9zzz expires soon maybe")]
    [InlineData("The certificate was issued 1 January 1850 and is lapsed.")]
    public void Detect_KeywordWithoutAParsablePlausibleDate_YieldsNoGuess(string line)
    {
        Assert.Null(FolioExpiry.Detect(line));
    }

    [Fact]
    public void Detect_FindsTheKeywordLine_InAMultiLineDocument()
    {
        var doc = string.Join('\n',
            "Medication Advisory Committee minutes, October 2026.",
            "Attendees recorded in the register. Date of issue: 15/10/2026.",
            "Our professional indemnity policy expires 30 June 2027.",
            "Next meeting to be scheduled.");
        var guess = FolioExpiry.Detect(doc);
        Assert.NotNull(guess);
        Assert.Equal(new DateOnly(2027, 6, 30), guess!.ExpiresOn);
        Assert.Equal("Our professional indemnity policy expires 30 June 2027.", guess.Hint);
    }

    [Fact]
    public void Detect_BlankOrWhitespace_YieldsNoGuess()
    {
        Assert.Null(FolioExpiry.Detect(""));
        Assert.Null(FolioExpiry.Detect("   \n  "));
        Assert.Null(FolioExpiry.Detect(null));
    }

    [Theory]
    [InlineData("2024-03-31", "2026-01-15", "expired")]
    [InlineData("2026-03-31", "2026-01-15", "due soon")]   // 75 days ahead
    [InlineData("2026-07-31", "2026-01-15", "")]            // beyond 90 days
    [InlineData("2026-04-15", "2026-01-15", "due soon")]    // exactly 90 days
    [InlineData("2026-04-16", "2026-01-15", "")]            // the day after the window
    public void Status_IsComputedLive_NeverStored(string iso, string todayIso, string expected)
    {
        var guess = new FolioExpiryGuess(DateOnly.Parse(iso), "hint");
        Assert.Equal(expected, guess.Status(DateOnly.Parse(todayIso)));
    }

    [Fact]
    public void Json_RoundTrips_AndEmptyStaysEmpty()
    {
        var guess = new FolioExpiryGuess(new DateOnly(2027, 6, 30), "policy expires 30 June 2027");
        var round = FolioExpiryGuess.FromJson(guess.ToJson());
        Assert.NotNull(round);
        Assert.Equal(guess.ExpiresOn, round!.ExpiresOn);
        Assert.Equal(guess.Hint, round.Hint);
        Assert.Null(FolioExpiryGuess.FromJson("{}"));
        Assert.Null(FolioExpiryGuess.FromJson("not json"));
    }
}