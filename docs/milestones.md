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
