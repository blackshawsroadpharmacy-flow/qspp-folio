# QSPP Manager

Desktop companion for Australian community pharmacies working through the
Quality & Safety Pharmacy Program (QSPP) — understand the requirements, prepare
evidence, run the operational registers, and walk into the assessment organised.

- Initial pharmacy: **Burke Road Compounding** (VIC, Australia)
- Stack: C# / .NET 10 (LTS) / Avalonia 11, SQLite + EF Core, QuestPDF for print
  rendering (licence eligibility pending owner confirmation — see docs/dependencies.md)
- Visual language: the owner's OpenScript design system (see
  `~/.hermes/skills/design/openscript-desktop-design/`), light theme
- Reference corpus: `~/Projects/qspp-pack` (read-only source material)

## Documentation

| File | What it is |
|---|---|
| `docs/product-brief.md` | The owner's brief, condensed into the canonical product spec |
| `docs/decisions.md` | Architecture/decisions ledger with reversibility notes |
| `docs/milestones.md` | Milestone plan + honest implemented/partial/blocked ledger |
| `docs/coverage-ledger.md` | Requirement corpus coverage against primary sources |
| `docs/dependencies.md` | Pinned dependencies, licences, and why each was chosen |

## Build and run

```bash
~/.dotnet/dotnet build qspp-manager.sln      # or after PATH export, `dotnet build`
~/.dotnet/dotnet run --project src/QsppManager.Desktop
```

Assets (fonts, icons) are fetched once by `tools/setup-deps.sh`; they are
committed to the repository so the app builds offline afterwards.

## Data

Application data (SQLite database + managed attachments) lives in the OS
application-data directory (`~/.local/share/QsppManager` on Linux by default; set
`QSPP_DATA_DIR` to override). The reference corpus in `~/Projects/qspp-pack` is
never modified by this application.