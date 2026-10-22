# M8 "Process and Sort" — Implementation Plan

**Goal:** Turn a directory of pharmacy documents into an organised, reviewable QSPP evidence folio — entirely local — inside the existing QSPP Manager app, reusing the evidence model (versions, checksums, audit, patient gates, links).

**Architecture notes (recon-verified):**

| Fact (verified this session) | Consequence |
|---|---|
| MinerU: real; `pip install -U "mineru[all]"`, Apache-2.0-based license, Python 3.12 venv, `mineru parse f.pdf --json` | Heavy OCR tier is a managed local Python worker, pinned version, evaluated before pinning |
| laya-apple: real (tc3oliver/laya-apple, in upstream Laya's community list); typed decision checkpoints on HF (`convaiinnovations/laya-typed-decisions`); MLX FP16 peak ≈ 943 MiB; `laya-apple serve` binds loopback (Jev-compatible API) | Stage-2 decision service = managed loopback process, version recorded; abstain/never-verify rule preserved |
| Apple Vision: OS-native OCR (no download) | Light OCR tier for 8 GB M1; a compiled Swift helper avoids python dependency on the light path |
| This machine: 24 GB "Mac17,9" (M5-class), macOS 26.6.2 | Heavy tiers can be benchmarked here; 8 GB M1 claims remain untested-and-unclaimed until exercised |
| App already has: `ImportEvidence` (sha-256 content-addressed store, signature checks, patient-data gate), `LinkEvidence` (State=Proposed), `SupersedeEvidence`, `DocumentTextIndex` (PdfPig→FTS, cached by sha), `PrintJob` pattern, EF migrations per milestone (M3, M4) | Folio approval reuses evidence import + proposed links as-is; nothing new pretends to be "Manager Verified" |
| `DatabaseBootstrapper.Prepare` → `Migrate()`; legacy stamped+migrated | New milestone migration `M8ProcessSort` |

## Stage plan (shipped in A/B/C; tests green + committed per stage)

### M8-A — Local pipeline core (no external models)
1. Domain: `FolioBatch` + `FolioItem` (+ `FolioReviewState`), EF config, migration `M8ProcessSort`.
2. `Infrastructure/ProcessSort/FolioScanner.cs` — recursive scan; signature sniff (reuse `AttachmentStore.ValidateSignature`); extension-vs-signature mismatch excluded; size guard (default 200 MB, configurable); sha256 pass; skip files already `managed` in prior batches (sha match) → `Duplicate` proposal, NOT re-recorded.
3. `Infrastructure/ProcessSort/FolioExtractor.cs` — digital PDFs via PdfPig with per-page text; quality gate: mean chars/page < 120 → `ScannedRoute` (needs OCR tier); images → `ScannedRoute`; unsupported kinds → `Unsupported`; results cached `appdata folio-cache/<sha>.json` (pages array); re-runs reuse cache (never re-OCR unchanged files).
4. `Infrastructure/ProcessSort/FolioClassifier.cs` — deterministic stage-1 scorer against the live corpus (Requirement wording/summary/examples + REM-01…19): title-token weight 3, summary 2, example 1, exact `REM-nn`/ref hit +8; outputs top-5 with scores, per-suggestion `model: "deterministic-v1"`; `< 0.25` → abstain → Needs Review; top-2 within 15% → "multiple"; ≥2 accepted by user allowed (links are cheap).
5. `Infrastructure/ProcessSort/SensitiveDetector.cs` — heuristics (Medicare format, DOB near name, "CONFIDENTIAL – PATIENT", HR/payroll keywords, credentials keywords) → item routed to `Sensitive` review; unapproved sensitive items NEVER published in assessor exports (export path check).
6. `Infrastructure/ProcessSort/FolioStager.cs` — staging → verify sha → publish via **APFS clonefile** (p/invoke, macOS) → hardlink fallback → copy fallback; source files never touched; multi-destination = clone per destination, one canonical managed copy (the imported `EvidenceItem`). Manifest: original path, sha, managed evidence id, parser+version, proposals+scores, approved destinations, review state, errors.
7. `Infrastructure/ProcessSort/FolioWorkspace.cs` — batch lifecycle (Scan → Run → Review → Complete/Cancelled), per-file state machine, resumable (interrupted batch resumes at exact item), failure isolation (bad file → failed item, batch continues), progress events on UI thread, audit `folio.batch`/`folio.item` lines.
8. Desktop: DocumentsView "Process and Sort" section (folders → summary → run → per-file progress + cancel/retry → review queue corrections → publish → open folder), Settings "Process and sort" mode block (chosen tier + why + model versions + offline statement).
9. Tests `ProcessSortPipelineTests.cs` + `ProcessSortClassifierTests.cs` (fixtures: synthetic digital PDFs via QuestPDF; OCR-dependent behaviour asserted as routing, not pixels).

### M8-B — OCR tiers + benchmarks
1. Light tier: Swift helper `tools/vision-ocr.swift` → compiled at package time (`vision-ocr`), Vision `VNRecognizeTextRequest` accurate, en-AU; used for images + low-text PDFs on ALL Macs (download-free).
2. Heavy tier: pinned MinerU in venv under appdata folio dir (user-approved first download; offline thereafter); managed process, bounded concurrency (1 on 8 GB default), per-item timeout (default 120 s), reproducible setup doc `docs/process-sort-ocr.md`.
3. Mode policy in `FolioOcrPlanner`: 8 GB → Vision always; ≥16 GB → MinerU for scanned PDFs (benchmarked); Settings explains the chosen mode + versions; never cloud.
4. Benchmark record `docs/process-sort-ocr.md` with REAL numbers on this machine (and explicitly "not measured on 8 GB M1" until it is).

### M8-C — Local decision model (Laya) + review UX depth
1. `laya-apple` managed loopback worker (`serve`), pinned runtime + checkpoint (+hashes recorded), user-approved install; per-decision version recorded on each suggestion; offline required.
2. Concise-excerpt prompt contract (top-5 candidates with short descriptions + ≤1.2k-char excerpt), choice/multiple/abstain mapping.
3. Accuracy measurement on the app's corpus-derived fixtures; record in the same doc; optional small LLM only if a measured gain shows (default OFF).
4. Review-queue completion: per-file propose/approve/correct UI depth, stale/expired surfacing, multi-doc request gap hints on the folio tree.

**Hard rules carried into every stage:** deterministic retrieval before any model; model suggestions never set review state (approval is user-only, via existing proposed-link flow); private information stays on-device; sensitive review before publication or assessor export; never move/delete source files; DB stays source of truth (folders are an aid).