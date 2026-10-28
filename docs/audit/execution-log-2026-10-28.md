# Execution log: audit fixes 2026-10-28

Branch `fix/audit-2026-10-26`, cut from `design/premium-polish` @ `5858779`.
Baseline: build 0 Error(s) / 83 Warning(s); tests 231 passed, 0 failed
(60 Domain + 171 Integration). Execution prompt: docs/prompts/2026-10-28-hermes-execution-prompt.md.

## FIX-04 restore zip slip (plan 003)

- Commit: a12cef2 "fix(FIX-04): restore rejects traversal, unlisted and duplicate entries"
- Tests before: 231. After: 236 (60 + 176). +5 new in RestoreArchiveSafetyTests.cs.
- Gates: BUILD GATE 0 Error(s), exit=0. TEST GATE filter RestoreArchiveSafetyTests: 5/5.
  Full suite: Failed 0, exit=0. RENDER GATE: not applicable (no UI change).
- Deviations: none. Plan excerpt matched the file byte for byte.
- Files: src/QsppManager.Infrastructure/Backup/BackupService.cs (manifest path containment,
  unlisted-entry refusal, duplicate-name refusal, extraction-loop containment check,
  hashcheck try/finally), tests/QsppManager.Integration.Tests/RestoreArchiveSafetyTests.cs (new).

## FIX-03 restore and upgrade data safety (plan 001)

- Commit: ce4e81f "fix(FIX-03): restore and upgrade cannot destroy live data"
- Tests before: 236. After: 242 (60 + 182). +6 new in RestorePromoterTests.cs (5 promote + 1 migration).
- Gates: BUILD GATE 0 Error(s), exit=0, 85 Warning(s) (baseline 83, +2 explained: EF1002 on
  the two new VACUUM INTO literals whose interpolated value is a quote-escaped path).
  TEST GATE filter RestorePromoterTests: 6/6. Full suite: Failed 0, exit=0.
  RENDER GATE: not applicable (no UI change). DELIVERY/skip: not run (no delivery).
- Deviations from plan:
  1. Plan test sketch said "EnsureCreated + delete __EFMigrationsHistory"; used
     db.Database.Migrate("20261019142616_InitialCreate") instead: an EnsureCreated db has the
     NEWEST schema, so stamping only InitialCreate and migrating the rest would collide with
     existing tables. Migrate(InitialCreate) produces a genuine v1-schema database, which is
     the real-world payload the stamp path exists for. Done conditions unchanged.
  2. Plan's Promote signature had no stamp hook; added optional stampFactory parameter
     (plan step 5 explicitly allows it) so Promote_KeepsOnlyNewestFiveSafetyCopies does not
     need wall-clock sleeps.
  3. Tests disable SQLite connection pooling (Pooling=False): pooled handles resurrect
     deleted files and hide recent writes from file copies, which produced phantom
     "table already exists" and false promotion results during development.
- Manual proof (scratch boot): NOT RUN. The desktop boot probe needs an unlocked display
  (the offscreen harness hangs while the screen is locked); the behaviour it would prove
  (no pre-migration folder on a fresh install) is asserted by the migration test and the
  File.Exists guard in Prepare.
## FIX-06: retiring evidence flags links and raises re-link tasks (plan 005, F-08)

- Commit: e3d59e5 on fix/audit-2026-10-26 (tree clean before start; N0 = 242).
- Gates: BUILD GATE 0 Error(s), exit=0. TEST GATE filter EvidenceRetireTests 5/5;
  full suite Failed 0, 247 passed (60 + 187), exit=0. No render gate in this plan.
- First build attempt had 1 Error (CS0246 missing using for UserSession in the new test
  file), fixed by adding using QsppManager.Infrastructure.Services, rebuild clean.
- Changes: MayVerifyEvidence treats Retired like ManagerVerified (Owner/Admin or
  Manager/Reviewer only); FlagLinksAfterRetire shared by supersede and retire; retiring
  requires a reason, stamps SupersededReason, flags every live link Superseded, raises one
  high-priority re-link task per affected requirement; superseding with an already-retired
  replacement is refused; supersede task title em dash removed.
- STOP-check: no other caller of SetEvidenceReview passes Retired (DocumentsView.cs:350 and
  RequirementsView.axaml.cs:1091 pass a target chosen from menus that do not offer Retired).
- Deviations from plan text: the audit excerpt wrote affected.Count; the helper returns
  affectedCount from the extraction so Audit.Append uses the returned count. Adaptation only;
  plan done conditions kept.

## FIX-02: manager verification needs evidence or a recorded reason (plan 002, F-03, part of F-20)

- Commit: e631566 on fix/audit-2026-10-26 (tree clean before start; N0 = 247).
- Gates: BUILD GATE 0 Error(s), exit=0, 87 Warning(s) (+2 vs 85; migration designer and the
  new gate method; benign). TEST GATE filter ReadinessEvidenceGateTests|three fixed tests
  12/12; failed run once (1 FAIL, see deviation 1) then full suite Failed 0, 253 passed
  (60 + 193), exit=0.
- Migration: 20261028030539_ReadinessNoEvidenceReason, single AddColumn<string> on
  RequirementStates, nullable false, defaultValue "". git diff --stat touched only the files
  the plan names. STOP condition (migration touching other tables) did not fire.
- STOP question (already-verified rows stop counting until they qualify): delivered to Hugh
  in chat before commit via question card; the card was cancelled without an answer and Hugh
  said continue. Proceeding on the plan default (rows stop counting until they qualify; no
  downgrade), flagged here for review. Reversible by recording a reason per requirement or
  reverting e631566.
- Deviations from plan:
  1. RestorePromoterTests.Prepare_BacksUpBeforePendingMigration hard-coded 7 history rows;
     FIX-02 added a migration so it read 8. Made the expectation relative
     (GetMigrations().Count()) which keeps the test's intent (Prepare migrates a v1 db fully
     to head) and stops it breaking per migration. Not a weakening: the assertion still
     requires every model migration stamped.
  2. Test 6 set Applicability on the state row rather than the requirement entity (the
     requirement has no such property; plan excerpt did not specify the row).
- Not run: RENDER GATE (Requirements page light+dark) and the step 4 dialog screenshot:
  screen locked (screencapture "could not create image from display"). UI prompt path is
  compile-verified; runtime check listed for the unlocked-screen sweep.

## FIX-05: recovery code, lockout, 600k hashes (plan 004, F-04 + F-05)

- Commit: e133e79 on fix/audit-2026-10-26 (tree clean before start; N0 = 253).
- Step 0 (blocking), asked verbatim: "After this change the login-screen reset only works if
  the owner has created a recovery code first. An owner who forgets their passphrase and
  never created a code cannot reset from the login screen. Is that acceptable, and will you
  create the code the moment this ships?" Answer, via question card: "Yes, acceptable; I will
  create the code". Recorded in the commit message. STOP condition did not fire.
- Gates: BUILD GATE 0 Error(s), exit=0. TEST GATE filter PasswordResetTests|AuthHardeningTests
  11/11 (one fix in-gate: my first draft of NewHashes_Use600kIterations asserted the lowered
  static 2,000 instead of the constant; plan says assert the constant, fixed before commit).
  Full suite: Failed 0, 260 passed (60 + 200), exit=0; suite minutes not increased
  (TestBase lowers the iteration static to 2,000, constant asserted directly).
- `rg -n "RESET MY PASSPHRASE" src` still matches AuthAndAudit.cs:230 (kept as step two).
- Callers re-check: ForgotPassphraseReset called only from LoginView.axaml.cs and tests.
- Not run: RENDER GATE (Login sign-in + reset modes, Settings, light and dark, scratch data)
  and the step 4 screenshots: screen locked (capture probe failed again this session).
  Behaviour covered by tests: no-code refusal text, wrong-code refusal, five-code lockout
  with throttled sixth attempt, owner-only creation.

## FIX-07: insert-missing corpus import (plan 006, F-09 + F-25)

- Commit: f68e345 on fix/audit-2026-10-26 (tree clean before start; N0 = 260).
- Gates: BUILD GATE 0 Error(s), exit=0. TEST GATE filter CorpusUpsertTests|SeedImporterTests
  7/7 (5 new + 2 existing pass unchanged). Full suite: Failed 0, 265 passed (60 + 205),
  exit=0. No migration, no seed/*.json change; git diff --stat shows only the four files the
  plan names.
- STOP conditions: no schema change; no Surveillance rows in the test seeds (guard added
  regardless, per plan); no command read the real data dir.
- In-gate fixes: a typo (JsonJsonValueKindSafe -> JsonValueKind) caught by build; one test
  assertion widened to count the per-row corpus.resourced line and the run-summary line
  separately (the plan's tail emits both when added == 0 and resourced > 0).
- NOT RUN: the two-cold-boot scratch timing check (plan gate "second boot not slower than
  the first by more than 1 second"). The probe script command was consent-blocked in the
  terminal and, per the block rule, was not retried. Equivalent behaviour covered by tests:
  Reimport_IsIdempotent_ZeroWritesWhenUnchanged (second import does zero writes) and
  Reimport_RetriesAfterPartialFailure. Also not run: the plan's scratch sqlite audit check
  (same probe). Document.imported suppression on reimport IS asserted by the idempotence test.

## FIX-09: applicability suggestions + hint parse fix (plan 008, F-13)

- Commit: 8e3e4f3 on fix/audit-2026-10-26 (tree clean before start; N0 = 265).
- Gates: BUILD GATE 0 Error(s), exit=0. TEST GATE filter ApplicabilityRulesTests|
  ApplicabilityScopeTests 12/12. Full suite: Failed 0, 277 passed (69 + 208), exit=0.
- Seed check (read-only, plan's exact command): always 402, if_compounding 36, if_indirect 7,
  if_prescribing 6. Seed file untouched.
- STOP checks: ParseEnum callers audited (RefKind/DomainPart/ApplicabilityHint args; no other
  caller passes values whose meaning changes when _ or - is removed: RefKind has UagKeyTask,
  no underscores); SuggestApplicability writes nothing (asserted by the read-only test).
- In-gate fixes (test-side only): nullable-tuple deconstruction had to use sug!.Value + named
  deconstruct; test 3 materialises the pharmacy row via a first SuggestApplicability call
  before Db.Pharmacies.First() (EnsurePharmacy creates it lazily).
- Not run: RENDER GATE (Requirements detail with and without a suggestion, light and dark):
  screen locked this session; behaviour pinned by the read-only workspace test and the
  domain tests. Scratch-run screenshot for the commit message likewise not captured.

## FIX-08: issue resolution drives deadlines (plan 007, F-10)

- Commit: 5c3a167 on fix/audit-2026-10-26 (tree clean before start; N0 = 277).
- Gates: BUILD GATE 0 Error(s), exit=0. TEST GATE filter
  IssueResolutionDeadlineTests|CancelCutoff_BusinessCalendarAndHolidays 6/6. Full suite: one
  in-gate run with 2 FAILs (existing tests now resolving a deadline issue without a choice),
  fixed to the plan's intent, then Failed 0, 283 passed (70 + 213), exit=0.
- Migration: 20261028074610_IssueResolvedChoice, single AddColumn<string> on
  InterpretationIssues, nullable false, defaultValue "". Only the named files changed.
- STOP conditions: migration scope clean; TodayPlanning.IsVictorianHoliday(2026-11-03) true
  (asserted in the new domain test Melbourne Cup sanity); acknowledged/resolved-with-no-choice
  rows keep planning-only dates (ResolvedIssue requires a non-empty choice) - mentioned in
  the commit message.
- Em dash handling: the two rewritten deadline strings drop the em dash (calendars use
  colons/periods); the unresolved mid-cycle string keeps its original wording (plan says keep
  exact unresolved wording).
- Existing tests updated (in-gate, plan-intended): ResolveIssue_RequiresANote_AndRecordsIt
  passes the first valid choice for the first issue; CorpusUpsertTests resolve passes
  "business" for ISSUE-CANCELWINDOW. One CS0176 (static ValidChoices via instance) fixed.
- Not run: RENDER GATE (Library issue detail, Today, Assessments, light and dark): screen
  locked this session. Date changes are asserted by tests 1-3 and the milestone is covered
  by the existing TodayQueriesTests (label and date unchanged while unresolved).

## FIX-10: OCR helper deployment target and README truth (backlog YAML, F-07)

- Commit: bcd2acc on fix/audit-2026-10-26 (tree clean before start; N0 = 283, unchanged; no
  dotnet source change, helper-only).
- otool before: build/qspp-ocr LC_BUILD_VERSION minos 26.0, sdk 26.5.
- otool after (swiftc -O -target arm64-apple-macos12.0): minos 12.0, sdk 26.5. Main binary
  minos 12.0 (sdk 15.5 checked on the packaged app before this session's changes).
- package-app.sh: always-rebuild (removed the [ ! -x ] shortcut; a failed build now aborts
  packaging with a clear error), -target arm64-apple-macos12.0, plist
  LSMinimumSystemVersion 11.0 -> 12.0.
- Vision floor check: VNRecognizeTextRequest (10.15+), recognitionLanguages (10.15+,
  en-AU from revision 2 = macOS 11.4+), .accurate, usesLanguageCorrection all exist at the
  macOS 12 floor. README records "nothing degrades".
- Helper gate: ./build/qspp-ocr --help prints usage, raw exit code 1 (unchanged convention;
  usage-on-bad-args). Fullsuite ProcessSort OCR tests pass in the suite (283 total).
- BUILD GATE 0 Error(s) exit=0 (formality: swiftc outside dotnet). TEST GATE Failed 0, 283,
  exit=0.
- Note: dist/ is gitignored; the README-20261025.txt floor edit is a working-tree artifact
  and is NOT in the commit. The packaged app's plist keeps 11.0 until the next package run
  (packaging deliberately not run; Hugh decides).

## FIX-12: task ownership and four-eyes evidence verification (backlog YAML, F-14/F-15)

- Commit: a014743; N0 = 283 -> 286.
- Policy asked of Hugh via card: four-eyes always on vs only with 2+ manager accounts.
  ANSWER: "Only blocks self-verification with 2+ managers" (the YAML default). Recorded in
  the commit message.
- Steps 1-3 implemented as written (CompleteTask ownership check; CreateTask assignee
  existence; SetEvidenceReview four-eyes with count > 1 == >= 2, IsActive aware). Step 4:
  WorkspaceWorkflowTests.cs:185 uses Owner for import and verify with a single account, so
  it stays valid and needed no change; no test relied on the refused behaviour.
- In-gate note: the first version of the new test fabricated a Contributor UserSession for
  CreateTask's assignee; the new assignee-existence check correctly refused it. Fixed the
  TEST to create a real Contributor account (the check is the fix's very behaviour; no
  weakening). A throwaway probe test confirmed the workspace code was never at fault; the
  probe file is deleted and NOT in the commit.
- Gates: BUILD GATE 0 Error(s) exit=0. TEST GATE filter TaskOwnershipTests 3/3; full Failed
  0, 286 passed (70 + 216), exit=0.

## FIX-11: deadline correctness patches (backlog YAML, F-11/F-21/F-22)

- Commit: c5eddcb; N0 = 286 -> 288. Steps 1-4 only; 5 and 6 skipped (unanswered).
- Step 1: 14-day internal checkpoint deleted; DeadlinesFor returns empty. Test:
  InProgress requirement's RequirementDetail.Deadlines is empty.
- Step 2: already delivered inside FIX-08 (holiday-aware cancel cutoff + Melbourne Cup test).
  Nothing further changed; noted here for traceability.
- Step 3: comparison FIRST (throwaway probe, deleted before commit): every date 2026-01-01
  to 2028-12-31, table vs computed -> 17 differing slots: 2026-01-05, 2026-04-27, 2026-12-26,
  2027-01-04, 2027-02-01, 2027-03-29, 2027-04-25, 2027-04-26, 2027-04-27, 2027-12-25,
  2027-12-26, 2028-01-03, 2028-01-31, 2028-05-01, 2028-09-22, 2028-09-29, 2028-12-27.
  Weekend-only slots have no business-day impact. On weekdays the computed rule adds false
  "observed Monday" holidays (its NextMonday/AustraliaDay/NewYear/Anzac/Boxing formulas
  substitute even when the fixed day fell on a weekday): 2026-01-05, 2027-01-04, 2027-02-01,
  2027-04-26, 2028-01-31, 2028-05-01, 2028-12-27. The table missed Easter Monday
  2027-03-29, Christmas 2027-12-25, the observed New Year Monday 2028-01-03, had 2027-04-27
  (no VIC basis), and 2028 Grand Final Friday 9/22 (rule says 9/29). The ask ran as a chat
  card (all three FIX-11 questions); IT TIMED OUT UNANSWERED after 60 minutes, so the YAML's
  literal instruction shipped: delegate + delete table. The false-Monday formula repair is
  an OPEN QUESTION for Hugh (needs a formula pass in TodayPlanning either way).
- Step 4: unused cutoff variable removed.
- Steps 5-6: SKIPPED unanswered per YAML stop condition; recorded for the final report.
- Gates: BUILD GATE 0 Error(s) exit=0; TEST GATE filter DeadlinePatchTests 2/2; full Failed
  0, 288 passed (70 + 218), exit=0. RENDER GATE for Today: not run (screen locked); maths
  covered by tests.

## FIX-15: SQLite pragmas, single-instance lock, startup check (backlog YAML, F-18)

- Commit: f643001; N0 = 288 -> 292.
- Implemented: WAL (PRAGMA journal_mode=WAL at the end of Prepare); boot connection string
  gains Default Timeout=5;Foreign Keys=True; quick_check gate at the start of Prepare with
  CorruptDatabaseException (message names the newest QsppManager-backup-* in the backups
  folder beside the db, or says none was found; file-not-database errors land on the same
  gate); App shows a plain boot-stop dialog then shuts down; FIX-03's broad failure catch
  retained untouched after the new corrupt catch; DataDirLock (FileShare.None .lock held for
  the process lifetime, keyed by directory) acquired in OnFrameworkInitializationCompleted
  before anything touches the database, second launch shows the already-open message.
- Deviation noted: quick_check runs BEFORE Prepare's writes rather than "after Prepare" per
  the YAML wording: the corrupt file must stop before the safety copy or migration reads or
  writes it (the same intent; said in the code comment). Fresh/zero-byte files skip the
  check (nothing to check; FIX-03's same rule).
- In-gate fixes: missing using QsppManager.Infrastructure.Runtime; CS0128 duplicate dbFile
  (renamed to the DataSource expression at the call); ShowBootStopDialog static lifetime
  lookup (App.Current.ApplicationLifetime); test-side collection-expression fix
  ([CS9176] -> Array.Empty<byte>()); fresh-file guard after the WAL test caught a
  missing-file quick_check misread as corruption.
- Step 2 confirmed by suite: RestorePromoter VACUUM INTO round-trip tests pass under WAL
  (292 total, 0 failed).
- Not run: the manual second-launch screenshot gate (screen locked this session); covered
  logically by DataDirLock_IsExclusivelyHeld. README WAL/backup note written to
  dist/README-20261025.txt (gitignored artifact).

## FIX-18: print and subprocess hardening (backlog YAML, F-24/F-34)

- Commit: d5b3c27; N0 = 292 -> 299.
- Steps 1-5 as written. lpstat/lp: ReadToEndAsync tasks + WaitForExit-first + Kill tree on
  timeout + failure outcome (discovery returns [] with a console error). Dead lp args block
  removed. FolioOcr and both Finder "open" calls use ArgumentList. Stage-2 pin: Uri.TryCreate
  with http scheme, 127.0.0.1/localhost host, empty UserInfo.
- Deviation (disclosed): the YAML's step 3 says host + user info only; the original check
  also required http:// so https://127.0.0.1 was previously refused. Scheme pinned to http
  to keep that behaviour (same-machine server), test case included.
- Gates: BUILD GATE 0 Error(s) exit=0 (one in-gate fix: today view needed the
  System.Diagnostics qualified name); TEST GATE filter ProcessSortStage2Tests 8/8; full
  Failed 0, 299 passed (70 + 229), exit=0.

## FIX-17: test honesty repairs (backlog YAML, F-28)

- Commit: 0f24d51; suite now 300 passed + 2 skipped (302 total), 0 failed. N0 passed = 299
  -> 300 (env-gated tests moved to Skipped, three tests added).
- Step 1 (tautology): ALREADY resolved by the FIX-05 rewrite of PasswordResetTests — the
  count form is on the line, the Select-bool pattern no longer exists anywhere (swept). No
  change needed; recorded here.
- Step 2: RealCorpusFactAttribute/DumpTitlesFactAttribute in tests/SkipGates.cs; Skip set at
  attribute construction from the env vars; in-body early returns removed. Two tests show
  [SKIP] as required.
- Step 3: split into
  Evidence_PatientData_ImportWithoutAttestation_IsRefused (asserted refusal, outright) and
  Evidence_PatientData_ReviewGate_CatchesARowWithoutAttestation (row written through Db
  directly; Privacy gate refusal asserted) — no early return remains.
- Step 4: both PrintPipelineTests PDF renders now Open(bytes) with PdfPig: NumberOfPages == 1
  (probed first with a throwaway test, deleted) and three known strings each asserted.
- Step 5 DEVIATION (disclosed in commit + here): the YAML recipe "EnsureCreated, delete
  __EFMigrationsHistory, Prepare" cannot pass and was not weakened — an EnsureCreated schema
  already contains every later column, so after stamping InitialCreate the following
  AddColumn migrations collide (duplicate column). The intended coverage (migrations
  exercised on the legacy path) is delivered with the TRUE legacy shape: migrate only to
  InitialCreate, then (test a) DROP the history table — matching the YAML's words — and
  (test b, NEW FINDING) history table present but EMPTY: Prepare used to decide "has
  history" from the table's existence, skipped the stamp and failed Migrate with
  'table "AuditEvents" already exists'. Prepare now stamps on ROWS (creates the table when
  absent) and both edge tests assert the InitialCreate row, history count ==
  GetMigrations().Count() and the Requirements table.
- Step 6: VicHolidays_BusinessDaysSkip_GazettedDays_WithProvenance moved out of
  AssessorPackTests into its own HolidayRuleTests.cs (no Application test project exists;
  the Integration project is where the test's project references already resolve).
- Gates: BUILD GATE 0 Error(s) exit=0. TEST GATE report: passed 300, skipped 2, failed 0
  (expected skipped = 2 met), exit=0. In-gate fixes: SkipGates.cs missing using Xunit;
  two Assert.Contains comparer overloads reduced to two-arg form; StartupIntegrityTests
  rewrite after an accidental clobber (file recovered from its own content).
