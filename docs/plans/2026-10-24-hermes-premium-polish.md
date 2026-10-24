# Hermes task: QSPP Folio, premium Mac polish pass

You are the design lead and senior Avalonia engineer on a small, expensive Mac software team. Your team's apps sit next to Things 3, Mimestream, Fantastical and Bear in the Dock and people pay for them partly because they are calm to use. Your job in this session is to take QSPP Folio from "a well-built internal tool" to "an app a pharmacist actually wants to open". It must feel inviting and quiet. A first-time user must never feel buried.

Read before you write. Work in phases. Stop where this prompt says stop.

---

## 1. Ground truth (carry this the whole session)

- Repo (canonical): `/Users/hughn/AI/qspp-folio`. Branch off `master` to `design/premium-polish` before the first change.
- Stack: C# / .NET 10, Avalonia 12.1.x (Fluent theme underneath), SQLite + EF Core 10, QuestPDF. macOS, Apple Silicon.
- Build and test env, every shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`
  - Build: `dotnet build qspp-manager.slnx --nologo`
  - Test: `dotnet test qspp-manager.slnx --nologo` (baseline is 197 passing: 167 Integration + 30 Domain. Confirm the real number in Phase 1.)
- Offscreen render already exists for Today: `--render-offscreen <png> --width W --height H` in `src/QsppManager.Desktop/Program.cs`.
- Read these first, in this order: `README.md`, `docs/design-system-conversion.md` (the kit contract), `docs/decisions.md` (DD-1 to DD-40), `docs/product-brief.md`, `src/QsppManager.Desktop/App.axaml` (tokens and styles), `src/QsppManager.Desktop/Ui.cs` (the kit), `MainWindow.axaml` + `.cs`, `Views/RequirementsView.axaml` + `.axaml.cs`, `Views/TodayScreen.cs`, `Views/Today/TodayControls.cs`, `seed/requirements.json`, `seed/remote-assessment.json`, `src/QsppManager.Domain/Entities/CoreEntities.cs`.
- Data: NEVER render or test against the live workspace in `~/Library/Application Support/QsppManager/`. Copy it once to `$HOME/.hermes/cache/scratch/qspp-render/` and point `QSPP_DATA_DIR` there for every render. Delete nothing.

Product rules that stay in force, no exceptions:
1. Standards, guide, rule and terms wording is reproduced verbatim. You may add a plain display title beside it. You may never edit, paraphrase over, or hide the verbatim text.
2. No compliance score, percentage, ring or gauge anywhere. Counts only ("12 of 40 started").
3. No invented dates or deadlines. Blank stays blank, shown as "Not set".
4. Colour is never the only signal. Every status has a word.
5. Exactly 10 destinations in the sidebar: Today, Requirements, Evidence & documents, Tasks & calendar, Registers, People & training, Assessments, Library, Print centre, Settings.
6. Behaviour lock: no change to workflow logic, service calls, validation, state machines, audit, backup or the assessor pack. This is a design pass plus one read-only display layer (section 4).
7. Token discipline: no hex values in views. New colours go into both theme dictionaries in `App.axaml`.

---

## 2. What "premium Mac" means here (concrete, not vibes)

Hold every screen to these. If a choice isn't on this list, pick the quieter option.

- **One window, one surface.** The sidebar runs full height with the traffic lights inside it. The toolbar is the same paper colour as the content, separated by a single 1px `Divider` rule. No coloured bars.
- **Typography does the hierarchy.** Big serif titles at regular weight, small confident sans for everything else, mono only for codes. Never bold a large heading.
- **Progressive disclosure.** Each screen opens with one short line of explanation, not a paragraph. Detail lives behind an ⓘ popover, a disclosure or a selection.
- **One primary action per region.** Everything else is a quiet button, a header link or a `…` menu. Actions that need a selection appear only when something is selected, next to the thing they act on.
- **Empty means short.** An empty list is one line plus one action, never a 300px white box.
- **Native control metrics.** Control height 26pt (28 for the one primary), 13pt text, radius 6, 12pt horizontal padding. Lists 30 to 52pt rows. These are Mac sizes, not web sizes.
- **Quiet motion.** 120 to 180ms opacity or height changes on disclosure and hover. No slides, no bounce, no scale-on-hover. Respect reduced motion.
- **Words a pharmacist uses.** Plain Australian English, sentence case, no internal acronyms in titles, no em dashes in new UI strings.

### Type scale (put these in `App.axaml` as named styles and use nothing else)

| Style | Face | Size / weight | Use |
|---|---|---|---|
| `title.large` | Serif | 26 Regular | Page title, Today greeting |
| `title.2` | Serif | 20 Regular | Detail pane title |
| `headline` | UI sans | 13 Semibold | Section headers, row titles |
| `body` | UI sans | 13 Regular | Default |
| `callout` | UI sans | 12 Regular, Muted | Secondary lines |
| `footnote` | UI sans | 11 Regular, Muted | Citations, metadata |
| `eyebrow` | UI sans | 10.5 Semibold, tracked 0.08em, caps, Muted | Group labels |
| `code` | JetBrains Mono | 11 Regular, Muted | Refs, ids, dates in tables |
| `quote` | Serif | 15 Regular, line height 1.45 | Verbatim source wording |

Serif: use `New York` if it's installed (check `/System/Library/Fonts/NewYork*.ttf`), otherwise `Georgia`. Set `Font.Serif` accordingly and widen DD-40's "serif is Today-only" rule to page titles, detail titles and verbatim quotes. Record the change as a decision.

The text-scale setting (100/115/130/150%) must still scale all of these.

---

## 3. Global fixes (shell and kit). Do these first, because every screen inherits them.

You are explicitly allowed to edit `Ui.cs`, `App.axaml`, `App.axaml.cs`, `MainWindow.axaml(.cs)` and `ShellViewModel.cs` for this section. The off-limits list in `design-system-conversion.md` is lifted for these files only, for this task.

1. **Kill the blue toolbar.** Cause: `TransparencyLevelHint="AcrylicBlur"` plus a transparent `ToolbarRow`, so the desktop wallpaper shows through. Make the toolbar opaque `Brush.Paper` with a 1px `Brush.Divider` bottom rule. Either set the transparency hint to `None` or confine any material to the sidebar with an opaque-enough tint that the result looks identical on any wallpaper. Then verify the same in dark mode.
2. **Toolbar contents.** Sidebar toggle, the screen filter (⌘F) and the ⌘K "Commands" button go on the left. The signed-in user goes on the right as initials in a 24pt circle with a menu (name, role, Sign out, Appearance). Remove the toolbar page title, because each page already has its title in the content. Height 52, content vertically centred with the traffic lights.
3. **Sidebar.**
   - Top: the pharmacy name ("Burke Road Compounding", headline) and "QSPP Folio" (footnote) under the traffic lights.
   - The same 10 items, in three quiet groups with eyebrow labels and no extra items: **Prepare**: Today, Requirements, Evidence & documents, Tasks & calendar. **Operate**: Registers, People & training. **Assess**: Assessments, Library, Print centre. **Settings** sits pinned at the bottom.
   - Row height 30, Lucide icons at 16pt with 1.5 stroke, 13pt labels.
   - Selected row: `SelBg` with Ink text in Semibold.
   - Hover is currently `#66163F35` (40% ink), which is why two rows look selected at once in the screenshots. Change it to about 6% ink in light and a matching value in dark.
   - Optional count badge on Today only, for overdue items, and only when the count is above 0.
4. **Remove every "Result messages appear here" strip** (eight screens). Replace them with one kit component, `Ui.Notice`: a slim banner pinned to the top of the page content, with an icon, a message, and a close button.
   - Success notices fade after 6s.
   - Errors stay and get a "Copy details" link.
   - The message text each screen produces is unchanged. Only its container changes.
5. **Buttons.** Restyle `Button`, `primary`, `ghost` and `destructive` to the metrics in section 2. The filled green primary appears once per region at most.
   - Destructive actions ("Retire") go in a `…` menu or a confirm sheet, never as a filled red button on the page.
   - `Ui.BtnRow` stays as the wrap container but is used far less (see the screen notes).
6. **Fluent leaks.** The selected-tab underline on the Requirements detail is blue. Find every Fluent resource still resolving to the toolkit accent (TabItem pipe, NumericUpDown spinner, CheckBox, ComboBox focus, DataGrid selection, CalendarDatePicker) and point it at the green tokens. Grep for remaining `SystemAccent`, `Accent`, `#0` and `#3` blue hexes.
7. **Read-only text that looks editable.** Verbatim wording is shown inside read-only TextBoxes, so it looks like an input. Replace these with the `quote` style: selectable text, 2px `Green` rule on the left, 14px left padding, no box.
8. **Empty grids.** Any `DataGrid` with zero rows is replaced by `Ui.EmptyState` at most 120pt tall: a 20pt icon (no tinted circle behind it), one headline line, one callout line, one quiet action. This applies to Evidence, Registers (all sections), Library source documents, Print centre recent jobs, Tasks and People.
9. **Page header.** Rebuild `Ui.Page` / `H1` / `Lead` as:
   - the `title.large` title, then
   - one `callout` line of at most 90 characters, then
   - an ⓘ button that opens a popover with the existing long explanation, word for word.

   Rewrite the one-liners yourself (examples in section 5). Fix the clipping bug: the Assessments lead runs off the right edge ("ready a").
10. **Dates.** Replace the day/month/year triple fields in Settings and in the dialogs with a styled `CalendarDatePicker`. It must allow blank, show "Not set" when empty, and use the `d MMM yyyy` format.
11. **Content width.** Single-column pages are capped at 760pt and centred. List and detail pages use the full width with a draggable split (list 320 to 420pt). Side gutters are 32pt.

Render all 10 screens after this section and look at them before moving on.

---

## 4. Requirements: make the titles human (the main content change)

### The problem, measured from `seed/requirements.json` (451) and `seed/remote-assessment.json` (19)

| RefKind | Count | Current title the user sees |
|---|---|---|
| UAGKeyTask | 284 | "UAG 1.2 section 1.2 Patient Safety and Quality Systems — quoted subclause 1.1.2.7 action b" |
| RULE | 51 | "Rule 13 — Accreditation assessments" (several rules share the same heading) |
| STD | 40 | "2.2.3.10 (clause 2.2.3 Complex compounding (if offered))" |
| UAG | 26 | "4.4 Pharmacy Premises — maps to standard clause 1.2.4" |
| TC | 23 | "Clause 16: Confidential information" |
| SURVEILLANCE | 19 | "Vaccination procedure" (already good) |
| HITLIST | 14 | "Hit-List #13 P3" |
| POLICY | 8 | "QSPP Exceptional Circumstances Policy" |
| OBLIGATION | 5 | "Fees and invoices: pay on strict 28-day terms" (already good) |

Also:
- The default list order mixes kinds ("T&C 18", then "Rule 1", then a UAG task).
- UAG key task wording is often a fragment that only makes sense with its parent stem, for example "c. Who are receiving palliative or end of life care."
- The UAG key task summary is boilerplate ("Quoted AS85000:2024 subclause 1.1.2.4 action c, reprinted in UAG section 1.2 Standard Subclause block.") but is shown as "Plain English:".

### The fix: a read-only display layer. No migration, no change to the stored `Title`.

1. **`RequirementDisplay` (pure, in `QsppManager.Domain/Services/`).** Given a requirement, it returns:
   - `DisplayTitle`
   - `Area`: the human group name, e.g. "Patient safety and quality systems"
   - `KindLabel`: the plain kind name (step 4)
   - `RefCode`: the existing code, kept for people who know it, e.g. "1.1.2.7 b"
   - `TitleBasis`: `source` or `editorial`

   Cover it with unit tests in `QsppManager.Domain.Tests`.
2. **Editorial titles live in a new reviewed file, `seed/display-titles.json`:** `{ "<id>": { "title": "...", "basis": "editorial", "reviewed": false } }`. Load it at startup, read-only. If an id has no entry, use the deterministic fallback in step 3.
3. **Deterministic fallback, by kind:**
   - **STD:** `subclauseTitle` when it's a real phrase. Otherwise editorial.
   - **UAG section:** the section name in sentence case ("Pharmacy premises").
   - **TC:** the clause heading ("Confidential information").
   - **RULE:** the rule heading, unless another rule shares it. Then the title must be editorial.
   - **POLICY:** drop the leading "QSPP" and the trailing "Policy" ("Exceptional circumstances").
   - **HITLIST:** editorial (from the summary).
   - **UAGKeyTask:** the verbatim wording with the leading "a. " letter and the trailing full stop removed, capped at 70 characters on a word boundary. Use this only if it reads as a complete instruction. Otherwise editorial.
4. **Acronym map for every label the UI shows.** The codes stay searchable and visible as small mono text.
   - UAG → "Assessment guide"
   - STD → "Standard"
   - T&C → "Terms"
   - REM / SURVEILLANCE → "Remote assessment"
   - Hit-List → "Readiness list"
   - P1 / P2 / P3 → "Priority 1" / "Priority 2" / "Priority 3"
   - AS85000:2024 → "the Standard (AS 85000:2024)" on first use in a detail pane only
   - 8CPA stays, with a tooltip.
5. **Writing the editorial titles.** Use the verbatim wording plus its parent stem (the UAG section's Standard Subclause block, or the parent STD clause) so each fragment is understood in context. Rules for every title:
   - starts with a verb where it's a task, otherwise a short noun phrase
   - 60 characters or fewer
   - sentence case, Australian English, no acronyms, no codes, no em dashes, no full stop
   - adds nothing the source doesn't say
   - two requirements never share a title within the same area

   Examples of the target voice:
   - "c. Involves pharmacy staff in the review of incidents." → "Involve staff in reviewing incidents"
   - "c. Who are receiving palliative or end of life care." (stem: identify patients at higher risk) → "Identify patients receiving palliative care"
   - Hit-List #13 → "Confirm potency testing covers every compounder"
   - Hit-List #2 → "Set up chemical spill kits and train staff"
   - Rule 13 → "Owner attends the whole onsite assessment"
   - STD 2.2.3.10 → "Assign beyond-use dates to compounded products"
6. **STOP gate.** Draft the first 40 editorial titles: 20 UAG key tasks across all four domains, 6 STD, 6 RULE, all 8 HITLIST that need one. Write them into a review table at `docs/review/display-titles-sample.md` with columns id, current title, verbatim wording, proposed title. Then stop and wait for Hugh to approve the voice. After approval, do the rest, mark them all `"reviewed": false`, and give Hugh a full review table the same way.
7. **Where display titles appear.** The Requirements list and detail, the Today ledger, the command palette, search results, Tasks and Evidence links, and the Print centre document picker (which currently shows "2.3.3 — 2.3.3 — clause 2.3 Clinical safety").
   - Display titles do NOT go into the assessor pack, the printed checklists, or anything that leaves the app as the requirement's text. Those keep the source title and the verbatim wording.
   - Search must still match codes ("1.1.2.7", "UAG 1.2", "Rule 13") as well as display titles and wording.

### The Requirements screen, rebuilt

**List pane:**
- A segmented control at the top: **By area** · **Remote round (19)** · **Readiness list (14)** · **Rules and terms**. The filter field sits under it.
- **By area** groups by the four domains, then by section, under sticky eyebrow headers with a count in words ("4 of 12 started"). Groups are collapsible and remember their state per viewer. The group holding the current selection opens automatically.
- **Rules and terms** groups Program rules, Terms, Policies and Obligations.
- Row layout:
  - `DisplayTitle` (headline, up to two lines)
  - then a callout line: area · status word with a 6pt status dot
  - then `RefCode` right-aligned in `code` style
- No row ever starts with a code or an acronym.

**Detail pane, top to bottom:**
1. Eyebrow: `KindLabel` · `Area` (e.g. "ASSESSMENT GUIDE · PATIENT SAFETY AND QUALITY SYSTEMS").
2. `DisplayTitle` in `title.2`. When `TitleBasis` is `editorial`, show a small "Plain title" footnote with an ⓘ explaining that the wording below is the official text.
3. A status stepper across the width: Not started → In progress → Ready for review → Verified. The current step is filled. The ONE next action is the primary button beside it ("Mark in progress"). "Record assessor outcome" and the other transitions go in a `…` menu. The transition rules are unchanged.
4. Owner as a token (initials + name) with a change menu. Delete the "Assign to me" button and put that action in the menu.
5. Tabs with a green underline: **Overview** · **Evidence (n)** · **Tasks (n)** · **Notes & issues**.
6. Overview:
   - **"What the source says"**: the verbatim wording in the `quote` style, then a footnote citation: "QSPP User Assessment Guide v1.1, Sep 2026, p.15 · verified".
   - Move "anchor approx. lines 23-23 (provisional)" and the other provenance internals behind a "Source details" disclosure.
   - Show "In plain English" only when the summary exists, isn't boilerplate, and isn't a near-duplicate of the wording. Otherwise hide it.
   - Expected evidence as a short list, with "Required" chips where the source marks it.
   - **Applies to us?** as a segmented control (Applies · Doesn't apply · Unsure). The reason field appears inline and there's one Save. Same service call as today.
   - **Next action**: a single-line field that grows, saved on blur or ⌘↩. Keep the existing save call.

---

## 5. Screen-by-screen notes

Each screen gets a one-line lead in place of today's paragraph. The old paragraph moves into the ⓘ popover word for word.

**Today** (keep DD-40 intact; refine only):
- The "249" numeral's descender overlaps "Wednesday 30 June 2027". Give the numeral its own line box with 8pt below.
- The provenance line reads "Ppaonline.com.au notice, pack KB 'Key dates' — applies from…". Format it for display as "Source: PPA notice (ppaonline.com.au) · Confirmed" with a separate consequence line. Use a formatter over the stored string. Don't edit the stored text or add facts.
- Run line: label both ends in `footnote` (start date, or "Today" when no start is recorded; deadline date). Label the dot "Today".
- Accreditation position: put the counts in a right-aligned column with dotted leaders, using `title.2` serif numerals at 17pt. Today they float at different x positions.
- Surveillance block: render the segmented bar properly, keep the legend on one line, and remove the orphan dash.
- Year ahead: label any mark that stands alone ("30 Jun · PPA certificate"). Label the today line.
- Greeting in `title.large` serif.
- Activity: a text disclosure "Recent activity (5)" with a chevron, not a box that looks like a combobox.

**Requirements:** section 4.

**Evidence & documents.** Lead: "Every document you hold, and what it proves."
- Rebuild as list and detail:
  - Left: documents with filter chips (All · Awaiting review · Approved · Expiring soon).
  - Right: the selected document with its review state, linked requirements (display titles), version history and the selection actions (Submit for review, Manager verifies, Needs rework, Supersede, Open file).
- The page toolbar has "Add evidence…" (primary) and "Process and Sort…" (quiet).
- The verified source corpus moves to a collapsed "Source documents" disclosure at the bottom, or links to Library. Don't duplicate the grid.
- **Investigate first:** both this grid and the Library grid render empty in the screenshots. Check the row count in the scratch database. If rows exist and nothing draws, that's a rendering bug. Fix it before restyling.

**Tasks & calendar.** Lead: "Your one-off and recurring jobs, with who does them."
- A segmented filter (Open · Overdue · This week · Done) in place of the combobox.
- Rows with a round completion checkbox like Things: title, due date in `code` style, owner initials, a linked requirement as a callout line.
- "Complete selected" goes. Ticking a row completes it, with a 5s Undo in a `Ui.Notice`.
- "New task" sits as a quiet + button in the header.
- An optional month mini-calendar on the right at ≥1200pt width, marking days with tasks. Read-only.

**Registers.** Lead: "Schedules, equipment and incidents in one place."
- A segmented control switches registers (Schedules · Equipment · Incidents · plus any others that exist), so one register shows at a time.
- The header row holds the title, the count, and "Add…" as a quiet button.
- Selection actions (Set calibration due, Send to maintenance, Back in service, Pause, Resume, Generate occurrences) appear in a detail strip when a row is selected. "Retire" goes in `…` with a confirm sheet.

**People & training.** Lead: "Your team and where each person is up to with training."
- Shrink the empty states (no tinted icon circles).
- "Add staff member…" and "Record training…" move into section headers.
- When people exist, each row shows the next training anniversary as a date and a tone word.

**Assessments.** Lead: "Seven steps from first read to the end of the cycle."
- The numbered list becomes a vertical timeline:
  - numbered 20pt circles: filled for done, outlined for current, hairline for later
  - each step title without "1 —"
  - the step's one fact line as callout text
  - the button as a text link "Open Requirements ›"
- Replace the metrics strip ("0 req. Accreditation verified") with the same ledger component as Today's Accreditation position. Fix the clipped lead.

**Library.** Lead: "The source documents, searchable, plus questions still open."
- The search field gets a full-width style with a ⌘F hint.
- The large empty results box only appears after a search.
- Source documents: fix the empty-grid bug (see Evidence). The actions (Open PDF, Extract text, Extract and index all) go in a `…` menu on the section header.
- Interpretation issues: each issue as a row with an "Open" status word.

**Print centre.** Lead: "Print checklists and the remote round pack."
- Lay it out like the macOS print panel: settings in a left column 360pt wide (Document, Variant, Printer, Copies, Duplex), with a live PDF preview on the right.
- Document picker uses display titles.
- An empty printer list shows "No printers found · Refresh" inside the picker, not an empty box plus a mono strip.
- The copies stepper is styled to tokens.
- "Render + preview" is primary. "Send to printer" is quiet and disabled until a preview exists.
- The remote round pack is its own small section.

**Settings.** Lead: none (the title is enough).
- Use a macOS Settings layout: category list on the left (Pharmacy · Program dates · Users and roles · Data and backups · Appearance), content on the right, capped at 640pt.
- Services checkboxes as a vertical list, each with a one-line description.
- Dates use the new picker.
- "Resulting known deadlines" is a compact two-column list (date in `code`, label) with no empty space.
- Every save keeps its existing service call.

**Dialogs.** Same type scale, control metrics and tokens. Primary on the right, Cancel to its left. Esc cancels and ↩ confirms.

---

## 6. NEVER (the anti-template list)

- No gradients, glassmorphism, glows or drop shadows. The one exception is a 1px rule.
- No coloured header bars, no blue, and no colour that isn't a token.
- No grids of identical cards with an icon in a tinted circle.
- No percentages, progress rings, scores, "You're 0% ready" or confetti.
- No emoji, sparkle icons or exclamation marks.
- No cheerleading copy ("Great job!", "Let's get started!").
- No wall of buttons. More than three visible buttons in one row is a defect.
- No codes or acronyms leading a title.
- No paragraph above the fold on any screen except inside an ⓘ popover.
- No large white empty boxes. No read-only text dressed up as an input.
- No bold headings above 20pt. No body text under 11pt.
- No new NuGet packages.

---

## 7. Scope

**Allowed:**
- everything under `src/QsppManager.Desktop/`
- the new `RequirementDisplay` service + tests in Domain
- loading `seed/display-titles.json` (read-only)
- `Program.cs`, but only to add `--page <name>` and `--theme light|dark` to the existing offscreen render
- docs

**Forbidden without asking:**
- any EF migration or schema change
- `Infrastructure/` and `Application/` beyond read-only display queries
- the vendor pack
- verbatim wording in seed files
- assessor pack and print content
- auth, audit, backup
- adding or removing a destination

---

## 8. Stop conditions (stop, report, wait for Hugh)

1. Phase 1 baseline: build or tests aren't green before you start.
2. The 40-title sample is ready (section 4, step 6).
3. Any change would need a migration or a new package.
4. An empty grid turns out to be a data or service bug rather than a rendering bug.
5. Build or tests fail and two fix attempts haven't cleared it.
6. A display title would have to say something the source doesn't.

---

## 9. Phases (commit at the end of each phase on `design/premium-polish`)

Use the existing commit message style. End each commit with the test count.

1. **Baseline.**
   - Branch.
   - Build and test, record the counts.
   - Copy the workspace to scratch.
   - Extend the offscreen render with `--page` and `--theme`.
   - Render all 10 screens in light at 1440×900 into `docs/screenshots/before/`.
   - Check row counts for the grids that look empty.
   - Report a table: screen, problems you see in the render, files involved.
2. **Shell and kit** (section 3). Render all 10 again into `docs/screenshots/after-kit/`.
3. **Display titles.** `RequirementDisplay`, tests, acronym map, the 40-title sample. STOP for approval, then the full set.
4. **Requirements screen** (section 4).
5. **Remaining screens** (section 5). One commit per screen.
6. **Verify.**
   - Build with no new warnings; all tests pass plus the new ones.
   - Render all 10 screens × light and dark × 1440×900 and 1100×760, plus Requirements and Today at 130% text scale, into `docs/screenshots/after/`. Look at every render if you can view images. Fix misalignment, clipping, overlap and anything that breaks section 6. If you can't view images, say so plainly.
   - WCAG AA contrast for every text token on every ground it sits on.
   - Keyboard-only pass: ⌘1 to ⌘0, Tab order, focus rings visible, Esc and ↩ in dialogs.
   - Grep the Desktop project for leftover hex colours, "Result messages appear here", and em dashes in strings you wrote.
7. **Document.**
   - Update `docs/design-system-conversion.md` with the type scale, control metrics, `Ui.Notice`, the empty-state rule, the page header pattern and the Requirements display layer.
   - Add decisions DD-41 (shell: opaque toolbar, sidebar groups, control metrics), DD-42 (display titles: separate from source, never exported, reviewed flag) and DD-43 (serif widened beyond Today).
   - Update `docs/milestones.md` honestly.
   - Replace `docs/screenshots/01-today.png`.

---

## 10. Done when

- No blue anywhere, on any wallpaper, in light or dark.
- The Requirements list reads like a list of jobs. No row starts with a code or acronym, and every code is still searchable.
- Every screen opens with a title and one line, then gets out of the way. No empty box is taller than 120pt. No row of more than three buttons.
- Verbatim wording is untouched, still visible, and still what the assessor pack and printouts use.
- The test count is the baseline plus your new tests, all green, and the before/after renders exist for all 10 screens.

**Final report:**
- what changed, per screen
- the render folders
- test counts, before and after
- the display-title review table location
- anything you stopped on or weren't sure of
