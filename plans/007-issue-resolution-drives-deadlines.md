# Plan 007: resolving an interpretation issue changes the deadlines that depend on it

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-08, audit finding F-10. Effort M.

## Summary

The brief says unresolved interpretation issues stay visible with a planning choice, and the Library screen tells the user that after resolving, "Planning flags for this question can now follow the settled source" (`LibraryView.cs:316`). The code does not: `ResolveIssue` only sets `Status = "resolved"` and a note (`QsppWorkspace.cs:422-436`), while `PharmacyDeadlines` hard-codes `planningOnly: true` for the cancellation cutoff and the mid-cycle window (`:832,837`). After this plan, resolving `ISSUE-CANCELWINDOW` or `ISSUE-MIDCYCLE` requires a structured choice, stores it, and `PharmacyDeadlines` uses that choice and drops the planning-only flag. The cancellation cutoff also becomes holiday-aware (Victorian public holidays), which satisfies step 2 of backlog item FIX-11.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10, EF Core 10 / SQLite. Files: `src/QsppManager.Domain/Services/DomainServices.cs` (deadline maths), `src/QsppManager.Domain/Services/TodayPlanning.cs` (Victorian holidays: `IsVictorianHoliday`), `src/QsppManager.Domain/Entities/CoreEntities.cs` (`InterpretationIssue`), `src/QsppManager.Infrastructure/Services/QsppWorkspace.cs`, `src/QsppManager.Desktop/Views/LibraryView.cs`.
- The three seeded issues: `ISSUE-CANCELWINDOW` (10 business days vs 10 calendar days), `ISSUE-MIDCYCLE` (months 15-21 vs 13-24), `ISSUE-POTENCY` (no date effect).
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`. EF tool: `~/.dotnet/tools/dotnet-ef`.
- Baseline 231 tests at `5858779`; record N0 first.
- **Never** open `~/Library/Application Support/QsppManager/` or `QsppManager-backup-*`.
- Never verify a build through a pipe. Run bare, then `echo "exit=$?"`. No em dashes in text you write.

## Out of scope

Choosing which reading is correct (that is Hugh's or the pharmacy's decision; the code only records and applies it). Annual attestation date, PPA date, the invented 14-day checkpoint (FIX-11 steps 1, 4, 5, 6). Deleting the duplicate holiday table in `Clock.cs`. Any change to `ISSUE-POTENCY` behaviour.

## Step 1: pure deadline functions

**File:** `src/QsppManager.Domain/Services/DomainServices.cs`, lines 52-64. Current code:

```csharp
    public static (DateOnly windowStart, DateOnly windowEnd) ConservativeMidCycleWindow(DateOnly cycleStart)
        => (AddCalendarMonths(cycleStart, 15), AddCalendarMonths(cycleStart, 21));

    public static DateOnly ConservativeCancelCutoff(DateOnly assessmentDate)
        => AddBusinessDays(assessmentDate, -10);
```

**Change:** keep both methods (other code and tests call them) and add beside them:

```csharp
    /// <summary>Mid-cycle window for a chosen reading, e.g. months 13-24 or 15-21.</summary>
    public static (DateOnly windowStart, DateOnly windowEnd) MidCycleWindow(DateOnly cycleStart, int startMonth, int endMonth)
        => (AddCalendarMonths(cycleStart, startMonth), AddCalendarMonths(cycleStart, endMonth));

    /// <summary>Last date to change or cancel without a fee: 10 business days (holiday-aware when a
    /// predicate is supplied) or 10 calendar days before the assessment.</summary>
    public static DateOnly CancelCutoff(DateOnly assessmentDate, bool businessDays, Func<DateOnly, bool>? isHoliday = null)
        => businessDays ? AddBusinessDays(assessmentDate, -10, isHoliday) : assessmentDate.AddDays(-10);
```

**Done when:** BUILD GATE passes and domain test `CancelCutoff_BusinessCalendarAndHolidays` (step 5) passes.

## Step 2: store the choice (schema)

**File:** `CoreEntities.cs`, class `InterpretationIssue` (the class already has `Status`, `SourcesJson`, `LastReviewedAtUtc`, `ReviewedByUserId` and `ResolutionNote`). Add after `ResolutionNote`:

```csharp
    /// <summary>Structured outcome when resolved: "business"/"calendar" (cancel window) or "15-21"/"13-24"
    /// (mid-cycle). Empty for issues with no date effect.</summary>
    public string ResolvedChoice { get; set; } = "";
```

**File:** `QsppDbContext.cs`: if an `InterpretationIssue` entity block exists add `e.Property(x => x.ResolvedChoice).HasMaxLength(40);`; the unique index on `Key` is at line 75 (`mb.Entity<InterpretationIssue>(e => e.HasIndex(x => x.Key).IsUnique());`), change it to a block that also sets the property length.

**Then run:**

```bash
~/.dotnet/tools/dotnet-ef migrations add IssueResolvedChoice --project src/QsppManager.Infrastructure; echo "exit=$?"
```

**Done when:** exit 0; the new migration's `Up` has exactly one `AddColumn<string>` on `InterpretationIssues` named `ResolvedChoice` (`nullable: false`, `defaultValue: ""`); `git diff --stat` shows only the entity, the DbContext, two new migration files and the snapshot. Anything else: STOP.

## Step 3: ResolveIssue takes and validates a choice

**File:** `QsppWorkspace.cs`. Current (lines 422-436):

```csharp
    public ServiceResult ResolveIssue(Guid issueId, string resolutionNote, UserSession session)
    {
        if (session == null || !session.CanVerify) return ServiceResult.Fail("Only Owner/Admin or Manager/Reviewer accounts can resolve an interpretation issue.");
        if (string.IsNullOrWhiteSpace(resolutionNote))
            return ServiceResult.Fail("Record how the question was settled ...");
        var i = db.InterpretationIssues.FirstOrDefault(x => x.Id == issueId);
        if (i == null) return ServiceResult.Fail("Interpretation issue not found.");
        i.Status = "resolved";
        i.ResolutionNote = resolutionNote[..Math.Min(resolutionNote.Length, 2000)];
        ...
```

**Change:** signature `ResolveIssue(Guid issueId, string resolutionNote, UserSession session, string choice = "")`. After the `i == null` check add:

```csharp
        var valid = ValidChoices(i.Key);
        choice = (choice ?? "").Trim();
        if (valid.Length > 0 && !valid.Contains(choice))
            return ServiceResult.Fail($"Choose how this was settled: {string.Join(" or ", valid)}.");
        if (valid.Length == 0) choice = "";
        i.ResolvedChoice = choice;
```

and add (public, so the UI can show the options):

```csharp
    public static string[] ValidChoices(string issueKey) => issueKey switch
    {
        "ISSUE-CANCELWINDOW" => ["business", "calendar"],
        "ISSUE-MIDCYCLE" => ["15-21", "13-24"],
        _ => [],
    };
```

Include the choice in the audit line: `$"{i.Key}; choice={i.ResolvedChoice}; {i.ResolutionNote}"`.

**Done when:** tests `Resolve_RequiresChoice_ForDeadlineIssues` and `Resolve_Potency_NeedsNoChoice` pass.

## Step 4: PharmacyDeadlines follows the resolved choice

**File:** `QsppWorkspace.cs`, `PharmacyDeadlines` (lines 813-843). Current relevant parts:

```csharp
        if (ph.OnsiteAssessmentDate is { } onsite)
        {
            list.Add(DeadlineInfo.Confirmed(onsite, "Assessment provider (recorded booking)"));
            var cutoff = DeadlineCalculator.ConservativeCancelCutoff(onsite);
            list.Add(DeadlineInfo.Calculated(cutoff, "ISSUE-CANCELWINDOW (conservative planning)", "Last date to change/cancel this assessment without an assumed cancellation fee — PLANNING ONLY: sources disagree (...)", planningOnly: true, issueKey: "ISSUE-CANCELWINDOW"));
        }
        if (ph.CycleStartDate is { } cs)
        {
            var (wStart, wEnd) = DeadlineCalculator.ConservativeMidCycleWindow(cs);
            list.Add(DeadlineInfo.Calculated(wStart, "ISSUE-MIDCYCLE (conservative planning)", $"Mid-Cycle Review window under the conservative reading (months 15–21, Rule 8 + T&C definition). Window: {wStart:dd MMM yyyy} – {wEnd:dd MMM yyyy}. PLANNING ONLY — sources disagree.", planningOnly: true, issueKey: "ISSUE-MIDCYCLE"));
```

**Change:** add a private helper

```csharp
    /// <summary>The recorded choice for a resolved issue, or null while the issue is open.</summary>
    private (string choice, string note)? ResolvedIssue(string key)
    {
        var i = db.InterpretationIssues.AsNoTracking().FirstOrDefault(x => x.Key == key && x.Status == "resolved" && x.ResolvedChoice != "");
        return i is null ? null : (i.ResolvedChoice, i.ResolutionNote);
    }
```

Replace the cancellation block body:

```csharp
            var rc = ResolvedIssue("ISSUE-CANCELWINDOW");
            var business = rc is null || rc.Value.choice == "business";           // unresolved: strictest reading
            var cutoff = DeadlineCalculator.CancelCutoff(onsite, business, TodayPlanning.IsVictorianHoliday);
            if (rc is { } r1)
                list.Add(DeadlineInfo.Calculated(cutoff, "ISSUE-CANCELWINDOW (resolved)", $"Last date to change/cancel without a fee, {(business ? "10 business days" : "10 calendar days")} before the assessment. Settled: {r1.note}", planningOnly: false, issueKey: "ISSUE-CANCELWINDOW"));
            else
                list.Add(DeadlineInfo.Calculated(cutoff, "ISSUE-CANCELWINDOW (conservative planning)", "Last date to change/cancel this assessment without an assumed cancellation fee. PLANNING ONLY: sources disagree (10 business days per Rule 10 vs 10 calendar days per Assessments Policy/Fees page)", planningOnly: true, issueKey: "ISSUE-CANCELWINDOW"));
```

and the mid-cycle block likewise: unresolved keeps the existing `ConservativeMidCycleWindow` (15-21) and planning-only text; resolved parses `choice` (`"15-21"` or `"13-24"`) with `var parts = rc.Value.choice.Split('-'); DeadlineCalculator.MidCycleWindow(cs, int.Parse(parts[0]), int.Parse(parts[1]))` and adds a `planningOnly: false` entry with source `ISSUE-MIDCYCLE (resolved)` and the note. Keep the exact unresolved wording of the existing deadlines (replace the em dashes in the strings you rewrite with colons or periods).

Also update `TodayBrief` (line 949-950): it calls `ConservativeMidCycleWindow(anchor)` to place a milestone marked planning-only. Replace with `var mid = PharmacyDeadlines(true).FirstOrDefault(d => d.InterpretationIssueKey == "ISSUE-MIDCYCLE");` and use `mid.Date` and `mid.PlanningOnly` for the milestone tuple (label `"Mid-cycle window opens"` plus `" (conservative reading)"` only when `PlanningOnly`).

**Done when:** tests `Resolve_Cancel_Calendar_*`, `Resolve_Cancel_Business_*`, `Resolve_Midcycle_1324_*` pass, and the existing `Deadlines_CarryPlanningOnlyFlags_ForIssuedConflicts` plus `TodayQueriesTests` still pass.

## Step 5: UI asks for the choice

**File:** `LibraryView.cs`, `ResolveAsync` (lines 309-318). Current:

```csharp
        var note = await Dialogs.Prompt(this, "Resolution record (required)",
            "Who settled this, and which source is taken as authoritative?");
        if (string.IsNullOrWhiteSpace(note)) return;
        var res = App.Workspace.ResolveIssue(row.Id, note, App.Session!);
```

**Change:** before the `ResolveIssue` call add

```csharp
        var options = QsppManager.Infrastructure.Services.QsppWorkspace.ValidChoices(row.Key);
        var choice = "";
        if (options.Length > 0)
        {
            choice = await Dialogs.Prompt(this, "Which reading was confirmed?", $"Type one of: {string.Join(" or ", options)}") ?? "";
            if (string.IsNullOrWhiteSpace(choice)) return;
        }
```

and pass `choice` as the fourth argument.

**Done when:** BUILD GATE passes; in a scratch run, resolving `ISSUE-CANCELWINDOW` with `calendar` makes the Today/Assessments cancellation date change and lose its planning-only label (screenshot both states).

## Step 6: tests (six new)

Workspace tests in new `tests/QsppManager.Integration.Tests/IssueResolutionDeadlineTests.cs : TestBase`. The test seed has no `issues.json`, so `ImportAll` uses the fallback set; the three keys exist. Use `Workspace.EnsurePharmacy()` to set dates and `Db.SaveChanges()`.

1. `Resolve_Cancel_Calendar_ChangesDateAndClearsPlanningFlag`: `OnsiteAssessmentDate = 2026-11-13`. Before: the `ISSUE-CANCELWINDOW` deadline is `2026-10-29` and `PlanningOnly`. Resolve with `"calendar"`. After: `2026-11-03`, `PlanningOnly == false`, source contains `(resolved)`.
2. `Resolve_Cancel_Business_ClearsPlanningFlag_KeepsBusinessDate`: resolve with `"business"`; date stays `2026-10-29`; flag false.
3. `Resolve_Midcycle_1324_ChangesWindow`: `CycleStartDate = 2026-01-01`. Before: window start `2027-04-01`, planning-only. Resolve `"13-24"`. After: start `2027-02-01`, flag false.
4. `Resolve_RequiresChoice_ForDeadlineIssues`: `ResolveIssue(id, "note", Owner)` on `ISSUE-CANCELWINDOW` fails; with `"weekly"` fails; with `"calendar"` succeeds.
5. `Resolve_Potency_NeedsNoChoice`: `ISSUE-POTENCY` resolves with an empty choice; `ResolvedChoice == ""`.

Domain test in `tests/QsppManager.Domain.Tests/DeadlineCalculatorTests.cs`:

6. `CancelCutoff_BusinessCalendarAndHolidays`: assessment `2026-11-13` (a Friday). `CancelCutoff(d, true)` = `2026-10-30`; `CancelCutoff(d, true, TodayPlanning.IsVictorianHoliday)` = `2026-10-29` (Melbourne Cup Day, Tue 3 Nov 2026, is skipped); `CancelCutoff(d, false)` = `2026-11-03`.

**Done when:** `dotnet test --no-build --filter "IssueResolutionDeadlineTests|CancelCutoff_BusinessCalendarAndHolidays"` shows `Passed: 6`.

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 6.
- RENDER GATE: Library (issue detail), Today and Assessments, light and dark, scratch data only.

## STOP conditions

- The generated migration touches anything other than `InterpretationIssues.ResolvedChoice`.
- `TodayPlanning.IsVictorianHoliday` does not flag 2026-11-03 (then the holiday source is wrong; report it, do not hard-code a date).
- A real pharmacy already has these issues in status `acknowledged` or `resolved` with no choice: such rows keep their status and keep showing planning-only dates because `ResolvedIssue` requires a non-empty choice. This is intended; mention it in the commit message.

## Rollback

`git revert` the commit. The `ResolvedChoice` column stays and is harmless (default empty). If the migration was never applied to a real database, `~/.dotnet/tools/dotnet-ef migrations remove --project src/QsppManager.Infrastructure` is also allowed.
