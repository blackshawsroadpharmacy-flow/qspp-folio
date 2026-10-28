# Plan 009: evidence expiry sweeps links Suspended and opens review work

Backlog item FIX-21, audit finding F-23. Effort M. hermes_execution: plan-file. STOP for owner review before execution.

## Summary

The product brief promises that evidence with an end date takes its attachments down for review when the date passes. Today nothing does this on schedule: the Evidence tab shows expiry, the Today list shows upcoming expiries, and after the date passes every affected link keeps carrying readiness.

After this plan: on app boot and again every 24 hours while the app is open, a sweep

1. reads the current date and finds every non-superseded `EvidenceLink` whose `EvidenceItem.EffectiveTo` is strictly before today;
2. marks each such link `Suspended` (only from `Active` or `Proposed`; `Superseded` rows stay untouched);
3. records one `TaskItem` per affected **requirement** (not per link): title "Review expired evidence", pointing at the requirement, due the day after the sweep, open by default, assigned to the requirement's owner if set;
4. is idempotent: running it twice on the same day changes nothing the second time.

Readiness then drops on its own: the FIX-02 gate already treats a suspended link as not qualifying, so the same "unexpired" predicate must become the single shared test used by both readiness and the sweep, not a second copy.

## Read this first (you have never seen this repo)

- Repo `<repo>`, solution `qspp-manager.slnx`, .NET 10, Avalonia UI, EF Core 10 / SQLite. Branch for the fix phase: `fix/audit-2026-10-26` (one fix = one commit, `fix(FIX-NN): <title>`).
- Relevant files:
  - `src/QsppManager.Domain/Entities/CoreEntities.cs`: `EvidenceItem` (has `EffectiveTo` as `DateOnly?`, line ~96), `EvidenceLink` (line ~108, `State` is `LinkState`), `TaskItem` (line 124; `RecurrenceDefId` + `OccurrenceDate` at 143-144 are the M3 idempotence pattern: unique index `(RecurrenceDefId, OccurrenceDate)` in `QsppDbContext.cs` line 67).
  - `src/QsppManager.Domain/Enumerations.cs`: `LinkState { Proposed, Active, Suspended, Superseded }` (line 71). `Suspended = 2` **already exists**; no enum addition is needed, so the backlog file list mentioning Enumerations.cs is satisfied by this check.
  - `src/QsppManager.Infrastructure/Services/QsppWorkspace.cs`: the workspace (`QsppDbContext db` primary-constructor parameter, line 15).
  - `src/QsppManager.Infrastructure/Persistence/QsppDbContext.cs` + `Migrations/`: index and migration conventions.
  - `src/QsppManager.Desktop/App.axaml.cs`: startup (FIX-15's `DataDirLock.TryAcquire` first, Prepare/migration, then UI). The sweep runs after Prepare succeeds; a sweep failure must NOT take the app down with an unhandled exception: log to `AppData.Ensure("logs")` and continue.
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- **Production data is sacred.** Never read, copy, list or open `~/Library/Application Support/QsppManager/` or any `QsppManager-backup-*`. All runs use `QSPP_DATA_DIR=$(mktemp -d)`; tests use temp dirs. Never call `AppConfig.Load()` or `AppConfig.Save()` from a test.
- Never verify a build or test through a pipe. Run bare, then `echo "exit=$?"`. Record N0 (currently 302 total: 300 passed + 2 opt-in skipped) before and after.
- No em dashes in any text you write. Never print credentials. If a gate fails twice for the same reason, stop and report.

## Out of scope

- Email, desktop notifications, or any outward alerting. Review work is a task row.
- Any UI change beyond what Today already shows naturally (the new task appears in Today because it is a task).
- Automatic readiness changes beyond what the existing FIX-02 gate already computes from suspended links. The sweep suspends links; readiness recomputes from links.
- FIX-22 registers. FIX-20 backup encryption. A restore-side or import-side expiry check beyond the shared predicate.

## Steps

### Step 1: the shared unexpired predicate

In `src/QsppManager.Application/DomainServices.cs` (or wherever FIX-02 placed the readiness evidence test), find the existing "evidence qualifies" check and confirm it answers two questions: link not superseded/suspended, and item's `EffectiveTo` is null or >= today. Promote the exact predicate to one named static function `EvidenceQualifies(EvidenceLink link, EvidenceItem item, DateOnly today)` and make readiness call it. The sweep must call the same function; the point is one place to change later. If FIX-02's predicate is already a named member, use it and skip the promotion.

### Step 2: sweep method on the workspace

Add `public ExpirySweepResult SweepEvidenceExpiry(DateOnly today)` to `QsppWorkspace`:

- Selects candidate links: join `EvidenceLinks` (State in {Active, Proposed}) to their `EvidenceItem` where `EffectiveTo != null && EffectiveTo < today`. Read `today` from the caller (tests pass fixed dates); inside the boot hook the caller passes the local date.
- Marks all candidates `Suspended` in one `SaveChanges()`.
- Groups affected requirement ids (link `RequirementId`); for each requirement that does NOT already have an open review task (the idempotence check below), creates one `TaskItem`:
  - `Title` = `"Review expired evidence"`; `Details` names the evidence titles that expired (DisplayTitles style), e.g. `"Expired before 2026-06-10: Certificate X, Policy Y"`; `RequirementId` = the requirement; `DueDate` = today plus 1 day; `AssignedToUserId` = the requirement state's `OwnerUserId` when set, else the acting user id of the sweep (boot sweep: the owner-admin account if exactly one exists, else unassigned is NOT valid per `CreateTask`'s assignee rule, so boot sweep assigns the first active owner-admin); `CreatedByUserId` same rule. This detail should match how FIX-12's `CreateTask` validates assignees; the sweep builds rows directly, so mirror the validation honestly.
- `ExpirySweepResult(int suspendedLinks, Guid[] requirementsFlagged, Guid[] tasksCreatedOrKept)`.
- Second run same day: a task whose `Sources` marker exists stays, nothing new is created, and links already `Suspended` are not touched again (the candidate query excludes them).

### Step 3: task idempotence marker (migration)

Add a nullable string column `SourceKey` to `TaskItem` (migration `TasksSourceKey`). Convention: `SourceKey = "expiry:<requirementId>"` for sweep-created tasks; null for manual and recurrence tasks. Unique index on `(SourceKey)` WHERE `SourceKey IS NOT NULL` (SQLite partial index; EF Core: `e.HasIndex(t => t.SourceKey).IsUnique()` with a filter, or a filtered index via raw SQL in the migration if the EF API refuses on this provider). This is the recurrence-index pattern generalised. Record in migration comments why: the review task must survive to a second boot, and re-running the sweep on a later day must still find the open review task and add nothing.

Edge case the index covers: an expiry creates a review task, someone completes it, more evidence for the SAME requirement expires a year later. A completed (not open) task with the same SourceKey exists, so a plain "any task with this SourceKey" check would suppress the new review row. The idempotence check is therefore: no OPEN task with this SourceKey. Completing one lets the next sweep create a fresh one when something expires again. Say this in the Details or a code comment so the next reader knows it is deliberate.

### Step 4: boot and daily hook (desktop only)

In `App.axaml.cs` after the existing startup sequence (FIX-15 lock, Prepare, migrations; before or after first frame, either is fine, but AFTER migrations):

- Call the sweep once at boot through `Workspace.SweepEvidenceExpiry(DateOnly.FromDateTime(DateTime.Now))`.
- Hold a `DispatcherTimer` running every 24 hours that calls the sweep again; the timer goes away on shutdown with the app.
- Show nothing in the UI beyond the natural task list change. If the sweep throws: log (the `logs` dir under the data dir), show the existing boot-stop dialog only for database-level exceptions (already handled by FIX-15), and continue startup for data-independent errors.
- In demo mode the sweep must be a no-op against demo rows unless demo data carries expired items, in which case it behaves identically on the demo dir (harmless).

### Step 5: tests

Temp-dir `QSPP_DATA_DIR` or context-constructor tests only. New file `tests/QsppManager.Integration.Tests/EvidenceExpiryTests.cs`:

1. `Sweep_MarksExpiredLinks_SupersededStays`: two active links, one with `EffectiveTo` yesterday, one with a future date; sweep sets the past one `Suspended`, leaves the future one `Active`; a `Superseded` row with an expired item stays `Superseded`.
2. `Sweep_CreatesOneReviewTaskPerRequirement`: three expired links across two requirements; sweep returns exactly 2 tasks; details reference the expired titles.
3. `Sweep_IsIdempotent_SecondRun_NoAddsNoDuplicates`: second sweep same day: suspendedLinks = 0 again (already suspended excluded), tasks = the same 2 (count of open review tasks unchanged), no duplicate titles.
4. `Sweep_ExpiredThenCompletedThenNewExpiry_CreatesFreshTask`: complete the review task, expire another link on the same requirement later, sweep again: a NEW open review task exists. Covers the source-index semantics.
5. `Readiness_DropsWhenLinkSuspended_BySweep`: requirement was Ready via an expired link; after sweep the state reads readiness per the FIX-02 rule (NotReady/NeedsEvidence family). The shared predicate makes this pass trivially; the point is to prove the connection.

### Gates, in order

1. BUILD GATE: full `-t:Rebuild -p:DesignTimeBuild=false` rebuild, bare, record exit + "N Error(s)".
2. TEST GATE: filtered run (EvidenceExpiryTests, ReadinessTests family, WorkflowM2Tests), then FULL `dotnet test --no-build`; record N1 = N0 + 5.
3. RENDER GATE: not applicable (no new UI copy; the task title appears in existing lists). The standing re-render sweep runs at phase end when the display is unlocked.
4. PRIVACY GATE if any seed/docs touch: none planned.

### Commit

`fix(FIX-21): evidence expiry suspends links and opens review work` with a body naming: the shared predicate, the migration, the open-task-only idempotence rule and why, the boot + daily hook placement, tests before/after counts, render gate n/a statement. Append the execution-log block (fix id, commit, tests, gate tails, deviations: any deviation from this plan is written plainly in that block).

## Risks and stop conditions

- Partial-index support on SQLite: if the filtered unique index fails to create, fall back to a plain non-unique index plus an explicit open-task query inside the sweep; disclose in the execution log.
- The sweep assigning tasks: if the install has multiple owner-admins and no clear rule for choosing, the boot sweep assigns the requirement owner when set; otherwise the FIRST active owner-admin by display name; disclose as a documented choice.
- STOP and ask Hugh if: FIX-02 is not in the branch (stop condition in the backlog), or the shared predicate needs a semantic change (that would move FIX-02's contract).