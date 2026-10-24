# M5/t7 — design-system conversion contract

Applies to every destination page and dialog in `src/QsppManager.Desktop/`.
Goal: one visual language (warm paper, sage, forest green) across all screens, dark-mode safe.

## Read first (authoritative)

1. `src/QsppManager.Desktop/Ui.cs` — the kit (H1/H2/H3, Chip, Section, EmptyState, buttons, Lucide, Grid columns, InfoLine, `Ui.Br`).
2. `src/QsppManager.Desktop/App.axaml` — tokens (`Color.*` / `Brush.*`, both theme dictionaries) + shared styles (Button/TextBox/ComboBox, `ListBox.list`, `Border.card`, `Border.issuebox`, `TextBox.result`, DataGrid `dg|DataGrid` auto-styled).
3. Reference conversion: `Views/RequirementsView.axaml` (left list pattern) + `RequirementsView.axaml.cs` (`StatusRow` → `Ui.Chip`, `IPageSearchFocus`, issue box). Match this level of conversion.

## Rules

1. No hard-coded colors. Replace `Color.Parse`, hex strings, and local brush helpers with `{DynamicResource Brush.X}` (axaml) or `Ui.Br("Brush.X")` (code). Tokens available in both themes: Paper, White (Surface), Sage, Ink, Green, GreenHover, Muted, Divider, ControlBorder, Acid, Focus, Alert, Caution, CautionBg, Error, RowHover, SelBg, Sidebar, HoverTint, DangerBg, OkBg.
2. Lists → `Classes="list"` on the ListBox with the two-line row template from RequirementsView. Tables stay DataGrid (the `dg|DataGrid` styles apply automatically).
3. Headings/subtitles: `Ui.H1`/`H2`/`H3` or Classes `h1/h2/h3`; helper text `Classes="small"/"lead"`; page body wrapped with left-aligned `MaxWidth` ≤ 1040.
4. Grouped content → `Ui.Section(title, body)` in code or `Border.card` in axaml. Issue/warning boxes → `Classes="issuebox"`.
5. Status labels → `Ui.Chip(text, tone)` with tones `ok / warn / bad / info / neutral` (never colour-only: the chip text itself is the label).
6. Empty lists → `Ui.EmptyState(iconName, title, hint, optional action)`.
7. Buttons: default Buttons are already themed; primary CTA gets `Classes="primary"`; destructive `Classes="destructive"`; subtle `Classes="ghost"`. In code prefer `Ui.PrimaryBtn/GhostBtn/DangerBtn`.
8. Long-running action feedback (uploads, saves) → `TextBox.result` result strip on the page (see RequirementsView `_resultBar`).
9. Margins: screens sit directly in the shell PageHost (no ScrollViewer of your own unless the page already scrolls internally); keep padding ~24–32 and don't fight the toolbar height.
10. Automation: name primary interactive controls (`Ui.Ax(c, name, help)`) and implement `IPageSearchFocus` when the page has a filter/search box:

```csharp
public void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }
public void SetFilter(string text) => SearchBox.Text = text;   // TextChanged handler re-queries
```

11. Behaviour lock: do NOT change any workflow logic, service calls, validation, or text semantics. Visual restyle + the `IPageSearchFocus` interface only. No new features.
12. Off-limits files (read-only for conversion): `Ui.cs`, `App.axaml`, `App.axaml.cs`, `MainWindow*`, `ShellViewModel.cs`, `Program.cs`, `*.csproj`, everything under `Infrastructure/` and `Application/` unless your task explicitly says otherwise. If something needs the kit extended, STOP that item and report it.
13. Verification (each agent): build your own compile with

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/hughn/AI/qspp-folio && dotnet build src/QsppManager.Desktop/QsppManager.Desktop.csproj --nologo 2>&1 | grep -E "error|Build succeeded|Build FAILED"
```

If the build reports file-in-use lock errors, a sibling agent is building — wait 30 s and retry (max 3 tries). Never delete bin/obj.
14. Report: files changed, the real build line, and any item you reported instead of guessing.

## The Today screen (24 Oct 2026)

Code-built page: `Views/TodayScreen.cs` (+ `Views/Today/TodayControls.cs`), hosted by `Views/TodayView.axaml(.cs)`; data assembled read-only in `ViewModels/TodayViewModel.cs` from `QsppWorkspace.TodayBrief`. Decision record: DD-40.

1. Hero is the only filled panel on the page: light = `Brush.HeroBg` (the ink green), dark = the deep-green `Color.HeroBg` designed for dark (never a naive inversion). Hero text = `Brush.HeroFg`; secondary lines `Brush.HeroMuted`; the thin run-line rules `Brush.HeroDiv`. All token-driven; WCAG AA verified on both grounds (min 6.03:1).
2. The only acid accent on Today is the run line's today-dot / elapsed segment (`Brush.Acid` on the hero). It appears nowhere else on the screen.
3. The big numeral is `TextBlock.heroNum` — `Font.Serif` (Georgia first, bundled Host Grotesk for the rest) at 104pt Light. `Font.Serif` is Today-only: titles elsewhere stay in the shipped families. Serif numerals + hairline rules are the "pharmacopoeia look"; no shadows, no card borders, radius 12 on the hero only.
4. The run line (`Today/TodayControls.cs` → `RunLine`) draws with `DrawingControl`: a 1px track from the start anchor (or from today when none is recorded — never a guessed start), a filled segment to today, a 7pt today dot, and small diamond/square/circle/bar ticks for recorded milestones. Start falls back to today; nothing is extrapolated.
5. Responsive: below 980 px the hero/position grid stacks (position block drops to row 1) and the week/inbox columns stack — the rule lives inside `TodayScreen` (its own `Root.SizeChanged`), so it also holds in off-screen renders and any future host that bypasses TodayView.
6. The renewals ledger hides an empty bucket group while any other group has rows; when the whole ledger is quiet it is a single line (never an empty three-section wall). Rows beyond a peek of 8 collapse behind a "Show all N" toggle.
7. Empty states speak with the ledger's voice: "No reference texts (AMH, APF, eMIMS, eTG) recorded. Add one →" — one line, deep-link where one exists.
8. Greeting is by hour with the Melbourne calendar date in the eyebrow; the greeting is personal (first name) but the pharmacy line comes from the workspace record, not a constant.