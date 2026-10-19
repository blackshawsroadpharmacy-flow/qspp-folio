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

## M5 — product finish
Screens against realistic volumes; screenshots of every destination (owner inspection pass);
A4 greyscale print inspection on physical hardware; keyboard/accessibility pass incl. 100/125/150%
text scaling; performance measurements; Linux packaging + Windows build (honestly labelled if
untested on Windows hardware).