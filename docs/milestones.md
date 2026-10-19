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

## M2 — full requirements + evidence coverage (next)
Page-aware corpus anchoring from the 451-entry seed (FAQ answers + lettered QCPP↔QSPP mapping
stay explicitly marked unverified); FTS5 search replacing LIKE v1; guided “Prepare for assessment”
journey; PDF/text/image preview renderer sharing Avalonia's Skia (resolves DD-20); interpretation-issue
destination screens; supersession impact review; adopt CommunityToolkit.Mvvm (DD-18) and PdfPig
text extraction (DD-19); EF migrations replacing EnsureCreated.

## M3 — daily operations
Recurring tasks (durable, idempotent, missed occurrences visible); registers (equipment incl.
calibration, incidents, risks, suppliers, temp logs); people/training matrix (S2/S3 initial vs
refresher routes from the verified fact sheet, competency, policy acknowledgements per version);
compounding module (supplier qualification, spill kits, BUD records, counselling evidence, recalls,
potency matrix preserving ISSUE-POTENCY wording); Print centre maturity (presets, batches, binders,
pre-filled paper templates, print-again with saved preset).

## M4 — assessments & continuity
Assessment events timeline; deadline services with injectable clocks + VIC holiday provenance
(boundary tests incl. leap years); findings → corrective actions → closure; assessor pack wizard
(scope → redaction review → preview → export/print with manifest + hashes); source-change compare
(DiffPlex) on imported UAG copies; backup/restore with restore test; restricted reviewer enforcement
across search/preview/export/print.

## M5 — product finish
Screens against realistic volumes; screenshots of every destination (owner inspection pass);
A4 greyscale print inspection on physical hardware; keyboard/accessibility pass incl. 100/125/150%
text scaling; performance measurements; Linux packaging + Windows build (honestly labelled if
untested on Windows hardware).