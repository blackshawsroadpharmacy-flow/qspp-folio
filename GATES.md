# QSPP Folio: acceptance gates

Working repo: `<repo>` (canonical; branch `fix/audit-2026-10-26` for the audit fix phase,
from `design/premium-polish`). Dev data dirs are disposable scratch folders.
Solution: `qspp-manager.slnx`. Unless noted, run gates from the repo root with env:
`export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`

Last full-gate pass: 28 Oct 2026 audit fix phase (`docs/audit/execution-log-2026-10-28.md`).
A box is ticked only with recorded evidence; "not run" means it has not been re-verified
in the latest pass.

## G1 Build green
- [x] CHECK: `dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false` → exit 0, 0 errors
- EXPECT: "Build succeeded. 0 Error(s)"
- EVIDENCE: full rebuild at FIX-17 recorded "0 Error(s)", exit 0 (`/tmp/f17-build2.log`)

## G2 Tests green
- [x] CHECK: `dotnet test --no-build` → all pass
- EXPECT: 300 passed + 2 skipped (the 2 are opt-in tests that run only with
  `QSPP_REAL_CORPUS=1` / `QSPP_DUMP_TITLES=1`), 0 failed, exit 0
- EVIDENCE: full suite at FIX-17: 302 total, 0 failed, exit 0 (`/tmp/f17-testall2.log`)

## G3 App launches on disposable data
- [x] Launch with `QSPP_DATA_DIR` scratch dir + `QSPP_PACK_ROOT` env; process stays alive; window renders; sign-in works
- EVIDENCE: scratch runs through the phases (neutral scratch data dir `qspp-fresh`;
  delivered app boot-verified at the `5858779` release). Fix-phase re-checks were
  headless (suite only); the visual re-sweep is pending screen unlock.

## G4 Light/dark design system
- [x] Token set (forest ink, primary green, sage, warm paper, off-white, muted, acid) for Light + Dark
- [ ] Appearance switch verified by screenshot of same screen in Light and Dark (re-sweep pending)
- EVIDENCE: tokens shipped; both-theme contact sheets came from the 24 Oct sweep
  (`c528d53`). The next 10-screen × 2-theme sweep is blocked by the locked display.

## G5 Native macOS menus and shortcuts
- [x] Native menu bar has App / File / Edit / View / Window / Help menus
- [x] ⌘, Settings; ⌘K palette; ⌘F screen filter; ⌘P print; ⌘W closes window keeping app alive; ⌘Q quits
- EVIDENCE: code-built menus in App.Initialize, commit `37b5e9a`, verified during the
  menu work. Not re-shot this session (locked display).

## G6 Close vs quit behaviour
- [ ] Closing the main window keeps the app running; reopening from Dock brings it back
- [ ] ⌘Q (Quit) fully exits the process
- EVIDENCE: not run this pass (interactive, locked display).

## G7 Data location + migration
- [x] QSPP_DATA_DIR override honoured (dev flow uses it)
- [x] Migration: legacy dir with existing qspp.db auto-migrates, original retained via safety backup, copy verified (row counts)
- [ ] macOS default app-data dir probe re-run
- EVIDENCE: Prepare/backup/migration exercised by the suite (FIX-03 legacy backup paths,
  FIX-15 `StartupIntegrityTests`: WAL, quick_check, legacy stamp completing migrations,
  FIX-17 history-rows test). Default-dir probe: not re-run; the standing rule is never
  to touch the production data folder; run it against a sandboxed HOME when needed.
- NOTE: do not run the default-dir probe against the real pharmacy workspace; use a
  sandboxed HOME for that check.

## G8 Portable pack discovery
- [x] App runs outside the repo tree (packaged .app from a neutral location) with no QSPP_PACK_ROOT and discovers bundled pack
- [ ] Missing-corpus recovery via Settings re-imports without duplicating
- EVIDENCE: delivered `QSPP Folio.app` boots on a scratch data dir at the `5858779`
  release with the bundled pack (bundle-scan clean). Recovery path: not re-verified this pass.

## G9 System integration dialogs
- [ ] Finder reveal opens at the correct path
- [ ] PDF opens in system viewer; clearly labelled fallback exists
- EVIDENCE: not run this pass (interactive). The subprocess starts involved were
  hardened in FIX-18 (ArgumentList forms).

## G10 CUPS verified on macOS
- [x] `lpstat` discovery degraded-gracefully path exercised from tests
- [ ] Test job submitted + job recorded on a real printer (never equates submission with physical printing)
- EVIDENCE: CupsPrintService tests green in the full suite; FIX-18 hardened submission
  (15 s timeout, kill process tree on expiry, no auto-retry, honest PrintOutcome).
  Real-printer submission: owner-run check, pending.

## G11 Uniform conversion
- [x] All 10 destinations restyled with after screenshots each (24 Oct sweep, `c528d53`)
- [ ] Dialogs restyled to match (re-sweep pending)
- EVIDENCE: 24 Oct sweep contact sheets. Dialog pass + fresh 10-screen × 2-theme
  re-sweep: blocked by locked display; not re-run.

## G12 Accessibility & scaling
- [x] Text scale 100/115/130/150 renders and stays usable (verified in the M7 work)
- [ ] Keyboard-only tab order + focus rings + palette usable (re-check this pass)
- [ ] AutomationProperties names on primary controls (AX tree spot-check)
- EVIDENCE: scaling shipped and verified when built. Keyboard-only and AX spot-checks:
  not re-run this pass.

## G13 Packaging
- [x] Reproducible script builds `QSPP Folio.app` (osx-arm64) with Info.plist + green QSPP icon (.icns)
- [x] .app launches by `open` outside the repo; window usable
- [x] Signing status stated honestly: ad-hoc signed, not distribution-ready; notarisation pending Apple Developer enrolment
- EVIDENCE: `5858779` release (scrubbed bundle, 0 personal hits, boots on scratch).
  FIX-10 raised the helper floor to macOS 12 and made packaging always rebuild the
  helper; FIX-16 added the verify step, the ditto no-._ zip and the seed privacy gate.
  The ditto/verify steps execute at the NEXT packaging run (not re-packaged this session,
  per the execution prompt).

## G14 Docs
- [x] README, GATES.md, docs updated with actual evidence; ledger distinguishes implemented / tested / partial / blocked
- EVIDENCE: FIX-19 (this pass) rewrote README and GATES with neutral wording and real
  counts; `docs/audit/` carries the audit, the backlog and the execution log.

## G15 Final report
- [ ] Fix-phase report to the owner: per-fix table, open questions, skipped items, log path
- EVIDENCE: pending (end of the fix phase).