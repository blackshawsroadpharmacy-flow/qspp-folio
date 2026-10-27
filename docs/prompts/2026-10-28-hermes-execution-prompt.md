# Hermes execution prompt: QSPP Folio fixes from the 26 Oct 2026 audit

You are Hermes, the resident agent for `~/AI/qspp-folio` (macOS arm64). You have never seen the conversation that produced this work. Everything you need is in the repo. Your job is to execute the fixes in order, one commit each, with every gate shown raw.

## 0. Read first, in this order

1. `docs/audit/fix-backlog-2026-10-26.md`: the gate vocabulary (BUILD, TEST, RENDER, PRIVACY, DELIVERY), the standing rules, and every FIX item.
2. `docs/audit/audit-2026-10-26.md`: evidence behind each finding (F-01 to F-35). Read the section a fix cites before you touch its files.
3. The plan file for the fix you are about to run (`plans/NNN-*.md`). Plans are self-contained; follow them step by step.

Written against commit `5858779` on branch `design/premium-polish`. Baseline: 231 tests pass.

## 1. Non-negotiable rules

1. **Production data is sacred.** Never read, copy, list or open `~/Library/Application Support/QsppManager/` or any `QsppManager-backup-*`. All runs use `QSPP_DATA_DIR=$(mktemp -d)`; tests use temp dirs. Never call `AppConfig.Load()` or `AppConfig.Save()` from a test (it resolves to the real data dir when the env var is unset).
2. Shell setup every session: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
3. Never verify a build or test through a pipe. Run `dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"` and `dotnet test --no-build; echo "exit=$?"` bare, and quote the raw tail (`0 Error(s)`, `Failed: 0`, total, exit code) in your report.
4. Start each fix from a clean tree (`git status --short` empty) and a TEST GATE run. Write the total down as N0. Each plan states "N0 + k" as its expected total.
5. One fix = one commit, message `fix(FIX-NN): <title>`. Do not bundle. Commit locally on a new branch `fix/audit-2026-10-26` cut from `design/premium-polish`. **Do not push, package, notarise, redeliver or touch `dist/`** unless Hugh says so in chat.
6. Never weaken or delete a test to make a gate pass. If a gate fails twice for the same reason, stop and report.
7. Internal identifiers stay `QsppManager`; display text says `QSPP Folio`. No em dashes in any text you write (docs, UI copy, commit messages).
8. Never print credentials, tokens or passphrases. If you find a real secret, stop and tell Hugh immediately.
9. If a plan's excerpt does not match the file as it now stands (an earlier fix moved it), adapt to the intent, say so in the report, and keep the plan's done conditions. If the intent is unclear, stop.

## 2. Execution order

Dependencies: 006 and 008 both edit `SeedImporter.cs` (different methods; do 006 first). 007 completes FIX-11 step 2. 002 changes three existing tests; do it before other plans add tests that verify requirements.

| Order | Item | Method | Notes |
|---|---|---|---|
| 1 | FIX-04 (plan 003) Zip Slip | plan | Smallest, no schema |
| 2 | FIX-03 (plan 001) restore and upgrade safety | plan | |
| 3 | FIX-06 (plan 005) retire links | plan | |
| 4 | FIX-02 (plan 002) readiness needs evidence | plan | EF migration; **ask Hugh** before commit about already-verified rows (plan STOP) |
| 5 | FIX-05 (plan 004) auth | plan | **Step 0 is blocking:** ask Hugh the lock-out question verbatim and wait |
| 6 | FIX-07 (plan 006) corpus upsert | plan | |
| 7 | FIX-09 (plan 008) applicability | plan | After 006 |
| 8 | FIX-08 (plan 007) issue resolution | plan | EF migration |
| 9 | FIX-10 OCR deployment target | direct | Backlog YAML steps; report `otool` before and after |
| 10 | FIX-12 task ownership, four-eyes | direct | Ask Hugh: four-eyes always on, off, or only with 2+ managers (default in YAML) |
| 11 | FIX-11 deadline patches | direct | Skip step 2 (done by plan 007). Steps 5 and 6 need Hugh's answers; do them last or skip |
| 12 | FIX-15 pragmas, lock file, quick_check | direct | |
| 13 | FIX-18 print and subprocess hardening | direct | |
| 14 | FIX-17 test honesty | direct | Expect skipped = 2 |
| 15 | FIX-16 packaging hygiene | direct | Ask before deleting anything in `dist/` |
| 16 | FIX-19 docs refresh | direct | PRIVACY pattern over README.md and GATES.md |

**Do not execute** (need Hugh or an outside party): FIX-01 rights and QuestPDF entity, FIX-13 licensing, FIX-14 notarisation and Intel, FIX-20 backup encryption, FIX-22 missing registers. FIX-21 and FIX-23 have no plan file yet: when you reach the end of the list, write the plan (same format as `plans/001`) and stop for Hugh's review rather than coding.

## 3. Per-fix loop

1. `git status --short` clean; run TEST GATE; note N0.
2. Read the plan (or the FIX YAML) and the audit section it cites.
3. Do the steps in order. After each step, run its "Done when" check and keep the output.
4. If the plan includes a migration: run the stated `dotnet-ef` command, then confirm the generated `Up` touches only what the plan names (`git diff --stat`). Anything else: stop.
5. Run BUILD GATE, then TEST GATE. Confirm the total equals the plan's expectation.
6. If the plan lists a RENDER GATE: use `tools/render/driver-render.py` with `QSPP_RENDER_DATA_DIR` set to a scratch copy; needs the Mac unlocked; vision-check before committing. If the display is locked, say so and skip the render, noting it as not run.
7. `git add` only the files the plan names, commit with the standard message and the attribution trailer the environment provides.
8. Append a block to `docs/audit/execution-log-2026-10-28.md` (create it on first use): fix id, commit hash, N0 and final test total, gate outputs (raw tails), deviations from the plan, anything not run.

## 4. Stop and ask Hugh when

- A plan STOP condition fires, or a decision is marked "ask Hugh" above.
- A migration touches anything beyond what the plan names.
- A test you did not expect to change starts failing.
- Any command would touch production data, `dist/`, the remote, signing or notarisation.
- You are about to make a behaviour change the plan does not describe.

Stop means: write what you found and what you need into the execution log and your chat reply, then wait. Do not continue to the next fix while a blocking question is open, except for fixes that do not depend on it.

## 5. Final report (when the list is done or blocked)

Reply with only:

- A table: fix id, commit hash, tests before and after, gates run (pass or not run), deviations.
- Open questions for Hugh, each with the fix it blocks.
- Anything skipped and why.
- Path of the execution log and the branch name.

No preamble, no summary of what the audit said.
