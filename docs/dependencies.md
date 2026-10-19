# Dependencies

Pinned after first restore (`dotnet list package --include-transitive`). Reasons
and licences recorded here; notices bundled at packaging time (M5).

| Package | Role | licence | Decision |
|---|---|---|---|
| Avalonia (11.x) | UI framework | MIT | adopt (owner brief) |
| Avalonia.Controls.DataGrid | registers/tables | MIT | adopt; verify version compat with Avalonia release in use |
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
| AvaloniaHostPdf / PDFtoImage / SkiaSharp | (investigate at M2) PDF page rendering for in-app preview | — | investigate; brief says prefer reliable renderer |

## Explicitly not adopted
| Thing | Why |
|---|---|
| PDFsharp/MigraDoc | AGPL — heavier obligations if the app is ever commercialised; kept as fallback only |
| Cloud services | brief forbids by default |
| Any package for redaction drawing boxes | Rule: redaction must remove content or be delegated to externally redacted derivatives (brief §8) |