# M1 8 GB heavy-tier exercise (run sheet)

Purpose: CLAIM or DECLINE the "MinerU heavy tier works on an 8 GB M1 Mac" honesty claim.
Until this run sheet is executed on that hardware, the claim stays **unclaimed**
(M8 honesty rule: never claim a configuration works until it has been exercised).
This Mac (M5 Pro, 24 GB) cannot substitute for it — memory behaviour, not speed,
is the thing being tested.

Run everything BELOW ON THE M1 MAC (macOS 26.6.2 or its current macOS).

## 0. Pre-flight
1. Free-disk check: `df -h ~` — need ≥ 6 GB (2.0 GB models + venv + workspace).
2. Close heavy apps. Note total RAM: `sysctl -n hw.memsize` (expect 8.59e9 ±).
3. Get the repo: `git clone` the private repo (or copy the working tree, EXCLUDING
   `tools/ocr-heavy/.venv` and `dist/`).

## 1. Install (user-approved download happens ON that Mac)
1. `bash tools/ocr-heavy/setup-mineru.sh`   — idempotent; prints the version.
2. Confirm models: `du -sh ~/.mineru/models` (expect ~2.0 GB: torch pipeline 859 MB
   + GGUF VLM 1.2 GB).
3. `mineru telemetry disable; mineru config set parse_server.local.mode managed`
   (loopback-only, telemetry off — same privacy posture as every other Mac).
4. Start the server: `bash scripts/serve-mineru.sh` then `lsof -nP -iTCP:8000 -sTCP:LISTEN`
   — MUST be 127.0.0.1, never 0.0.0.0.

## 2. Memory ceiling measurement (the actual claim)
1. With the server up and idle, record resident: 
   `ps -axo rss,command | grep -E "mineru|doclib" | grep -v grep | awk '{s+=$1} END {print s/1048576 " GiB"}'`
   → reference value from the 24 GB Mac: ~3.66 GiB resident.
2. Run the THREE fixtures under `/tmp/ocr-fixture/` (copy them over):
   the 2-page scan, the 10-page scan, the table image — via
   `mineru parse <file> --tier standard --pages all --json --force > /tmp/out.json`
   and watch RSS during each run (same `ps` sum).
   → 24 GB reference: batch peak ~4.3 GiB.
3. Watch for: swap pressure (`sysctl -n vm.swapusage`), app kill/relaunch, model
   reload stalls. An 8 GB Mac will swap at the 3.7 GiB resident level; the question
   is whether parse still COMPLETES and the Mac stays usable.

## 3. In-app exercise (full loop, opt-in as designed)
1. Build/package the app with the same packaging script; deliver to /Applications.
2. Process-and-Sort on the fixture folder with the OCR engine picker set to
   MinerU heavy. Confirm: parser label reads mineru-4-standard; batch completes;
   server off → Vision fallback still works; nothing leaves the device.

## 4. Claim discipline
- PASS = fixtures complete under the server without the Mac becoming unusable,
  and the in-app loop finishes → record measured resident + peak numbers WITH the
  M1 hardware line in milestones.md ("M1 (8 GB): resident X GiB, peak Y GiB"),
  and only then mark the 8 GB claim claimed.
- FAIL/MARGINAL = same table with the honest numbers and the claim stays declined:
  heavy tier remains "not for 8 GB" in Settings; the standard tier (Vision) keeps
  being the only default there.

## 5. Cleanup
`bash scripts/stop-mineru.sh` — the server is never resident by default.

Back to this Mac afterwards: record the numbers in the phase report. The M1's
results are recorded SEPARATELY from every other Mac's (M8 honesty rule).