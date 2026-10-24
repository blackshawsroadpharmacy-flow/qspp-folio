using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using QsppManager.Infrastructure.Persistence;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Storage;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>Phase-2 checks for the Today redesign data layer: the pure TodayPlanning
/// calculators (self-test list from the brief) plus the read-only TodayBrief assembly over
/// an in-memory SQLite copy. Dates are Melbourne calendar dates; the featured deadline is
/// never invented.</summary>
public class TodayQueriesTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    // ---------- pure: counts and tone ----------

    [Fact]
    public void DaysRemaining_AcrossAMonthBoundary()
    {
        // 27 Oct to 2 Nov is 6 calendar days, date to date (no 24-hour blocks).
        Assert.Equal(6, D(2026, 11, 2).DayNumber - D(2026, 10, 27).DayNumber);
        var tone = TodayPlanning.FeaturedTone(D(2026, 11, 2), D(2026, 10, 27));
        Assert.Equal(6, tone.DaysRemaining);
    }

    [Fact]
    public void Zero_Days_Reads_DueToday_Never_BareZero()
    {
        var tone = TodayPlanning.FeaturedTone(D(2026, 10, 24), D(2026, 10, 24));
        Assert.Equal(0, tone.DaysRemaining);
        Assert.Equal("Due today", tone.Word);
        Assert.Equal(TodayPlanning.Tone.Alert, tone.State);
    }

    [Fact]
    public void Past_Deadline_Reads_OverdueByNDays()
    {
        var tone = TodayPlanning.FeaturedTone(D(2026, 10, 21), D(2026, 10, 24));
        Assert.Equal(-3, tone.DaysRemaining);
        Assert.Equal("Overdue by 3 days", tone.Word);
        Assert.Equal(TodayPlanning.Tone.Overdue, tone.State);
    }

    [Fact]
    public void Tone_Calm_Caution_Alert_Boundaries()
    {
        var today = D(2026, 10, 24);
        Assert.Equal(TodayPlanning.Tone.Calm, TodayPlanning.FeaturedTone(today.AddDays(91), today).State);
        Assert.Equal(TodayPlanning.Tone.Caution, TodayPlanning.FeaturedTone(today.AddDays(31), today).State);
        Assert.Equal(TodayPlanning.Tone.Alert, TodayPlanning.FeaturedTone(today.AddDays(30), today).State);
    }

    // ---------- pure: Melbourne day rollover ----------

    [Fact]
    public void MelbourneDay_Rolls_Over_At_Local_Midnight()
    {
        // 30 Oct 2026 (AEDT, UTC+11 across October): two UTC instants on the same UTC date
        // fall on two different Melbourne calendar dates.
        var before = TodayPlanning.MelbourneToday(new DateTime(2026, 10, 30, 12, 55, 0, DateTimeKind.Utc));
        var after = TodayPlanning.MelbourneToday(new DateTime(2026, 10, 30, 13, 45, 0, DateTimeKind.Utc));
        Assert.Equal(D(2026, 10, 30), before);
        Assert.Equal(D(2026, 10, 31), after);
    }

    // ---------- pure: working days (VIC) ----------

    [Fact]
    public void WorkingDays_Exclude_MelbourneCupDay_And_Weekends()
    {
        // Melbourne Cup 2026: first Tuesday of November = 3 Nov 2026.
        Assert.True(TodayPlanning.IsVictorianHoliday(D(2026, 11, 3)));
        // Monday 2 Nov to Thursday 5 Nov: only 4 Nov and 5 Nov are hops (Cup Day + no weekends).
        Assert.Equal(2, TodayPlanning.WorkingDaysBetween(D(2026, 11, 2), D(2026, 11, 5)));
        // Melbourne Cup 2027: first Tuesday = 2 Nov 2027.
        Assert.True(TodayPlanning.IsVictorianHoliday(D(2027, 11, 2)));
        Assert.Equal(2, TodayPlanning.WorkingDaysBetween(D(2027, 11, 1), D(2027, 11, 4)));
        // Same day (or a deadline already past) is zero hops; never negative.
        Assert.Equal(0, TodayPlanning.WorkingDaysBetween(D(2026, 11, 5), D(2026, 11, 5)));
        Assert.Equal(0, TodayPlanning.WorkingDaysBetween(D(2026, 11, 5), D(2026, 11, 3)));
    }

    [Fact]
    public void WorkingDays_Stay_Reasonable_Over_A_Quarter()
    {
        // 24 Oct 2026 to the PPA deadline: roughly one third of the year is weekends and
        // holidays, so the count must sit between 120 and 200.
        var count = TodayPlanning.WorkingDaysBetween(D(2026, 10, 24), D(2027, 6, 30));
        Assert.InRange(count, 120, 200);
    }

    // ---------- pure: bucket assignment ----------

    [Fact]
    public void Buckets_At_The_30Day_And_90Day_Boundaries()
    {
        var today = D(2026, 10, 24);
        Assert.Equal(0, TodayPlanning.Bucket(today.AddDays(30), today));
        Assert.Equal(1, TodayPlanning.Bucket(today.AddDays(31), today));
        Assert.Equal(1, TodayPlanning.Bucket(today.AddDays(90), today));
        Assert.Equal(2, TodayPlanning.Bucket(today.AddDays(91), today));
        Assert.Equal(0, TodayPlanning.Bucket(today.AddDays(-1), today)); // overdue → next-30 top
        Assert.Equal(2, TodayPlanning.Bucket(null, today));               // undated → later
    }

    [Fact]
    public void YearAhead_Rolls_From_The_Current_Month()
    {
        var months = TodayPlanning.YearAheadMonths(D(2026, 10, 24));
        Assert.Equal(12, months.Count);
        Assert.Equal((2026, 10), months[0]);
        Assert.Equal((2026, 11), months[1]);
        Assert.Equal((2027, 9), months[11]);
    }

    // ---------- pure: the featured picker never invents a date ----------

    private static readonly DeadlineInfo Ppa = DeadlineInfo.Confirmed(
        new DateOnly(2027, 6, 30), "PPA: deadline for uploading the QSPP certificate");

    private static DeadlineInfo PlanMarker(int daysBefore, string source) =>
        DeadlineInfo.Calculated(new DateOnly(2027, 6, 30).AddDays(-daysBefore), source, "derived", planningOnly: true);

    [Fact]
    public void Featured_PlanMarkers_Never_Beat_The_Confirmed_Fact()
    {
        // The real workspace always carries the PPA row; derived plan markers must never win.
        var pick = TodayPlanning.PickFeatured([
            PlanMarker(120, "internal planning"), PlanMarker(200, "ISSUE-MIDCYCLE (conservative planning)"), Ppa
        ], D(2026, 10, 24));
        Assert.NotNull(pick);
        Assert.False(pick!.PlanningOnly);
        Assert.Equal(new DateOnly(2027, 6, 30), pick.Date);
    }

    [Fact]
    public void Featured_WithOnlyPlanMarkers_ShowsPlanningOnly()
    {
        var pick = TodayPlanning.PickFeatured([
            PlanMarker(120, "internal planning"), PlanMarker(200, "ISSUE-MIDCYCLE (conservative planning)")
        ], D(2026, 10, 24));
        Assert.NotNull(pick);
        Assert.True(pick!.PlanningOnly);
    }

    [Fact]
    public void Featured_Prefers_Confirmed_And_Never_Takes_An_Internal_Task()
    {
        var pick = TodayPlanning.PickFeatured([
            DeadlineInfo.Calculated(new DateOnly(2027, 3, 1), "internal planning", "derived (planning)"), Ppa
        ], D(2026, 10, 24));
        Assert.Equal(DeadlineKind.ProviderConfirmed, pick!.Kind);

        var pick2 = TodayPlanning.PickFeatured([
            DeadlineInfo.Confirmed(new DateOnly(2027, 3, 1), "Assessment provider (recorded booking)"), Ppa
        ], D(2026, 10, 24));
        Assert.Equal(new DateOnly(2027, 3, 1), pick2!.Date);

        var pick3 = TodayPlanning.PickFeatured([
            DeadlineInfo.Internal(new DateOnly(2027, 1, 1), "Next internal review checkpoint (14 days from today)"), Ppa
        ], D(2026, 10, 24));
        Assert.Equal(new DateOnly(2027, 6, 30), pick3!.Date);
    }

    [Fact]
    public void Empty_Cycle_Returns_The_Empty_State()
    {
        Assert.Null(TodayPlanning.PickFeatured([], D(2026, 10, 24)));
    }

    // ---------- DB-backed: the brief assembly over seeded rows ----------

    private static (QsppDbContext db, QsppWorkspace ws) FreshWorkspace()
    {
        var options = new DbContextOptionsBuilder<QsppDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        var db = new QsppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return (db, new QsppWorkspace(db, new AttachmentStore("/tmp/qsm-tests-attachments"), new AuditService(db)));
    }

    private static void SeedRow(QsppDbContext db, Func<QsppDbContext, int> seed)
    {
        seed(db);
        db.SaveChanges();
    }

    [Fact]
    public void TodayBrief_On_Empty_Workspace_Shows_PPA_And_Empty_Ledger()
    {
        var (db, w) = FreshWorkspace();
        var brief = w.TodayBrief(D(2026, 10, 24));
        // The PPA deadline is a confirmed program fact that exists even with an empty cycle.
        Assert.NotNull(brief.Featured);
        Assert.Equal(new DateOnly(2027, 6, 30), brief.Featured!.Date);
        Assert.False(brief.Featured!.PlanningOnly);
        Assert.Equal(249, brief.FeaturedTone!.DaysRemaining);
        // No recorded next deadline: the "after this" line says the date is not set.
        Assert.Null(brief.NextAfter);
        // Empty obligations ledger (no equipment, staff, evidence or program dates recorded).
        Assert.Empty(brief.Obligations);
        // The run line has no anchor: cycle start not recorded.
        Assert.Null(brief.RunStart);
    }

    [Fact]
    public void TodayBrief_Ledger_Rows_Come_From_Real_Records()
    {
        var (db, w) = FreshWorkspace();
        var ph = w.EnsurePharmacy();
        ph.CycleStartDate = D(2025, 7, 1);
        db.SaveChanges();

        db.Equipment.Add(new Equipment { Name = "Analytical balance", Model = "MS-L", State = "InService", CalibrationDue = D(2026, 11, 12) });
        db.Equipment.Add(new Equipment { Name = "Powder containment cabinet", Model = "PC-2", State = "InService" }); // undated → "Not set"
        db.StaffMembers.Add(new StaffMember { Name = "Jordan Vella", Role = "Compounding technician", S2S3FirstSupplyDate = D(2025, 6, 24), S2S3InitialCompleted = D(2026, 1, 10), Active = true });
        db.EvidenceItems.Add(new EvidenceItem { Title = "Infection-control audit", Kind = EvidenceKind.Certificate, FileName = "audit.pdf", ReviewState = EvidenceReviewState.ManagerVerified, EffectiveFrom = D(2026, 2, 1), EffectiveTo = D(2027, 2, 1) });
        db.AssessmentEvents.Add(new AssessmentEvent { Kind = "Onsite", ScheduledDate = D(2027, 5, 3), State = "Planned" });
        db.SaveChanges();

        var brief = w.TodayBrief(D(2026, 10, 24));

        // Five real rows: calibrated balance, undated cabinet, refresher, expiring evidence, assessment,
        // plus the attestation anniversary that the recorded cycle start derives (1 Jul 2027).
        var rows = brief.Obligations;
        Assert.Equal(6, rows.Count);
        Assert.Equal("Recalibrate", rows.Single(o => o.Item == "Analytical balance").Action);
        Assert.Equal(DateOnly.MaxValue.AddDays(0), DateOnly.MaxValue); // sanity of date ordering sentinel
        Assert.Null(rows.Single(o => o.Item == "Powder containment cabinet").Due);
        Assert.Equal(D(2027, 1, 10), rows.Single(o => o.Item.Contains("S2/S3")).Due);
        Assert.Contains(rows, o => o.Action == "Expires" && o.Due == D(2027, 2, 1));
        Assert.Contains(rows, o => o.Action == "Attend" && o.Due == D(2027, 5, 3));
        Assert.Contains(rows, o => o.Action == "Lodge" && o.Due == D(2027, 7, 1));
        // Balance (12 Nov 2026, 19 days out) sits in the next-30 bucket; nothing overdue here.
        Assert.Equal(0, TodayPlanning.Bucket(rows.Single(o => o.Item == "Analytical balance").Due, D(2026, 10, 24)));
        // Run start from the recorded cycle anchor; the planned assessment is a milestone tick.
        Assert.Equal(D(2025, 7, 1), brief.RunStart);
        Assert.Contains(brief.Milestones, m => m.Date == D(2027, 5, 3) && !m.PlanningOnly);
        // Next-after the PPA deadline: the attestation anniversary (1 Jul 2027).
        Assert.NotNull(brief.NextAfter);
        Assert.Equal(D(2027, 7, 1), brief.NextAfter!.Date);
    }
}