using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using QsppManager.Application;
using QsppManager.Desktop.Views;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Desktop.ViewModels;

/// <summary>Today screen briefing (Today redesign, 24 Oct). The featured deadline and the
/// 12-month obligations ledger come from the pure, tested planning layer
/// (TodayPlanning + QsppWorkspace.TodayScreenBrief). Display strings are assembled here
/// from stored facts only: no invented dates, no percentages, no compliance score.</summary>
public class TodayViewModel(ShellViewModel shell, TodayView view) : ViewModelBase
{
    public string Greeting => $"Good {(DateTime.Now.Hour < 12 ? "morning" : DateTime.Now.Hour < 17 ? "afternoon" : "evening")}, Hugh";

    private string _eyebrow = "";
    public string Eyebrow { get => _eyebrow; private set => SetProperty(ref _eyebrow, value); }

    // ---- hero ----
    private bool _hasDeadline;
    public bool HasDeadline { get => _hasDeadline; private set => SetProperty(ref _hasDeadline, value); }
    private string _daysLabel = "";
    public string DaysLabel { get => _daysLabel; private set => SetProperty(ref _daysLabel, value); }
    private string _deadlineFullDate = "";
    public string DeadlineFullDate { get => _deadlineFullDate; private set => SetProperty(ref _deadlineFullDate, value); }
    private string _deadlineTitle = "";
    public string DeadlineTitle { get => _deadlineTitle; private set => SetProperty(ref _deadlineTitle, value); }
    private string _deadlineBody = "";
    public string DeadlineBody { get => _deadlineBody; private set => SetProperty(ref _deadlineBody, value); }
    // Owner item 17: the hero consequence replaces the raw provenance body — the formatted
    // source line plus one derived consequence ("Applies to 8CPA programs from 1 Jul 2027").
    private string _deadlineConsequence = "";
    public string DeadlineConsequence { get => _deadlineConsequence; private set => SetProperty(ref _deadlineConsequence, value); }
    private string _deadlineProvenance = "";
    public string DeadlineProvenance { get => _deadlineProvenance; private set => SetProperty(ref _deadlineProvenance, value); }
    private string _afterThis = "";
    public string AfterThis { get => _afterThis; private set => SetProperty(ref _afterThis, value); }
    private string _heroAutomationName = "";
    public string HeroAutomationName { get => _heroAutomationName; private set => SetProperty(ref _heroAutomationName, value); }

    // Run-line drawing is custom; the control binds to these.
    private DateOnly? _runStart;
    public DateOnly? RunStart { get => _runStart; private set => SetProperty(ref _runStart, value); }
    private DateOnly? _deadlineDate;
    public DateOnly? DeadlineDate { get => _deadlineDate; private set => SetProperty(ref _deadlineDate, value); }

    // ---- accreditation position (the ledgers block) ----
    public ObservableCollection<PositionRow> Position { get; } = [];

    // ---- year ahead + renewals ----
    public ObservableCollection<MonthMark> Marks { get; } = [];
    private string _yearAheadRange = "";
    public string YearAheadRange { get => _yearAheadRange; private set => SetProperty(ref _yearAheadRange, value); }

    public ObservableCollection<LedgerRow> Bucket30 { get; } = [];
    public ObservableCollection<LedgerRow> Bucket90 { get; } = [];
    public ObservableCollection<LedgerRow> BucketLater { get; } = [];

    // Count + suffix + status brush: the figures render right-aligned in the exact
    // Assessments stat style, hue when they need attention (owner item 16).
    public record PositionRow(string Label, string Count, string Suffix, string CountBrush, System.Windows.Input.ICommand Command);
    public record MonthMark(int MonthIndex, double Fraction, string Shape, Guid RecordId, string CategoryKey, string Tooltip, bool Tall, string Chip = "");
    public record LedgerRow(Guid RecordId, string Category, string CategoryKey, string Item, string Caption,
        string Action, string DateLabel, string DaysLabel, string Owner, string Chip, string ChipTone, bool Overdue);
    public record TaskRow(string Title, string MetaLine, string DueLabel, Guid Id);

    // ---- this week + review inbox ----
    public ObservableCollection<TaskRow> ThisWeek { get; } = [];
    public ObservableCollection<string> Inbox { get; } = [];

    /// <summary>The full planning brief from the last Refresh (the screen builder reads it).</summary>
    public QsppWorkspace.TodayScreenBrief Brief { get; private set; } =
        new QsppWorkspace.TodayScreenBrief(null, null, null, null, new(), new(), new());

    // ---- activity ----
    public ObservableCollection<ActivityRow> Activity { get; } = [];
    public record ActivityRow(string Line, string? RawTooltip, string? RevealPath);

    // commands
    public System.Windows.Input.ICommand OpenHeroCommand => new RelayCommand(_ => shell.Navigate(ShellPage.Assessments));
    public System.Windows.Input.ICommand OpenRegistersCommand => new RelayCommand(_ => shell.Navigate(ShellPage.Registers));
    public System.Windows.Input.ICommand OpenIssuesCommand => new RelayCommand(_ => OpenIssues());
    public System.Windows.Input.ICommand OpenLedgerCommand => new RelayCommand(p => OpenLedger(p));
    public System.Windows.Input.ICommand OpenTaskCommand => new RelayCommand(_ => shell.Navigate(ShellPage.Tasks));
    public System.Windows.Input.ICommand OpenInboxCommand => new RelayCommand(_ => shell.Navigate(ShellPage.Documents));
    public System.Windows.Input.ICommand OpenVerifiedCommand => new RelayCommand(_ => shell.Navigate(ShellPage.Requirements));
    public System.Windows.Input.ICommand OpenRoundCommand => new RelayCommand(_ => shell.OpenRequirementsFiltered("REM-"));
    public System.Windows.Input.ICommand RevealCommand => new RelayCommand(p => { if (p is string path && path.Length > 0) RevealInFinder(path); });

    private void OpenLedger(object? p)
    {
        if (p is not LedgerRow r) return;
        var page = r.CategoryKey switch
        {
            "equipment" => ShellPage.Registers,
            "people" => ShellPage.People,
            "documents" => ShellPage.Documents,
            _ => ShellPage.Assessments,
        };
        shell.Navigate(page);
    }

    private static void RevealInFinder(string fullPath)
    {
        try { System.Diagnostics.Process.Start("open", $"-R \"{fullPath}\""); } catch { /* Finder may refuse paths it cannot see */ }
    }

    private static readonly Regex PathRx = new(@"((?:/Users|/Volumes|~/)[A-Za-z0-9 ._\-&,()'@+]*)", RegexOptions.Compiled);

    public void Refresh()
    {
        var today = TodayPlanning.MelbourneToday(DateTime.UtcNow);
        Eyebrow = $"{App.Workspace.EnsurePharmacy().Name} · {today:dddd dd MMMM yyyy}".ToUpperInvariant();

        // ---------- hero ----------
        var brief = App.Workspace.TodayBrief(today);
        Brief = brief;
        var f = brief.Featured;
        if (f is null)
        {
            HasDeadline = false; // the panel reads "No QSPP deadline recorded" with one action
        }
        else
        {
            HasDeadline = true;
            var tone = brief.FeaturedTone!;
            DeadlineDate = f.Date;
            DeadlineTitle = ShortTitle(f);
            DeadlineBody = ShortBody(f);
            DeadlineConsequence = ConsequenceLine(f);
            DeadlineProvenance = f.PlanningOnly ? "Planning only" : "Confirmed";
            var weeks = Math.Floor(tone.DaysRemaining / 7.0);
            var wd = TodayPlanning.WorkingDaysBetween(today, f.Date);
            DaysLabel = tone.State switch
            {
                TodayPlanning.Tone.Overdue => tone.Word,
                _ when tone.DaysRemaining == 0 => "Due today",
                _ => $"{tone.DaysRemaining} days",
            };
            var dateLine = weeks >= 1
                ? $"{f.Date:dddd dd MMMM yyyy} \u00B7 {weeks} weeks \u00B7 {wd} working days (VIC)"
                : $"{f.Date:dddd dd MMMM yyyy} \u00B7 {wd} working days (VIC)";
            DeadlineFullDate = dateLine;
            HeroAutomationName = tone.DaysRemaining switch
            {
                0 => $"Due today: {DeadlineTitle}, {f.Date:dddd dd MMMM yyyy}",
                > 0 => $"{tone.DaysRemaining} days until {DeadlineTitle}, {f.Date:dddd dd MMMM yyyy}",
                _ => tone.Word + $" until {DeadlineTitle}, {f.Date:dddd dd MMMM yyyy}",
            };
            AfterThis = brief.NextAfter is { } n
                ? $"After this: {ShortTitle(n)}, {n.Date:dd MMM yyyy}" + (n.PlanningOnly ? " (planning only)" : "")
                : "After this: date not set";
            RunStart = brief.RunStart;
            OnPropertyChanged(nameof(RunStart));
            OnPropertyChanged(nameof(DeadlineDate));
        }

        // ---------- accreditation position (ledger rows, each opens its records) ----------
        var tasks = App.Workspace.OpenTasks();
        var overdueCount = tasks.Count(t => t.DueDate is { } d && d < today);
        var week = tasks.Where(t => t.DueDate is { } d && d >= today && d <= today.AddDays(7)).ToList();
        var (verified, total) = App.Workspace.ReadinessSummary();
        var issues = App.Workspace.Issues().Count(i => i.Status != "resolved");
        var round = App.Workspace.SurveillanceRound();
        Position.Clear();
        Position.Add(new("Overdue tasks", overdueCount.ToString(), "",
            overdueCount > 0 ? "Brush.Ochre" : "Brush.Muted", OpenTaskCommand));
        Position.Add(new("Evidence awaiting review", App.Workspace.EvidenceAwaitingReview().Count.ToString(), "",
            App.Workspace.EvidenceAwaitingReview().Count > 0 ? "Brush.Violet" : "Brush.Muted", OpenInboxCommand));
        Position.Add(new("Manager-verified", verified.ToString(), $"of {total}",
            verified > 0 ? "Brush.Green" : "Brush.Muted", OpenVerifiedCommand));
        Position.Add(new("Unresolved interpretations", issues.ToString(), "",
            issues > 0 ? "Brush.Ochre" : "Brush.Muted", OpenIssuesCommand));
        RoundLead = round.Items == 0 ? "" :
            $"{round.Items} requests: {round.WithEvidence} attached, {round.InProgress} in progress, {round.Verified} verified";
        OnPropertyChanged(nameof(RoundLead));
        RoundCounts = (round.Items, round.WithEvidence, round.InProgress, round.Verified);
        OnPropertyChanged(nameof(RoundCounts));
        RoundNextEvent = string.IsNullOrWhiteSpace(round.NextEvent) ? "No planned assessment event recorded." : $"QCPP portal \u00B7 next planned event: {round.NextEvent}";
        OnPropertyChanged(nameof(RoundNextEvent));

        // ---------- year ahead + renewals ledger ----------
        var allRows = BriefRows(brief, today);
        Bucket30.Clear(); Bucket90.Clear(); BucketLater.Clear();
        foreach (var r in allRows.Where(r => TodayPlanning.Bucket(r.Due, today) == 0)
                     .OrderBy(r => r.Overdue ? -1 : r.Due!.Value.DayNumber).ThenBy(r => r.Item))
            Bucket30.Add(DisplayRow(r, today));
        foreach (var r in allRows.Where(r => TodayPlanning.Bucket(r.Due, today) == 1).OrderBy(r => r.Due!.Value.DayNumber))
            Bucket90.Add(DisplayRow(r, today));
        foreach (var r in allRows.Where(r => TodayPlanning.Bucket(r.Due, today) == 2).OrderBy(r => r.Due?.DayNumber ?? int.MaxValue).ThenBy(r => r.Item))
            BucketLater.Add(DisplayRow(r, today));

        var window = TodayPlanning.YearAheadMonths(today);
        YearAheadRange = new DateOnly(window[0].Year, window[0].Month, 1).ToString("MMM yyyy", Au).ToUpperInvariant()
            + " to " + new DateOnly(window[11].Year, window[11].Month, 1).ToString("MMM yyyy", Au).ToUpperInvariant();
        OnPropertyChanged(nameof(YearAheadRange));
        Marks.Clear();
        foreach (var r in allRows)
        {
            if (r.Due is not { } d) continue;
            if (d < new DateOnly(window[0].Year, window[0].Month, 1) || d >= new DateOnly(window[11].Year, window[11].Month, 1).AddMonths(1)) continue;
            var mIdx = window.IndexOf((d.Year, d.Month));
            if (mIdx < 0) continue;
            var dms = new DateOnly(d.Year, d.Month, 1);
            var frac = (d.DayNumber - dms.DayNumber + 0.5) / DateTime.DaysInMonth(d.Year, d.Month);
            Marks.Add(new(mIdx, frac, r.CategoryKey switch { "equipment" => "square", "people" => "circle", "documents" => "diamond", _ => "triangle" },
                r.RecordId, r.CategoryKey, $"{r.Item} · {r.Action}, {d:dd MMM yyyy}", Tall: false));
        }
        if (f is { } fp && !fp.PlanningOnly)
        {
            var mIdx = window.IndexOf((fp.Date.Year, fp.Date.Month));
            if (mIdx >= 0)
            {
                var dms = new DateOnly(fp.Date.Year, fp.Date.Month, 1);
                var frac = (fp.Date.DayNumber - dms.DayNumber + 0.5) / DateTime.DaysInMonth(fp.Date.Year, fp.Date.Month);
                Marks.Add(new(mIdx, frac, "bar", Guid.Empty, "program", $"{ShortTitle(fp)} · deadline, {fp.Date:dd MMM yyyy}", Tall: true,
                    Chip: $"{fp.Date:dd MMM} · {YearChipKind(fp)}"));
            }
        }
        OnPropertyChanged(nameof(Marks));

        // ---------- this week (overdue + due in 7 days, merged) + review inbox ----------
        ThisWeek.Clear();
        var titles = RequirementTitleCache();
        var users = App.Auth.ListUsers().ToDictionary(u => u.Id, u => u.DisplayName);
        foreach (var t in tasks.Where(t => t.DueDate is { } d && d <= today.AddDays(7)).OrderBy(t => t.DueDate))
            ThisWeek.Add(new TaskRow(t.Title,
                $"assigned to {users.GetValueOrDefault(t.AssignedToUserId, "?")} · {ReqLabel(titles, t.RequirementId)}",
                t.DueDate!.Value < today ? $"{today.DayNumber - t.DueDate.Value.DayNumber} d overdue" : t.DueDate!.Value == today ? "today" : t.DueDate!.Value.ToString("dd MMM yyyy"),
                t.Id));
        Inbox.Clear();
        foreach (var e in App.Workspace.EvidenceAwaitingReview())
            Inbox.Add($"{e.EvidenceTitle} · {e.FileName} · review: {e.ReviewState}");

        // ---------- activity ----------
        Activity.Clear();
        foreach (var r in ActivityLines()) Activity.Add(r);

        RoundCardVisible = round.Items > 0;
        OnPropertyChanged(nameof(RoundCardVisible));
    }

    public record RoundShape(int Items, int WithEvidence, int InProgress, int Verified);
    public (int, int, int, int) RoundCounts = (0, 0, 0, 0);
    private bool _roundCardVisible;
    public bool RoundCardVisible { get => _roundCardVisible; private set => SetProperty(ref _roundCardVisible, value); }
    private string _roundLead = "";
    public string RoundLead { get => _roundLead; private set => SetProperty(ref _roundLead, value); }
    private string _roundNextEvent = "";
    public string RoundNextEvent { get => _roundNextEvent; private set => SetProperty(ref _roundNextEvent, value); }

    private static readonly System.Globalization.CultureInfo Au = new("en-AU");

    private LedgerRow DisplayRow(BriefRow r, DateOnly today)
    {
        var days = r.Due is { } d ? d.DayNumber - today.DayNumber : (int?)null;
        var daysLabel = days switch
        {
            null => "",
            < 0 => $"{Math.Abs(days.Value)} d overdue",
            0 => "today",
            _ => $"in {days} d",
        };
        return new(r.RecordId, r.Category, r.CategoryKey, r.Item, r.Caption, r.Action,
            r.Due?.ToString("dd MMM yyyy") ?? "Not set", daysLabel, r.Owner, r.Chip, r.ChipTone, r.Overdue);
    }

    // ---- obligations display shapes ----
    public record BriefRow(Guid RecordId, string Category, string CategoryKey, string Item, string Caption,
        string Action, DateOnly? Due, string Owner, string Chip, string ChipTone, bool Overdue = false)
    {
        public bool IsFeatured { get; init; }
    }

    private List<BriefRow> BriefRows(QsppWorkspace.TodayScreenBrief brief, DateOnly today)
    {
        var list = new List<BriefRow>();
        foreach (var o in brief.Obligations)
        {
            var overdue = o.Due is { } d && d < today;
            list.Add(new BriefRow(o.RecordId, o.Category, o.CategoryKey, o.Item, o.Caption, o.Action, o.Due, o.Owner,
                ChipFor(o.EvidenceState), ChipToneFor(o.EvidenceState), overdue));
        }
        return list;

        static string ChipFor(string state) => state switch
        {
            "ManagerVerified" => "Manager verified",
            "ReadyForReview" => "Under review",
            "NeedsRework" => "Needs rework",
            "Retired" => "Retired",
            "Draft" => "Draft",
            "Planned" => "Planned",
            "InProgress" => "In progress",
            _ => "",
        };
        static string ChipToneFor(string state) => state switch
        {
            "ManagerVerified" => "ok",
            "ReadyForReview" => "review",   // violet for ready-for-review (3D)
            "NeedsRework" => "bad",
            "Planned" => "info",
            "InProgress" => "warn",
            "Draft" or "Retired" => "info",
            _ => "neutral",
        };
    }

    // ---- honest shortening of stored strings (substrings only, no new regulatory text) ----
    internal static string ShortTitle(DeadlineInfo d)
    {
        var s = d.Source;
        var cut = s.IndexOf('(');
        if (cut > 0) s = s[..cut];
        cut = s.IndexOf("—");
        if (cut > 0) s = s[..cut];
        if (s.StartsWith("T&C Clause 5c", StringComparison.Ordinal)) s = "T&C Clause 5c: annual attestation statement";
        s = s.Trim().TrimEnd(',', '.', ';', ':').Trim();
        if (s.Length == 0) s = d.Source;
        return char.IsUpper(s[0]) ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    internal static string ShortBody(DeadlineInfo d)
    {
        // The stored consequence: the parenthetical source reference plus the tail after the dash,
        // quoted verbatim from the record. The generic confirmation sentence is dropped.
        var s = d.Source;
        var open = s.IndexOf('(');
        var close = s.IndexOf(')');
        var parentheses = open >= 0 && close > open ? s[(open + 1)..close] : "";
        var dash = s.LastIndexOf("— ");
        var tail = dash >= 0 && dash + 2 < s.Length ? s[(dash + 2)..] : "";
        if (d.Explanation.Length > 0 && d.Explanation != "Date confirmed by the assessment provider/program.")
            tail = d.Explanation;
        var parts = new List<string>();
        if (parentheses.Length > 0) parts.Add(parentheses);
        if (tail.Length > 0) parts.Add(tail);
        var body = string.Join(" · ", parts.Where(p => p.Length > 0));
        if (body.Length == 0) body = d.Explanation;
        if (body.Length == 0) body = s;
        return char.IsUpper(body[0]) ? body : char.ToUpperInvariant(body[0]) + body[1..];
    }

    /// <summary>Owner item 17: one consequence line for the hero. The stored source tail is
    /// a curated display rewrite where the record warrants one (same pattern as the T&C
    /// rewrite in ShortTitle); otherwise the stored tail, sentence-cased. The stored
    /// provenance string is never modified.</summary>
    internal static string ConsequenceLine(DeadlineInfo d)
    {
        var s = d.Source;
        var dash = s.LastIndexOf("— ");
        var tail = dash >= 0 && dash + 2 < s.Length ? s[(dash + 2)..].Trim() : "";
        if (tail.Length == 0) tail = d.Explanation.Length > 0 && d.Explanation != "Date confirmed by the assessment provider/program."
            ? d.Explanation : "";
        if (tail.Contains("applies from 1 Jul 2027 8CPA programs"))
            tail = "Applies to 8CPA programs from 1 Jul 2027";
        if (tail.Length == 0) return "";
        return char.IsUpper(tail[0]) ? tail : char.ToUpperInvariant(tail[0]) + tail[1..];
    }

    /// <summary>Owner item 19: the short kind for the year-ahead chip. The PPA deadline reads
    /// "PPA certificate"; other records fall back to their short title's first words.</summary>
    internal static string YearChipKind(DeadlineInfo d)
    {
        var head = ShortTitle(d);
        var role = head.IndexOf(':');
        var kind = role > 0 ? head[..role].Trim() : FirstWords(head, 3);
        return head.Contains("certificate", StringComparison.OrdinalIgnoreCase) && role >= 0
            ? $"{kind} certificate" : kind;
    }

    private static string FirstWords(string s, int max)
    {
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Take(Math.Min(max, parts.Length)));
    }

    // ---- activity sentences ----
    private IEnumerable<ActivityRow> ActivityLines()
    {
        var events = App.Audit.Recent(10);
        var today = TodayPlanning.MelbourneToday(DateTime.UtcNow);
        var i = 0;
        // Consecutive logins at the head collapse into one sentence.
        var loginCount = 0;
        while (i < events.Count && events[i].Action == "user.login" && DateOnly.FromDateTime(events[i].AtUtc.ToLocalTime()) == today)
        { loginCount++; i++; }
        if (loginCount > 1) yield return new($"Signed in {loginCount} times today", "user.login", null);
        else if (loginCount == 1) yield return new("Signed in", "user.login", null);

        for (; i < events.Count; i++)
        {
            var e = events[i];
            var when = e.AtUtc.ToLocalTime();
            var m = PathRx.Match(e.Details);
            var path = m.Success ? m.Groups[1].Value.Trim() : null;
            var line = Sentence(e);
            yield return new($"{when:dd MMM h:mm tt} · {line}", $"{e.Action} \u00B7 {e.EntityType}{(m.Success ? "" : $": {e.Details}")}", path);
        }
    }

    /// <summary>Event keys rewritten as plain sentences; the raw key stays in the tooltip.</summary>
    private static string Sentence(AuditEvent e)
    {
        if (e.Action == "user.login") return "Signed in";
        if (e.Action == "folio.batch.bulk-approved" && Regex.Match(e.Details, "approved=(\\d+)") is { } m && m.Success)
            return $"Published {m.Groups[1].Value} items to the QCPP folio";
        var words = e.Action.Split('.').Where(p => p != "user" && p != "folio").Select(CamelWord).ToList();
        if (words.Count == 0) words = ["record updated"];
        var sentence = string.Join(" ", words.Select((w, ix) => ix == 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
        if (e.EntityType.Length > 0 && !sentence.Contains(e.EntityType, StringComparison.OrdinalIgnoreCase))
            sentence += $" \u00B7 {e.EntityType}";
        return sentence;
    }

    private static string CamelWord(string p)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (i > 0 && char.IsUpper(c)) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().ToLowerInvariant();
    }
    private Dictionary<Guid, string> RequirementTitleCache() =>
        // Ledger labels read as jobs (owner item 15): display title first, the code after,
        // never source wording. The code stays same-weight inside muted ledger lines.
        App.Workspace.RequirementRows(App.Session).ToDictionary(r => r.Id, r => $"{r.DisplayTitleText} · {r.Ref}");

    private static string ReqLabel(Dictionary<Guid, string> titles, Guid? reqId)
        => reqId.HasValue && titles.TryGetValue(reqId.Value, out var t) ? t : "no linked requirement";

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