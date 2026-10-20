# Hugh's QSPP Manager

Desktop companion for Australian community pharmacies working through the
Quality & Safety Pharmacy Program (QSPP) — understand the requirements, prepare
evidence, run the operational registers, and walk into the assessment organised.

- Initial pharmacy: **Burke Road Compounding** (VIC, Australia)
- Stack: C# / .NET 10 (LTS) / Avalonia 12.1.4, SQLite + EF Core 10, QuestPDF for
  print rendering (Community licence eligibility confirmed by the owner — DD-9)
- Visual language: QSPP design system — warm paper `#F5F5EB`, sage `#DCE4CF`,
  forest green `#23614D`, light + dark themes, in-app text scale (100/115/130%),
  Lucide icon set, Host Grotesk + JetBrains Mono
- Reference corpus: verified vendor pack (`vendor/qspp-pack`, read-only source
  material); the app never modifies it

## Documentation

| File | What it is |
|---|---|
| `docs/product-brief.md` | The owner's brief, condensed into the canonical product spec |
| `docs/decisions.md` | Architecture/decisions ledger with reversibility notes |
| `docs/milestones.md` | Milestone plan + honest implemented/partial/blocked ledger |
| `docs/coverage-ledger.md` | Requirement corpus coverage against primary sources |
| `docs/dependencies.md` | Pinned dependencies, licences, and why each was chosen |
| `GATES.md` | Acceptance gates (G1–G15) with the evidence that closed each |

## Build and run (macOS, Apple Silicon)

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"   # user-local SDK 10.0.402
dotnet build qspp-manager.slnx
dotnet test qspp-manager.slnx                                   # 68 tests
dotnet run --project src/QsppManager.Desktop
```

Data lives in `~/Library/Application Support/QsppManager/` by default on macOS;
`QSPP_DATA_DIR` overrides it (disposable test dirs, demo mode, headless runs).

## Packaged app

```bash
scripts/package-app.sh          # → dist/Hugh's QSPP Manager.app (osx-arm64, self-contained)
python3 scripts/make-icon.py    # regenerates assets/art/QsppManager.icns (needs Pillow)
open "dist/Hugh's QSPP Manager.app"
```

The bundle embeds the verified corpus pack at `Contents/Resources/qspp-pack`,
so document import and the Library work without the original vendor checkout.
Pack discovery order (DD-27): `QSPP_PACK_ROOT` env → Settings override →
vendored repo pack → bundled `Contents/Resources/qspp-pack` → legacy Linux path.

Intel Macs: the script targets `osx-arm64`; change `-r osx-arm64` to
`-r osx-x64` in `scripts/package-app.sh` (x64 artefact is labelled untested —
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
  every hash before extracting anything.
- Status of the remaining work and known limitations: `docs/milestones.md` (M5
  ledger) and the final report for this milestone.