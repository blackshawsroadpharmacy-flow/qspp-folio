using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;

namespace QsppManager.Application;

/// <summary>Injectable "today" (M4): production uses the local clock; tests pin a fixed
/// date. Everything in the workspace reads TODAY through this delegate — no service
/// hardcodes DateTime.Today except at the composition root.</summary>
public static class Clock
{
    public static Func<DateOnly> Today = () => DateOnly.FromDateTime(DateTime.Today);
    public static void Pin(DateOnly date) => Today = () => date;
    public static void Unpin() => Today = () => DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>Victorian public holidays used by business-day deadlines and recurring-task
/// checks (M4 · DD-24). Dates for 2026–2028 come from the Victorian Government's published
/// declarations; **verify current dates before relying on them for a deadline** — the
/// source table the app was built from may lag gazetted changes.</summary>
public static class VicHolidays
{
    public static readonly Dictionary<int, DateOnly[]> PublicHolidays = new()
    {
        [2025] = [ new(2025,1,1), new(2025,1,27), new(2025,3,10), new(2025,4,18), new(2025,4,21),
                   new(2025,4,25), new(2025,6,9), new(2025,9,26), new(2025,12,25), new(2025,12,26) ],
        [2026] = [ new(2026,1,1), new(2026,1,26), new(2026,3,9), new(2026,4,3), new(2026,4,6),
                   new(2026,4,25), new(2026,6,8), new(2026,9,25), new(2026,11,3), // Melbourne Cup day
                   new(2026,12,25), new(2026,12,28) ], // Boxing Day 26 Dec falls Saturday → observed Monday
        [2027] = [ new(2027,1,1), new(2027,1,26), new(2027,3,8), new(2027,3,26), new(2027,4,27),
                   new(2027,6,14), new(2027,9,24), new(2027,11,2), new(2027,12,27), new(2027,12,28) ],
        [2028] = [ new(2028,1,1), new(2028,1,26), new(2028,3,13), new(2028,4,14), new(2028,4,17),
                   new(2028,4,25), new(2028,6,12), new(2028,9,22), new(2028,11,7), new(2028,12,25), new(2028,12,26) ],
    };

    public static bool IsHoliday(DateOnly d) =>
        PublicHolidays.TryGetValue(d.Year, out var list) && list.Contains(d);

    /// <summary>Weekend + VIC holiday predicate for DeadlineCalculator.</summary>
    public static bool IsNonBusinessDay(DateOnly d) => DeadlineCalculator.IsWeekend(d) || IsHoliday(d);
}