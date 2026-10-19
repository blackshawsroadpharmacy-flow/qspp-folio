using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using Xunit;

namespace QsppManager.Domain.Tests;

public class RecurrenceCalculatorTests
{
    private static RecurringTaskDef Def(RecurrenceCadence cadence, DateOnly anchor, int customDays = 0) =>
        new() { Title = "t", Cadence = cadence, AnchorDate = anchor, CustomDays = customDays };

    [Fact]
    public void Monthly_Sequence_ClampsIntoShorterMonths_ThenReturnsToAnchorDay()
    {
        // Anchor 31 Jan: Feb has 28 days (2025) → 28 Feb; March → 31 Mar (anchor day restored).
        var def = Def(RecurrenceCadence.Monthly, new DateOnly(2025, 1, 31));
        var occ = RecurrenceCalculator.Occurrences(def, new DateOnly(2025, 3, 31));
        Assert.Equal(new DateOnly(2025, 1, 31), occ[0]);
        Assert.Equal(new DateOnly(2025, 2, 28), occ[1]);
        Assert.Equal(new DateOnly(2025, 3, 31), occ[2]);
    }

    [Fact]
    public void Monthly_LeapYear_Feb29_Then_Mar31()
    {
        var def = Def(RecurrenceCadence.Monthly, new DateOnly(2024, 1, 31));
        var occ = RecurrenceCalculator.Occurrences(def, new DateOnly(2024, 3, 31));
        Assert.Equal(new DateOnly(2024, 2, 29), occ[1]);
        Assert.Equal(new DateOnly(2024, 3, 31), occ[2]);
    }

    [Fact]
    public void Quarterly_Generates_EveryThirdMonth_FromAnchor()
    {
        var def = Def(RecurrenceCadence.Quarterly, new DateOnly(2025, 2, 15));
        var occ = RecurrenceCalculator.Occurrences(def, new DateOnly(2025, 11, 30));
        Assert.Equal(4, occ.Count); // Feb, May, Aug, Nov
        Assert.Equal(new DateOnly(2025, 2, 15), occ[0]);
        Assert.Equal(new DateOnly(2025, 5, 15), occ[1]);
        Assert.Equal(new DateOnly(2025, 8, 15), occ[2]);
        Assert.Equal(new DateOnly(2025, 11, 15), occ[3]);
    }

    [Fact]
    public void Weekly_And_CustomDays_AdvanceByDays()
    {
        var weekly = Def(RecurrenceCadence.Weekly, new DateOnly(2025, 3, 3));
        Assert.Equal(new DateOnly(2025, 3, 10), RecurrenceCalculator.NextAfter(weekly, weekly.AnchorDate));
        var custom = Def(RecurrenceCadence.CustomDays, new DateOnly(2025, 3, 3), 45);
        Assert.Equal(new DateOnly(2025, 4, 17), RecurrenceCalculator.NextAfter(custom, custom.AnchorDate));
        // CustomDays below 1 is guarded: NextAfter never produces a zero/negative step.
        var broken = Def(RecurrenceCadence.CustomDays, new DateOnly(2025, 3, 3), 0);
        Assert.Equal(new DateOnly(2025, 3, 4), RecurrenceCalculator.NextAfter(broken, broken.AnchorDate));
    }

    [Fact]
    public void TrainingRules_Initial_SixMonthDeadline_AndOverdueLabel()
    {
        var noSupply = new StaffMember { Name = "A" };
        Assert.Null(TrainingRules.InitialStatus(noSupply, new DateOnly(2026, 10, 20)).dueBy);
        Assert.Contains("No first-supply date recorded", TrainingRules.InitialStatus(noSupply, new DateOnly(2026, 10, 20)).status);

        var s = new StaffMember { Name = "B", S2S3FirstSupplyDate = new DateOnly(2026, 1, 10) };
        var (due, status) = TrainingRules.InitialStatus(s, new DateOnly(2026, 3, 1));
        Assert.Equal(new DateOnly(2026, 7, 12), due);
        Assert.Contains("Initial due by", status);

        var overdue = TrainingRules.InitialStatus(s, new DateOnly(2026, 7, 20));
        Assert.StartsWith("OVERDUE", overdue.status);

        var done = new StaffMember { Name = "C", S2S3FirstSupplyDate = new DateOnly(2026, 1, 10), S2S3InitialCompleted = new DateOnly(2026, 4, 1) };
        Assert.Contains("(in time)", TrainingRules.InitialStatus(done, new DateOnly(2026, 10, 20)).status);
    }

    [Fact]
    public void TrainingRules_Refresher_AnniversaryOfLastCompletion()
    {
        var s = new StaffMember { Name = "D", S2S3InitialCompleted = new DateOnly(2025, 6, 1) };
        var (due, status) = TrainingRules.RefresherStatus(s, new DateOnly(2026, 6, 5));
        Assert.Equal(new DateOnly(2026, 6, 1), due);
        Assert.StartsWith("OVERDUE", status);

        var withRefresher = new StaffMember { Name = "E", S2S3InitialCompleted = new DateOnly(2024, 6, 1), S2S3RefresherCompleted = new DateOnly(2025, 9, 10) };
        var (due2, status2) = TrainingRules.RefresherStatus(withRefresher, new DateOnly(2026, 10, 20));
        Assert.Equal(new DateOnly(2026, 9, 10), due2);
        Assert.Contains("Refresher completed 10 Sep 2025", status2);
        Assert.Contains("next due 10 Sep 2026", status2);
    }
}