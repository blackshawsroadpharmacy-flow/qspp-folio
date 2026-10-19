using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using Xunit;

namespace QsppManager.Domain.Tests;

public class DeadlineCalculatorTests
{
    private static readonly Func<DateOnly, bool> NoHolidays = _ => false;

    [Fact]
    public void AddBusinessDays_SkipsWeekend_Forward()
    {
        var friday = new DateOnly(2026, 10, 16);   // Friday
        Assert.Equal(new DateOnly(2026, 10, 19), DeadlineCalculator.AddBusinessDays(friday, 1, NoHolidays));
        Assert.Equal(new DateOnly(2026, 10, 20), DeadlineCalculator.AddBusinessDays(friday, 2, NoHolidays));
        Assert.Equal(new DateOnly(2026, 10, 30), DeadlineCalculator.AddBusinessDays(friday, 10, NoHolidays));
    }

    [Fact]
    public void AddBusinessDays_HonoursInjectedHolidays()
    {
        var date = new DateOnly(2026, 10, 16);
        var melbourneCup = new Func<DateOnly, bool>(d => d == new DateOnly(2026, 11, 3)); // Tuesday
        var start = new DateOnly(2026, 10, 30); // Friday
        // +1 → Mon 2 Nov; +2 → Tue 3 Nov is a holiday → Wed 4 Nov
        Assert.Equal(new DateOnly(2026, 11, 4), DeadlineCalculator.AddBusinessDays(start, 2, melbourneCup));
    }

    [Fact]
    public void AddBusinessDays_Backwards_ConservativeCancelCutoff()
    {
        // Onsite assessment Friday 6 Nov 2026 → conservative cutoff 10 business days back = Fri 23 Oct 2026
        var onsite = new DateOnly(2026, 11, 6);
        Assert.Equal(new DateOnly(2026, 10, 23), DeadlineCalculator.ConservativeCancelCutoff(onsite));
    }

    [Fact]
    public void AddCalendarMonths_ClampsEndOfMonth()
    {
        Assert.Equal(new DateOnly(2026, 2, 28), DeadlineCalculator.AddCalendarMonths(new DateOnly(2026, 1, 31), 1));
        Assert.Equal(new DateOnly(2028, 2, 29), DeadlineCalculator.AddCalendarMonths(new DateOnly(2027, 1, 31), 13));
        var start = new DateOnly(2026, 3, 31);
        Assert.Equal(new DateOnly(2026, 4, 30), DeadlineCalculator.AddCalendarMonths(start, 1));
    }

    [Fact]
    public void AddCalendarMonths_KeepsDayWhenPossible()
    {
        Assert.Equal(new DateOnly(2027, 1, 1), DeadlineCalculator.AddCalendarMonths(new DateOnly(2026, 1, 1), 12));
    }

    [Fact]
    public void MonthsBetween_Floors()
    {
        Assert.Equal(2, DeadlineCalculator.MonthsBetween(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 15)));
        Assert.Equal(1, DeadlineCalculator.MonthsBetween(new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 1)));
        Assert.Equal(0, DeadlineCalculator.MonthsBetween(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void ConservativeMidCycleWindow_Months15To21()
    {
        var (start, end) = DeadlineCalculator.ConservativeMidCycleWindow(new DateOnly(2026, 1, 1));
        Assert.Equal(new DateOnly(2027, 4, 1), start);
        Assert.Equal(new DateOnly(2027, 10, 1), end);
    }

    [Fact]
    public void ZeroDays_ReturnsSameDate()
    {
        var d = new DateOnly(2026, 10, 24);
        Assert.Equal(d, DeadlineCalculator.AddBusinessDays(d, 0));
        Assert.Equal(d, DeadlineCalculator.AddCalendarDays(d, 0));
    }

    [Fact]
    public void CancellationAndMidCycle_PlanningHelpers_FlagSourcesExplicitly()
    {
        // These helpers exist to keep the source-wording conflict visible in calculations.
        // ISSUE-CANCELWINDOW: Rule 10 (10 business days) vs Assessments Policy (10 calendar days).
        // ISSUE-MIDCYCLE: months 15-21 (Rule 8 + T&C definition) vs months 13-24 (Rule definitions) vs "approx 18 months" (hub).
        var cutoff = DeadlineCalculator.ConservativeCancelCutoff(new DateOnly(2026, 11, 6));
        var (w1, w2) = DeadlineCalculator.ConservativeMidCycleWindow(new DateOnly(2026, 1, 1));
        Assert.True(cutoff < new DateOnly(2026, 11, 6));
        Assert.True(w1 < w2);
    }
}

public class StateMachineTests
{
    [Theory]
    [InlineData(EvidenceReviewState.Draft, EvidenceReviewState.ReadyForReview, true)]
    [InlineData(EvidenceReviewState.Draft, EvidenceReviewState.ManagerVerified, false)]
    [InlineData(EvidenceReviewState.ReadyForReview, EvidenceReviewState.ManagerVerified, true)]
    [InlineData(EvidenceReviewState.ReadyForReview, EvidenceReviewState.NeedsRework, true)]
    [InlineData(EvidenceReviewState.NeedsRework, EvidenceReviewState.Draft, true)]
    [InlineData(EvidenceReviewState.ManagerVerified, EvidenceReviewState.Draft, false)]
    [InlineData(EvidenceReviewState.ManagerVerified, EvidenceReviewState.Retired, true)]
    [InlineData(EvidenceReviewState.Retired, EvidenceReviewState.Draft, false)]
    public void EvidenceTransitions_FollowTheDefinedMachine(EvidenceReviewState from, EvidenceReviewState to, bool expected)
        => Assert.Equal(expected, StateMachines.CanTransitionEvidence(from, to));

    [Fact]
    public void OnlyManagerOrAdmin_CanVerify()
    {
        Assert.True(StateMachines.MayVerifyEvidence(EvidenceReviewState.ManagerVerified, UserRole.ManagerReviewer));
        Assert.True(StateMachines.MayVerifyEvidence(EvidenceReviewState.ManagerVerified, UserRole.OwnerAdmin));
        Assert.False(StateMachines.MayVerifyEvidence(EvidenceReviewState.ManagerVerified, UserRole.Contributor));
        Assert.False(StateMachines.MayVerifyEvidence(EvidenceReviewState.ManagerVerified, UserRole.ReviewerReadOnly));
    }

    [Fact]
    public void Contributor_CannotSetManagerVerified_Readiness()
    {
        Assert.False(StateMachines.MayApplyReadiness(RequirementReadiness.ManagerVerified, UserRole.Contributor));
        Assert.True(StateMachines.MayApplyReadiness(RequirementReadiness.ManagerVerified, UserRole.OwnerAdmin));
        Assert.True(StateMachines.MayApplyReadiness(RequirementReadiness.InProgress, UserRole.Contributor));
    }

    [Theory]
    [InlineData(RequirementReadiness.NotStarted, RequirementReadiness.InProgress, true)]
    [InlineData(RequirementReadiness.NotStarted, RequirementReadiness.ManagerVerified, false)]
    [InlineData(RequirementReadiness.ReadyForReview, RequirementReadiness.ManagerVerified, true)]
    [InlineData(RequirementReadiness.ManagerVerified, RequirementReadiness.InProgress, true)]
    [InlineData(RequirementReadiness.AssessorAccepted, RequirementReadiness.InProgress, false)]
    public void ReadinessTransitions_FollowTheDefinedMachine(RequirementReadiness from, RequirementReadiness to, bool expected)
        => Assert.Equal(expected, StateMachines.CanTransitionReadiness(from, to));
}