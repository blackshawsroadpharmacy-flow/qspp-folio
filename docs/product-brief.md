# Product brief (condensed from the owner's build prompt, 19–20 Oct 2026)

This file is the canonical internal spec. The full original prompt is the owner's
chat message of 20 Oct 2026; where this file and the prompt differ, the prompt wins
and this file must be updated.

## Purpose
A desktop application where a Pharmacist Manager or Pharmacy Owner
(Burke Road Compounding, VIC first) understands, prepares for, completes and
maintains QSPP accreditation. It must never claim to award accreditation or
impersonate official program systems.

## The six questions the app must always answer
1. What do I need to do next, and why?
2. Which requirements apply to this pharmacy?
3. What evidence is needed and what do we already have?
4. Who owns the work, and when is it due?
5. What is ready for review, printing or assessment?
6. What changed, expired or needs attention since my last visit?

## Destinations (sidebar)
Today · Requirements · Evidence & documents · Tasks & calendar · Registers ·
People & training · Assessments · Library · Print centre · Settings

Two views over the same records: a guided "Prepare for assessment" journey and
direct daily management. Never enter the same evidence twice.

## Non-negotiable model
- Explicit coverage ledger over source-derived requirements and UAG key tasks;
  every requirement carries source document, version, anchor, and provenance.
- Distinct identifier namespaces: Standard clause / UAG section + key-task letter /
  Program Rule / T&C clause / policy provision / QC2020 reference. Same numbers in
  different documents are never the same requirement.
- Interpretation issues (cancellation business vs calendar days; mid-cycle window
  wording; potency-cadence wording) persist as *unresolved* with competing sources
  and a conservative planning choice clearly marked as planning, not a deadline.
- State machines separate: requirement readiness, evidence review, task, finding,
  export. Manager-verified ≠ assessor-accepted; acceptance is only recorded when an
  external outcome is supplied.
- Evidence links carry scope, period, version and review context. Uploading a file
  never auto-sets compliance. Expired/superseded evidence flags affected links and
  creates review work without erasing history.

## Source-data rules (from the owner, must honour)
README/KB section-count disagreement → derive from sources (see coverage ledger).
Lookup-line anchors are approximations; build page/version-aware references.
capacity-toolkit.pdf is an HTML wall — excluded from import.
Two UAG copies preserved with distinct hashes and provenance; no unofficial
"build 2" designation asserted.
cookies.json and browser profiles are excluded from every surface (import, logs,
source control, packages, shared backups, assessor exports).
Readiness hitlist items import as unverified planning items (blank owners).

## Printing (first-class, Milestone 1)
Print in every printable context, Ctrl+P, print-again with saved presets, blank/
pre-filled/completed/selected-range forms, batch printing, Print centre. Print
layouts are dedicated documents (A4, greyscale-safe, repeated headers, page x of y,
document ID/version, print date separated from effective date). Real CUPS/Windows
adapters behind `IPrintService`; truthful job states; spooler acceptance is not
paper. Privacy gate blocks any export/print path for restricted originals.

## Privacy & trust
Offline-first; data in OS app-data dir; roles: Owner/Admin, Manager/Reviewer,
Contributor, read-only Reviewer; append-only audit trail; checksummed immutable
attachment versions; backup/restore verified by checksum; no patient-identifying
data beyond what the pharmacy already uses; no cloud upload by default; redaction
removes underlying content or is delegated to externally-redacted derivatives with
review flags.

## External repos (gated adoption)
Avalonia, CommunityToolkit.Mvvm, EF Core, NodaTime, PdfPig (extraction only),
Open XML SDK, ClosedXML, DiffPlex, Lucide assets, Serilog, PDF rendering engine
(QuestPDF — licence eligibility must be confirmed; PDFsharp/MigraDoc AGPL is the
fallback). See docs/dependencies.md for the pinned, decided list.

## Milestones
1. Vertical slice: tokens, shell, requirement→evidence→task→review→print, Today
   screen, persisted state surviving restart. *(current)*
2. Full requirements/evidence coverage + guided preparation journey.
3. Daily operations: recurring tasks, registers, people/training, compounding,
   acknowledgements, print centre maturity.
4. Assessments: events, deadline services with tests, findings/CAR, assessor
   exports, source-change review, backup/restore, restricted reviewer.
5. Product finish: polish vs real screenshots, accessibility passes, performance,
   packaging. No OS/platform claimed as tested unless actually tested.

## Acceptance checks (summarised)
The twelve end-to-end workflows in the owner's prompt §13.2, plus automated tests
for deadline boundaries/conflicts, applicability, evidence version invalidation,
recurrence idempotence, authorisation, imports, attachment integrity, export/print
privacy filtering, and restore. Demo mode uses unmistakably fictional data; real
Burke Road workspace never receives fake completed evidence.