# Hugh's QSPP Manager — M5 macOS modernization: acceptance gates

Working repo: `/Users/hughn/AI/qspp-manager-omarch/qspp-manager` (branch `master`).
Dev data dir (disposable): `$HOME/.hermes/cache/scratch/qspp-dev-a`. Pack: `/Users/hughn/AI/qspp-manager-omarch/vendor/qspp-pack`.
.NET SDK 10.0.402 at `$HOME/.dotnet` (export PATH + DOTNET_ROOT per shell). Solution: `qspp-manager.slnx`.

Unless noted, run gates from the repo root with env:
`export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`

## G1 Build green
- [ ] CHECK: `dotnet build qspp-manager.slnx -c Debug --nologo` → exit 0, 0 errors (warnings tolerated, list any new ones)
- EXPECT: "Build succeeded. 0 Error(s)"

## G2 Tests green
- [ ] CHECK: `dotnet test qspp-manager.slnx -c Debug --nologo` → all pass
- EXPECT: "Passed! - Failed: 0" with passed count >= 68 (may grow if new tests added)

## G3 App launches on disposable data
- [ ] Launch with `QSPP_DATA_DIR` scratch dir + `QSPP_PACK_ROOT` env; process stays alive; window renders; sign-in works with test account
- EVIDENCE: pending

## G4 Light/dark design system
- [ ] New token set (forest ink, primary green, sage, warm paper, off-white, muted, acid) defined for Light + Dark theme variants
- [ ] Appearance switch verified by screenshot of same screen in Light and Dark
- EVIDENCE: pending

## G5 Native macOS menus and shortcuts
- [ ] Native menu bar has App / File / Edit / View / Window / Help menus (screenshot)
- [ ] ⌘, opens Settings; ⌘K opens command palette; ⌘F focuses current screen's search/filter; ⌘P routes to Print centre render; ⌘W minimises/closes window keeping app alive; ⌘Q quits app
- EVIDENCE: pending

## G6 Close vs quit behaviour
- [ ] Closing the main window keeps the app running; reopening from Dock brings it back
- [ ] ⌘Q (Quit) fully exits the process
- EVIDENCE: pending

## G7 Data location + migration
- [ ] macOS default app-data dir = `~/Library/Application Support/QsppManager` (verified only when launched without QSPP_DATA_DIR override, using a copy-safe probe)
- [ ] QSPP_DATA_DIR override still honoured (dev flow uses it)
- [ ] Migration: legacy dir with existing qspp.db auto-migrates to new dir, original retained, copy verified (row counts)
- EVIDENCE: pending
- NOTE: do not run the default-dir probe against the real pharmacy workspace; use a sandboxed HOME for that check.

## G8 Portable pack discovery
- [ ] App runs outside the repo tree (packaged .app from a neutral location) with no QSPP_PACK_ROOT and discovers bundled seed/pack (or falls back gracefully)
- [ ] Missing corpus locations are recoverable via Settings; recovery attempt re-imports documents without duplicating
- EVIDENCE: pending

## G9 System integration dialogs
- [ ] Finder reveal opens at the correct path
- [ ] PDF opens in system viewer; clearly labelled fallback exists
- EVIDENCE: pending

## G10 CUPS verified on macOS
- [ ] `lpstat` discovery exercised from the app on macOS (real result recorded, honest if none)
- [ ] If a printer exists: test job submitted + job recorded; never equates submission with physical printing
- EVIDENCE: pending

## G11 Uniform conversion
- [ ] All 10 destinations restyled (Today, Requirements, Evidence/docs, Tasks/calendar, Registers, People, Assessments, Library, Print centre, Settings) with after screenshots each
- [ ] Dialogs restyled to match
- EVIDENCE: pending

## G12 Accessibility & scaling
- [ ] Keyboard-only: tab order, focus rings visible, command palette usable
- [ ] AutomationProperties names on primary controls (spot-check AX tree)
- [ ] Text scale 100/115/130/150 renders and stays usable (screenshot at 150)
- [ ] Status/overdue never colour-alone (text labels accompany colour)
- EVIDENCE: pending

## G13 Packaging
- [ ] Reproducible script builds `QsppManager.app` (osx-arm64) with Info.plist + green QSPP icon (.icns)
- [ ] .app launches by `open` outside the repo; window usable; screenshot
- [ ] Signing/notarisation status stated honestly (expected: unsigned ad-hoc or unsigned, not ready for distribution)
- EVIDENCE: pending

## G14 Docs
- [ ] README, docs/milestones.md, docs/decisions.md, docs/dependencies.md updated with actual evidence; ledger distinguishes implemented / tested / partial / blocked

## G15 Final report
- [ ] What changed; before/after screenshots; build/run instructions + .app path; test results; limitations (native effects, signing, notarisation, physical printer)