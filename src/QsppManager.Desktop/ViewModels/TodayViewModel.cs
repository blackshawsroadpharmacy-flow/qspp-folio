using System.Collections.ObjectModel;
using Avalonia.Controls;
using QsppManager.Application;
using QsppManager.Desktop.Views;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Desktop.ViewModels;

/// <summary>Real persisted briefing: no decorative figures. Every number opens its records;
/// planning-only dates are labelled, never presented as program deadlines.</summary>
public class TodayViewModel(ShellViewModel shell, TodayView view) : ViewModelBase
{
    public string Greeting => $"Good {(DateTime.Now.Hour < 12 ? "morning" : DateTime.Now.Hour < 17 ? "afternoon" : "evening")}";
    public string SubGreeting => $"{(App.Workspace.EnsurePharmacy().Name)} — here is the accreditation position today, {DateTime.Today:dd MMM yyyy}.";

    // Scalar bindings on this VM need change notification: Refresh() runs after
    // DataContext is set, so snapshot-at-attach bindings would keep the "0" defaults.
    private string _overdueCount = "0";
    public string OverdueCount { get => _overdueCount; private set => SetProperty(ref _overdueCount, value); }
    private string _awaitingReviewCount = "0";
    public string AwaitingReviewCount { get => _awaitingReviewCount; private set => SetProperty(ref _awaitingReviewCount, value); }
    private string _verifiedShare = "0 of 0";
    public string VerifiedShare { get => _verifiedShare; private set => SetProperty(ref _verifiedShare, value); }
    private string _issuesCount = "0";
    public string IssuesCount { get => _issuesCount; private set => SetProperty(ref _issuesCount, value); }

    // M7: the surveillance round card — real counts from the workspace; hidden when the pharmacy has no REM items.
    private string _roundLead = "";
    public string RoundLead { get => _roundLead; private set => SetProperty(ref _roundLead, value); }
    private string _roundNextEvent = "";
    public string RoundNextEvent { get => _roundNextEvent; private set => SetProperty(ref _roundNextEvent, value); }
    public System.Windows.Input.ICommand OpenRoundCommand => new RelayCommand(_ => { view.Refresh(); shell.OpenRequirementsFiltered("REM-"); });

    public ObservableCollection<DeadlineRow> Deadlines { get; } = [];
    public ObservableCollection<TaskRow> Overdue { get; } = [];
    public ObservableCollection<TaskRow> DueThisWeek { get; } = [];
    public ObservableCollection<string> Awaiting { get; } = [];
    public ObservableCollection<RecentRow> Recent { get; } = [];

    public record DeadlineRow(string DateLabel, string Title, string Explanation, bool PlanningOnly);
    public record TaskRow(string Title, string MetaLine, string DueLabel, Guid Id);
    public record RecentRow(string Line);

    public void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekEnd = today.AddDays(7);

        var tasks = App.Workspace.OpenTasks();
        var overdue = tasks.Where(t => t.DueDate is { } d && d < today).ToList();
        var week = tasks.Where(t => t.DueDate is { } d && d >= today && d <= weekEnd).ToList();

        var users = App.Auth.ListUsers().ToDictionary(u => u.Id, u => u.DisplayName);
        var titles = RequirementTitleCache();

        Overdue.Clear();
        foreach (var t in overdue)
            Overdue.Add(new TaskRow(t.Title, $"assigned to {users.GetValueOrDefault(t.AssignedToUserId, "?")} · {ReqLabel(titles, t.RequirementId)}", t.DueDate!.Value.ToString("dd MMM yyyy"), t.Id));
        DueThisWeek.Clear();
        foreach (var t in week)
            DueThisWeek.Add(new TaskRow(t.Title, $"assigned to {users.GetValueOrDefault(t.AssignedToUserId, "?")} · {ReqLabel(titles, t.RequirementId)}", t.DueDate!.Value.ToString("dd MMM yyyy"), t.Id));
        OverdueCount = overdue.Count.ToString();

        var awaiting = App.Workspace.EvidenceAwaitingReview();
        Awaiting.Clear();
        foreach (var e in awaiting)
            Awaiting.Add($"{e.EvidenceTitle} — {e.FileName} · review: {e.ReviewState}");
        AwaitingReviewCount = awaiting.Count.ToString();
        NoAwaiting().IsVisible = awaiting.Count == 0;

        var (verified, total) = App.Workspace.ReadinessSummary();
        VerifiedShare = $"{verified} of {total}";

        var issues = App.Workspace.Issues().Count(i => i.Status != "resolved");
        IssuesCount = issues.ToString();

        var round = App.Workspace.SurveillanceRound();
        RoundLead = round.Items == 0 ? "" :
            $"{round.Items} evidence requests are open with the QCPP remote assessment portal. " +
            $"{round.WithEvidence} have evidence attached, {round.InProgress} are in progress, {round.Verified} verified.";
        RoundNextEvent = string.IsNullOrWhiteSpace(round.NextEvent) ? "No planned assessment event recorded." : $"Next planned event: {round.NextEvent}";
        var roundCard = view.FindControl<StackPanel>("RoundCard");
        if (roundCard is { }) roundCard.IsVisible = round.Items > 0;

        Deadlines.Clear();
        foreach (var d in App.Workspace.PharmacyDeadlines())
            Deadlines.Add(new DeadlineRow(d.Date.ToString("dd MMM yyyy"), d.Source, d.Explanation, d.PlanningOnly));

        Recent.Clear();
        foreach (var a in App.Audit.Recent(8))
            Recent.Add(new RecentRow($"{a.AtUtc.ToLocalTime():dd MMM yyyy h:mm tt} — {a.ActorName}: {a.Action} {a.EntityType} {a.Details}".Trim()));
        NoOverdue().IsVisible = overdue.Count == 0;
        NoDueWeek().IsVisible = week.Count == 0;
    }

    private Dictionary<Guid, string> RequirementTitleCache() =>
        App.Workspace.RequirementRows(App.Session).ToDictionary(r => r.Id, r => r.Ref);

    private static string ReqLabel(Dictionary<Guid, string> titles, Guid? reqId)
        => reqId.HasValue && titles.TryGetValue(reqId.Value, out var t) ? t : "no linked requirement";

    private TextBlock NoOverdue() => view.FindControl<TextBlock>("NoOverdue")!;
    private TextBlock NoDueWeek() => view.FindControl<TextBlock>("NoDueWeek")!;
    private TextBlock NoAwaiting() => view.FindControl<TextBlock>("NoAwaiting")!;

    public System.Windows.Input.ICommand OpenTasksCommand => new RelayCommand(_ => { view.Refresh(); OpenTasks(); });
    public System.Windows.Input.ICommand OpenDocumentsCommand => new RelayCommand(_ => { view.Refresh(); OpenDocuments(); });
    public System.Windows.Input.ICommand OpenRequirementsCommand => new RelayCommand(_ => { view.Refresh(); OpenRequirements(); });

    public void OpenTasks() => shell.SelectedNav = shell.NavItems.First(n => n.Page == ShellPage.Tasks);
    public void OpenDocuments() => shell.SelectedNav = shell.NavItems.First(n => n.Page == ShellPage.Documents);
    public void OpenRequirements() => shell.SelectedNav = shell.NavItems.First(n => n.Page == ShellPage.Requirements);
    public void OpenIssues()
    {
        var nav = shell.NavItems.FirstOrDefault(n => n.Page == ShellPage.Library);
        if (nav != null) shell.SelectedNav = nav;   // issues live in the Library with their competing sources
    }
}

public static class AppViewExtensions
{
    public static List<AuditEventFlat> AppRecent(this ShellViewModel shell) => App.Audit.Recent(8)
        .Select(a => new AuditEventFlat(a.AtUtc, a.ActorName, a.Action, a.EntityType, a.Details))
        .ToList();
}

public record AuditEventFlat(DateTime AtUtc, string ActorName, string Action, string EntityType, string Details);