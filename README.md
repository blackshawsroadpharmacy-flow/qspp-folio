# QSPP Folio

> QSPP Folio. This folder (`<repo>`) is the CANONICAL working repo of the program
> (so agreed by the owner, 24 Oct 2026): the Avalonia macOS app "QSPP Folio" for
> Australian community pharmacies working through the Quality & Safety Pharmacy
> Program. `dist/` holds the latest packaged build (rebuildable; `dist/README`
> carries the run instructions and the macOS 12 floor).

Desktop companion for Australian community pharmacies working through the
Quality & Safety Pharmacy Program (QSPP): understand the requirements, prepare
evidence, run the operational registers, and walk into the assessment organised.

- Ships to a pharmacy as a one-machine desktop install; each pharmacy's data stays
  in its own folder (`~/Library/Application Support/QsppManager/` by default,
  `QSPP_DATA_DIR` overrides)
- Stack: C# / .NET 10 (LTS) / Avalonia 12.1.4, SQLite + EF Core 10, QuestPDF for
  print rendering (Community licence eligibility confirmed by the owner, DD-9)
- Visual language: QSPP design system. Warm paper `#F5F5EB`, sage `#DCE4CF`,
  forest green `#23614D`, light + dark themes, in-app text scale (100/115/130%),
  Lucide icon set, Host Grotesk + JetBrains Mono
- Reference corpus: verified vendor pack (`vendor/qspp-pack`, tracked in git,
  read-only source material); the app never modifies it

## Documentation

| File | What it is |
|---|---|
| `docs/product-brief.md` | The owner's brief, condensed into the canonical product spec |
| `docs/decisions.md` | Architecture/decisions ledger with reversibility notes |
| `docs/milestones.md` | Milestone plan + honest implemented/partial/blocked ledger |
| `docs/audit/audit-2026-10-26.md` | The 26 Oct external audit (F-01 to F-35) and what each finding means |
| `docs/audit/execution-log-2026-10-28.md` | The fix-phase log: every fix, its commit, test counts and every gate raw |
| `docs/dependencies.md` | Pinned dependencies, licences, and why each was chosen |
| `GATES.md` | Acceptance gates (G1–G15) with the evidence that closed each |

## Build and run (macOS, Apple Silicon)

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"   # user-local SDK 10.0.x
dotnet build qspp-manager.slnx
dotnet test qspp-manager.slnx                                   # 300 passed + 2 opt-in skipped
dotnet run --project src/QsppManager.Desktop
```

Data lives in `~/Library/Application Support/QsppManager/` by default on macOS;
`QSPP_DATA_DIR` overrides it (disposable test dirs, demo mode, headless runs).
One process per data folder: launching a data directory that is already open stops
with the "already open" message and changes nothing (FIX-15).

## Packaged app

```bash
scripts/package-app.sh          # → dist/QSPP Folio.app (osx-arm64, macOS 12 floor)
python3 scripts/make-icon.py    # regenerates assets/art/QsppManager.icns (needs Pillow)
open "dist/QSPP Folio.app"
```

The packaged OCR helper `qspp-ocr` targets arm64-apple-macos12.0, so Processes and
Sorts work down to macOS 12 (FIX-10). The bundle embeds the verified corpus pack at
`Contents/Resources/qspp-pack`, so document import and the Library work without the
original vendor checkout. Pack discovery order (DD-27): `QSPP_PACK_ROOT` env →
Settings override → vendored repo pack → bundled `Contents/Resources/qspp-pack` →
legacy Linux path.

Intel Macs: the script targets `osx-arm64`; change `-r osx-arm64` to
`-r osx-x64` in `scripts/package-app.sh` (x64 artefact is labelled untested;
no Intel hardware was verified this milestone).

## Keyboard map (macOS)

| Keys | Action |
|---|---|
| ⌘1…⌘0 | Navigate to each destination (Today → Settings) |
| ⌘K | Command palette (search requirements, jump, act) |
| ⌘F | Filter/search on the current screen (falls through to the palette where a page has no filter) |
| ⌘P | Print current screen (where the page supports it) |
| ⌘, | Settings |
| ⌘W | Close window (app stays running; reopen from the Dock) |

## More

- The audit trail is append-only and hash-chained (tamper-*evidence*).
- Backups: `BackupService` VACUUM-copy with per-file sha256; restore validates
  every hash before extracting anything; a startup corruption check names the newest
  backup before anything else touches the data (FIX-15).
- Sign-in hardening: PBKDF2 at 600k iterations, lockout backoff, and an owner-issued
  recovery code as the only login-screen reset path (FIX-05).
- Status of the remaining work and known limitations: `docs/milestones.md` (M5
  ledger) and the fix-phase report in `docs/audit/execution-log-2026-10-28.md`.