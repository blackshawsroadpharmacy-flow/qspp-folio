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

## M6.1 · owner-noise pass (owner feedback 20 Oct 2026) · **COMPLETE (verified)**

After delivery the owner flagged the 470-requirement wall of "Owner not assigned"
chips as overwhelming. Shipped, all live-verified:

- List rows are quiet: no owner text at all; a per-row note appears only when a
  requirement has evidence or open tasks ("2 evidence · 1 open task"). Ownership
  became a detail-page fact, not a list-side repetition.
- Detail page: unassigned shows muted "No owner yet" text with a hover tooltip
  explaining what it means; one-click "Assign to me" button claims ownership
  (audit-logged per requirement). Assigned shows "Owner: <name>" with its own tooltip.
- Bulk claim: "Assign N unassigned to me" button above the list (hidden when none
  remain, and read-only reviewers never see it), assigning every unassigned
  requirement to the signed-in user as ONE audit line
  (`requirement.owner.bulk, assigned=470`) so the audit trail stays short. New
  `QsppWorkspace.AssignAllOwnersToUser` get-or-creates lazy state rows, so a fresh
  import's backlog is claimable in one click.
- Chose explicit claim over "default the owner on import": silent auto-assignment
  would state accountability nobody recorded; one deliberate click does the same
  with an audit trail. The owner's alternative ask (different colour + hover hint)
  is also in: the quiet state is muted with the tooltip.

Tests: 74/74 green (3 new `OwnerBulkAssignTests`: bulk claims all + single audit
line, independent re-assignment after bulk, read-only refusal).

## M7 · surveillance round response workflow · **CODE COMPLETE (verified 20 Oct 2026)**

### What shipped
- **Today card "Surveillance round"** (hidden when no REM items): the real counts
  (`{items} evidence requests are open with the QCPP remote assessment portal. {n}
  have evidence attached, {n} are in progress, {n} verified.`) plus the nearest
  PLANNED assessment event as the round's response window if one is recorded.
  "Work through the items" deep-links to Requirements with the search pre-filtered
  to `REM-` (`ShellViewModel.OpenRequirementsFiltered`).
- **Round brief in code**: `QsppWorkspace.SurveillanceRound()` (counts + next planned
  event) and `SurveillanceRoundPackItems()` (per-REM rows built from the same read
  paths as the UI, so the printed pack can never disagree with the app).
- **Print centre → "Remote assessment round pack"**: one click composes every REM
  evidence request into one dated A4 PDF — verbatim ask, plain English, expected
  evidence (required vs example), recorded notes and next action, readiness/
  applicability/owner line, linked evidence table with review states, sign-off
  block, honest footer ("Printing a document does not approve it"). Recorded as a
  `RemoteRoundPack` print job; preview + "Send to printer" + "Open PDF externally"
  work on the last rendered file as usual.
- **Fixed en route — latent Today bug**: `TodayViewModel` had no change
  notification, so every scalar card (overdue count, verified share) bound its
  pre-Refresh default ("0 of 0"). Now derives from `ViewModelBase` with
  `SetProperty` — the M6.1-verified counts finally display as data arrives
  (visible live: MANAGER-VERIFIED jumped to "0 of 470" as soon as refresh moved).
- **Fixed en route — thread affinity**: the round pack read `_printer/_copies/
  _duplex` inside `Task.Run`, throwing "calling thread cannot access this object";
  all UI reads now happen before the background compose.

Tests: 77/77 green (3 new `SurveillanceRoundTests`: summary counts match real
states + finds the planned event; zero-REM rounds are empty; round pack composes a
real >10 KB PDF with zero audit/state side effects).

### Verification record
- Live: dev app relaunch, sign-in, Today card shows 19 requests / 0 with evidence /
  0 in progress / 0 verified + "No planned assessment event recorded."; deep-link
  lands on Requirements with `REM-` filter showing exactly 19 rows; round pack
  rendered "19 REM items in one PDF" and the exact PDF (6 pages, 153 KB) opens in
  macOS Preview; page 1 visually checked (header, summary, item blocks, bullets).
- sqlite: one `PrintJobs` row `RemoteRoundPack | Remote assessment round pack — 19
  items | State=Prepared`; zero new requirement/audit writes from rendering.

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

## M7.2 · full source wording + proportional detail page · **CODE COMPLETE (verified 22 Oct 2026)**

Two problems from the owner's Requirements detail screenshot: the verbatim quote was
cut mid-sentence at 400 characters (M1 `trim400`), and the detail page wasted the
window (four oversized tab headers wrapped into a giant orphan line, the excerpt box
stayed small, the tab content capped at 560px).

| Item | Status | Evidence |
|---|---|---|
| Corpus wording enrichment: all 44 truncated quotes re-derived as the requirement's FULL anchored segment (Rule 10: 400 → 879 chars, clean sentence end) | ✔ | `build/enrich_wording.py` + enriched `seed/requirements.json`; per-item containment gate (old quote ⊂ new segment) — 44/44 validated, 0 forced |
| Boot-time wording refresh for already-imported databases: wording-only upsert by deterministic id, one audited `corpus.wording_refresh` line, zero-diff boots write nothing | ✔ | `SeedImporter.WordingRefresh()`; 3 integration tests (`CorpusWordingRefreshTests`); live on the owner's real DB: `updated=44` at first boot, chain verified |
| Detail-page text areas expand and contract with their content (excerpt MinHeight 96/cap 480, reason + next action cap 280, notes cap 340); only extreme lengths scroll internally | ✔ | `RequirementsView` M7.2 edits; live boot |
| Detail tabs compact (13.5px, Fluent `TabItemHeaderFontSize` override), tab content pane 560 → 900, result bar capped 110 with internal scroll | ✔ | `App.axaml` + `RequirementsView` |
| Tests | ✔ | 84/84 green (was 80/80) |

Honest label: the regen extraction used fresh `pdftotext`; anchor line ranges for 4
files were re-located by content (recorded in the seed's anchor note); the 13 hitlist
items and obligations keep their original complete short wordings (analysis text, not
source quotes). REM-01…19 wordings were never truncated.

## M7.3 · passphrase resets · **CODE COMPLETE (verified 22 Oct 2026)**

The owner forgot the workspace passphrase. A passphrase is PBKDF2-hash-only (it can
never be shown again — only replaced), and the app had no reset path at all. Shipped
three, all audited by distinct actions so the audit always answers "who set which".

| Item | Status | Evidence |
|---|---|---|
| Login screen "Forgotten your passphrase? Reset it" flip-panel: typed confirmation phrase (`RESET MY PASSPHRASE`), new passphrase + repeat; success message in brand green | ✔ | `LoginView.axaml(.cs)`; live on the real DB |
| `AuthService.ForgotPassphraseReset` — workstation safety net: exact confirm phrase, 8+ chars, distinct loud audit (`user.passphrase.forgot_reset`, actor `workstation`); roles/data untouched | ✔ | 4 `PasswordResetTests` including refused wrong-phrase attempts and data-intactness |
| `AuthService.ResetStaffPassphrase` + Settings "Set/reset a passphrase…" (Owner-admin only) | ✔ | service + UI; non-owner attempt refused in test |
| `AuthService.ChangeOwnPassphrase` — signed-in change requires the CURRENT passphrase; old stops working immediately | ✔ | test: old login fails, new works, role/data unchanged |
| Packaged app redistributed with the reset flow | ✔ | M7.3 package; real-DB reset applied, hash round-trip verified, audit chain intact (29 events) |

## M8-A · Process and Sort — local pipeline core · **CODE COMPLETE (verified 22 Oct 2026)**

**Owner brief:** turn a directory of pharmacy documents into an organised, reviewable QSPP
evidence folio — entirely local, with classification targets including the standing
requirements and the REM-01…19 surveillance asks. (Full plan: `docs/plans/2026-10-22-process-and-sort.md`.)

| Item | Status | Evidence |
|---|---|---|
| In Evidence & documents: "Process and Sort…" — pick source + output folder, scan summary, run, per-file progress list, cancel (stops after the current file, batch resumable), review queue, publish, open result | ✔ | `ProcessSortDialog`/`FolioApproveDialog`; approval reuses the manual evidence flow exactly — links start Proposed, nothing self-verifies |
| Scan: recursive, extension-vs-signature validated, size guard, bookkeeping files ignored, unsupported kinds listed honestly (DOCX joins in a later stage; never guessed) | ✔ | `FolioScanner`; tests incl. HTML-named-.pdf rejection and oversized reasons |
| Extraction: PdfPig digital PDFs with per-page text cached by sha256 (`folio-cache/`); effectively-empty pages (scans/images) route to the OCR queue instead of being mis-filed — OCR tiers land in M8-B (Apple Vision light tier; benchmarked MinerU heavy tier; local only) | ✔ | `FolioExtractor`; routing asserted by test, never silent |
| Deterministic classifier v1 vs the live corpus (wording, summaries, evidence examples, REM refs): exact-ref hits, full-title phrase, weighted token overlap; abstains below threshold; flags "multiple" when top scores tie within 15% — every suggestion records `deterministic-v1` + version | ✔ | `FolioClassifier`; namespace-confusion tests (Std 1.1.2 vs UAG 1.1.2 never swap), REM-05 direct-hit test, abstention test |
| Sensitive gate BEFORE proposals: patient-identifier/personnel/credential heuristics hold the file for review; attaching still demands the explicit redaction attestation (existing rule, unchanged) | ✔ | `SensitiveDetector`; attestation-refusal test |
| Transactional folio publish: staging → sha-verify → publish via APFS clonefile (hardlink only from the managed copy; copy fallback cross-volume); sources never moved or deleted; manifest.json records original path, sha, managed evidence id, parser + model/version, proposed + approved destinations, review state, errors | ✔ | `FolioStager` + tests (clone verified + source untouched; checksum-mismatch aborts publication) |
| Resumable, failure-isolated batches: per-item state persisted; one broken PDF isolates to its own row while the rest completes; re-runs recognise unchanged files by sha and import nothing new | ✔ | corrupt-PDF, interruption/resume and duplicate-recognition tests |
| Raster OCR tier live: `qspp-ocr` Swift helper (tools/ocr/, Apple Vision `.accurate` + PDFKit render, auto-rotation recovery, 200 DPI, per-run wall-clock timeouts 90/240 s, JSON stdout) wired into the extractor and batch; images and scanned PDFs OCR locally then classify — cached by sha256+engine so repeats are free; without the helper items stay queued with the reason, never mis-filed | ✔ | 5 new OCR tests on versioned raster fixtures (plain, rotated 90°, 2-page scan); helper runs Vision on-device, no network |
| Benchmarks (this machine: M5-class 24 GB, macOS 26.6): 1-page PNG 0.28 s / ~76 MiB peak RSS; rotated PNG 0.28 s / ~80 MiB; 2-page scan PDF 0.52 s / ~159 MiB; 10-page scan PDF 1.61 s / ~500 MiB peak (page rasteriser transient) | ✔ | `/usr/bin/time -l` on the committed fixtures; per-hardware rows to grow as other Macs are exercised |
| Local typed-decision stage-2 live (`FolioDecisionStage2`): pinned `laya-apple==1.7.0` (`tools/decision` uv venv, Python 3.12) serving `convaiinnovations/laya-typed-decisions@f9ab0b228f0f` (843 MiB checkpoint, sha256-verified by the downloader) on loopback 8642. Client sends ONLY the title + ≤1.2k-char excerpt + candidate descriptions (never an OCR dump) as a typed `choice` question with `multiple`/`abstain` options; loopback pinned in code; the app connects when a server is up, and a down server leaves stage-1 untouched (tested) | ✔ | `FolioDecisionStage2.cs`; 8 mock-loopback tests incl. refusal of non-loopback URLs |
| Honest stage-2 evaluation (`tools/decision/bench_process_sort.py`, this machine, 9 labeled cases from app fixtures with real DB wording): **destination choice 5/7 (71%)**, abstention via typed `noul` 0/2, median latency 25 ms, p95 48 ms, serve RSS 45 MiB (checkpoint mmap'd into unified memory). Confidence barely discriminates (right: 0.39–0.52; wrong: 0.30) → **stage-2 is a weak signal, never sole authority**: reorders only when confident (≥0.60) AND the winner is inside stage-1's top-2; `multiple`/`abstain` override needs ≥0.80 — measured thresholds live in `FolioStage2Mapping` with the reasoning | ✔ | bench output above; thresholds documented in code |
| Deterministic stage-1 remains the ranking backbone: every item still records `deterministic-v1` for stage-1 suggestions; a stage-2 winner records model+revision head + rationale "stage-2 pick (answer_confidence 0.xx)"; raw answers land in the audit chain (`folio.item.stage2`) | ✔ | mapping tests; approval still a user action |
| **Real-data gap fixed (Hugh's first QCPP run):** the morning batch classified fine (123 items: 35 clear / 70 multiple / 16 sensitive) but NOTHING attached — approval was one dialog per file and past batches were unreachable. Now: (1) **bulk "Approve proposed…"** — one press imports every CLEAR single-destination proposal and links it to its requirement through the normal pipeline (kinds guessed conservatively from filenames — certificate/training/register names — fully correctable later); (2) **batch history picker** in the dialog reopens any past batch (review, approve, publish stay resumable across sessions) with the queue breakdown in the summary; (3) **DOCX joined stage-v1** — local zip+OpenXML body text (`docx-openxml`, sha+parser cached), so the 101 previously-skipped Word policies/registers now classify; legacy .doc rejected with guidance | ✔ | `ApproveProposed` + `FolioKindHint`/`DefaultTitleFrom`; picker in ProcessSortDialog; `ExtractDocx`; 5 new tests (bulk idempotent + sensitive untouched + read-only refused, kind hints, docx parses, empty-zip unreadable, legacy .doc guidance); 116 tests green |
| OCR helper bundled with the .app (package-app.sh compiles + copies to Contents/MacOS; discovery order QSPP_OCR_HELPER → app MacOS dir → walk-up repo build/) | ✔ | packaging script; helper built by tests too |
| **Feedback from Hugh's second session (22 Oct):** (1) scan/process now show an animated indeterminate progress bar + live line "Processing N of total — current-file.pdf" with the grid auto-scrolling to the active file; the folder scan moved OFF the UI thread (a synchronous 224-file Google Drive hash pass used to freeze the window — read as "frozen"); double-run guarded. (2) "Open Requirements →" in the dialog closes it and lands directly in Requirements, where affected rows show their attachment counts and each requirement's Evidence tab lists the attachments (verified live: 61 approvals from Hugh's bulk run are attached and counted) | ✔ | ProcessSortDialog busy row + async scan; DocumentsView passes the shell; suite green 86 Integration + 30 Domain |
| Latent M1 bug found and fixed: `"\x89PNG"u8` encodes U+0089 as C2 89 — the PNG signature check never matched a real PNG; now numeric byte comparison (DD-38) | ✔ | image-signature test |
| Migration `M8ProcessSort` + Entitysets FolioBatches/FolioItems; audit lines `folio.batch.*`, `folio.item.*` (incl. `folio.item.stage2`), `folio.published` | ✔ | `Folio.cs`, migration, tests |
| Tests | ✔ | 34 folio tests (14 pipeline + 12 classifier + 5 OCR + 1 runner + 8 stage-2 — see the suites); suite green: 86 Integration + 30 Domain |

**MinerU heavy-tier benchmark (24 Oct, standard tier, user-approved ~2 GB install; 16 GB gate exercised on THIS Mac):**
| Measurement | Result |
|---|---|
| Install | `mineru` 4.0.12, Python 3.12, `tools/ocr-heavy/.venv` (never committed); models 2.0 GB in `~/.mineru/models/` (859 MB torch pipeline + 1.2 GB GGUF VLM); license Apache-2.0-based (checked April 2026 text); telemetry disabled; doclib server loopback-only by default (`127.0.0.1`, config `doclib.tcp` + UDS socket) |
| Benchmarked on the same synthetic fixtures as Apple Vision | 2-page scan: 3.0 s warm (3.2 cold incl. engine load) vs Vision 0.7 s; 10-page scan: 6.8 s (terminal-spot variance 8.7–10.8 s) vs Vision 1.3 s; table image: 5.3 s vs 0.3 s |
| Table structure | MinerU only: emits an HTML `<table>` with the header row intact and every cell recovered (`Area/Frequency/Last done/Next due/Signed` + rows incl. `Narcotics safe`); Apple Vision reads the same tokens correctly but FLAT (no structure) |
| Text recall on synthetic scans | TIE — full-line recall equal on the fixed fixture; the one earlier miss ("…required wind") was the FIXTURE's clipped line, not an OCR gap (both engines identical) |
| Memory (16 GB gate input) | doclib server RESIDENT with engines loaded ≈ 3.66 GiB; batch peak (server + workers + CLI, sampled during runs) ≈ 4.3 GiB transient. Fits a 16 GB Mac IF the server starts on demand and stops after the batch; NOT a resident companion. 8 GB M1 remains unclaimed |
| Wire format | `content.content` markdown with `<!-- page N of M -->` markers; sha256 + `short_id` per doc; parse cache by content hash (`--force` bypasses) |
| Decision | DEFAULT STAYS APPLE VISION for this phase — 5–30× faster at equal text recall on this fixture set; MinerU's advantage is structure (tables) and it costs 3.7 GiB resident. Default change (if ever) decided by the REAL scanned pharmacy corpus, not synthetic fixtures. Helper must be start/stop per batch (laya-serve pattern), never resident |

**Wider corpus round (24 Oct):** the real requirement corpus run against harder shapes.
| Measurement | Result |
|---|---|
| Real-corpus pipeline tests (`ProcessSortWiderCorpusTests`, 7 tests): the repo's actual 451-requirement + REM seed copied into a temp DB | ✔ | Table-layout PDFs, expired certificates ("policy expiry 31 March 2024 (lapsed)"), garbled non-word text, blank scans, two-topic documents, Medicare data INSIDE table cells, batch failure isolation |
| Findings | ✔ | (1) A generic bookings TABLE hits a whole CLUSTER of corpus refs (bookings rules 4/6/7/8/10, business continuity, security register) — no single winner from generic wording; the pipeline honestly offers the cluster, which is exactly the human-choice it should be. (2) Expired certificates classify to REM-06 by wording AS DESIGNED — expiry detection is genuinely not built (not-yet; the human approval step carries the dates). (3) Garbled text and blank scans abstain — never invent a destination. (4) Sensitive detection sees Medicare numbers inside table layouts. (5) Raster-noise 60 dpi: Vision reads ~29 fragment tokens (1 real word); MinerU refuses to fabricate (one image block, no text) — destroyed scans land Needs Review on both engines |
| Suite | ✔ | 116 + 7 = 123 tests green (93 Integration + 30 Domain) |

**Packaged delivery + relaunch persistence (24 Oct, after MinerU + wider-corpus commits):** | | |
|---|---|---|
| Fix landed before packaging: double theme-change crash ("The Control already has a parent" at `MainWindow.RebuildPageHost`, logged from the morning session — app survived, page rebuild did not) — the TextScaleContent wrapper now unparents the old page before re-wrapping (`ba003b9`); build + 123 green | ✔ | `MainWindow.axaml.cs`; error log analysis |
| Backup → package (qspp-ocr helper + seed + pack verified inside the bundle) → redeliver to `~/Applications` → three consecutive launches over one session (pids 14185 → 14537 → 14624) | ✔ | DB unchanged across every relaunch: FolioItems 345, EvidenceLinks 61, state spread 35 proposed / 194 needs-review / 4 multiple / 51 sensitive-hold / 61 approved; ZERO errors in the relaunch logs (the single 06:21 ERR predates today's builds and is fixed by `ba003b9`) |
| MinerU doclib server stopped after benchmarking — never resident by default; laya stage-2 stays on loopback 8642 as the only resident local watcher | ✔ | `mineru server stop`; lsof shows 127.0.0.1:8642 only |

**Heavy-tier opt-in wiring (24 Oct, after benchmark):** the benchmarked tier is now reachable IN THE APP, still opt-in, still default-off.
| Piece | ✔ | Notes |
|---|---|---|
| Engine abstraction: `FolioOcrEngine` (EngineKeyName/UnavailableReason/Ocr); `FolioOcr` (Apple Vision) and `FolioHeavyOcr` (MinerU, pinned CLI + local doclib server) both derive; `HeavyWithVisionFallback` composes them | ✔ | extraction cache key = file sha256 + ENGINE KEY, so Vision and MinerU results never cross-contaminate (test: engine-keyed cache separation) |
| Parser label honesty at every extraction route: the engine that PRODUCED the text is what the item records — the helper's own engine string is recorded by its stable prefix; a real heavy crash keeps the heavy label + error (never pretends Vision wrote it) | ✔ | `FolioExtractor.ParserLabel`; heavy-server-down → Vision result recorded as apple-vision-2; heavy-crash → mineru label + error kept |
| Per-batch opt-in in the Process-and-Sort dialog: an "OCR engine" picker (Apple Vision default / MinerU heavy) appears ONLY when the MinerU CLI is actually installed on the machine — no dead options | ✔ | `_engineChoice`; discovery `QSPP_MINERU_CLI` env → repo venv walk-up |
| Settings: the document-intelligence disclosure now names the tier (MinerU 4 standard, 2 GB models), its measured trade (table structure wins, 3–11 s/doc), the loopback rule, and the start script `scripts/serve-mineru.sh` + `scripts/stop-mineru.sh` | ✔ | `SettingsView` heavy paragraph |
| On-demand server scripts (start idempotent, status check, telemetry already disabled; stop) | ✔ | `scripts/serve-mineru.sh`, `scripts/stop-mineru.sh` |
| Source-folder / output-folder pick buttons added to the dialog rows | ✔ | chooseSource/chooseOutput |
| Tests (`ProcessSortHeavyOcrTests`, 6, hermetic fake CLI — run on machines WITHOUT the 2 GB install; vision legs gate on the helper like the other OCR suites) | ✔ | server-down→Vision fallback recorded honestly; real parse failure stays mineru-labelled + queued with its error; unusable CLI degrades quietly (HeavyOcrReady false, mode honest); silent exit stays queue-honest; page-marker splitting (incl. no-markers fallback + blank abstinence) |
| Suite | ✔ | **129 tests green (99 Integration + 30 Domain)** |

**Evidence expiry detection (24 Oct, after opt-in wiring):** expiry dates are now READ FROM THE DOCUMENT'S OWN TEXT — conservatively, honestly, and never asserted as truth.
| Piece | ✔ | Notes |
|---|---|---|
| `FolioExpiry` detector: a candidate line must contain an expiry keyword (expires/expiry/expired/expiring, valid until/thru/from, next due, renewal date, review date, lapsed, not valid after) AND a plausible date ON THE SAME LINE after the keyword; "valid from X to Y" ranges take the binding end only when Y exists (a "from" with no end yields NO guess); only years 1995–2099 parse; AU d/M/yyyy-first with an M/d fallback that only fires when the second slot exceeds 12; ordinal suffixes stripped by targeted regex (a blanket "st " replace would corrupt "August") | ✔ | `FolioExpiry.cs`; body-sentence dates without keywords are never claimed; garbled or implausible text yields nothing, never a guess |
| Stored as `FolioItems.ExpiryJson` = `{"expiresOn":"2024-03-31","hint":"<the exact source line>"}` via migration `M8FolioExpiry` | ✔ | the hint rides along so the human can verify the detection; nothing is stored when the document names no expiry |
| Status (`expired` / `due soon` within an inclusive 90-day window / current) is COMPUTED at display time — a stored status rots, a computed one cannot | ✔ | `FolioExpiryGuess.Status(today)` |
| Approval copy-through: `ApproveItem` passes the detected date to `ImportEvidence` as `EffectiveTo` + the source line as `ExpiresHintText` (both fields already existed on EvidenceItem); bulk approval inherits automatically; duplicate-sha approvals never overwrite an existing managed record; no detection → no dates invented | ✔ | review dialog shows the detection BEFORE approval ("correct it in Documents if the document says otherwise"); link periods stay a review decision, untouched by detection |
| Review surfacing: batch rows append "expiry 30 Jun 2026 (expired)/due soon" to the proposal detail; the approve dialog shows a quiet muted line with the date, live status and the source sentence | ✔ | `ProcessSortView` (`DetailOf` + `FolioApproveDialog`); quiet by design (M6.1) |
| Tests: `FolioExpiryTests` (24 detector cases incl. every negative) + `ProcessSortExpiryTests` (2 pipeline copy-through tests) | ✔ | suite green: **155 total (125 Integration + 30 Domain)** |

**Sensitive-detector tuning (24 Oct, owner go-ahead):** replayed the REAL corpus's 33 distinct held documents through candidate rules, tuned, and re-verified the held set before shipping. Outcome: **20 of 33 clear, 13 hold — every remaining hold is a genuine record.**
| Piece | ✔ | Notes |
|---|---|---|
| Grounding: the real over-flags were (a) security policies and instruction letters (MHR Security & Access Policy, IT Access Policy, WorkCover registration) tripping the bare-word credential rule, and (b) an assessment guide + to-do list + policy templates merely LISTING rosters/payroll/performance forms | ✔ | cached extraction text replayed per document (read-only, real DB) |
| Credential rule now requires an actual VALUE-SHAPED assignment ("password: token", "passphrase = token", "SECRET: token") — security policies explaining password rules pass WITH OR WITHOUT a policy-shaped title; a real secret in any document is still held | ✔ | `SensitiveDetector.CredentialValue`; new reason wording "credential value content (a password/passphrase/key with an assigned value)" |
| Personnel rule: a POLICY-SHAPED filename (policy/procedure/sop/guide/template/checklist/kit/plan/to-do/manual) that talks ABOUT rosters or payroll passes; RECORDS (CVs, signed contracts, warnings, wage agreements, recruitment/position/performance forms) still hold. Shape is judged by FILE NAME only, with LETTER-based boundaries — "Template_FT-and-PT" must count (underscore is a word character, plain \b would miss it) | ✔ | documented tradeoff: a filled record whose filename is policy-shaped would pass the gate — approval review remains the final human guard |
| Patient/confidential, Medicare pairing, DOB pairing: UNCHANGED. Widening "confidential" to also cover "confidentiality" was tried and REVERTED — three policies about confidential handling tripped it (IT access policy, sustainability statement, induction checklist) | ✔ | narrow pairing ("confidential" + a patient-word) is what the real referral-list documents word, and it carried no false positives |
| Held rows now SAY WHY: the batch list shows the recorded reasons on SensitiveHold rows | ✔ | `ProcessSortView.DetailOf` |
| Tests: `SensitiveDetectorTests` (21) — every real over-flag shape as a pass case, every real record shape as a hold case, credential-value holds regardless of title, the confidentiality-widening revert pinned, roster-note negative preserved, blank text yields nothing | ✔ | suite green: **176 total (146 Integration + 30 Domain)** |
| Effect on the live DB: existing holds are untouched (they are already-staged decisions); a RE-SCAN of the same folders creates fresh items under the tuned rules and the cleared documents flow to classification | ✔ | sha256 recognition links duplicates to the same review flow as always |

**Stage-1 classifier v2 — matching and allocation (24 Oct):** the classifier was replayed against the REAL corpus's human-approved ground truth (182 distinct documents over the live 470-requirement set) and the over-matching shapes were fixed where they were measured, not guessed. Baseline v1: 61 clears (33.5%), 119 multiple (65.4%), 2 abstain. Final v2: **93 clear (51%), 84 multiple (46%), 5 abstain** — with previously-cleared picks preserved (28/35 cleared-proposal tops kept clear and 20/26 approved-label tops kept; of the other flips the old pick stayed INSIDE the offered cluster in 19 of 21 cases, and only two documents genuinely changed shape — a WhatsApp photo now abstains instead of a lucky UAG hit, and the Expiry Date Log offered a different cluster than its bulk echo). Of the 6 label changes every one moved away from a "(bulk)"-echo label and 3 are cleanly defensible: the position-description template now lands on REM-08 "Position descriptions with S&Q responsibilities" directly, the Expiry Date Log and the After-Hours Referral Procedure escape generic UAG 2.1 filler; the remaining 3 (CV, DAA packing, complaints procedure) are judgment calls recorded here, not hidden).
| Piece | ✔ | Notes |
|---|---|---|
| Inverse-candidate-frequency token weights — a token carried by many requirements' wording ("delivery", "pharmacy", "assessment") moves at a fraction of the weight of a distinctive one ("cytotoxic", "workcover", "first aid"): `w(t)=max(0.35, 2.5/(0.8+log2(1+df)))`, df built once from the LIVE candidate list each run | ✔ | this is what broke the generic-score ties (v1 had whole-standard PDFs tying 30/30 between UAG clauses) |
| Title wording ×3, summary ×2, examples ×1 under token weights; the file NAME is weighted ON TOP of the body (×2) — "…First_Aid_Certificate.pdf" or "Award Wages 2026" is a label, not body noise | ✔ | `FolioClassifier`; filename-title verbatim match +4 retained |
| Light deterministic singularisation (final -s on ≥5-letter tokens, ies→y) — "cancellations" now meets "Cancellation windows apply" where plural mismatches used to lose real matches | ✔ | status/analysis/synthesis endings protected; no external stemmer |
| Title two-word PHRASE bonus (+3 per adjacent title bigram in the document, capped at two) — single-token IDF deflates a family's shared title, but "assessment booking" adjacency is precise evidence | ✔ | ordered-token bigrams (stopword-free pairs) |
| Weak-cluster honesty: top between 4.5 and the confident 6.0 with a near-tied companion (≥85% of top) is offered as MultipleCandidates ("weak cluster — review" in the rationale) instead of abstaining — a document aimed at a family of family-worded requirements is a real cluster, not a silence; a lone weak signal still abstains | ✔ | fires on 1 of 182 real docs (a fridge temperature log → fees/cold-chain cluster); zero 4.5–6-without-tie cases in the corpus |
| Version honesty: suggestions record ModelVersion "2"; the multiple rule (top-2 within 15%) and ref-hit boosts are unchanged | ✔ | model stays `deterministic-v1` — this stage remains deterministic retrieval |
| Tests: `FolioClassifierV2Tests` (6) pin the new mechanics at unit level with REALISTIC candidate populations (IDF only bites when many candidates share wording, so tiny two-candidate worlds can't test it): distinctive-beats-filler, weak-cluster offered, weak-single abstains, stemming unifies plurals, phrase bonus, filename weighting. Existing stage-1 corpus tests (bookings-cluster offer, mixed-topics no-swallow, namespace discipline, REM boosts, abstinence honesty) all pass UNMODIFIED | ✔ | suite green: **182 total (152 Integration + 30 Domain)** |

**UI/UX audit (24 Oct):** every multi-action row audited for the "button past the window edge" clipping defect Hugh reported on the Requirements evidence tab; plus a colour-language pass so action buttons read at a glance.
| Piece | ✔ | Notes |
|---|---|---|
| THE REPORTED DEFECT: the requirement evidence actions row held FIVE horizontal buttons inside a fixed StackPanel — "Needs rework" (the fifth) rendered past the window edge, invisible until resized | ✔ | all shared `Ui.BtnRow` rows (7 register sections + 2 assessment rows) now WRAP on a WrapPanel; the requirement evidence row, readiness row, status strip, Library search/source/issues/compare rows, Print Centre and Settings rows and the Process-and-Sort folder row wrap too |
| Colour language pinned in App.axaml and applied: filled green = the creating action (ONE per surface); green outline `verify` = a positive compliance claim ("Manager verifies selected", "Record assessor outcome", "Verify audit chain now"); amber outline `caution` = corrective ("Needs rework"); filled red `destructive` = removal/overwrite ("Retire", "Restore from backup…"); everything else quiet outlined | ✔ | the Documents row had TWO filled-green primaries and seven actions — now one primary, review actions colour-coded, row wraps |
| The stray raw hex in the button styles (#FFFEF8 in primary/destructive) replaced with the existing `Brush.White` token — the token discipline (DD-28) now holds for button foregrounds too | ✔ | no new tokens needed |
| Linked-evidence tab when a requirement has no links: the fixed-180 px empty grid left a dead void between the heading and the actions; now a quiet one-line hint ("No evidence linked yet — attach the first document with the green action below") and the grid height follows the actual link count up to 180 px | ✔ | the empty tab reads intentional (M6.1/M7.2) |
| Suite | ✔ | **182 tests green (152 Integration + 30 Domain), build 0 errors** |
| Verification honesty: fixes are code-verified and compile/test-verified; the running app relaunches to the login wall, so the ON-PAGE pixel confirmation of the wrapped row is Hugh's first glance after signing in |

**Quit on macOS fixed (24 Oct):** Hugh could not quit the app to restart it. Verified live on the running binary: the window had already been closed once, which under the old macOS behaviour only HID it — the app kept running with no visible window, its ⌘Q handler unreachable (it lives in the window's key handling), and the dock-Quit route refused by the toolkit ("user cancelled", AppleScript error -128). The fix: closing the window now genuinely QUITS (the hide-on-close macOS behaviour is removed; the default last-window-close shutdown ends the process). Quit routes are now: the red close button, ⌘Q, the app-menu Quit, and the dock-Quit — any of them ends the process. Relaunching after that is the normal LaunchServices relaunch. Suite green: **182 tests (152 Integration + 30 Domain)**.

**Today redesign — Phase 2, data layer (24 Oct):** pure planning maths (new `QsppManager.Domain/Services/TodayPlanning.cs`) + read-only brief assembly on `QsppWorkspace` (no mutations). `TodayPlanning`: Melbourne calendar date from UTC (rollover-safe), Victorian public holidays 2025-2035 (Melbourne Cup = first Tuesday of November, Easter per the Anonymous Gregorian algorithm, documented weekend substitutions), working-days-between (hops; 0 on or before the start), featured deadline pick (earliest eligible on/after today; plan markers never beat a confirmed fact; pure fallback labelled Planning only; empty is allowed), tone words (0 = "Due today", never a bare zero), bucket boundaries (30/31, 90/91 inclusive; overdue sorts to the top of the next-30 bucket; undated under "Later this cycle"), year-ahead window rolling from the current month. `QsppWorkspace`: `TodayDeadlineCandidates` (PharmacyDeadlines minus internal tasks), `TodayObligations` (equipment calibration due + undated "Not set" rows, S2/S3 refreshers from recorded training only, expiring evidence via EffectiveTo, accreditation expiry, planned assessment events, attestation anniversaries from a recorded cycle start — every row carries its record id), `TodayScreenBrief` (featured + tone + next-after + run-line anchor + milestone ticks + ledger + year-ahead window). Real-workspace probe (read-only): no cycle start/expiry/assessment recorded, zero equipment, staff, expiring-evidence or recurring rows — so the real Today ledger reads QUIET until dates are recorded, and the featured deadline is the PPA confirm (30 Jun 2027; 249 days on 24 Oct 2026). Reference texts (AMH/APF/eMIMS): NO table or field exists anywhere — pending the approved schema addendum per stop condition 2; the ledger will show the one-line empty state meanwhile. Suite: **197 tests green (167 Integration + 30 Domain)**.

**Not yet (next stages per plan):** M1 exercise (run sheet committed, `docs/plans/m1-8gb-exercise.md`; the 8 GB claim stays unclaimed until executed on that hardware). MinerU heavy-tier benchmark DONE (24 Oct; default unchanged). Heavy-tier opt-in wiring DONE (24 Oct). Evidence expiry detection DONE (24 Oct). Sensitive tuning DONE (24 Oct; 20 of 33 real holds clear on replay). Classifier v2 DONE (24 Oct; 51% single-clear on replay, up from 33.5%).
Nothing here transmits document content or extracted text off-device — all parsing, caching and classification are on-disk, on-this-Mac operations.

**Today redesign — Phases 3–5, screen shipped (24 Oct):** the Today screen is rebuilt on the tested planning layer (`Views/TodayScreen.cs` + `Views/Today/TodayControls.cs`, hosted by the 8-line `TodayView.axaml`). The hero panel is the page's only filled panel: a light 104-pt serif numeral counting days to the featured deadline (the PPA 30 Jun 2027 — 249 days on 24 Oct 2026), date line with weeks and working days, title, source line with provenance, a run line (1px track + today dot + milestone ticks; the screen's only acid accent), tone word ("Confirmed"), and the next-after line. The accreditation position block answers overdue/awaiting/verified/interpretations; the surveillance round keeps its live counts; the year-ahead strip draws its marks as clickable shapes (tall bar = featured); renewals render in three buckets with a peek of 8 + show-all, quiet single line when nothing is dateable; reference texts show the held-stop honest line pending the approved schema addendum; this-week/inbox rows deep-link; activity is a collapsed summary. Responsive below 980 px (single column), rule lives inside the screen. CLI mechanics in 1:1 translation: `--selftest` (18 deterministic checks over the planning maths in `Infrastructure/Services/TodaySelftest.cs`, covered by a suite test) and `--render-offscreen <path> [--hold] [--width N --height M]` with `QSPP_RENDER_THEME=dark` for verification. Verification: build clean; suite **198 green (168 Integration + 30 Domain)**; all 8 light/dark × real/demo × 1440×900/1000×760 renders produced, visually reviewed and fixed (empty-bucket hiding, self-contained responsive rule), plus a below-threshold 900×760 collapse check; WCAG AA computed on both hero grounds (every text pair 6.03:1–11.76:1 vs the 4.5:1 body / 3:1 large-text thresholds; the acid dot 8.71:1 / 10.27:1 vs 3:1 UI). The off-screen render itself runs on the REAL macOS backend and is captured from the OS compositor — headless compositing never produced frames (documented dead end). Docs updated (decision DD-40, design-system Today section). Screenshot at `docs/screenshots/01-today.png`.

## 2026-10-24 — Premium polish, Phase 1 (harness, baseline, blank-table ROOT CAUSE fixed)

Branch `design/premium-polish`; brief `docs/plans/2026-10-24-hermes-premium-polish.md`.

- Render harness extended: `--page <ShellPage>` and `--theme light|dark` render the signed-in
  shell screen through the real chrome (synthetic OwnerAdmin session, expanded sidebar), so all
  ten destinations are pixel-checkable: Today, Requirements, Documents, Tasks, Registers, People,
  Assessments, Library, PrintCentre, Settings.
- **Blank-table bug root-caused and FIXED (one line):** every `DataGrid` in the app drew
  NOTHING — no column headers, no rows — on Evidence (61 items), Library (21 sources) and every
  other grid screen, in production, since the Avalonia 12 migration. Cause: the standalone
  `Avalonia.Controls.DataGrid` 12.1.2 package ships its own Fluent control-theme and `App.axaml`
  never included `avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml`, so the DataGrid had no
  control theme at all; it measured to its height and drew blank. The cosmetic `dg|DataGrid`
  styles compiled fine and masked it. Fix: the `StyleInclude` next to `<FluentTheme />`.
  (Reproduced on the real render harness; grids now draw headers + rows.)
- Baseline before-set (real data snapshot, 1440×900): `docs/screenshots/before/01-today` …
  `10-settings`.

## Premium polish, Phase 2 (shell and kit)
- **Sidebar:** header (pharmacy name headline + "QSPP Folio" footnote), three quiet groups
  Prepare / Operate / Assess with eyebrow labels, ten destinations only, Settings pinned at the
  bottom with its own active state; rows 30pt, Lucide icons 16pt @1.5, 6% ink hover; Today shows
  the overdue-count badge only when it is above zero (same query as the Today position card).
- **Toolbar:** opaque Paper with 1px divider; toggle, screen filter (⌘F) and Commands (⌘K) left;
  the signed-in user right as a 24pt initials circle with menu (identity, Sign out, Appearance).
  Page title removed from the toolbar (lives in the page content now).
- **NoticeHost** replaces all eight "Result messages appear here" strips: icon + message + close;
  success fades after 6s (180ms, skipped under reduced motion), errors stay and offer
  "Copy details". Message text per screen unchanged.
- **Type + metrics:** body 13.5; h1 serif 26 Regular; lead 13 muted wrap; buttons 26/28 radius 6;
  ComboBox + CalendarDatePicker + TabItem metrics; the accent-leak Fluent brushes (tab pipe,
  CalendarDatePicker glyph, text-control focus borders) pinned to the green identity inside both
  theme dictionaries. BoxShadow tokens (`Shadow.Overlay`) replace the string-embedded
  `{DynamicResource}` crash on the real MainWindow (caught by launching from source — offscreen
  shell bypasses that XAML).
- **Quote style:** verbatim excerpt renders as a selectable serif quote with a 2px green rule
  (maxHeight 480, scrolls past the cap) instead of a read-only TextBox that looked editable.
- **Date input:** `Ui.DateField` (CalendarDatePicker, "Not set" watermark, d MMM yyyy) replaces
  the type-it-and-hope yyyy-MM-dd prompts (assessment events, recall received, calibration due,
  Settings dates) and a `Dialogs.Confirm` sheet stands in front of destructive Retire.
- **Empty state discipline (3.8):** TableHost empty states on every zero-row grid — Registers
  (all six sections), Print centre recent jobs, Library source documents join Tasks/People/
  Evidence/Documents from Phase 1.
- After-kit renders (real scratch snapshot, 1440×900): `docs/screenshots/after-kit/01-today` …
  `10-settings`; dark Today + real-MainWindow chrome verified from source.
- Suite: 198 green on the finished tree.
