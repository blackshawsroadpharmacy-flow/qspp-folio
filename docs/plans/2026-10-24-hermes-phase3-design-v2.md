# Hermes: Phase 3 brief, design language v2

Hugh has reviewed the after-kit renders (`9902f9c`) and the Phase 3 display-title commit (`3f615a3`). The structure is better. The look is still bland:

- The type reads as one voice.
- Colour does almost no work.
- The hero numeral collides with the line under it.

This brief adds a richer type system and a meaningful colour system, then fixes a list of visible defects. It overrides the original plan (`docs/plans/2026-10-24-hermes-premium-polish.md`) only where it says so. Everything else in the original still holds: the product rules, the behaviour lock, the scope, the stop conditions and the commit discipline.

Work order: 3A to 3F below, then STOP with a contact sheet for Hugh. After his OK, continue with the original plan's remaining work using the v2 rules.

---

## Display-title sample (Hugh edits this block before sending)

`STATUS: NOT YET APPROVED`

If Hugh changes this to APPROVED, write the remaining titles in the same voice. One rule is added: every title must make sense read alone in a list, with no parent context. For example, "Record in a consistent manner" fails; "Keep records in a consistent format" passes.

---

## 3A. Fix the hero numeral first (it looks sloppy, so it goes first)

Root causes, both confirmed in code:
1. `Views/TodayScreen.cs` builds `_heroNumeral` with `Margin = new(0, -14, 0, -22)`. The negative bottom margin pulls the date line up into the digits. On top of that, `FontSize = 96` in code overrides the 104 in the `heroNum` style, so the size has two sources of truth.
2. `Font.Serif` lists "New York" first, but it doesn't resolve, so Georgia renders. Georgia's figures are old-style: the 4 and the 9 descend below the baseline into the next line. The same old-style figures make "0 of 470" and the other counts look uneven.

Fix:
- Delete every negative margin in the hero.
- Put the numeral and "days" in their own grid row with a fixed height equal to the numeral's line height. Align "days" to the numeral's baseline. Leave 14pt between that row and the date line.
- Size the numeral at **80pt**, in the `display.hero` style from 3C. Set the size in the style only, never in code.
- Every number in the app uses **lining, tabular** figures (3B explains how).
- Add a layout assertion to the offscreen render harness for Today:
  - the numeral row's bounds bottom must be at or above the date line's top
  - the date line must not intersect the title
  - the check runs at 100%, 130% and 150% text scale
  - a failure exits non-zero

  This overlap must not come back.

---

## 3B. Make the fonts real, then verify what actually renders

The kit references fonts that aren't in the source tree. `App.axaml` points at `avares://QsppManager.Desktop/Assets/Fonts/JetBrainsMono-Regular.ttf` and `HostGrotesk.ttf`, but there is no `Assets/Fonts` folder. So most text falls back to whatever the OS supplies, and that is a big part of why everything looks the same.

1. **Add a `--fonts` diagnostic flag** that prints, for every `Font.*` token and every text style, the family Avalonia actually resolved and whether lining and tabular figures are available. Run it before and after.
2. **Display serif: bundle Newsreader** (Production Type, SIL OFL 1.1, from Google Fonts or the GitHub release). Include Light, Regular, Medium and Italic, plus the optical-size "Display" cuts if available.
   - Fallback: Source Serif 4 (OFL).
   - Confirm the digits 0 to 9 sit on the baseline (no descenders) and are tabular. If the face defaults to old-style figures, apply OpenType `lnum` + `tnum` through Avalonia's font features API, if Avalonia 12.1 exposes one. Otherwise switch to the fallback face and say so.
3. **UI sans: get the real macOS system font.**
   - Try the family names Avalonia's macOS font manager accepts for SF Pro, in order: `$Default`, `.AppleSystemUIFont`, `SF Pro Text`, `SF Pro Display`, `SF Pro`.
   - Use whichever resolves, and use the Display cut for sizes 17pt and above where the platform allows.
   - If none resolve, bundle Inter (OFL) and use Inter Display for 17pt and above.
   - Report exactly which family won.
4. **Mono: bundle JetBrains Mono** Regular and Medium (OFL) at the path the tokens already point to.
5. **Licensing.** Put each font's licence file next to it in `Assets/Fonts/` and list the fonts in `docs/dependencies.md`. Fonts are assets, not NuGet packages, so this is allowed.

---

## 3C. Type system v2 (replaces the original section 2 type table)

The rule that makes this work: **two neighbouring levels must differ in at least two of family, size (3pt or more), weight and colour.** Today, section titles, labels and body are all sans at 13 to 16pt in similar weights, which is why it reads as monotone.

| Style | Family | Size / line | Weight | Colour | Notes |
|---|---|---|---|---|---|
| `display.hero` | Serif (Display cut) | 80 / 80 | Light | HeroFg | Lining, tabular. Today only |
| `title.page` | Serif | 30 / 36 | Regular | Ink | Tracking -0.2. One per page |
| `title.detail` | Serif | 22 / 28 | Regular | Ink | Requirement and document detail titles |
| `stat` | Serif | 28 / 32 | Regular | Meaning hue (3D) or Ink | Counts on Today, Assessments, section headers. Lining, tabular |
| `title.section` | Sans (Display cut) | 17 / 22 | Semibold | Ink | Tracking -0.1. "Equipment register", "Pharmacy profile" |
| `eyebrow` | Sans | 11 / 14 | Semibold | Page or area hue | Caps, tracking 0.08em. Above titles and groups |
| `headline` | Sans | 13.5 / 18 | Medium | Ink | List row titles, table first column |
| `body` | Sans | 13 / 19 | Regular | Ink at 88% | |
| `callout` | Sans | 12 / 16 | Regular | Muted | Secondary row lines, leads |
| `label` | Sans | 11.5 / 14 | Medium | Muted | Form labels in **sentence case**. "(optional)" as a lighter suffix |
| `code` | Mono | 11 / 14 | Regular | Muted | Refs, ids, hashes, table dates |
| `quote` | Serif | 16 / 24 | Regular | Ink | Verbatim wording |
| `quote.cite` | Sans | 11 / 14 | Regular | Muted | Citation under a quote |

Also:
- **Kill all-caps form labels.** "PHARMACY NAME", "COPIES" and "CYCLE START (OPTIONAL)" become sentence-case `label`. Caps are reserved for eyebrows. Two kinds of caps text is part of the monotony.
- **Leads.** Every page lead is `callout`, at most 90 characters, and the full text goes in the ⓘ popover (original 3.9, still outstanding).
- **One family per role.** No view sets `FontSize`, `FontWeight` or `FontFamily` inline. Everything goes through these styles. Grep for inline font settings in `Views/` and remove them.

---

## 3D. Colour system v2 (lifts the original "colour does no work" restraint, keeps the discipline)

Colour now carries meaning in four families:
- **Where you are:** destination.
- **What it belongs to:** requirement area.
- **What state it's in:** status.
- **What kind of date it is:** Today categories.

The hues are earthy and desaturated, taken from the apothecary illustration palette Hugh already uses: capsule teal, clay, echinacea berry, kraft umber, seed-head ochre, foxglove violet. Forest stays the brand and action colour.

### Tokens

Add each hue to both theme dictionaries as `Color.<Hue>`, `Color.<Hue>Tint` and their brushes:
- Light tint = the hue at 12% over Paper.
- Dark tint = the hue at 16% over the dark Paper.

Text contrast was checked against Paper and the light tint. Re-verify every pair you actually use against WCAG AA (4.5:1 for text under 18pt).

| Hue | Light | Dark | Light-on-tint contrast |
|---|---|---|---|
| Forest | `#23614D` | `#5CB08A` | 5.5 |
| Teal | `#1F6A6A` | `#6CC2BC` | 4.9 |
| Clay | `#9A4228` | `#E58F6E` | ≥4.6 (verify) |
| Berry | `#A0345A` | `#E77FA3` | 5.1 |
| Umber | `#7A5A2E` | `#CFAE7E` | 4.9 |
| Ochre (text) | `#7E5400` | `#E2B35A` | ≥4.6 (verify) |
| Ochre (fill, no text on it) | `#D9A441` | `#C99536` | n/a |
| Violet | `#6A4C9C` | `#B39BE3` | 5.2 |
| Red | `#A32A2A` | `#E07171` | 5.4 |
| Stone | `#5E635A` | `#A3A89E` | ≥4.5 (verify) |

### What each hue means (every use of colour must trace to a row here)

**Destinations.** These set the sidebar icon, the page header tile and the page eyebrow:

| Destination | Hue |
|---|---|
| Today | Ochre |
| Requirements | Forest |
| Evidence & documents | Teal |
| Tasks & calendar | Clay |
| Registers | Umber |
| People & training | Berry |
| Assessments | Violet |
| Library | Ink |
| Print centre | Stone |
| Settings | Stone |

**Requirement areas.**
- The four guide domains, with names taken from the seed, not invented: domain 1 Teal, domain 2 Clay, domain 3 Berry, domain 4 Umber.
- Program rules, terms, policies and obligations: Stone.
- The remote round and the readiness list: Ink.

**Status.** Always a dot or icon plus a word:
- Not started: Stone, hollow dot
- In progress: Ochre, half dot
- Ready for review: Violet
- Verified or approved: Forest, check
- Overdue, blocked or rejected: Red
- Planning only: Stone, dashed outline

**Today categories** (the year-ahead marks and the ledger glyphs): people Berry, equipment Umber, evidence Teal, program Forest. The featured QSPP deadline stays an Ink tall bar. Acid stays the run line's today dot only.

**Actions never change hue per page.** Primary buttons and links are Forest everywhere. Destructive actions are Red and only ever inside a confirm sheet or a `…` menu.

### Where colour goes (the accent placements)

1. **Sidebar.**
   - Each icon in its destination hue at all times, at 85% opacity when not selected.
   - Selected row: the hue's tint as background, the hue for the icon, Ink Semibold text.
   - Group eyebrows stay Muted.
2. **Page header.**
   - A 32pt rounded-square tile (radius 8): hue tint fill, a 16pt Lucide icon in the hue.
   - The tile sits left of the `title.page` serif title, with the page `eyebrow` in the hue above the title. For example "PREPARE" in forest above "Requirements".
   - One tile per page. Never repeat tiles in grids.
3. **Section headers.** `title.section` followed by a count in a small tint pill (for example "Equipment" with "0"), plus a quiet "Add…" on the right.
4. **Chips** (`Ui.Chip`). Tint background, hue text, leading dot or icon, radius 5, 11pt Medium. Replace the current flat grey/beige chips ("Not started", "Unknown").
5. **Requirement list.**
   - A 6pt area dot before each row's second line.
   - The selected row gets a 3px left rail in its area hue plus the SelBg fill.
   - Group headers carry the area eyebrow in the area hue.
6. **Detail panes.** A 3px top rule in the area hue across the top of the requirement detail. The eyebrow is in the area hue.
7. **Today.**
   - Accreditation position counts use `stat` and take a meaning hue only when they need attention: overdue above 0 Red, awaiting review above 0 Violet, unresolved interpretations above 0 Ochre, verified above 0 Forest. Zeros stay Muted.
   - The surveillance bar uses status hues.
   - Year-ahead marks and ledger glyphs use category hues.
8. **Assessments timeline.** Step badges as 24pt circles: done = Forest fill with a check, current = Violet ring with the number, later = Stone hairline.
9. **Data bars and segmented counts** use status hues only.

### Colour limits (these replace the original "NEVER" colour bullets)

- Tints only behind small things: chips, tiles, pills, the selected row, a notice. Large panels stay Paper or Surface. The hero remains the only big filled panel.
- At most three hues visible in any one region besides neutrals and Forest actions.
- No gradients, no glow, no saturated or neon colour, no blue.
- No decorative colour. If a hue can't be traced to a table row above, remove it.
- Colour is never the only signal (unchanged).

---

## 3E. Surfaces and tables (the other half of "premium")

1. **Grouped settings rows, macOS System Settings style**, for Settings, the Print centre options and any form with more than three fields:
   - a Surface group box (radius 10, 1px Divider), with rows separated by inset 1px rules
   - label on the left in `body`, control on the right, row height 40
   - a footnote under the group in `callout`

   This replaces the current long single-card forms.
2. **Tables (DataGrid restyle).**
   - Header: 11.5pt Medium Muted, sentence case, no vertical rules, 1px bottom rule.
   - Rows: 36pt, hover tint, first column in `headline`.
   - No horizontal scrollbar at 1100pt width or wider. Use star widths and hide low-value columns: sha256 moves to the detail or inspector, `Full text` becomes an icon column.
   - Clipped headers ("Patier", "Full te") are defects.
   - Display formatting only, no data changes:
     - Raw enum names get human labels ("PolicyOrSop" becomes "Policy or SOP").
     - File-name titles get a display form ("Position-Description-PA-Lvl-3-Dispensary" becomes "Position description: PA level 3, dispensary"). The real file name stays in the detail.
     - A lone "—" in a cell becomes empty, or "Not set" where a value is expected.
3. **Notices.** Banners are only for the result of an action the user just took. Standing information ("0 task(s) shown…", "No printers visible to CUPS…") becomes an inline `callout` line where it applies, never a permanent banner.
4. **Empty states.** At most 120pt tall, top-aligned under their header, never centred in a void (Tasks, People and Registers currently are). The icon is 20pt in the page hue.

---

## 3F. Defects from the after-kit renders (fix all of these in this pass)

1. **Toolbar user chip says "OW".** It should show Hugh's initials ("HN"). Find where `_session.DisplayName` comes from (it looks like a role or workspace string, not the person's name) and use the signed-in person's full name.
2. **Sidebar header.** The pharmacy name truncates ("Burke Road Compounding Pharm") on some screens and wraps on others. Make it identical everywhere: up to two lines in 13pt Semibold, then "QSPP Folio" in `callout`, with a fixed block height so the nav never shifts between screens.
3. **Toolbar filter field.**
   - Make it a Mac search field: magnifier glyph, Sage fill, no border, radius 7, height 28, placeholder "Filter".
   - The ⌘F hint goes in the ⓘ tooltip, not the placeholder.
   - "⌘ Commands ⌘K" shows the ⌘ glyph twice. It becomes "Commands" plus a keycap-styled "⌘K".
4. **Registers.** "Retire" still renders as a filled red button in the action row. Move it into a `…` menu (the confirm sheet stays). Each register's actions appear only when a row is selected.
5. **Settings.**
   - The date pickers are tiny with an oversized "24" calendar glyph. Make them 180pt fields with a 14pt calendar icon, inside the grouped rows from 3E.
   - "Resulting known deadlines" has about 250pt of empty space and prints the raw provenance string. Make it a compact two-column list: date in `code`, label in `body`, provenance as a footnote line.
6. **Assessments.**
   - The metrics strip ("0 req. Accreditation verified") becomes a row of `stat` counts with sentence-case labels.
   - Step titles lose "1 —" and use the 3D badges.
   - Buttons become text links with a chevron.
   - Each step shows its fact line, with the advice line behind "More".
7. **Requirements detail.**
   - The "Next action" heading and "NEXT ACTION" label are a double caption. Keep one.
   - The "Plain English" line repeats the quote. Hide it when it duplicates.
   - Placeholder text with em dashes ("Reason for the decision — assessors…") becomes plain sentences.
8. **Today** (on top of 3A):
   - Accreditation counts in a right-aligned `stat` column with dotted leaders.
   - Fix the orphan dash under the surveillance legend.
   - Label the ends of the run line.
   - Label a lone year-ahead mark ("30 Jun · PPA certificate").
   - "Activity" becomes a text disclosure, not a box that looks like a combobox.
   - The hero provenance becomes "Source: PPA notice (ppaonline.com.au) · Confirmed" with the consequence on its own line. This is a display formatter only.

---

## Verification and the review stop

1. Build clean, all tests green (record the count; it was 198 before `3f615a3`), and the new Today layout assertion passes at 100, 130 and 150%.
2. Run `--fonts` and paste the resolved families into the report.
3. Render all 10 screens in light and dark at 1440×900 into `docs/screenshots/v2/`.
4. Build two contact sheets with Pillow (`scripts/make-icon.py` already depends on it): `docs/screenshots/v2/contact-light.png` and `contact-dark.png`. Each is 10 screens in a 5×2 grid, each tile 720px wide, with the screen name under it.
5. If you can view images, compare every screen against 3C and 3D and fix anything that misses the rules. In particular check:
   - neighbouring type levels differ in two properties
   - every hue traces to a table row
   - no region shows more than three hues
6. Commit as "Premium polish phase 3 v2: …" with the test count.
7. **STOP.** Report:
   - the contact sheet paths
   - the font resolution table
   - the contrast table for every text and background pair you used
   - anything you couldn't do

   Wait for Hugh before continuing to the Requirements rebuild and the other screens. Those then use the v2 type and colour rules throughout.

---

## Changes to the original plan

These override the original document. Everything not listed still stands.

- **Original section 2 type table:** replaced by 3C.
- **Original NEVER list, colour bullets:** replaced by the colour limits in 3D. Still banned: gradients, glassmorphism, glows, drop shadows on content, blue, emoji, scores and percentages.
- **The ban on icons in tinted shapes:** relaxed for exactly two places, the sidebar and one page-header tile per page. Grids of icon tiles remain banned.
- **"Serif only on Today" (DD-40):** widened to the roles in 3C. Record this as DD-43, as already planned, and include the font bundling.
- **New decision DD-44:** the colour meaning system (the 3D tables), with the contrast evidence.
