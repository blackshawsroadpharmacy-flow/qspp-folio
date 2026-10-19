using QsppManager.Domain.Entities;

namespace QsppManager.Domain.Services;

/// <summary>Calendar/deadline primitives. Business days are Sat/Sun weekends plus an
/// injected holiday predicate (jurisdiction holidays have provenance and are supplied
/// by the caller — NodaTime does not carry regulatory calendars).</summary>
public static class DeadlineCalculator
{
    public static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>Adds N business days, skipping weekends + supplied holidays. days==0 returns start.</summary>
    public static DateOnly AddBusinessDays(DateOnly start, int days, Func<DateOnly, bool>? isHoliday = null)
    {
        if (days == 0) return start;
        isHoliday ??= _ => false;
        var step = days > 0 ? 1 : -1;
        var remaining = Math.Abs(days);
        var d = start;
        while (remaining > 0)
        {
            d = d.AddDays(step);
            if (IsWeekend(d) || isHoliday(d)) continue;
            remaining--;
        }
        return d;
    }

    /// <summary>Adds calendar days. days==0 returns start. Inclusive start, result = start + days.</summary>
    public static DateOnly AddCalendarDays(DateOnly start, int days) => start.AddDays(days);

    /// <summary>Adds calendar months with end-of-month clamping (31 Jan + 1 month = 28/29 Feb).</summary>
    public static DateOnly AddCalendarMonths(DateOnly start, int months)
    {
        if (months == 0) return start;
        var total = (start.Year * 12 + (start.Month - 1)) + months;
        var y = Math.DivRem(total, 12, out var m0);
        var month = m0 + 1;
        var day = start.Day;
        var dim = DateTime.DaysInMonth(y, month);
        return new DateOnly(y, month, Math.Min(day, dim));
    }

    /// <summary>Elapsed whole months between two dates (a → b), floor.</summary>
    public static int MonthsBetween(DateOnly a, DateOnly b)
    {
        var months = (b.Year - a.Year) * 12 + (b.Month - a.Month);
        if (b.Day < a.Day) months--;
        return months;
    }

    /// <summary>Conservative mid-cycle window under issue ISSUE-MIDCYCLE: months 15-21 of a
    /// 36-month cycle per Program Rule 8 and T&C definitions (hub wording is approximate).
    /// Returns the earliest sensible scheduling date (month 15) — PLANNING ONLY while the
    /// interpretation issue is unresolved.</summary>
    public static (DateOnly windowStart, DateOnly windowEnd) ConservativeMidCycleWindow(DateOnly cycleStart)
        => (AddCalendarMonths(cycleStart, 15), AddCalendarMonths(cycleStart, 21));

    /// <summary>Conservative cancellation-cutoff guardrail under ISSUE-CANCELWINDOW:
    /// the safest reading is 10 BUSINESS days (Program Rule 10); the Assessments Policy and
    /// Fees page say 10 calendar days. Returns the last date you may change/cancel without
    /// assuming a fee, using the conservative rule while unresolved.</summary>
    public static DateOnly ConservativeCancelCutoff(DateOnly assessmentDate)
        => AddBusinessDays(assessmentDate, -10);
}

/// <summary>Recurring-task occurrence maths (M3). Anchor-based sequences: month/quarter/year
/// anchors keep their anchor day and clamp into shorter months (31 Jan monthly → 28/29 Feb → 31 Mar).
/// Missed occurrences simply stay in the list as open tasks with past due dates.</summary>
public static class RecurrenceCalculator
{
    /// <summary>All occurrence dates from the anchor up to and including horizon (bounded).</summary>
    public static List<DateOnly> Occurrences(RecurringTaskDef def, DateOnly horizon, int max = 200)
    {
        var list = new List<DateOnly>();
        var d = def.AnchorDate;
        while (d <= horizon && list.Count < max)
        {
            list.Add(d);
            d = NextAfter(def, d);
        }
        return list;
    }

    public static DateOnly NextAfter(RecurringTaskDef def, DateOnly current) => def.Cadence switch
    {
        RecurrenceCadence.Weekly => current.AddDays(7),
        RecurrenceCadence.Fortnightly => current.AddDays(14),
        RecurrenceCadence.CustomDays => current.AddDays(Math.Max(1, def.CustomDays)),
        RecurrenceCadence.Monthly => NextAnchorAfter(def.AnchorDate, current, 1),
        RecurrenceCadence.Quarterly => NextAnchorAfter(def.AnchorDate, current, 3),
        RecurrenceCadence.PerAnnum => NextAnchorAfter(def.AnchorDate, current, 12),
        _ => current.AddDays(Math.Max(1, def.CustomDays)),
    };

    /// <summary>Smallest anchor+k·step (clamped to month end) strictly after current.</summary>
    public static DateOnly NextAnchorAfter(DateOnly anchor, DateOnly current, int stepMonths)
    {
        for (var k = 1; k <= 240; k++)
        {
            var cand = DeadlineCalculator.AddCalendarMonths(anchor, k * stepMonths);
            if (cand > current) return cand;
        }
        return current;
    }
}

/// <summary>S2/S3 training status rules (training fact sheet; refresher approved list 6 Oct 2026):
/// initial within 6 months of first supply; refresher due on the anniversary of the LAST
/// completed training (initial or refresher); 3-hour annual minimum.</summary>
public static class TrainingRules
{
    public const string InitialNotNeeded = "No first-supply date recorded — record one when staff begin Schedule 2/3 supply.";

    public static (DateOnly? dueBy, string status) InitialStatus(StaffMember s, DateOnly today)
    {
        if (s.S2S3FirstSupplyDate is null) return (null, InitialNotNeeded);
        var dueBy = s.S2S3FirstSupplyDate.Value.AddDays(183); // ~6 months (fact sheet: "within six months")
        if (s.S2S3InitialCompleted is { } done) return (dueBy, done <= dueBy ? $"Initial completed {done:dd MMM yyyy} (in time)" : $"Initial completed {done:dd MMM yyyy} (AFTER the six-month deadline — check with the manager)");
        return (dueBy, today > dueBy ? $"OVERDUE — initial was due by {dueBy:dd MMM yyyy}" : $"Initial due by {dueBy:dd MMM yyyy}");
    }

    public static (DateOnly? due, string status) RefresherStatus(StaffMember s, DateOnly today)
    {
        if (s.S2S3InitialCompleted is null && s.S2S3RefresherCompleted is null)
            return (null, "Awaiting initial completion.");
        var last = new[] { s.S2S3InitialCompleted, s.S2S3RefresherCompleted }.Max();
        var due = last!.Value.AddDays(365);
        if (s.S2S3RefresherCompleted is { } r && r >= last.Value.AddDays(-31)) // completed on time
            return (due, r >= today ? $"Refresher completed {r:dd MMM yyyy} — next not expected until {due:dd MMM yyyy}" : $"Refresher completed {r:dd MMM yyyy}; next due {due:dd MMM yyyy}");
        return (due, today > due ? $"OVERDUE — refresher was due {due:dd MMM yyyy}" : $"Refresher due {due:dd MMM yyyy}");
    }
}

public enum DeadlineKind
{
InternalTask = 0,
ProgramCalculated = 1,
ProviderConfirmed = 2,
}

/// <summary>A deadline with provenance. ProgramCalculated dates carry an explanation and
/// cite source; PlanningOnly flags the conservative reading of an unresolved issue.</summary>
public record DeadlineInfo(
    DeadlineKind Kind,
    DateOnly Date,
    string Source,
    string Explanation,
    bool PlanningOnly,
    string? InterpretationIssueKey)
{
    public static DeadlineInfo Internal(DateOnly d, string what)
        => new(DeadlineKind.InternalTask, d, "internal", what, PlanningOnly: false, InterpretationIssueKey: null);

    public static DeadlineInfo Calculated(DateOnly d, string source, string explanation, bool planningOnly = false, string? issueKey = null)
        => new(DeadlineKind.ProgramCalculated, d, source, explanation, planningOnly, issueKey);

    public static DeadlineInfo Confirmed(DateOnly d, string source)
        => new(DeadlineKind.ProviderConfirmed, d, source, "Date confirmed by the assessment provider/program.", PlanningOnly: false, InterpretationIssueKey: null);
}

/// <summary>State-machine transition rules. All transitions validated in ONE place.</summary>
public static class StateMachines
{
    private static readonly Dictionary<EvidenceReviewState, EvidenceReviewState[]> EvidenceAllowed = new()
    {
        [EvidenceReviewState.Draft] = [EvidenceReviewState.ReadyForReview],
        [EvidenceReviewState.ReadyForReview] = [EvidenceReviewState.ManagerVerified, EvidenceReviewState.NeedsRework],
        [EvidenceReviewState.NeedsRework] = [EvidenceReviewState.Draft],
        [EvidenceReviewState.ManagerVerified] = [EvidenceReviewState.Retired],
        [EvidenceReviewState.Retired] = [],
    };

    public static bool CanTransitionEvidence(EvidenceReviewState from, EvidenceReviewState to)
        => EvidenceAllowed.TryGetValue(from, out var allowed) && allowed.Contains(to);

    private static readonly Dictionary<RequirementReadiness, RequirementReadiness[]> ReadinessAllowed = new()
    {
        [RequirementReadiness.NotStarted] = [RequirementReadiness.InProgress],
        [RequirementReadiness.InProgress] = [RequirementReadiness.ReadyForReview, RequirementReadiness.NotStarted],
        [RequirementReadiness.ReadyForReview] = [RequirementReadiness.ManagerVerified, RequirementReadiness.InProgress],
        [RequirementReadiness.ManagerVerified] = [RequirementReadiness.AssessorAccepted, RequirementReadiness.InProgress],
        [RequirementReadiness.AssessorAccepted] = [],
    };

    public static bool CanTransitionReadiness(RequirementReadiness from, RequirementReadiness to)
        => ReadinessAllowed.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Who may APPLY the given readiness transition (target state permission).</summary>
    public static bool MayApplyReadiness(RequirementReadiness target, UserRole actor)
        => target switch
        {
            RequirementReadiness.NotStarted or RequirementReadiness.InProgress
                => actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer or UserRole.Contributor,
            RequirementReadiness.ReadyForReview
                => actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer or UserRole.Contributor,
            RequirementReadiness.ManagerVerified
                => actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer,
            RequirementReadiness.AssessorAccepted
                => actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer,
            _ => false,
        };

    public static bool MayVerifyEvidence(EvidenceReviewState target, UserRole actor)
        => target == EvidenceReviewState.ManagerVerified
            ? actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer
            : actor is not UserRole.ReviewerReadOnly;

    public static bool MayConfirmAssessorAcceptance(UserRole actor)
        => actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer;
}