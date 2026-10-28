using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-17 step 6: the Victorian holiday rule gets its own file — it does not belong
/// inside the assessor pack tests (F-28's misplaced-test finding). The single source under
/// test is TodayPlanning.IsVictorianHoliday via the VicHolidays delegate.</summary>
public class HolidayRuleTests
{
    [Fact]
    public void BusinessDaysSkip_GazettedDays_WithProvenance()
    {
        // Christmas Fri 25 Dec 2026 + Boxing day observed Mon 28 Dec → Friday 24 Dec + 1 business day = Tue 29 Dec
        Assert.Equal(new DateOnly(2026, 12, 29),
            DeadlineCalculator.AddBusinessDays(new DateOnly(2026, 12, 24), 1, VicHolidays.IsNonBusinessDay));
        // Melbourne Cup day (Tue 3 Nov 2026) is a VIC public holiday
        Assert.True(VicHolidays.IsHoliday(new DateOnly(2026, 11, 3)));
        Assert.False(VicHolidays.IsHoliday(new DateOnly(2026, 11, 4)));
    }
}