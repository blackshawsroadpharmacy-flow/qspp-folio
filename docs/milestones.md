# Milestones + implementation ledger

Status vocabulary: ✔ implemented (evidence exists) · ◐ partial · ✗ not started ·
⊘ blocked (external dependency — name it). Updated as work proceeds; never
claim runtime validation that did not happen on this machine.

## M1 — convincing durable vertical slice (current)
| Item | Status | Evidence |
|---|---|---|
| .NET 10 SDK + repo scaffold | ◐ | tools/setup-deps.sh; sln build pending SDK install |
| OpenScript tokens (light) + component styles | ◐ | src/QsppManager.Desktop/Styles/Tokens.axaml |
| Application shell (sidebar, top bar, Ctrl+K palette) | ✗ | |
| Domain entities + EF Core model + migrations | ✗ | |
| Corpus import (seed JSON) with provenance | ◐ | seed/reqs imported at first run; agent building |
| Requirement browser (tree, detail, source anchor) | ✗ | |
| Evidence: add/link/review states + persisted attachments | ✗ | |
| Tasks (one-off, complete, evidence link) | ✗ | |
| Print: checklist/form via QuestPDF → preview + CUPS lp | ✗ | |
| Today screen w/ real persisted state | ✗ | |
| Headless smoke test + domain tests | ✗ | |

## M2 — full requirements + evidence coverage
Full corpus import verified against coverage ledger; guided "Prepare for
assessment" journey; FTS5 search; document library with versions, duplicate
detection by hash; document preview (PDF render, text/image); interpretation
issue surfaces; supersession impact review.

## M3 — daily operations
Recurring tasks (durable, idempotent, missed-occurrence visible); registers
(equipment incl. calibration, incidents, risks, suppliers, temperature logs);
people/training matrix incl. competency + policy acknowledgements; compounding
module (competency, suppliers, spill kits, BUD records, counselling, recalls,
potency matrix); Print centre (presets, batches, binders, blank/pre-filled forms).

## M4 — assessments & continuity
Assessment events timeline; deadline services with injectable clocks + jurisdiction
holidays (tests for boundaries/leap years/weekends); findings → corrective actions;
assessor pack wizard (SCOPE → redaction review → preview → export/print; manifest
with hashes); source-change compare (DiffPlex); backup/restore; restricted reviewer
role.

## M5 — product finish
Screens against real data volumes; screenshots of every destination; A4 greyscale
print inspection; accessibility (keyboard, 100/125/150%, reduced motion); perf
measurements; packaging for Linux (and Windows target build, honestly labelled
untested unless actually run).