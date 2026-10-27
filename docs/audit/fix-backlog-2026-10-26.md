# QSPP Folio fix backlog, 26 Oct 2026

Written against commit `5858779`, branch `design/premium-polish`. Source of findings: `docs/audit/audit-2026-10-26.md` (finding ids F-01 to F-34). Plan files live in `plans/`.

Ordered by leverage: impact on the sale or on data safety, divided by effort, weighted by confidence. Hermes works top to bottom, skipping `user-decision` and `external-dependency` items until Hugh answers.

## Shell setup (every session)

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd ~/AI/qspp-folio
git status --short            # must be clean before starting a fix
```

## Gate vocabulary

| Gate | Command and done condition |
|---|---|
| **BUILD GATE** | `dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false` then `echo "exit=$?"`. Done = the raw output contains `0 Error(s)` and `exit=0`. The known baseline is 83 warnings; a higher number must be explained. Never verify through a pipe (`| tail`, `| grep`); run it bare and read the tail of the output file instead. |
| **TEST GATE** | `dotnet test --no-build` then `echo "exit=$?"`. Done = `Failed: 0`, `exit=0`, and the total reported. Baseline at `5858779` is 231. A plan that adds tests states the new expected total. |
| **RENDER GATE** | 10 screens x light/dark via `tools/render/driver-render.py`, always with `QSPP_RENDER_DATA_DIR` pointing at a scratch copy, never the real mirror. Contact sheets via `/usr/bin/python3 tools/render/contact-sheet.py`. Vision-verify before any commit. Needs the Mac unlocked. |
| **PRIVACY GATE** | `rg -l -a -i "hugh\|burke\|blackshaws\|burkeroad\|hughn78\|herbs-recommender\|/Users/hughn" dist/QSPP\ Folio.app` returns no files; `find dist -name '*.pdb' -o -name '.DS_Store'` returns nothing. |
| **DELIVERY GATE** | Back up the production data dir with `cp -Rp` and verify with `diff -rq` before any redelivery. Cold-boot the new build against a scratch data dir (`QSPP_DATA_DIR`). Menu dump and screenshot verification. |

Standing rules for every fix:

1. Never open `~/Library/Application Support/QsppManager/` or any `QsppManager-backup-*`. Tests and runs use `QSPP_DATA_DIR=$(mktemp -d)`.
2. One fix, one commit, message `fix(FIX-NN): <title>`. No bundling.
3. If a gate fails twice for the same reason, stop and report. Do not weaken a test to pass.
4. Internal identifiers stay `QsppManager`; display strings stay `QSPP Folio`.

## Summary table

| ID | Title | Severity | Effort | Execution | Plan |
|---|---|---|---|---|---|
| FIX-01 | Decide rights to ship program PDFs and QuestPDF entity | 🔴 | L | user-decision | none |
| FIX-02 | Readiness needs reviewed evidence | 🔴 | M | plan-file | 002 |
| FIX-03 | Restore and upgrade cannot destroy live data | 🔴 | M | plan-file | 001 |
| FIX-04 | Zip Slip in restore | 🟠 | S | plan-file | 003 |
| FIX-05 | Login reset gate, lockout, PBKDF2 upgrade | 🟠 | M | plan-file | 004 |
| FIX-06 | Retire path cannot leave Active links | 🟡 | S | plan-file | 005 |
| FIX-07 | Corpus update channel (versioned upsert) | 🟠 | L | plan-file | 006 |
| FIX-08 | Resolved interpretation issues drive deadlines | 🟠 | M | plan-file | 007 |
| FIX-09 | Applicability from service scope; fix enum parse | 🟠 | M | plan-file | 008 |
| FIX-10 | OCR helper deployment target and README truth | 🟠 | S | direct-patch | none |
| FIX-11 | Deadline correctness patches | 🟡 | S | direct-patch | none |
| FIX-12 | Task ownership and four-eyes rules | 🟡 | S | direct-patch | none |
| FIX-13 | Licensing and activation model | 🟠 | XL | user-decision | none |
| FIX-14 | Notarisation, Intel build, updater | 🟠 | XL | external-dependency | none |
| FIX-15 | SQLite pragmas, single-instance lock, startup check | 🟡 | M | direct-patch | none |
| FIX-16 | Packaging hygiene | 🟢 | S | direct-patch | none |
| FIX-17 | Test honesty repairs | 🟢 | S | direct-patch | none |
| FIX-18 | Print and subprocess hardening | 🟡 | S | direct-patch | none |
| FIX-19 | Stale documentation refresh | 🟢 | S | direct-patch | none |
| FIX-20 | Backup encryption | 🟡 | M | user-decision | none |
| FIX-21 | Evidence expiry flags links and creates review work | 🟡 | M | plan-file (not yet written) | none |
| FIX-22 | Missing registers (cold chain, CD, complaints, cleaning) | 🟡 | XL | user-decision | none |
| FIX-23 | Shared `DbContext` across threads | 🟡 | L | plan-file (not yet written) | none |

---

## Critical

```yaml
- id: FIX-01
  title: Decide rights to ship program PDFs and the QuestPDF licence entity
  from_finding: [F-01, A10]
  goal: A written decision from Hugh on each bundled document and on the QuestPDF entity, recorded in docs/decisions.md as DD-41, with the package script changed to match.
  why_it_matters: >
    Commercial: the zip given to other pharmacies contains AS 85000:2024, the UAG,
    QC2020, Program Rules, T&C and 8 policies (21 PDFs, ~35 MB). If the rights holder
    objects the product is pulled. QuestPDF Community is conditional on the using
    organisation's revenue; selling the app changes which organisation that is.
    Technical: nothing, so this is purely a decision.
  files: [scripts/package-app.sh, vendor/qspp-pack (untracked), docs/decisions.md, seed/documents.json]
  steps:
    1. Hermes lists the 21 bundled PDFs with size and sha256 into docs/audit/bundled-documents.md (command: unzip -l on the distribution zip, shasum -a 256 on each file in vendor/qspp-pack). Observable state: file exists with 21 rows.
    2. Hermes asks Hugh, per document, one of: ship as is (permission in hand), ship a link and let the pharmacy supply the file, or exclude. Observable state: Hugh's answers pasted into the same file.
    3. Hermes asks Hugh to confirm in writing which legal entity will sell the app and whether its revenue is under the QuestPDF Community threshold, or to buy a licence. Observable state: answer recorded as DD-41.
    4. If any document is excluded, Hermes changes scripts/package-app.sh so the PACK_SRC copy skips it, and confirms the app still boots with the pack missing (Settings shows "pack not found", Library handles it). 
  gates: BUILD GATE; TEST GATE; PRIVACY GATE; `unzip -l dist/*.zip | grep -c "\.pdf$"` equals the number Hugh approved.
  hermes_execution: user-decision
  effort: L
  risk: Removing PDFs breaks document full-text search and source-compare features for those documents.
  stop_conditions: Hugh has not answered step 2 or 3. Do not ship any build to another pharmacy until DD-41 exists.

- id: FIX-02
  title: Readiness cannot reach ManagerVerified without reviewed evidence
  from_finding: [F-03, F-20]
  goal: SetReadiness(ManagerVerified) fails unless the requirement has at least one Active link to ManagerVerified, unexpired evidence, or an explicit audited no-evidence-needed reason; the assessor pack and journey count only such requirements.
  why_it_matters: The product's central number can currently be green with an empty evidence folder. An assessor visit would expose that and the owner would have relied on the app.
  files: [src/QsppManager.Infrastructure/Services/QsppWorkspace.cs, src/QsppManager.Infrastructure/Backup/AssessorPackService.cs, tests/QsppManager.Integration.Tests/WorkspaceWorkflowTests.cs]
  steps: see plans/002-readiness-requires-evidence.md
  gates: BUILD GATE; TEST GATE (expected total in the plan); RENDER GATE for Requirements only.
  hermes_execution: plan-file
  effort: M
  risk: Existing real data has requirements already verified without evidence; the plan must not silently downgrade them.
  stop_conditions: Any step would write to the production data dir. The plan's behaviour for already-verified rows needs Hugh's ok.

- id: FIX-03
  title: Restore and schema upgrade cannot destroy live data
  from_finding: [F-02, F-25]
  goal: Before any restore promote or migration, a verified copy of the live DB and attachments exists; a restored or migrated DB passes PRAGMA integrity_check or is rolled back.
  why_it_matters: One bad restore or upgrade ends the pharmacy's compliance record. README tells users to back up manually, which they will not.
  files: [src/QsppManager.Desktop/App.axaml.cs, src/QsppManager.Infrastructure/Persistence/DatabaseBootstrapper.cs, src/QsppManager.Infrastructure/Backup/BackupService.cs, tests/QsppManager.Integration.Tests]
  steps: see plans/001-restore-and-upgrade-data-safety.md
  gates: BUILD GATE; TEST GATE; DELIVERY GATE.
  hermes_execution: plan-file
  effort: M
  risk: Safety copies double disk use; the plan caps retained copies.
  stop_conditions: Any command targets the real data dir. A test needs the real DB.
```

## High

```yaml
- id: FIX-04
  title: Zip Slip in backup restore
  from_finding: [F-06]
  goal: Restore extracts only entries listed in the manifest, rejects any entry whose resolved path leaves the target directory, and rejects unlisted entries.
  why_it_matters: A crafted backup writes files anywhere the user can write.
  files: [src/QsppManager.Infrastructure/Backup/BackupService.cs, tests/QsppManager.Integration.Tests/WorkflowM4Tests.cs]
  steps: see plans/003-restore-zip-slip.md
  gates: BUILD GATE; TEST GATE.
  hermes_execution: plan-file
  effort: S
  risk: Old backups containing extra files (none known) would be refused.
  stop_conditions: A real backup in ~/Library is needed to test. Use a generated one.

- id: FIX-05
  title: Login-screen reset gate, lockout and PBKDF2 upgrade
  from_finding: [F-04, F-05]
  goal: An account cannot be taken over from the login screen without a recovery code or the owner's session; repeated failures slow down; hashes upgrade to 600,000 iterations on next login.
  why_it_matters: On a shared pharmacy Mac the role model is void today.
  files: [src/QsppManager.Infrastructure/Services/AuthAndAudit.cs, src/QsppManager.Desktop/Views/LoginView.axaml.cs, src/QsppManager.Desktop/Views/SettingsView.cs, src/QsppManager.Infrastructure/Runtime/AppConfig.cs, tests/QsppManager.Integration.Tests/PasswordResetTests.cs]
  steps: see plans/004-auth-reset-lockout-pbkdf2.md
  gates: BUILD GATE; TEST GATE; RENDER GATE for Login and Settings.
  hermes_execution: plan-file
  effort: M
  risk: Owner lock-out if the plan refuses reset before a recovery code exists.
  stop_conditions: Hugh has not confirmed the lock-out policy in plan step 0.

- id: FIX-06
  title: Retiring evidence cannot leave Active links
  from_finding: [F-08]
  goal: Moving ManagerVerified evidence to Retired is reserved for the supersession flow or an Owner/Manager, and always marks its links Superseded and raises review tasks.
  why_it_matters: History and readiness diverge silently today.
  files: [src/QsppManager.Domain/Services/DomainServices.cs, src/QsppManager.Infrastructure/Services/QsppWorkspace.cs, tests/QsppManager.Integration.Tests/WorkspaceWorkflowTests.cs]
  steps: see plans/005-evidence-retire-links.md
  gates: BUILD GATE; TEST GATE.
  hermes_execution: plan-file
  effort: S
  risk: UI paths that call SetEvidenceReview(Retired) get an error message.
  stop_conditions: A view calls Retired in a way the plan did not list.

- id: FIX-07
  title: Corpus update channel (versioned upsert)
  from_finding: [F-09, F-25]
  goal: A corpus version stamp in the seed lets new requirements, documents and issues reach an existing install without touching states, owners, links or evidence, and a failed import is retried next boot.
  why_it_matters: A subscription product that cannot deliver the next Program Rules version has no reason to be renewed.
  files: [src/QsppManager.Infrastructure/Seed/SeedImporter.cs, src/QsppManager.Desktop/App.axaml.cs, seed/requirements.json, tests/QsppManager.Integration.Tests/RemoteAssessmentSeedTests.cs]
  steps: see plans/006-corpus-versioned-upsert.md
  gates: BUILD GATE; TEST GATE.
  hermes_execution: plan-file
  effort: L
  risk: Removing or renumbering a requirement orphans states; the plan only adds and rewords.
  stop_conditions: A seed change would delete a requirement row.

- id: FIX-08
  title: Resolved interpretation issues drive deadlines
  from_finding: [F-10, F-11]
  goal: ResolveIssue stores a structured outcome; PharmacyDeadlines drops the PlanningOnly flag and uses the resolved reading for the three seeded issues.
  why_it_matters: The code comment and the product brief promise this; the code does not do it.
  files: [src/QsppManager.Infrastructure/Services/QsppWorkspace.cs, src/QsppManager.Domain/Entities/CoreEntities.cs, src/QsppManager.Domain/Services/DomainServices.cs, tests]
  steps: see plans/007-issue-resolution-drives-deadlines.md
  gates: BUILD GATE; TEST GATE.
  hermes_execution: plan-file
  effort: M
  risk: Needs an EF migration for one new column.
  stop_conditions: dotnet ef is not installed or generates a diff touching other tables.

- id: FIX-09
  title: Applicability from service scope, and fix enum parse
  from_finding: [F-13]
  goal: The 49 conditional hints import correctly; Settings service-scope flags produce an applicability suggestion on each requirement that the manager accepts or rejects.
  why_it_matters: Question 2 of the six is unanswered today and a new pharmacy faces 451 manual decisions.
  files: [src/QsppManager.Infrastructure/Seed/SeedImporter.cs, src/QsppManager.Infrastructure/Services/QsppWorkspace.cs, src/QsppManager.Desktop/Views/RequirementsView.axaml.cs, tests]
  steps: see plans/008-applicability-from-service-scope.md
  gates: BUILD GATE; TEST GATE; RENDER GATE for Requirements.
  hermes_execution: plan-file
  effort: M
  risk: The suggestion must never set Applicability itself; the brief requires a recorded reason.
  stop_conditions: The plan would auto-write Applicable or NotApplicable.

- id: FIX-10
  title: OCR helper deployment target and README truth
  from_finding: [F-07]
  goal: build/qspp-ocr is built with a deployment target equal to the main binary (macOS 12.0), Info.plist LSMinimumSystemVersion and dist README state the same floor.
  why_it_matters: Process and Sort silently fails on macOS 12 to 25 today.
  files: [scripts/package-app.sh, build/qspp-ocr (tracked binary), dist/README-20261025.txt]
  steps:
    1. Run `otool -l build/qspp-ocr | grep -A4 LC_BUILD_VERSION` and record minos. Observable: prints minos 26.0 today.
    2. In scripts/package-app.sh change the swiftc line to `swiftc -O -target arm64-apple-macos12.0 -o "$ROOT/build/qspp-ocr" "$ROOT/tools/ocr/qspp-ocr.swift"` and remove the `[ ! -x ]` shortcut so the helper is always rebuilt. If swiftc rejects macos12.0 for Vision APIs used, find the lowest accepted target and use it.
    3. Rebuild the helper only (not the whole app): run the swiftc line. Observable: `otool -l build/qspp-ocr | grep -A4 LC_BUILD_VERSION` shows minos equal to the chosen floor.
    4. Set LSMinimumSystemVersion in the plist to the floor of the highest of (main binary minos 12.0, helper minos). Update dist README line "arm64 macOS 11+" to the same number.
    5. Check that VNRecognizeTextRequest options used in tools/ocr/qspp-ocr.swift (recognitionLanguages en-AU, accurate) exist at that floor; if not, record in the README which features degrade.
  gates: `otool` outputs above; BUILD GATE (Swift is outside dotnet; run `./build/qspp-ocr --help` and report raw exit code); TEST GATE.
  hermes_execution: direct-patch
  effort: S
  risk: Older Vision revisions give different text; ProcessSort tests that use the helper may shift scores.
  stop_conditions: The helper cannot build for any target below 26. Then the floor is 26 and Hugh must decide whether to drop OCR on older macOS.
```

## Medium and below

```yaml
- id: FIX-11
  title: Deadline correctness patches
  from_finding: [F-11, F-21, F-22]
  goal: No invented dates; business-day maths uses one Victorian holiday table; hard-coded PPA date moves to seed with provenance and applicability.
  why_it_matters: Wrong dates shown as confirmed erode trust faster than missing dates.
  files: [src/QsppManager.Infrastructure/Services/QsppWorkspace.cs, src/QsppManager.Domain/Services/DomainServices.cs, src/QsppManager.Domain/Services/TodayPlanning.cs, src/QsppManager.Application/Clock.cs, tests/QsppManager.Domain.Tests/DeadlineCalculatorTests.cs]
  steps:
    1. Delete the 14-day internal checkpoint: make DeadlinesFor (QsppWorkspace.cs:805-811) return an empty list. Observable: a test asserts RequirementDetail.Deadlines is empty for an InProgress requirement.
    2. Make DeadlineCalculator.ConservativeCancelCutoff accept an optional holiday predicate and have PharmacyDeadlines pass TodayPlanning.IsVictorianHoliday. Add a test that a cutoff spanning 2026-11-03 (Melbourne Cup Day) lands one day earlier than without the predicate.
    3. Make Clock.VicHolidays.IsHoliday delegate to TodayPlanning.IsVictorianHoliday and delete the duplicate table, then compare 2026-2028 outputs of both before deleting (record any difference in the commit message and ask Hugh if they differ).
    4. Remove the unused `cutoff` variable at QsppWorkspace.cs:824,826.
    5. Annual attestation: ask Hugh whether year 1 is due at cycle start +12 months. If yes change `cs.AddYears(y)` to `cs.AddYears(y + 1)` and update labels. Observable: test with CycleStartDate 2026-01-01 expects 2027-01-01 first.
    6. Move the PPA date (QsppWorkspace.cs:818) to a new seed file seed/program-dates.json with fields date, source, appliesTo; show only if the pharmacy has set the matching flag. Needs Hugh's confirmation of applicability first.
  gates: BUILD GATE; TEST GATE (new count reported); RENDER GATE for Today.
  hermes_execution: direct-patch
  effort: S
  risk: Step 3 may change business-day answers for 2027-2028.
  stop_conditions: Steps 5 or 6 without Hugh's answer. Do those steps last and skip if unanswered.

- id: FIX-12
  title: Task ownership and four-eyes rules
  from_finding: [F-14, F-15]
  goal: CompleteTask requires the assignee or an Owner/Manager; CreateTask validates the assignee exists; the user who imported evidence cannot be its ManagerVerified reviewer unless the pharmacy has a single account.
  why_it_matters: Ownership and independent review are core claims in the brief.
  files: [src/QsppManager.Infrastructure/Services/QsppWorkspace.cs:371-397,278-304, tests/QsppManager.Integration.Tests/WorkspaceWorkflowTests.cs]
  steps:
    1. In CompleteTask after the Cancelled check add: `if (t.AssignedToUserId != session.UserId && !session.CanVerify) return ServiceResult.Fail("Only the assignee or an Owner/Manager can complete this task.");` Observable: new test with a Contributor completing someone else's task is refused.
    2. In CreateTask add `if (!db.Users.Any(u => u.Id == assignedTo)) return ServiceResult.Fail("The assignee does not exist.");`. Observable: test.
    3. In SetEvidenceReview for target ManagerVerified add: refuse when item.CreatedByUserId == session.UserId and db.Users.Count(u => u.IsActive && (u.Role == UserRole.OwnerAdmin || u.Role == UserRole.ManagerReviewer)) > 1. Observable: test with two managers; single-account pharmacy still allowed (message tells the user why).
    4. Update tests that rely on the old behaviour (WorkspaceWorkflowTests.cs:185 uses Owner for both import and verify; it has one manager so it stays valid).
  gates: BUILD GATE; TEST GATE.
  hermes_execution: direct-patch
  effort: S
  risk: Single-owner pharmacies must keep working; step 3's count rule protects them.
  stop_conditions: Hugh wants four-eyes always on or always off. Ask.

- id: FIX-13
  title: Licensing and activation model
  from_finding: [F-12, A10]
  goal: A decision on how $1,000/yr is enforced offline (signed licence file with expiry and pharmacy name, honour system, or online activation), recorded as DD-42.
  why_it_matters: Today anyone can copy the zip.
  files: [docs/decisions.md]
  steps: Hermes writes a one-page options note (signed licence file verified with an embedded public key; online activation; honour system with watermark) with the offline-failure behaviour of each, and asks Hugh to choose.
  gates: none until chosen.
  hermes_execution: user-decision
  effort: XL
  risk: Over-strict licensing locks a pharmacy out of its own compliance data. Backups and read access must stay free after expiry.
  stop_conditions: Always. Do not implement before the decision.

- id: FIX-14
  title: Notarisation, Intel build, updater
  from_finding: [A7]
  goal: Developer ID signing, notarisation, a universal or x64 build on request, and a version check that tells the user a new build exists.
  why_it_matters: Removes Gatekeeper friction at the front counter; gives customers an upgrade path.
  files: [scripts/package-app.sh]
  steps: Deliberate hold. Hermes may only prepare a runbook once Hugh enrols in the Apple Developer Program.
  gates: `spctl --assess -vv "dist/QSPP Folio.app"` prints `accepted source=Notarized Developer ID`.
  hermes_execution: external-dependency
  effort: XL
  risk: Notarisation rejects unsigned nested binaries (qspp-ocr must be signed with the hardened runtime).
  stop_conditions: No Developer ID in the keychain.

- id: FIX-15
  title: SQLite pragmas, single-instance lock, startup check
  from_finding: [F-18]
  goal: WAL mode, busy timeout 5 s, foreign keys on, `PRAGMA quick_check` at boot with a visible failure dialog, and an exclusive lock file so a second copy refuses to start on the same data dir.
  why_it_matters: Two copies or a power cut currently risk lock errors or corruption with no message.
  files: [src/QsppManager.Desktop/App.axaml.cs:95-99, src/QsppManager.Infrastructure/Persistence/DatabaseBootstrapper.cs, src/QsppManager.Infrastructure/Backup/BackupService.cs]
  steps:
    1. Change the connection string to `Data Source=<path>;Default Timeout=5;Foreign Keys=True` and after Prepare run `PRAGMA journal_mode=WAL;`. Observable: a test opens a temp DB through the same helper and reads `PRAGMA journal_mode` = wal.
    2. Confirm BackupService.VACUUM INTO still works under WAL (existing round-trip test).
    3. Add `PRAGMA quick_check;` after Prepare; on a result other than `ok` stop the boot and show a dialog naming the newest backup file.
    4. Create `<data>/.lock` with FileShare.None held for the process lifetime; on IOException show "QSPP Folio is already open for this data folder" and exit.
    5. Add -wal and -shm files to the backup consideration: VACUUM INTO output is a single file, nothing else to copy.
  gates: BUILD GATE; TEST GATE; a manual second launch against a scratch dir refuses with the message (screenshot).
  hermes_execution: direct-patch
  effort: M
  risk: WAL leaves -wal/-shm beside the DB; copying only qspp.db while the app runs loses recent writes (README must say use Settings backup).
  stop_conditions: Any test that copies the .db file while open starts failing for reasons other than the pragma.

- id: FIX-16
  title: Packaging hygiene
  from_finding: [F-30, F-31, F-32]
  goal: Zip without AppleDouble files; codesign failure stops the script; seed scrub scripted; old bundles removed from dist with Hugh's ok.
  why_it_matters: Small leaks and silent failures in the one script that makes the product.
  files: [scripts/package-app.sh, .gitignore]
  steps:
    1. Replace `2>/dev/null || echo "(codesign skipped)"` (package-app.sh:89) with a bare `codesign ... ` followed by `codesign --verify --deep --strict "$DIST/$APP_NAME.app"; echo "verify exit=$?"` and let `set -e` stop on failure.
    2. Add a zip step using `ditto -c -k --norsrc --keepParent` or `zip -X` so no `._*` files are written. Observable: `unzip -l <zip> | grep -c "/\._"` prints 0.
    3. Add a scrub step that fails the build if `rg -l -a -i "hugh|burke|blackshaws|burkeroad|/Users/hughn" seed` finds anything (the PRIVACY GATE pattern on the seed directory).
    4. Ask Hugh before deleting the four old bundles and three zips in dist/. Do not delete without a yes.
    5. Decide with Hugh whether vendor/qspp-pack stays untracked; if so document how to restore it in README.
  gates: BUILD GATE; PRIVACY GATE; unzip count above.
  hermes_execution: direct-patch
  effort: S
  risk: Low.
  stop_conditions: Step 4 without a yes.

- id: FIX-17
  title: Test honesty repairs
  from_finding: [F-28]
  goal: No test passes by returning early; the tautology is fixed; migrations are exercised.
  why_it_matters: A green suite that skips real checks gives false assurance.
  files: [tests/QsppManager.Integration.Tests/PasswordResetTests.cs:84-91, DocFullTextRealCorpusTests.cs:20, DisplayTitlesRunnerTests.cs:21, WorkspaceWorkflowTests.cs:170-182, PrintPipelineTests.cs, AssessorPackTests.cs:151]
  steps:
    1. PasswordResetTests: replace `Assert.Equal(1, Db.Users.Select(u => u.Role == UserRole.OwnerAdmin).Count())` with `Assert.Equal(1, Db.Users.Count(u => u.Role == UserRole.OwnerAdmin))`.
    2. Mark the two env-gated tests with xunit `Skip` via a custom `[Fact(Skip=...)]` when the env var is absent (use a small `SkippableFact` helper or `Assert.Skip` available in xunit 2.9) so they show as skipped, not passed.
    3. Split Evidence_PatientData_WithoutAttestation_IsRefused_OrBlockedAtReview into two tests, one for import refusal and one for the review gate (create the item directly through Db).
    4. PrintPipelineTests: assert page count and a known string using PdfPig (already a dependency) on the rendered bytes.
    5. Add one migration test: create a temp DB with EnsureCreated, delete __EFMigrationsHistory, call DatabaseBootstrapper.Prepare, assert the history row for InitialCreate and that Migrate() completes.
    6. Move the VicHolidays test out of AssessorPackTests.cs into the Domain or Application tests.
  gates: BUILD GATE; TEST GATE: report passed, skipped and failed separately; expected skipped = 2.
  hermes_execution: direct-patch
  effort: S
  risk: Total passed drops by 2 and skipped rises by 2; that is the point.
  stop_conditions: Step 5 fails because the legacy path really is broken. Report it; that is a finding.

- id: FIX-18
  title: Print and subprocess hardening
  from_finding: [F-24, F-34]
  goal: lp/lpstat timeouts work; subprocess arguments use ArgumentList; loopback pin uses parsed host.
  why_it_matters: A hung printer queue freezes the UI today.
  files: [src/QsppManager.Infrastructure/Printing/CupsPrintService.cs:22-25,62-75, src/QsppManager.Infrastructure/ProcessSort/FolioOcr.cs:58-66, src/QsppManager.Infrastructure/ProcessSort/FolioDecisionStage2.cs:34-38, src/QsppManager.Desktop/ViewModels/TodayViewModel.cs:120, src/QsppManager.Desktop/Views/ProcessSortView.cs:284]
  steps:
    1. CupsPrintService: start output reads as `ReadToEndAsync` tasks, call `WaitForExit(timeout)` first, kill the process tree on timeout and return a failure outcome. Delete the dead `args` block at :62-67.
    2. FolioOcr: build with `psi.ArgumentList.Add(fullPath)` and the flags as separate items; remove the interpolated `Arguments`.
    3. FolioDecisionStage2: parse with `Uri.TryCreate`, require `uri.Host` to be `127.0.0.1` or `localhost` and `uri.UserInfo` empty.
    4. TodayViewModel:120 and ProcessSortView:284: use `ArgumentList` with `-R` and the path.
    5. Add a test for the loopback check with `http://127.0.0.1:1@example.com/`.
  gates: BUILD GATE; TEST GATE.
  hermes_execution: direct-patch
  effort: S
  risk: Low.
  stop_conditions: None.

- id: FIX-19
  title: Stale documentation refresh
  from_finding: [F-29]
  goal: README.md, GATES.md, docs/milestones.md reflect 231 tests, QSPP Folio name, current gates and the findings in the audit.
  why_it_matters: Another pharmacy's IT person reads these first.
  files: [README.md, GATES.md]
  steps:
    1. README.md: replace "Hugh's QSPP Manager" and "Burke Road" with neutral wording, "68 tests" with the real count after running TEST GATE, and the stale `/Users/hughn/...` paths in GATES.md header with `<repo>`.
    2. GATES.md: record evidence or "not run" for each of G1 to G15; do not tick boxes without evidence.
  gates: PRIVACY GATE pattern run over README.md and GATES.md returns no files.
  hermes_execution: direct-patch
  effort: S
  risk: None.
  stop_conditions: None.

- id: FIX-20
  title: Backup encryption
  from_finding: [F-17]
  goal: A backup can be encrypted with a passphrase the pharmacy chooses, with a clear warning that a lost passphrase means lost backup.
  why_it_matters: Backups hold password hashes, audit and evidence text and travel on USB sticks.
  files: [src/QsppManager.Infrastructure/Backup/BackupService.cs, src/QsppManager.Desktop/Views/SettingsView.cs]
  steps: Hermes proposes AES-256-GCM with PBKDF2 or Argon2 key derivation, a format version bump to qspp-backup-2, and asks Hugh to confirm the format and the lost-passphrase policy before coding.
  gates: BUILD GATE; TEST GATE; a round trip test with wrong passphrase refused.
  hermes_execution: user-decision
  effort: M
  risk: Format change; old backups must still restore.
  stop_conditions: Before coding.

- id: FIX-21
  title: Evidence expiry flags links and creates review work
  from_finding: [F-23]
  goal: On boot and daily, evidence past EffectiveTo marks its links Suspended and creates one review task per affected requirement, idempotently.
  why_it_matters: The brief promises it; Today only lists expiring items.
  files: [src/QsppManager.Infrastructure/Services/QsppWorkspace.cs, src/QsppManager.Domain/Enumerations.cs]
  steps: Plan file not yet written. Write plans/009 after FIX-02 lands, because the readiness gate and expiry share the "unexpired" test.
  gates: BUILD GATE; TEST GATE.
  hermes_execution: plan-file
  effort: M
  risk: Duplicate tasks; needs the same unique-index idempotence used for recurrences.
  stop_conditions: FIX-02 not merged.

- id: FIX-22
  title: Missing registers
  from_finding: [F-27]
  goal: Dedicated registers for cold chain, controlled drugs, complaints and cleaning, if Hugh confirms QSPP expects them.
  why_it_matters: A Victorian pharmacy buying a compliance tool expects these on the first demo.
  files: [src/QsppManager.Domain/Entities, src/QsppManager.Desktop/Views/RegistersView.cs]
  steps: First, Hugh confirms which registers QSPP requires and which are covered by recurring tasks. Hermes then proposes entities.
  gates: BUILD GATE; TEST GATE.
  hermes_execution: user-decision
  effort: XL
  risk: Scope growth; some belong in dispensing software, not here.
  stop_conditions: Before any entity is added.

- id: FIX-23
  title: Shared DbContext across threads
  from_finding: [F-19]
  goal: Background work uses its own DbContext (factory), UI uses another; no EF object crosses threads.
  why_it_matters: EF contexts are not thread-safe; failures would be intermittent and hard to reproduce.
  files: [src/QsppManager.Desktop/App.axaml.cs, src/QsppManager.Desktop/Views/ProcessSortView.cs:165, src/QsppManager.Infrastructure/ProcessSort/FolioWorkspace.cs]
  steps: Plan file not yet written. Needs a read of every Task.Run call site first.
  gates: BUILD GATE; TEST GATE.
  hermes_execution: plan-file
  effort: L
  risk: Broad change touching service construction.
  stop_conditions: More than 8 call sites.
```
