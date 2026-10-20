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
cd /Users/hughn/AI/qspp-manager-omarch/qspp-manager && dotnet build src/QsppManager.Desktop/QsppManager.Desktop.csproj --nologo 2>&1 | grep -E "error|Build succeeded|Build FAILED"
```

If the build reports file-in-use lock errors, a sibling agent is building — wait 30 s and retry (max 3 tries). Never delete bin/obj.
14. Report: files changed, the real build line, and any item you reported instead of guessing.