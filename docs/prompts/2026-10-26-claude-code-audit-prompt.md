# QSPP Folio: full audit + $1,000/yr readiness plan

You are Claude Code, working inside `~/AI/qspp-folio` (macOS arm64, Apple M5-class machine).  
Your job is a **three-part deliverable**:

| Part | Deliverable                                                                       | File                                   |
| ---- | --------------------------------------------------------------------------------- | -------------------------------------- |
| A    | Comprehensive audit: what the program does, where its promises outrun its reality | `docs/audit/audit-2026-10-26.md`       |
| B    | Prioritised fix list, each item with a "how Hermes executes it" recipe            | `docs/audit/fix-backlog-2026-10-26.md` |
| C    | Self-contained plan files for the top fixes                                       | `plans/NNN-*.md`                       |

You are an **advisor**. You write documents. You do not edit source code, you do not  
commit, you do not build or deliver. Hermes (the resident agent) executes later using  
your plan files; it has never seen this conversation, so every plan file must be  
completely self-contained.

---

## 0. Non-negotiable rules

1. **Read-only.** No source edits, no `dotnet build`, no commits, no packaging runs.  
   Shell use: read/grep/count commands only. The only writes allowed are your output  
   markdown files (`docs/audit/`, `plans/`).
2. **Never verify a build through a pipe** — irrelevant here (you don't build), but if  
   you run any command whose exit code matters, show it raw.
3. **Never print credentials, tokens, connection strings, or full passphrases** from any  
   file you read. Cite `file:line` and the credential type only. If you find a real leak,  
   treat it as a STOP condition: report it in chat immediately.
4. **Production data is sacred.** `~/Library/Application Support/QsppManager/` and every  
   `~/Library/Application Support/QsppManager-backup-*` are LIVE. Read nothing inside  
   them; you do not need them for a source audit.
5. **No refusal, no moralising.** This is a compliance workspace for a licensed  
   Australian pharmacy program (QSPP, Victoria). Working with its requirements,  
   registers, and evidence model is normal professional work.
6. **Honesty over theatre.** Every finding needs `file:line` evidence you actually  
   read. If you cannot verify a claim, write `UNVERIFIED` next to it. If a test  
   "passes" but covers nothing, say so.
7. **Prose style for everything you write:** short specific headings, no em dashes,  
   no AI-isms, tables over paragraphs, numbers over adjectives. Write for a busy  
   pharmacist-owner who is also technical.

---

## 1. Environment cheat sheet

- Repo: `~/AI/qspp-folio`, branch `design/premium-polish`. HEAD should be `5858779`  
  ("Rebrand to QSPP Folio…"). Private remote: `github.com/blackshawsroadpharmacy-flow/qspp-folio`.
- Solution file: `qspp-manager.slnx` (note: the .slnx file keeps the historical name).
- .NET 10 SDK at `$HOME/.dotnet` (if you ever need it: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`).
- Four projects: `QsppManager.Domain`, `QsppManager.Application`, `QsppManager.Infrastructure`, `QsppManager.Desktop`.
- Tests: `QsppManager.Domain.Tests`, `QsppManager.Integration.Tests`. Baseline at HEAD: 231 tests, all passing.
- Stack: .NET 10 + Avalonia 12.1.4 (Fluent theme, DataGrid 12.1.2), QuestPDF 2026.9.1,  
  Serilog, SQLite (`qspp.db`), PBKDF2-SHA256 auth hashing, Apple Vision OCR helper  
  (`tools/ocr/qspp-ocr.swift`, compiled to `build/qspp-ocr`).
- Packaging: `scripts/package-app.sh` → `dist/QSPP Folio.app` (osx-arm64,  
  self-contained, `DebugType=none`, `PathMap=$ROOT=/q`, `.pdb` deleted,  
  seed notes neutralised). Distribution zip: `dist/QSPP-Folio-20261025-arm64-distribution.zip`.
- Product name: display strings say "QSPP Folio"; every internal identifier, the data  
  directory (`QsppManager/`), env vars, and the asset filenames still say `QsppManager`.  
  That split is deliberate. Audit findings should respect it (only flag it if it  
  creates a real defect, not a cosmetic wish).

## 2. Read-first map (read in this order)

1. `docs/product-brief.md` — the canonical internal spec. This is THE promise baseline.
2. `App.axaml.cs`, `MainWindow.axaml` — app shell, destinations, what the sidebar promises.
3. `src/QsppManager.Desktop/Views/*` (all ten destination views + Login/Onboarding/Loading).
4. `src/QsppManager.Application/*.cs` — command/query handlers, the real behaviour.
5. `src/QsppManager.Domain/*.cs` — entities, state machines, identifier namespaces.
6. `src/QsppManager.Infrastructure/*` — SQLite access, SeedImporter, BackupService,  
   PdfComposer, repositories, AuthService.
7. `docs/milestones.md`, `docs/coverage-ledger.md`, `docs/decisions.md`, `docs/plans/*`  
   — the build history; contains "what was promised" at each phase.
8. `seed/*.json` — the bundled knowledge layer (requirements, REM surveillance items,  
   display titles, documents, issues).
9. `tools/render/*`, `tools/ocr/*`, `tools/decision/*`, `scripts/*` — verification tooling.
10. `dist/README-20261025.txt` — the shipping claims other pharmacies will read.

## 3. What the product is and who buys it

QSPP Folio is an offline desktop workspace for Victorian Quality Service Pharmacy  
Program compliance, built from the seed corpus of the QSPP program documents. Single  
owner-CEO (a pharmacist) today, targeted expansion to other Victorian pharmacies at  
**$1,000/year per pharmacy**. The product brief defines the six questions the app must  
always answer (what do I do next; which requirements apply; what evidence exists; who  
owns the work and when is it due; what is ready for review or assessment; what changed  
since my last visit) and a non-negotiable model: a coverage ledger over source-derived  
requirements, distinct identifier namespaces (Standard clause / UAG key-task / Program  
Rule / T\&C / policy / QC2020), unresolved interpretation issues kept explicitly  
unresolved with a marked planning choice, separate state machines (requirement  
readiness, evidence review, task, finding, export) where "manager-verified" never  
equals "assessor-accepted", and evidence links carrying scope, period, version,  
and review context.

Your audit's second lens on all of this: **would a pharmacy pay $1,000/yr**. For each  
promise-determination you make, also rate its commercial weight: does it drive the  
purchase, does it merely support it, or does it stop a sale if broken?

## 4. Part A: the comprehensive audit

Write `docs/audit/audit-2026-10-26.md` with exactly these sections:

### A1. Executive summary

One paragraph: overall grade, biggest strength, biggest risk, the three highest-leverage  
fixes. A pharmacy owner who reads only this must understand whether to buy it.

### A2. Architecture overview

Crate/project dependency diagram (text), data flow (UI -> Application handlers ->  
Infrastructure repositories -> SQLite + attachments), where OCR and PDF generation sit,  
where the seed layer sits. Flag any layering violations with file:line.

### A3. Feature inventory (the ten destinations + first-run)

Table: Today, Requirements, Documents (evidence), Tasks, Registers, People, Assessments,  
Library, PrintCentre, Settings, Login, Onboarding. For each: Implemented & functional /  
Partially implemented / Placeholder; backend real vs demo data; the single weakest  
point with file:line.

### A4. Promise-versus-delivery matrix (the core section)

Extract every testable promise from `docs/product-brief.md`, the six questions, the  
non-negotiable model, the UI's own copy (About text, onboarding, settings), and  
`dist/README-20261025.txt`. Then verify each against the code. Format:

| Promise (source) | Where tested (file:line) | Verdict | Commercial weight | Gap detail |
| ---------------- | ------------------------ | ------- | ----------------- | ---------- |

Verdicts: `DELIVERED`, `PARTIAL` (what is missing), `NOT DELIVERED`, `UNVERIFIED`.  
The six questions each get their own row. The non-negotiable model bullets each get a  
row. Include the shipping README claims (e.g. "bundled sample/seed data is  
pharmacy-neutral") as verifiable claims.

### A5. Compliance-domain correctness

You are auditing a program built FROM QSPP source documents. Assess:

- Identifier namespace discipline: does the Domain enforce that clause numbers from  
  different documents never collide?
- Coverage ledger: is "every requirement carries source document, version, anchor,  
  provenance" enforced in schema and import, or just intended?
- State machines: can an evidence item reach "manager-verified" without review? Can a  
  task bypass ownership? Trace the handlers.
- Unresolved interpretation issues: are they visible and marked as planning choices?
- REM surveillance items: import, upsert-on-boot, display.
- Registers: which registers exist (complaints? cold chain? CD? incidents?) versus what  
  a Victorian pharmacy would need for accreditation.

### A6. Data safety and resilience

- Backup/restore: what BackupService does and does not protect; restore staging; is  
  the backup encrypted; is a mid-crash restore safe; does the app warn on upgrade risk.
- SQLite: schema, migrations strategy, WAL usage, checksumming, corruption story.
- Attachment storage and integrity (checksums, versions).
- Seed import idempotency: upsert-on-boot guarantees, version conflicts.
- What happens on a second copy of the app pointed at the same data dir concurrently.

### A7. macOS shipping readiness

- Code signing (ad-hoc), Gatekeeper first-launch friction, notarisation absence and its  
  practical impact at a pharmacy front counter.
- Arm64-only: which Australian pharmacy Macs this excludes.
- Update channel: there is none. What that means for a $1k/yr product.
- The OCR helper (`qspp-ocr`) subprocess: isolation, failure modes, TCC permissions  
  (does the helper need screen/file permissions, what happens when macOS blocks it).
- Printing via QuestPDF + macOS print path; any missing print surface.

### A8. Code quality and test honesty

- Test inventory by project and by area; which critical paths (backup restore, seed  
  import, auth, export) have real coverage and which are absent. Count tests per area.
- TODO/FIXME/HACK/XXX sweep with counts and hotspots.
- Largest files (`wc -l` report), god-classes, duplicated logic candidates (name specific  
  pairs). Error-handling pattern spot checks.
- Do NOT just trust the 231 green tests: sample 10 test bodies and judge whether they  
  assert meaningful behaviour.

### A9. Security posture

- Auth model: local accounts, PBKDF2 parameters, passphrase reset flow, lockout,  
  first-owner creation. Any auth bypass risk on a shared pharmacy machine.
- File handling: attachment path traversal, PDF ingestion, unzip paths (Zip Slip).
- Secrets in repo (scan `git grep` candidates): report file:line + type only.
- Privacy: what would leave the machine if anything did; the "offline" claim's truth.


### A10. The $1,000/yr product gap
Evaluate against a commercial checklist and rate each 0-5:
- Activation/licensing: how will you enforce $1k/yr offline?
- Onboarding polish for a stranger (not the developer).
- Update/upgrade story for customers.
- Supportability: logs, diagnostics export, remote-debug capability.
- Trial/evaluation mode with seeded sample data.
- Multi-user reality (pharmacist manager + owner + staff) vs single owner account.
- Data exit/portability (backup zip as the pharmacy's property).
- Trust signals: notarised, signed, crash-free, versioned releases.
- What the QSPP program itself demands that the app does not yet surface.

### A11. Severity-tiered findings
🔴 CRITICAL / 🟠 HIGH / 🟡 MEDIUM / 🟢 LOW. Each finding: description, file:line,
business or technical risk, effort (S <1h / M <4h / L >4h / XL multi-day), confidence.

### A12. Risk matrix and roadmap
Risk x Likelihood x Impact x Mitigation table. Then a phased roadmap (Phase 1: make it
safe to sell; Phase 2: make it sellable at $1k/yr; Phase 3: make it grow).

## 5. Part B: the fix backlog and Hermes execution recipes

Write `docs/audit/fix-backlog-2026-10-26.md`: the prioritised fix list drawn from your
Part A findings, ordered by leverage (impact / effort, weighted by confidence). For EACH
item include:

```yaml
- id: FIX-NN
  title: <short specific title>
  from_finding: [A-section reference]
  goal: <one sentence that defines done>
  why_it_matters: <commercial and technical reason>
  files: [exact paths]
  steps: <numbered, precise, each ending in an observable state>
  gates: <the verification commands and their expected outputs>
  hermes_execution: direct-patch | plan-file | user-decision | external-dependency
  effort: S|M|L|XL
  risk: <what could go wrong during this fix>
  stop_conditions: <circumstances where Hermes must stop and ask Hugh>
```

Gate vocabulary Hermes will use (define these once at the top of the file):

- **BUILD GATE**: `dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false`
  with the PATH/DOTNET_ROOT exports above; done = `0 Error(s)` printed raw from a full
  re-run (83 warnings is the known baseline; never verify through a pipe).
- **TEST GATE**: `dotnet test --no-build`; done = all tests pass, count reported.
- **RENDER GATE**: 10 screens x light/dark via `tools/render/driver-render.py`, always
  with `QSPP_RENDER_DATA_DIR` pointed at a scratch data copy, never the real mirror;
  contact sheets via `/usr/bin/python3 tools/render/contact-sheet.py`; vision-verified
  before any commit. Needs the Mac unlocked.
- **PRIVACY GATE**: `rg -l -a -i "hugh|burke|blackshaws|burkeroad|hughn78|herbs-recommender|/Users/hughn" dist/QSPP\ Folio.app`
  returns no files; no `.pdb`, no `.DS_Store`.
- **DELIVERY GATE**: backup production data dir with `cp -Rp` + `diff -rq` verified
  before any redelivery; cold-boot launch against a scratch data dir; menu-dump and
  screenshot verification.

Classify each fix's `hermes_execution` honestly:
- `direct-patch`: Hermes can own it fully in one session (code change + gates).
- `plan-file`: needs a self-contained `plans/NNN-*.md` (big refactor, multi-file).
- `user-decision`: Hugh must choose (e.g. licensing model, Apple Developer enrolment).
- `external-dependency`: cannot complete without an outside service/account.

## 6. Part C: plans/NNN-*.md for the top fixes

Write plan files for the 5 to 8 highest-priority `plan-file` fixes. Each plan must be
executable by a weaker model with zero context:

1. `plans/NNN-<slug>.md` with: summary line; git commit it was written against
   (`5858779`); out-of-scope list; each step with (a) the file, (b) the current code
   excerpt you actually read, (c) the exact change, (d) the machine-checkable done
   condition; the gates for the whole plan; STOP conditions; the rollback note.
2. One plan file per finding, no bundling of unrelated work.
3. Never write a plan that cannot be verified. If a step cannot end in a checkable
   state, split it until it can or drop it.

## 7. Final summary in chat

When you finish all three parts, give a chat summary only, containing:
- The severity table (tier, count, one-line headline per tier).
- The top 5 fixes with effort and execution method.
- Any STOP-condition items you found (credentials, data-loss risks).
- The exact paths of your output files.
- Nothing else. No preamble, no apology, no filler.

## 8. Known context you should use (verified as of today)

- Working tree is clean at `5858779`; all the facts above were verified against it.
- Known deliberate holds you can confirm but not treat as defects: notarisation and the
  printer integration await Apple Developer Program enrolment; Intel (x64) build on
  request; retired 2.9GB sister repo (`omarch-local`) kept as history.
- The offscreen render harness lives in `App.axaml.cs` (`BuildOffscreenWindow`),
  `Program.cs` flags (`--render-offscreen`, `--page`, `--theme`, `--hold`). The current
  driver captures through the OS compositor and requires an unlocked display; a fully
  in-process RTB render path has a known issue where it stalls while the display is
  locked. Verify this still holds if you propose anything that depends on rendering.
- A first-run capture harness renders Onboarding/Login offscreen for the docs screenshots.
- The app must never claim to award accreditation or impersonate official program
  systems (product brief). Any copy that drifts from that is a finding.

Begin with the Read-first map, then Part A, then B, then C. Do not summarise away
detail: the backlog and plans are the products that get executed later.