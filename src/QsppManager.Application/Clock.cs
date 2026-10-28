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
/// checks (M4 · DD-24). FIX-11 step 3: the single source is TodayPlanning.IsVictorianHoliday
/// (Domain); the hard-coded 2025-2028 table that used to live here is deleted. The two
/// disagreed on 17 day-slots in 2026-2028 (see FIX-11 in the execution log); the computed
/// rule now wins everywhere. Before relying on a deadline, check the dates are current:
/// the rule covers 2025-2035 and Grand Final Friday is formula-derived for years the AFL
/// has not published yet.</summary>
public static class VicHolidays
{
    /// <summary>Delegates to the computed Victorian holiday rule (FIX-11: one table). The
    /// hard-coded dates this used to read are gone.</summary>
    public static bool IsHoliday(DateOnly d) => TodayPlanning.IsVictorianHoliday(d);

    /// <summary>Weekend + VIC holiday predicate for DeadlineCalculator.</summary>
    public static bool IsNonBusinessDay(DateOnly d) => DeadlineCalculator.IsWeekend(d) || IsHoliday(d);
}