# Plan 010: no shared DbContext between UI and background work

Backlog item FIX-23, audit finding F-19. Effort L (phase 1). hermes_execution: plan-file. STOP for owner review before execution.

## Summary

One QsppDbContext instance serves the whole process: `App.Folio` is built with `(Db, Workspace, ...)` (`App.axaml.cs` line 195), `FolioWorkspace` takes that same context and the `QsppWorkspace` it wraps takes it too (its primary constructor is `QsppWorkspace(QsppDbContext db, ...)`). EF Core contexts are not thread-safe. Meanwhile the app DOES launch background work that reaches into that context:

Recon of every `Task.Run` call site (5 spawns, under the backlog's 8-site stop condition):

| Site | Background work | Reaches the shared context? |
|---|---|---|
| `PrintCentreView.cs:322` | compose requirement checklist PDF, `File.WriteAllBytes`, `App.Printer.RecordJob(job).GetAwaiter().GetResult()` | yes: `RecordJob` persists a PrintJob through the shared context |
| `PrintCentreView.cs:378` | same shape for the surveillance round pack (`ComposeRoundPack` + record) | yes |
| `ProcessSortView.cs:165` | `App.Folio.ApproveProposed(batchId, session)`, which imports through the normal pipeline | yes: `work.ImportEvidence` runs on `QsppWorkspace`'s context |
| `ProcessSortView.cs:197` | `App.Folio.ScanAndQueue(session, sourceDir, outputDir)` | yes: FolioWorkspace's own `db` field |
| `ProcessSortView.cs:219` | the per-file process loop (OCR + classify per item, progress posted to the UI thread) | yes: FolioWorkspace internal state writes |

No failure is reported in the wild yet (SQLite + serialized saves have been lucky), but the exposure is real and intermittent failures by their nature arrive late. This plan makes background work safe in two stages and stops (asks Hugh) before the big one.

## Stage 1 (this plan's commit): serialize EF access behind one process-wide gate

- Add a single `App.DbGate = new SemaphoreSlim(1, 1)` (or a small static `DbGate` type in `src/QsppManager.Infrastructure/`) and wrap EVERY background region in the recon table with `await App.DbGate.WaitAsync()` / `finally DbGate.Release()`. The `PrintCentreView` lambdas become async bodies that do the compose (pure, outside the gate), then gate the `RecordJob` plus adjacent db touches only.
- The UI thread's own db calls stay ungated where they already are (UI is single-threaded); any UI path that awaits background work already resumes on the dispatcher thread, so the gate only ever serializes overlaps.
- Honest framing for Hugh: this removes the crash/corruption exposure (no two threads inside the context at once) without restructuring anything. It does NOT yet make the codebase satisfy the backlog's literal goal ("Background work uses its own DbContext; no EF object crosses threads"): an object still crosses a thread boundary, just never concurrently with another user. For a single-user desktop app with one context this is a legitimate stable end state; the full factory rewrite is stage 2.

## Stage 2 (separate plan, requires Hugh's go-ahead; NOT begun under this plan)

Convert `QsppWorkspace` from a primary-constructor context to an `IDbContextFactory<QsppDbContext>` pattern: every public method opens its own short-lived context (or a workspace-owned scoped context per operation), UI queries stop sharing one long-lived instance, and the static `App.Db` goes away. That is an XL change across a file with hundreds of `db.` references, far beyond the backlog's "L" estimate once `QsppWorkspace` is included. This plan does not start it; if stage 1 lands and Hugh wants the full rewrite, a new plan (011) gets written first.

## Read this first (you have never seen this repo)

- Repo `<repo>`, solution `qspp-manager.slnx`, .NET 10, Avalonia UI, EF Core 10 / SQLite. Branch `fix/audit-2026-10-26`; one fix = one commit, `fix(FIX-23): <title>`.
- The FIX-15 connection string is `Data Source={path};Default Timeout=5;Foreign Keys=True`; WAL is persisted in the file header at `Migrate`. Tests must keep `Pooling=False` (carried lesson: sqlite pool reuse across test contexts breaks isolation).
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- **Production data is sacred.** Never read, copy, list or open `~/Library/Application Support/QsppManager/` or any `QsppManager-backup-*`. All runs use `QSPP_DATA_DIR=$(mktemp -d)`; tests use temp dirs. Never call `AppConfig.Load()` or `AppConfig.Save()` from a test.
- Never verify a build or test through a pipe. Run bare, then `echo "exit=$?"`. Record N0 (currently 302 total: 300 passed + 2 opt-in skipped) before and after.
- No em dashes in any text you write. Never print credentials. If a gate fails twice for the same reason, stop and report.

## Out of scope

- Any UI text, layout, or behaviour change. This is thread discipline only.
- `IDbContextFactory` registration and the `QsppWorkspace` rewrite (stage 2, above).
- Moving the per-file process loop OFF `Task.Run` (it already posts progress correctly via `Progress<T>` + `Dispatcher.UIThread.Post`).
- The OCR subprocess internals (FIX-18 already hardens those).

## Steps

### Step 1: the gate

A tiny static type in `src/QsppManager.Infrastructure` (visible to Desktop): `QsppDbGate` with `SemaphoreSlim Lock = new(1, 1)` and convenience `Task<T> RunAsync<T>(Func<T> work)` that acquires, runs, releases in finally. The gate lives on, process-wide, one permit. No per-context trick, no pooling.

### Step 2: call-site conversions (all 5 spawns, no others)

- `PrintCentreView.RenderChecklistAsync` (322) and `RenderRoundPackAsync` (378): compose stays on the background thread (pure); the `RecordJob` + write-then-record tail goes through the gate; the `.GetAwaiter().GetResult()` inside the lambda becomes `await q.RunAsync(() => App.Printer.RecordJob(job).GetAwaiter().GetResult())` or an async lambda so nothing blocks a thread inside the gate.
- `ProcessSortView` approve (165) and scan (197) and process loop (219): the whole call body goes through `RunAsync`. For the process loop this makes file OCR wait on db save points; a queued file cannot be processed while the UI thread holds... the UI thread does NOT hold the gate (its calls are ungated); contention between the loop and a UI db call is resolved by brief blocking of one side, bounded by single operations.
- Read-only query calls the lambdas make (e.g. `App.Workspace.*` fetches done BEFORE dispatching) stay on the UI thread before the Task.Run; moving reads inside the gate is allowed but not required; keep the diff minimal: gate exactly the bodies that today touch db state, list which reads were left outside and why in the execution log.

### Step 3: tests

`tests/QsppManager.Integration.Tests/BackgroundContextTests.cs`:

1. `Gate_SerializesConcurrentTouches`: two concurrent `RunAsync` calls increment a shared counter's guard (interleaved would double-count without the gate); assert final value and that the gate's CurrentCount returns to 1 after both complete and after a thrown work item (release on exception, no leak).
2. `Gate_ReleasesOnException`: a work function throws; subsequent RunAsync still completes (semaphore not starved).
3. `PrintJobs_RecordeFromBackground_Succeeds`: a test-context RecordJob submitted through the gate produces the persisted job row (proves the print path's gated shape end to end without the UI).

No attempt to write a race repro of the old code: the gate's contract tests plus the green full suite are the evidence.

### Gates, in order

1. BUILD GATE: full `-t:Rebuild -p:DesignTimeBuild=false`, bare, exit + "N Error(s)".
2. TEST GATE: filtered BackgroundContextTests + Cups tests, then FULL `dotnet test --no-build`; record N1 = 302 + 3 = 305 total.
3. RENDER GATE: not applicable (no UI change). Standing re-sweep at phase end when unlocked.

### Commit

`fix(FIX-23): serialize shared context access behind one gate (stage 1)` body: recon table (as above), stage-2 deferral note, deviation list, tests before/after. Append the execution-log block in the usual shape.

## Risks and stop conditions

- Deadlock risk is the one real hazard: no code inside the gate may synchronously wait on the UI thread (the process loop's `Progress<T>` posts are fire-and-forget `Post`s, safe; recheck each wrapped body for `Dispatcher.InvokeTask` style waits inside gate-held sections and move them out if found).
- STOP and ask Hugh if: any wrapped body needs more than moving UI waits out (structure change = stage-2 territory), a filtered/full run fails twice for the same reason, or the site count grows past the table above during implementation (new `Task.Run` discovered): report, do not widen.
- If Hugh answers that stage 2 should replace stage 1 outright, discard stage 1 and write plan 011 instead; effort stays XL, not L.