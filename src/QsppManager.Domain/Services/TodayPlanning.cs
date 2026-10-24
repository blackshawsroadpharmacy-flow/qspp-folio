using QsppManager.Domain.Entities;

namespace QsppManager.Domain.Services;

/// <summary>Pure, testable planning maths for the Today screen (Today redesign, 24 Oct 2026).
/// No database access; callers feed records in and receive records out. Calendar dates are
/// DateOnly and remain Australia/Melbourne dates by construction: call
/// <see cref="MelbourneToday"/> to obtain the current Melbourne date and compare dates
/// date-to-date (never with 24-hour blocks).</summary>
public static class TodayPlanning
{
    // ---------- the current Melbourne date (rollover-safe) ----------

    /// <summary>The current calendar date in Australia/Melbourne for a given UTC instant.
    /// Falls back to a fixed UTC+10 offset only if the zone cannot be resolved — logged by
    /// the caller where it can be seen.</summary>
    public static DateOnly MelbourneToday(DateTime utcNow, TimeZoneInfo? zone = null)
    {
        if (zone is null)
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne"); }
            catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.CreateCustomTimeZone("vic-fallback", TimeSpan.FromHours(10), "VIC fallback", "VIC fallback (UTC+10, no DST)"); }
            catch (InvalidTimeZoneException) { zone = TimeZoneInfo.CreateCustomTimeZone("vic-fallback", TimeSpan.FromHours(10), "VIC fallback", "VIC fallback (UTC+10, no DST)"); }
        }
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return DateOnly.FromDateTime(local.Date);
    }

    // ---------- Victorian public holidays (provenance: VIC public holiday rules) ----------

    /// <summary>VIC public holidays affecting business-day counting. Only for years this app
    /// can plausibly plan in (2025-2035). Melbourne Cup Day: first Tuesday of November.
    /// Weekend-fixed holidays shift forward to the next free Monday/Tuesday (documented,
    /// deterministic — never inferred from a source that is not the holiday rules).</summary>
    public static bool IsVictorianHoliday(DateOnly d)
    {
        if (d.Year is < 2025 or > 2035) return false;
        return MelbourneCup(d) || NewYear(d) || AustraliaDay(d) || LabourDay(d)
            || GoodFriday(d) || EasterMonday(d) || AnzacDay(d) || KingsBirthday(d)
            || GrandFinalFriday(d) || Christmas(d) || BoxingDay(d);
    }

    private static DateOnly NextMonday(DateOnly d) { while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1); return d; }
    private static DateOnly NthWeekday(int year, int month, DayOfWeek dow, int n)
    {
        var d = new DateOnly(year, month, 1);
        while (d.DayOfWeek != dow) d = d.AddDays(1);
        return d.AddDays(7 * (n - 1));
    }
    private static DateOnly LastWeekday(int year, int month, DayOfWeek dow)
    {
        var d = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        while (d.DayOfWeek != dow) d = d.AddDays(-1);
        return d;
    }

    /// <summary>Anonymous Gregorian Easter (Meeus/Jones/Butcher), valid 1583-4099.</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100; var c = year % 100;
        var d = b / 4; var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4; var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }

    private static bool MelbourneCup(DateOnly d) => d == NthWeekday(d.Year, 11, DayOfWeek.Tuesday, 1);
    private static bool NewYear(DateOnly d) => d == new DateOnly(d.Year, 1, 1) || d == NextMonday(new DateOnly(d.Year, 1, 1));
    private static bool AustraliaDay(DateOnly d) => d == new DateOnly(d.Year, 1, 26) || d == NextMonday(new DateOnly(d.Year, 1, 26));
    private static bool LabourDay(DateOnly d) => d == NthWeekday(d.Year, 3, DayOfWeek.Monday, 2);
    private static bool GoodFriday(DateOnly d) => d == EasterSunday(d.Year).AddDays(-2);
    private static bool EasterMonday(DateOnly d) => d == EasterSunday(d.Year).AddDays(1);
    private static bool AnzacDay(DateOnly d) => d == new DateOnly(d.Year, 4, 25) || d == NextMonday(new DateOnly(d.Year, 4, 25));
    private static bool KingsBirthday(DateOnly d) => d == NthWeekday(d.Year, 6, DayOfWeek.Monday, 2);
    private static bool GrandFinalFriday(DateOnly d) => d == LastWeekday(d.Year, 9, DayOfWeek.Friday);
    private static bool Christmas(DateOnly d) => d == new DateOnly(d.Year, 12, 25) || d == NextMonday(new DateOnly(d.Year, 12, 25));
    private static bool BoxingDay(DateOnly d)
    {
        var box = new DateOnly(d.Year, 12, 26);
        // If Boxing Day itself is a weekend, its observed day is the Tuesday after Christmas's
        // observed Monday ( VIC: Christmas observed Mon, Boxing observed Tue).
        var obs = box.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? box.AddDays(2) : box.AddDays(1);
        return d == box || d == obs;
    }

    // ---------- working days ----------

    /// <summary>Business-day HOPS from `from` up to `to` excluding weekends and Victorian
    /// holidays. Returns 0 when to is on or before from — the hero panel treats a deadline
    /// on today as 0 ("Due today"). The label belongs to the view; the label must say
    /// "working days (VIC)".</summary>
    public static int WorkingDaysBetween(DateOnly from, DateOnly to, Func<DateOnly, bool>? isHoliday = null)
    {
        if (to <= from) return 0;
        isHoliday ??= VictorianNoopHolidays;
        var count = 0;
        for (var d = from.AddDays(1); d <= to; d = d.AddDays(1))
            if (!DeadlineCalculator.IsWeekend(d) && !isHoliday(d)) count++;
        return count;
    }

    private static bool VictorianNoopHolidays(DateOnly d) => IsVictorianHoliday(d);

    // ---------- the featured deadline ----------

    /// <summary>Tone of a featured deadline relative to today (brief section 3a states).</summary>
    public enum Tone { Calm, Caution, Alert, Overdue }

    public record FeaturedState(int DaysRemaining, Tone State, string Word);

    /// <summary>days-remaining → state word. 0 = "Due today" (never "0 days").</summary>
    public static FeaturedState FeaturedTone(DateOnly deadline, DateOnly today)
    {
        var days = deadline.DayNumber - today.DayNumber;
        return days switch
        {
            < 0 => new(days, Tone.Overdue, $"Overdue by {Math.Abs(days)} days"),
            0 => new(0, Tone.Alert, "Due today"),
            <= 30 => new(days, Tone.Alert, "Due soon"),
            <= 90 => new(days, Tone.Caution, $"Due in {days} days"),
            _ => new(days, Tone.Calm, $"Due in {days} days"),
        };
    }

    /// <summary>Picks the deadline the hero shows, per the brief's rule: the earliest eligible
    /// date on or after today; plan markers are ignored unless nothing else exists (then the
    /// pick carries PlanningOnly = true). Eligible = the PPA deadline and provider-confirmed
    /// bookings and stored program dates; plan markers = internal-planning derivatives and
    /// conservative-reading (PlanningOnly-flagged) rows and internal tasks.</summary>
    public static DeadlineInfo? PickFeatured(IReadOnlyList<DeadlineInfo> candidates, DateOnly today)
    {
        var eligible = candidates
            .Where(c => c.Date >= today)
            .Where(c => c.Kind != DeadlineKind.InternalTask)
            .Where(c => Source(c) != "internal planning")
            .Where(c => !c.PlanningOnly)
            .OrderBy(c => c.Date)
            .ToList();
        if (eligible.Count == 0)
        {
            // Fall back to planning-only rows (labelled as planning), never to a guessed date.
            var planning = candidates
                .Where(c => c.Date >= today)
                .Where(c => c.Kind != DeadlineKind.InternalTask)
                .Where(c => c.PlanningOnly)
                .OrderBy(c => c.Date).ToList();
            return planning.Count == 0 ? null : planning[0];
        }
        var confirmed = eligible.FirstOrDefault(c => c.Kind == DeadlineKind.ProviderConfirmed)
            ?? eligible.FirstOrDefault(c => c.Kind == DeadlineKind.ProgramCalculated && c.Date >= today)
            ?? eligible[0];
        return confirmed;
    }

    /// <summary>The next deadline after the featured one ("After this:" line), same
    /// eligibility, strictly later date. Null when none is recorded.</summary>
    public static DeadlineInfo? NextAfterFeatured(IReadOnlyList<DeadlineInfo> candidates, DeadlineInfo featured)
        => candidates.Where(c => c.Date > featured.Date)
            .Where(c => c.Kind != DeadlineKind.InternalTask)
            .Where(c => Source(c) != "internal planning")
            .OrderBy(c => c.Date)
            .FirstOrDefault()
            is { } next && next.Date > featured.Date ? next : null;

    private static string Source(DeadlineInfo c) => c.Source;

    // ---------- the 12-month obligations ledger ----------

    public static string CategoryKey(string raw) => raw switch
    {
        "Equipment" => "equipment",
        "People" => "people",
        "Evidence" => "documents",
        "Program" => "program",
        "Reference" => "reference",
        _ => "program",
    };

    /// <summary>Bucket assignment for the renewals ledger: 0 = next 30 days (overdue items
    /// sort to its top), 1 = 31 to 90 days, 2 = later this cycle. Undated rows (date null)
    /// render under "Later this cycle" as "Not set". Boundaries are inclusive on the day:</summary>
    public static int Bucket(DateOnly? due, DateOnly today) => due switch
    {
        null => 2,
        { } d when d < today => 0,        // overdue → top of "Next 30 days" in alert tone
        { } d when d.DayNumber - today.DayNumber <= 30 => 0,
        { } d when d.DayNumber - today.DayNumber <= 90 => 1,
        _ => 2,
    };

    /// <summary>The year-ahead strip window: twelve months starting with the current month.</summary>
    public static List<(int Year, int Month)> YearAheadMonths(DateOnly today)
    {
        var list = new List<(int Year, int Month)>();
        var d = new DateOnly(today.Year, today.Month, 1);
        for (var i = 0; i < 12; i++, d = d.AddMonths(1)) list.Add((d.Year, d.Month));
        return list;
    }
}