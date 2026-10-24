using QsppManager.Domain.Services;

namespace QsppManager.Infrastructure.Services;

/// <summary>The --selftest mechanic (translated 1:1 from the sibling's CLI check): a
/// deterministic pass/fail sweep over the pure planning maths that produce the Today screen.
/// Exits 0 when everything passes; prints one PASS/FAIL line per check. Covered indirectly
/// by TodaySelftestTests so the sweep cannot silently rot.</summary>
public static class TodaySelftest
{
    public static int Run(TextWriter w)
    {
        var failures = 0;
        void Check(string name, bool ok, string detail = "")
        {
            w.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : "  " + detail)}");
            if (!ok) failures++;
        }

        // Melbourne rolls over at LOCAL midnight, not UTC midnight: 2026-10-23 13:00Z is
        // 2026-10-24 00:00 AEDT (UTC+11).
        var mel = TodayPlanning.MelbourneToday(new DateTime(2026, 10, 23, 13, 0, 0, DateTimeKind.Utc));
        Check("Melbourne midnight rollover", mel == new DateOnly(2026, 10, 24), $"got {mel}");

        var today = new DateOnly(2026, 10, 24);
        var d = (int days) => today.AddDays(days);

        // Tone boundaries (never a bare zero).
        var t91 = TodayPlanning.FeaturedTone(d(91), today);
        var t90 = TodayPlanning.FeaturedTone(d(90), today);
        var t31 = TodayPlanning.FeaturedTone(d(31), today);
        var t30 = TodayPlanning.FeaturedTone(d(30), today);
        var t1 = TodayPlanning.FeaturedTone(d(1), today);
        var t0 = TodayPlanning.FeaturedTone(today, today);
        var tMinus = TodayPlanning.FeaturedTone(d(-3), today);
        Check("91 days reads calm", t91.State == TodayPlanning.Tone.Calm && t91.DaysRemaining == 91);
        Check("90 days reads caution", t90.State == TodayPlanning.Tone.Caution, t90.State.ToString());
        Check("31 days reads caution", t31.State == TodayPlanning.Tone.Caution);
        Check("30 days reads alert", t30.State == TodayPlanning.Tone.Alert);
        Check("1 day reads alert", t1.State == TodayPlanning.Tone.Alert);
        Check("0 days reads 'Due today', never '0 days'", t0.State == TodayPlanning.Tone.Alert && t0.Word == "Due today");
        Check("past reads overdue with |N| days", tMinus.State == TodayPlanning.Tone.Overdue && tMinus.Word.Contains("3"));

        // Buckets — inclusive at 30/31 and 90/91; nothing due → "Later".
        Check("bucket overdue lands in the 30-day group", TodayPlanning.Bucket(d(-2), today) == 0);
        Check("bucket today lands in the 30-day group", TodayPlanning.Bucket(today, today) == 0);
        Check("bucket 30 is inclusive", TodayPlanning.Bucket(d(30), today) == 0);
        Check("bucket 31 moves to the 31-90 group", TodayPlanning.Bucket(d(31), today) == 1);
        Check("bucket 90 is inclusive", TodayPlanning.Bucket(d(90), today) == 1);
        Check("bucket 91 moves to later", TodayPlanning.Bucket(d(91), today) == 2);
        Check("no date reads later", TodayPlanning.Bucket(null, today) == 2);

        // Working days exclude weekends and Melbourne Cup Day (first Tuesday of November).
        var wd = TodayPlanning.WorkingDaysBetween(new(2026, 10, 24), new(2026, 11, 10));
        var cup = new DateOnly(2026, 11, 3);
        Check("Melbourne Cup 2026 is the first Tuesday of November", cup.DayOfWeek == DayOfWeek.Tuesday && cup.Day <= 7);
        Check("working-day count excludes Nov weekends and Cup Day", wd == 11, $"got {wd}");

        // The year-ahead window is the current month plus eleven.
        var months = TodayPlanning.YearAheadMonths(today);
        Check("year ahead is 12 months from the current one",
            months.Count == 12 && months[0] == (2026, 10) && months[11] == (2027, 9));

        if (failures == 0) w.WriteLine("Today selftest: all checks pass.");
        else w.WriteLine($"Today selftest: {failures} check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }
}