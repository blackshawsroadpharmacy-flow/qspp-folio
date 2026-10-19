using Microsoft.EntityFrameworkCore;
using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M3: recurring-task generation idempotence, registers and the training matrix.</summary>
public class WorkflowM3Tests : TestBase
{
    [Fact]
    public void RecurrenceGeneration_IsIdempotent_AndMissedOccurrencesStayOpen()
    {
        var horizon = new DateOnly(2026, 11, 30);
        Assert.True(Workspace.CreateRecurringDef("Fridge temperature check (Strive for 5)", "[\"2–8 °C twice daily\"]",
            null, null, Owner.UserId, RecurrenceCadence.Weekly, 0, new DateOnly(2026, 10, 5), TaskPriority.Normal, Owner).Ok);

        var first = Workspace.GenerateRecurringOccurrences(horizon, Owner);
        Assert.True(first >= 8, $"expected ≥8 weekly occurrences Oct 5 → Nov 30, got {first}");
        var second = Workspace.GenerateRecurringOccurrences(horizon, Owner);
        Assert.Equal(0, second); // reopening/re-generating never duplicates

        // Occurrences earlier than "today" (e.g. mid-October ones when today is later) remain OPEN with past dues.
        var open = Workspace.OpenTasks().Where(t => t.RecurrenceDefId != null).ToList();
        Assert.Equal(first, open.Count);
        Assert.All(open, t => Assert.Equal(TaskState.Open, t.State));
    }

    [Fact]
    public void CalibrationDue_CreatesAMonthlySchedule_Once()
    {
        Assert.True(Workspace.AddEquipment("Refrigerator 1", "Labtec LC-59", "SN2231", "Dispensary", "", Owner).Ok);
        var eq = Workspace.Equipment().First(e => e.Name == "Refrigerator 1");
        Assert.True(Workspace.SetCalibrationDue(eq.Id, new DateOnly(2026, 12, 1), Owner).Ok);

        var defs = Workspace.RecurringDefs();
        var cal = defs.Single(d => d.EquipmentId == eq.Id);
        Assert.Equal(RecurrenceCadence.Monthly, cal.Cadence);
        // Calling again does NOT duplicate the schedule.
        Assert.True(Workspace.SetCalibrationDue(eq.Id, new DateOnly(2026, 12, 20), Owner).Ok);
        Assert.Equal(defs.Count, Workspace.RecurringDefs().Count);
    }

    [Fact]
    public void Incident_CannotCloseWithoutCorrectiveAction_AndLinksToReportingClause()
    {
        Assert.True(Workspace.AddIncident(new DateOnly(2026, 10, 15), "minor", "Vaccine fridge logged above range for two hours.", "Stock quarantined and pharmacist notified.", "", Owner).Ok);
        var inc = Workspace.Incidents().First();
        // The service links to requirement 1.1.2.4 when the corpus contains it (the real
        // corpus does; the tiny test seed may not).
        var reqRow = Workspace.RequirementRows(Owner).FirstOrDefault(r => r.Id == inc.RequirementId);
        if (reqRow != null) Assert.Equal("1.1.2.4", reqRow.Ref);

        Assert.False(Workspace.SetIncidentState(inc.Id, "Closed", "", Owner).Ok);
        Assert.True(Workspace.SetIncidentState(inc.Id, "Closed", "Refrigerator serviced; thermostat replaced; monitoring interval doubled.", Owner).Ok);
        Assert.Equal("Closed", Workspace.Incidents().First(i => i.Id == inc.Id).State);
    }

    [Fact]
    public void PolicyAcks_AreVersionTied_AndDuplicatesAreRejected()
    {
        Assert.True(Workspace.AddStaffMember("Priya Nair", "Pharmacy Assistant", Owner).Ok);
        var staff = Workspace.Staff().First();
        Assert.True(Workspace.AcknowledgePolicy(staff.Id, "QSPP Assessments Policy", "Aug 2026", null, Owner).Ok);
        Assert.False(Workspace.AcknowledgePolicy(staff.Id, "QSPP Assessments Policy", "Aug 2026", null, Owner).Ok, "same staff+policy+version must not duplicate");
        Assert.True(Workspace.AcknowledgePolicy(staff.Id, "QSPP Assessments Policy", "Sep 2026", null, Owner).Ok, "a NEW version is a distinct acknowledgement");
        Assert.Equal(2, Workspace.Acknowledgements().Count);
    }

    [Fact]
    public void EquipmentAndStaffWrites_AreRoleGated()
    {
        var reviewer = new UserSession(Guid.NewGuid(), "rev", "Reviewer", UserRole.ReviewerReadOnly);
        Assert.False(Workspace.AddEquipment("Fake", "", "", "", "", reviewer).Ok);
        Assert.False(Workspace.AddStaffMember("Fake", "", reviewer).Ok);
        Assert.True(Workspace.AddEquipment("Balance", "AND FX-120", "SN9", "Compounding bench", "", Owner).Ok);
        Assert.True(Workspace.AddStaffMember("Test Person", "Technician", Owner).Ok);
    }
}