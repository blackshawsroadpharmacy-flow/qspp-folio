# Dependencies

Pinned after first restore (`dotnet list package --include-transitive`). Reasons
and licences recorded here; notices bundled at packaging time (M5).

| Package | Role | licence | Decision |
|---|---|---|---|
| Avalonia (12.1.4) | UI framework | MIT | adopt; **pinned 12.1.4 uniformly (DD-17)** — DataGrid requires 12.x |
| Avalonia.Controls.DataGrid (12.1.2) | registers/tables | MIT | adopt; pinned compatible with 12.1.4 (NU1603 fixed, t4) |
| Avalonia.Desktop (12.1.4) | macOS/Windows/Linux windowing | MIT | adopt |
| Avalonia.Themes.Fluent (12.1.4) | base control themes under the QSPP token dictionaries | MIT | adopt |
| Avalonia.Headless + Headless.XUnit | UI smoke tests | MIT | adopt |
| CommunityToolkit.Mvvm | MVVM source generators | MIT | adopt |
| Microsoft.EntityFrameworkCore.Sqlite | persistence | MIT | adopt |
| Microsoft.Data.Sqlite | (transitive) | MIT | — |
| NodaTime | TZ/calendar maths (no holiday calendar — jurisdictions supplied by us) | Apache-2.0 | adopt |
| UglyToad.PdfPig | PDF text extraction for search/import; NOT a viewer | Apache-2.0 | adopt |
| QuestPDF | print/PDF document generation | Community: free where org gross revenue < $1M — **eligibility pending owner confirmation (DD-9)**; else MigraDoc (AGPL, internal use) | adopt pending confirmation |
| ClosedXML | spreadsheet exports (registers, matrices) | MIT | adopt |
| DocumentFormat.OpenXml | Word templates only (brief §11) | MIT | adopt (deferred until doc-template feature) |
| DiffPlex | source-change diffs | Apache-2.0 | adopt (M4) |
| Lucide (static SVG assets) | icons | ISC | adopt (bundled) |
| Serilog (+ Sinks.Console/File) | diagnostics logging, no document content/secrets | Apache-2.0 | adopt |
| xunit / xunit.runner.visualstudio | tests | Apache-2.0 | adopt |
| AvaloniaHostPdf / PDFtoImage / SkiaSharp | (investigated M2) PDF page rendering for in-app preview | — | **resolved: not adopted (DD-20)** — native Skia collision crashed boot; preview opens the rendered print file in the system viewer |

## M5 additions (verified 2026-10-20)

| Thing | Role | licence | Decision |
|---|---|---|---|
| Lucide `lucide-static@0.544.0` subset (56 icons) | toolbar/nav/status glyphs, bundled at `assets/icons`, rendered as parsed Geometry (no package dependency at runtime) | ISC | adopt |
| Host Grotesk (Regular + Italic) | app UI typeface, `assets/fonts`, embedded | OFL | adopt |
| JetBrains Mono (Regular/Medium/SemiBold) | reference ids, checksums, dates in tables (`mono` classes) | OFL | adopt |
| Pillow (11.3.0, build-time only) | `scripts/make-icon.py` draws the .icns (green squircle, shield + check) | MIT-CM | build-time tool on the dev machine; never ships inside the app |
| `iconutil` (macOS) | iconset → icns | system | build-time only |
| QuestPDF (2026.9.1) | print/PDF composition | Community (eligibility confirmed, DD-9) | adopt |
| Serilog Sinks.Console 6.1.1 / Sinks.File 7.0.0 | logs (no document content or secrets) | Apache-2.0 | adopt |
| EF Core Sqlite + Design 10.0.13 | persistence + migrations | MIT | adopt |
| ClosedXML 0.105.1 · DiffPlex 1.9.0 · PdfPig 0.1.16 | exports · source-diffs · PDF text | MIT · Apache-2.0 · Apache-2.0 | adopt |
| xunit | 68 tests (30 Domain + 38 Integration), green at close-out | Apache-2.0 | adopt |
| CommunityToolkit.Mvvm | still deferred (DD-18, second deferral stands) | MIT | not adopted yet |

## Explicitly not adopted
| Thing | Why |
|---|---|
| PDFsharp/MigraDoc | AGPL — heavier obligations if the app is ever commercialised; kept as fallback only |
| Cloud services | brief forbids by default |
| Any package for redaction drawing boxes | Rule: redaction must remove content or be delegated to externally redacted derivatives (brief §8) |