# Milestones + implementation ledger

Status vocabulary: ✔ implemented (evidence exists) · ◐ partial · ✗ not started ·
⊘ blocked (external dependency — name it). Updated as work proceeds; never
claim runtime validation that did not happen on this machine.

## M1 — convincing durable vertical slice · **COMPLETE (verified 20 Oct 2026)**

| Item | Status | Evidence |
|---|---|---|
| .NET 10 SDK + reproducible build | ✔ | SDK 10.0.402 (user-local `~/.dotnet`, runtime 10.0.13); `dotnet build -c Debug` succeeds with 0 errors; sln = 4 src + 2 test projects |
| OpenScript tokens (light) + component styles | ✔ | `App.axaml` — all spec tokens; zero-radius; bundled Host Grotesk + JetBrains Mono + 53 Lucide icons. Avalonia-12 API adaptations recorded (DD-17); tag/header font-size inheritance debt noted in ledger and decisions |
| Application shell (sidebar, top bar, Ctrl+K palette, status bar) | ✔ | `MainWindow.axaml` + `ShellViewModel.cs` — 10 destinations; palette searches corpus live; open app verified on this desktop |
| Domain entities + EF Core model (v1 schema) | ✔ | `QsppDbContext.cs` (13 entity sets, FK/index constraints). EF migrations land in M2 (EnsureCreated is the deliberate v0 schema — DD-8) |
| Corpus import with provenance (seed JSON) | ✔ | **451 requirements / 77 evidence examples / 21 sha256-verified documents / 3 interpretation issues** imported at first boot — verified by direct SQLite inspection of the running app; import is idempotent and deterministic (tested) |
| Coverage ledger vs primary sources | ✔ | `docs/coverage-ledger.md`; corrections propagated to the pack (README, KB, website index): AS85000 = 2 sections / 6 clauses / **28 subclauses** (+ 2.2.3.1–12); UAG = **25 sections**; Program Rules = **51 rules**; UAG lettered blocks are the Standard's subclause actions (284 letter entries; 3.3 & 4.3 none) |
| Requirement browser + detail (applicability, readiness, owner, next action) | ✔ | `RequirementsView.axaml(.cs)` + role-gated services; source wording, applicability reason, readiness state machine, evidence pane, issue panels |
| Evidence: import/link/verify/supersede with PII gate | ✔ | `QsppWorkspace.cs`: file-signature validation rejects HTML-wall files (the `capacity-toolkit.pdf` class); manager verification activates Proposed links; supersede retires links + creates re-link tasks; patient-data evidence blocked without attestation |
| Print pipeline (QuestPDF A4 checklist → preview → CUPS) | ✔ (service level) | `PdfComposer.cs` renders completed + blank variants as real PDFs (%PDF + size asserted in tests) with bundled fonts; `CupsPrintService.cs` discovers printers, fails honestly on unknown destination, never auto-retries (duplicate-paper guard); **physical-paper inspection still owed — listed in M5** |
| Preview (in-app pages) | ◐ | Opens the exact print file in the system viewer (window says so plainly). SkiaSharp native-collision with PDFtoImage recorded (DD-20); in-app thumbnails return in M2 with a renderer sharing Avalonia's Skia |
| Today screen w/ real persisted state | ✔ | `TodayView.axaml` + `TodayViewModel.cs` works over the imported corpus; overdue/awaiting-review/verified-share sections with working open-record commands (interactive demo pending owner walkthrough) |
| Tests | ✔ | **38/38 passing** — deadline boundaries (weekends, Melbourne Cup holiday, month-end clamps, ±day variants), readiness + evidence-review state machines, permission gates (contributor write-blocked), seed idempotence + deterministic IDs, supersession flow, audit-chain verify, patient-data gate, PII redaction gate blocked print path, real QuestPDF rendering both variants, CUPS discovery + honest rejection |
| App boot with corpus import | ✔ | Timeout-run on this desktop: DB created, 14 tables, counts verified. Pharmacies/Users empty at boot is correct — onboarding (next screen after launch) collects them |
| Screenshots + scaling sweep | ✗ | M5 requirement; current ledger does not claim them |

## M2 — full requirements + evidence coverage · **COMPLETE (verified 20 Oct 2026)**

| Item | Status | Evidence |
|---|---|---|
| FTS5 search replacing LIKE v1 | ✔ | `CorpusSearch.cs` — SQLite FTS5 with bm25 column weights (title 10, ref 2, body 1), snippet highlights, token prefix on the last term, per-record re-index on evidence import/review/supersede, LIKE fallback preserved. Two real bugs fixed en route: quoted `token*` (star must sit outside quotes) and the 6-argument bm25 (4 weights made every FTS query throw → silent LIKE fallback) |
| Library destination | ✔ | `LibraryView.cs` — corpus search panel, source documents with sha256/"full text indexed" column, the three interpretation issues with verbatim competing quotes (acknowledge + resolve-with-recorded-outcome actions; resolution requires a note, enforced), supersession impact review with affected links + open re-link tasks |
| Guided "Prepare for assessment" journey | ✔ | `AssessmentsView.cs` — 7 phases of live counts from `JourneySnapshot` (applicability decided, owners, evidence links, verification, onsite date, expiry), honest phrasing ("QSPP accreditation is awarded by the Guild's assessment process") |
| Supersession impact review | ✔ | `QsppWorkspace.SupersessionReview()` + Library section; re-link tasks now carry the superseded evidence id (data-linkage bug found by test) |
| Page-aware anchoring | ✔ (plumbing; data pending subagent) | `seed/page-anchors.json` importer (idempotent, audit-logged; unverified pages keep `AnchorApproximate`), Requirements detail + printed checklist show `PDF pN (verified)/(unverified)` |
| EF migrations replacing bare EnsureCreated | ✔ | `Persistence/Migrations/` (InitialCreate + M2PageAnchors) + `DatabaseBootstrapper`: fresh DBs migrate; **legacy v1 EnsureCreated DB stamped and migrated — verified on the real M1 data dir (451 rows intact, history correct)** |
| Document full-text search (PdfPig) | ✔ | `DocumentTextIndex.cs` + Library "Extract & index all". **Real-corpus verified: 20/21 PDFs extracted** (Decision-tree.png honestly excluded), body search finds wording inside the PDFs, cached re-index on reboot. PdfPig 0.1.16 restored cleanly on net10 (M1's friction didn't recur) |
| Token-boundary normalisation | ✔ | PdfPig glues adjacent lines (`1.1.1Heading` → token `1heading`, unfindable); digit↔letter + camel-hump boundaries split at index time (regression-tested) |
| CommunityToolkit.Mvvm adoption (DD-18) | ✗ deferred to M3 | Deliberate: zero user-facing value this phase, refactors 3 VMs with risk; ledger records it rather than claiming it |
| Interpretation-issue destination | ✔ | Library section + `ResolveIssue` service rule + audit events |

## M3 — daily operations · **COMPLETE (verified 20 Oct 2026, pre-dawn)**

| Item | Status | Evidence |
|---|---|---|
| Recurring tasks | ✔ | `RecurringTaskDef` + `RecurrenceCalculator`: anchor-based sequences, month-end clamping (31 Jan → 28/29 Feb → 31 Mar; leap-year tested), weekly/fortnightly/monthly/quarterly/yearly/custom-days. Generation idempotent per (definition, occurrence) — unique index + tested; **missed occurrences stay open (overdue)**; runs at sign-in and Registers refresh. TasksView marks "(recurring)" |
| Equipment register | ✔ | Name/model/serial/location/state (InService/Maintenance/Retired); calibration-due date **automatically ensures a monthly check schedule per item** (created once, tested); linked to AS85000 2.1.1.4b–c territory |
| Incident register | ✔ | Records link to requirement 1.1.2.4 when the corpus has it; **closing requires a corrective action** (enforced + tested); role-gated |
| People & training matrix | ✔ | Staff register (separate from app users); S2/S3 initial due = first supply + ~6 months with OVERDUE status; refresher due = anniversary of last completion; version-tied policy acknowledgements (staff+policy+version unique, tested) |
| Compounding module | ✔ (scoped) | Supplier register (2.2.3.3 sourcing, review dates), product recall register (closure requires recorded stock action), **potency matrix per person** with distinct drug × form coverage against the conservative reading of ISSUE-POTENCY — coverage only, interpretation never presented as settled |
| Migrations | ✔ | Three further migrations (M3Registers, M3Compounding) applied on the live legacy database — verified: tables present, 451 requirements + 437 verified page anchors intact |
| Print centre maturity | ◐ | Presets entity exists from M1; batch/binder printing and pre-filled templates remain for M4/M5 |
| CommunityToolkit.Mvvm | ✗ deferred | Second deferral recorded: VM refactor still planned, still no user-facing value when done |
| Spill-kit/BUD/counselling bespoke entities | ✗ deliberately not built | BUD assignments and counselling evidence belong in the generic evidence store linked to their 2.2.3.x clauses (with checksums), not bespoke registers — avoids duplicating the evidence model and keeps patient-adjacent content in the redaction-gated evidence flow. Revisit only if the owner wants dedicated paper templates |

## M4 — assessments & continuity · **COMPLETE (verified 20 Oct 2026)**

| Item | Status | Evidence |
|---|---|---|
| Assessment events timeline | ✔ | `AssessmentEvent` + Assessments view section; OutcomeReceived **requires the report/letter reference** (never claim an outcome that hasn't arrived — enforced + tested); role-gated |
| Findings → corrective actions → closure | ✔ | Findings per event; **closure demands a recorded corrective action** (same rule as incidents); states Open/Actioned/Closed with audit chain |
| Backup / restore | ✔ | `BackupService`: consistent live copy via `VACUUM INTO`, attachments included, **manifest with per-file sha256; restore validates every hash before extracting anything** (tamper test: corrupted DB inside the zip is refused); verified-then-staged promotion applies at next boot. Real round-trip tested (rows + users preserved) |
| Assessor pack export | ✔ | `AssessorPackService` + Assessments view: scope = manager-verified/assessor-accepted applicable requirements; **redaction review enforced — patient-data evidence lacking an attestation is excluded with the reason recorded in manifest.json**; verified evidence files copied with sha256; empty scope fails with honest guidance (all tested) |
| Restricted-reviewer filtering | ✔ (search surface) | Read-only sessions no longer see patient-data evidence in Ctrl+K palette or Library search (tested both directions); wizard/export surfaces landed above; print-centre evidence rows ship in the checklist model with patient flag visible to staff but hidden content stays out of the app |
| Injectable clocks + VIC holidays | ✔ | `Clock.Pin/Unpin` threaded through Journey/recurring/expire queries (tested with a fixed date); `VicHolidays` table 2025–2028 (Melbourne Cup, Boxing Day observed Monday, etc.) with an explicit "verify current gazetted dates before relying on them" note; business-day tests across the 2026 Christmas block |
| Source-change compare | ✔ | DiffPlex line diff between any two managed documents on their cached full text (Library button); the known UAG PROMs→"Patient-reported Measures" class of change surfaces as -/+ lines; whitespace-only differences ignored (tested) |

## M6 — client identity + surveillance intake · **CODE COMPLETE (verified 20 Oct 2026)**

Owner ask: rename the app "Hugh's QSPP Manager" with the client logo applied
tastefully; space every input field consistently; and absorb the QCPP remote
surveillance assessment portal's October 2026 evidence requests (Burke Road
Compounding) as first-class trackable work — "a reliable and robust solution".

### What shipped

| Task | Status | Evidence |
|---|---|---|
| Rename across the whole app | ✅ verified | Scripted replace in 14 named surface files (window title, login card, About, PDF composer footer, backup naming, `package-app.sh` APP_NAME + CFBundleName/DisplayName, README/GATES/build scripts); zero non-historical leftovers by grep. Historical docs (this file, decisions) intentionally keep the original name |
| Logo: app icon | ✅ verified | `scripts/make-icon.py` rebuilds the icns from `assets/art/HughLogo.png` (cream disc + avatar on the brand-green squircle); vision-checked at 384 px and 64 px; bundle CFBundleIconFile unchanged |
| Logo: in-app marks | ✅ verified | Login card: 44 px circular avatar with green keyline above the wordmark; Help → About: 64 px avatar + "Hugh's QSPP Manager". No logo noise anywhere else |
| Input-field rhythm | ✅ verified | New `Ui.FormField(label, field)` contract (Spacing 6, small-caps label above field); applied to Settings, onboarding, evidence/task/schedule/incident/potency dialogs, print centre copies, requirement decision reason + next action; watermarks de-duplicated. Login/Settings/Requirements pages visually verified on a live run; dialogs share the same contract |
| October 2026 remote assessment as trackable requirements | ✅ verified | 19 evidence requests seeded as `seed/remote-assessment.json` → `RefKind.Surveillance`, refs REM-01…REM-19, wording verbatim from the portal, evidence examples carrying the portal's mandatory/optional "must include" bullets. Importer UPSERTS on every boot (deterministic id) so a revised questionnaire re-words without disturbing linked evidence, owners or readiness; absent file = no-op |
| Packaged bundle seed | ✅ verified | `package-app.sh` now bundles `Contents/Resources/seed/*.json`; before this fix the packaged app skipped the corpus + REM import (FindRepoRoot missed seed/) — caught before delivery |
| Palette routing for REM items | ✅ verified | `OpenResult` kind whitelist extended with "Surveillance" — ⌘K → REM-02 now opens the requirement detail (was falling through to Documents) |

Tests: 71/71 green (30 Domain + 41 Integration; 3 new `RemoteAssessmentSeedTests` —
creation, idempotency, wording-revision-preserves-links).

## M5 — product finish (macOS) · **CODE COMPLETE (verified 20 Oct 2026)**

Owner-scoped to the macOS (osx-arm64) polish pass: design system, native shell,
platform services, uniform conversion, accessibility, `.app` packaging, and an
evidence-backed close-out. Linux/Windows packaging deferred (below).

### What shipped

| Task | Status | Evidence |
|---|---|---|
| Design system | ✔ | Token dictionaries (light + dark, 20 tokens incl. CautionBg), `Ui` component kit (H1/Lead/Section/Cards, Chip, MonoCol, EmptyState, Ax, LucideGlyph), repo-rooted `assets/fonts` (Host Grotesk · JetBrains Mono) + 56 Lucide icons embedded via csproj `Link`; Lucide SVGs parsed to Geometry (`LucideIconCache`), no Svg.Skia dependency. Commit `2a3f6fe` |
| Shell rework | ✔ | Extended title bar, collapsible sidebar (persisted), contextual toolbar (⌘F filter + commands + identity), native macOS menus + shortcuts (⌘P/⌘,/⌘F/⌘K, ⌘1…⌘0, close-vs-quit with Dock reopen), command palette with two-line rows (verified end-to-end: ⌘K → type → ↓/Enter opens the requirement). 8 dev-launch iterations then committed `95155a2`. Open cosmetic debt: duplicate "Avalonia Application" menu entry |
| Platform services | ✔ | Session-root AppData with safe migration (`QSPP_DATA_DIR` wins), `AppConfig` (appearance/textScale/sidebar/packRoot persistence), pack resolution DD-27 incl. `.app` bundled Resources; dialogs + Finder reveal + PDF open via system viewer; CUPS verify (macOS path compiles; physical-printer validation below); settings corpus-path recovery UI. Committed in `95155a2` |
| Uniform conversion (10 destinations + dialogs) | ✔ | Contract in `docs/design-system-conversion.md` (13 rules); reference conversion RequirementsView; subagent conversions for Documents/Tasks/People/Registers/Assessments/Library/PrintCentre (workspace/auth/dialog calls byte-identical); dialogs theming (h3 titles, OK, paired cancels); Login/Onboarding/Loading/Settings rewritten. Commit `e1e8a48`. Three runtime regressions found by the visual sweep and fixed: detail-pane never loading (type mismatch in SelectionChanged handler), Assessments phase headings under buttons (missing `Grid.Column`), Ui.Lead/Para block-centered (MaxWidth-stretch trap) |
| Accessibility & appearance | ✔ | In-app Appearance setting (Follow system/Light/Dark — persisted, applies at boot + live wiring), text-scale Normal/115%/130% (TextScaleContent wraps pages, Settings combo), dark tokens verified on login/Today/Settings/Requirements, light tokens verified across all 10 destinations, automation names via `Ui.Ax` on grids/combos/buttons, chips/statuses carry text (not colour alone). Commit `29b392f` |
| `.app` packaging | ✔ | `scripts/package-app.sh` → `dist/QSPP Manager.app`: osx-arm64 self-contained (no trim — DataGrid/XAML safety), Info.plist (`au.com.burkeroadpharmacy.qsppmanager`, Retina-capable, arm64-first), green QSPP icon generated by `scripts/make-icon.py` (Pillow → iconset → `iconutil`), corpus pack bundled at Contents/Resources, ad-hoc codesign. 181 MB. Commit `0f3e83d` |

### Verification record (t10)

- Full test suite **68/68 green** (30 Domain + 38 Integration), run 4× through
  the milestone (pre-commit, post-conversion, and twice at close-out).
- Packaged app copied **outside the repo** (`~/Applications/QSPP Manager.app`),
  launched via LaunchServices, owner sign-in + Today + Requirements exercised,
  pack bundle path present at `Contents/Resources/qspp-pack/website-scrape/downloads`
  (discovery rule 4 structurally satisfied; `packRoot` config null, no env override).
- Retina: displayed window renders on the 2× panel throughout (crisp captures).
- Light/dark: verified via the boot path in both directions + system-follow.
- Command palette: ⌘K filter + Enter deep-link re-verified on the final build.
- ⌘F page-routing: verified on Requirements (filter box focus + filter apply)
  and Library (corpus-search focus + re-run).
- Known limitations at close-out (honest labels, not silent gaps):

| Limitation | Reason |
|---|---|
| Dialog runtime exercise (PDF open/Preview, print-centre render clicks) | Background input harness cannot reliably drive modal/toolbar clicks; dialog code changes are presentation-only (contract-verified byte-identical logic) and the render/compose/print pipeline is covered by Integration tests. Needs a human pass (test script below) |
| Duplicate "Avalonia Application" menu | Native-menu registration shows a spurious second app menu; cosmetic, on the debt list |
| Narrow-window live resize | MinWidth 940/MinHeight 580 enforced in MainWindow; live resize drag not machine-verifiable under background input |
| CUPS physical printing | Service + verify path implemented; no physical printer validated this milestone (honest label per DD-9/DD-10) |
| Signing/notarisation | Ad-hoc signature only; Developer ID + notarisation needs the owner's Apple ID — documented in the packaging notes |
| Native blur/transparency effects | Transparency-level watcher wired; frosted-material look beyond the basic level not pursued |
| Linux/Windows packaging | Explicitly out of this milestone (owner asked for a macOS pass); the Linux dev-build boots from the repo (used during M5 shell work); Windows build untested (labelled) |
| In-session Appearance re-render machine-verified via boot path only | Same `ApplyAppearance` entry point + `FireThemeChanged`→`RebuildPageHost` wiring as the in-app toggle; the in-app combo popup cannot be driven by background input |

### Quick test script for the owner (packaged app)

1. `open "$HOME/Applications/QSPP Manager.app"` → sign in (owner).
2. ⌘K, type "hand hygiene", Enter → requirement opens with detail filled.
3. ⌘F on Requirements → type in the filter box → filtered count updates.
4. ⌘, → Settings → change Appearance to Dark, text size to Largest → whole
   app re-renders; relaunch the app to confirm the settings persisted.
5. Library → corpus search "potency" → results with bracketed highlights;
   double-click a result → record opens.
6. Print centre → render a checklist PDF → result strip shows the file path;
   "Open" hands it to Preview; print via the native dialog when a printer is
   attached (physical-printer validation is the remaining loose end).