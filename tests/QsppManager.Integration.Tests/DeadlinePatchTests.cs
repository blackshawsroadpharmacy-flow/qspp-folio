using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Application;   // DeadlineInfo via Application
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-11: no invented dates. The 14-day internal checkpoint is gone; business-day
/// maths uses one Victorian holiday rule (TodayPlanning).</summary>
public class DeadlinePatchTests : TestBase
{
    [Fact]
    public void Requirement_Deadlines_IsEmpty_NoInventedCheckpoint()
    {
        var row = Workspace.RequirementRows(Owner).First();
        Workspace.SetReadiness(row.Id, RequirementReadiness.InProgress, Owner);
        var detail = Workspace.RequirementDetail(row.Id, Owner);
        Assert.NotNull(detail);
        Assert.Empty(detail!.Deadlines);
    }

    [Fact]
    public void HolidaySources_Agree_OnTheRule()
    {
        // the deleted table delegated to the computed rule; Melbourne Cup still reads as a holiday
        Assert.True(VicHolidays.IsHoliday(new DateOnly(2026, 11, 3)));
        Assert.False(VicHolidays.IsHoliday(new DateOnly(2026, 11, 4)));
        Assert.True(VicHolidays.IsNonBusinessDay(new DateOnly(2026, 11, 3)));
    }
}