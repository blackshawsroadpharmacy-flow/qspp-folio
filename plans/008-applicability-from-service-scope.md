# Plan 008: applicability suggestions from the services a pharmacy offers, and the hint import bug

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-09, audit finding F-13. Effort M.

## Summary

The product's second question is "which requirements apply to this pharmacy?". Today it is answered by hand, 451 times. Two defects sit under it:

1. **Hint import bug.** The seed spells conditional hints `if_compounding` (36 rows), `if_indirect` (7) and `if_prescribing` (6). `SeedImporter.ParseEnum` uses `Enum.TryParse` (`SeedImporter.cs:538-539`), which does not match `IfCompounding` against `if_compounding`, so all 49 fall back to the default `Always`. The hint is lost in every installed database.
2. **Unused scope.** The Settings tick-boxes (`SettingsView.cs:246-250`) save `Pharmacy.ServiceScopeFlags`, and nothing reads them.

After this plan: hints import correctly and existing databases are repaired on next boot (hint is seed metadata, not user state); a pure rule maps (hint, services offered) to a **suggestion**; the requirement detail shows the suggestion with a "Use this suggestion" button that fills the choice and reason, and the manager still has to press Save. The app never sets applicability by itself.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10, Avalonia UI, EF Core / SQLite. Relevant files: `src/QsppManager.Domain/Enumerations.cs` (`ApplicabilityHint`, `ServiceScopeFlag`, `Applicability`), `src/QsppManager.Infrastructure/Seed/SeedImporter.cs`, `src/QsppManager.Infrastructure/Services/QsppWorkspace.cs`, `src/QsppManager.Desktop/Views/RequirementsView.axaml.cs`.
- Enum values: `ApplicabilityHint { Always, IfCompounding, IfSterile, IfPrescribing, IfIndirect, Unknown }`. `ServiceScopeFlag` flags: SimpleCompounding, ComplexCompounding, SterileCompounding, Prescribing, IndirectSupply, Vaccination, FullScopePractice. Settings exposes only Complex, Sterile, Prescribing, Indirect.
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- Baseline 231 tests at `5858779`; record N0 first. If plan 006 has landed, `SeedImporter.ImportRequirements` looks different (insert-missing); step 2 below touches `WordingRefresh` and `ParseEnum`, which plan 006 does not change.
- **Never** open `~/Library/Application Support/QsppManager/` or `QsppManager-backup-*`.
- Never verify a build through a pipe. Run bare, then `echo "exit=$?"`. No em dashes in text you write.

## Out of scope

A bulk "accept all suggestions" action. Adding "no compounding at all" as an explicit setting (with no service ticked the app makes no conditional suggestion and says so). Changing the Settings screen. Vaccination and FullScopePractice flags (no hint values use them).

## Step 1: fix the enum parse

**File:** `src/QsppManager.Infrastructure/Seed/SeedImporter.cs`, lines 538-539. Current:

```csharp
    private static T ParseEnum<T>(string raw) where T : struct, Enum
        => Enum.TryParse<T>(raw, ignoreCase: true, out var v) ? v : default;
```

**Change:**

```csharp
    private static T ParseEnum<T>(string raw) where T : struct, Enum
        => Enum.TryParse<T>((raw ?? "").Replace("_", "").Replace("-", ""), ignoreCase: true, out var v) ? v : default;
```

Every other use of `ParseEnum` passes values without underscores (`UAGKeyTask`, `RULE`, `STD`, `Program`, digits), so they are unaffected.

**Done when:** test `ConditionalHints_ImportWithUnderscoreSpelling` (step 6) passes.

## Step 2: repair hints in installed databases

**File:** same, `WordingRefresh` (lines 299-335). Current core of the loop:

```csharp
            var w = S(el, "wording");
            var s = S(el, "summary");
            var r = db.Requirements.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id);
            if (r is null || (r.WordingExcerpt == w && r.PlainSummary == s)) continue;
            r.WordingExcerpt = w;
            r.PlainSummary = s;
            updates++;
```

**Change:**

```csharp
            var w = S(el, "wording");
            var s = S(el, "summary");
            var hint = ParseEnum<ApplicabilityHint>(el.TryGetProperty("applicabilityHint", out var aEl) && aEl.ValueKind == JsonValueKind.String ? aEl.GetString()! : "Unknown");
            var r = db.Requirements.IgnoreQueryFilters().FirstOrDefault(x => x.Id == id);
            if (r is null) continue;
            if (r.ApplicabilityHint != hint) { r.ApplicabilityHint = hint; hintFixes++; }
            if (r.WordingExcerpt == w && r.PlainSummary == s) continue;
            r.WordingExcerpt = w;
            r.PlainSummary = s;
            updates++;
```

Declare `var hintFixes = 0;` next to `var updates = 0;`. Replace the `if (updates > 0)` block with `if (updates > 0 || hintFixes > 0)`, and write two audit lines when applicable: the existing `corpus.wording_refresh` (only if `updates > 0`) and a new `audit.Append("system", null, "corpus.hint_refresh", "requirements", "*", $"applicability hint corrected on {hintFixes} requirements");` (only if `hintFixes > 0`). Zero diffs still write nothing.

**Done when:** test `HintRefresh_RepairsExistingRows` and the existing `CorpusWordingRefreshTests` pass.

## Step 3: the pure rule

**File:** new `src/QsppManager.Domain/Services/ApplicabilityRules.cs`:

```csharp
namespace QsppManager.Domain.Services;

/// <summary>Maps a requirement's applicability hint and the services a pharmacy says it offers to a
/// SUGGESTION. It never decides: the manager records the decision and its reason.</summary>
public static class ApplicabilityRules
{
    private const ServiceScopeFlag AnyCompounding =
        ServiceScopeFlag.SimpleCompounding | ServiceScopeFlag.ComplexCompounding | ServiceScopeFlag.SterileCompounding;

    public static (Applicability value, string reason)? Suggest(ApplicabilityHint hint, ServiceScopeFlag offered)
    {
        if (hint == ApplicabilityHint.Unknown) return null;
        if (hint == ApplicabilityHint.Always)
            return (Applicability.Applicable, "Applies to every pharmacy in the program.");
        if (offered == ServiceScopeFlag.None) return null;    // services not set yet: no conditional suggestion

        (bool has, string what) = hint switch
        {
            ApplicabilityHint.IfCompounding => ((offered & AnyCompounding) != 0, "compounding"),
            ApplicabilityHint.IfSterile => ((offered & ServiceScopeFlag.SterileCompounding) != 0, "sterile compounding"),
            ApplicabilityHint.IfPrescribing => ((offered & ServiceScopeFlag.Prescribing) != 0, "pharmacist prescribing"),
            ApplicabilityHint.IfIndirect => ((offered & ServiceScopeFlag.IndirectSupply) != 0, "indirect supply"),
            _ => (false, ""),
        };
        if (what == "") return null;
        return has
            ? (Applicability.Applicable, $"The pharmacy offers {what} (Settings, services).")
            : (Applicability.NotApplicable, $"The pharmacy has not said it offers {what} (Settings, services).");
    }
}
```

**Done when:** domain tests in step 6 pass.

## Step 4: the workspace method (read-only)

**File:** `QsppWorkspace.cs`, next to `SetApplicability` (line 103). Add:

```csharp
    /// <summary>A suggestion only while the requirement's applicability is still Unknown. Writes nothing.</summary>
    public (Applicability value, string reason)? SuggestApplicability(Guid requirementId)
    {
        var ph = EnsurePharmacy();
        var r = db.Requirements.AsNoTracking().FirstOrDefault(x => x.Id == requirementId);
        if (r is null) return null;
        var st = db.RequirementStates.AsNoTracking().FirstOrDefault(s => s.RequirementId == requirementId && s.PharmacyId == ph.Id);
        if (st is not null && st.Applicability != Applicability.Unknown) return null;
        return ApplicabilityRules.Suggest(r.ApplicabilityHint, (ServiceScopeFlag)ph.ServiceScopeFlags);
    }
```

**Done when:** BUILD GATE passes and test `SuggestApplicability_OnlyWhenUnknown_AndWritesNothing` passes.

## Step 5: show it in the requirement detail

**File:** `src/QsppManager.Desktop/Views/RequirementsView.axaml.cs`, `ApplicabilityBlock` (lines 906-955). It builds `seg` (three pill buttons held in `buttons`), a `reason` TextBox, a `Save` button, and ends with `return new StackPanel { Spacing = 8, Children = { seg, reason, save } };`. Variables `pending`, `current`, `buttons`, `reason` are in scope.

**Change:** before the `return` add

```csharp
        Control? suggestionRow = null;
        if (current == Applicability.Unknown && App.Workspace.SuggestApplicability(d.Requirement.Id) is { } sug)
        {
            var useIt = new Button { Content = "Use this suggestion", Height = 26 };
            useIt.Click += (_, _) =>
            {
                pending = sug.value;
                reason.Text = sug.reason;
                foreach (var (v, btn) in buttons) { if (v == sug.value) btn.Classes.Add("on"); else btn.Classes.Remove("on"); }
            };
            suggestionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children =
                {
                    new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 520,
                        Text = $"Suggested from the services you offer: {(sug.value == Applicability.Applicable ? "applies" : "doesn't apply")}. {sug.reason} Nothing is recorded until you press Save." },
                    useIt,
                },
            };
        }
        var children = new List<Control> { seg };
        if (suggestionRow is not null) children.Add(suggestionRow);
        children.Add(reason); children.Add(save);
        var block = new StackPanel { Spacing = 8 };
        foreach (var c in children) block.Children.Add(c);
        return block;
```

(and delete the old `return new StackPanel {...};`). Add `using QsppManager.Domain;` only if the file does not already import `Applicability`'s namespace (it does: line 1303 uses `Applicability`).

**Done when:** BUILD GATE passes. In a scratch run (`QSPP_DATA_DIR=$(mktemp -d)`), tick "Complex compounding" in Settings, save, open a `if_compounding` requirement: the suggestion appears; "Use this suggestion" fills the reason and highlights "Applies"; nothing is saved until Save. Capture screenshots (light and dark) for the commit message.

## Step 6: tests (twelve new)

**Domain** (`tests/QsppManager.Domain.Tests/ApplicabilityRulesTests.cs`, nine cases):

- `[Theory] Suggest_ByHintAndScope(ApplicabilityHint hint, ServiceScopeFlag offered, Applicability expected)` with seven `InlineData` rows: (Always, None, Applicable); (IfCompounding, ComplexCompounding, Applicable); (IfCompounding, Prescribing, NotApplicable); (IfSterile, ComplexCompounding, NotApplicable); (IfSterile, SterileCompounding, Applicable); (IfPrescribing, Prescribing, Applicable); (IfIndirect, IndirectSupply, Applicable).
- `[Fact] Suggest_ConditionalWithNoServicesSet_ReturnsNull`: (IfCompounding, None) is null.
- `[Fact] Suggest_UnknownHint_ReturnsNull`.

**Integration** (new `tests/QsppManager.Integration.Tests/ApplicabilityScopeTests.cs : TestBase`, three cases):

- `ConditionalHints_ImportWithUnderscoreSpelling`: remove every requirement through `Db` (`Db.Requirements.RemoveRange(Db.Requirements.ToList()); Db.SaveChanges();` so the first-run import path runs again), rewrite `Path.Combine(TempRepo, "seed", "requirements.json")` with a single `Std` row carrying `applicabilityHint = "if_compounding"` (same anonymous-object shape as `TestBase.WriteSeedRequirements`), call `Seeds.ImportAll()`, assert the stored row's `ApplicabilityHint == ApplicabilityHint.IfCompounding`.
- `HintRefresh_RepairsExistingRows`: set a stored row's `ApplicabilityHint = Always` via `Db`, seed says `if_compounding`, `Seeds.ImportAll()`; hint becomes `IfCompounding`; an audit row with action `corpus.hint_refresh` exists; a second `ImportAll()` adds no audit rows.
- `SuggestApplicability_OnlyWhenUnknown_AndWritesNothing`: pharmacy `ServiceScopeFlags = ComplexCompounding`; requirement with hint `IfCompounding` suggests Applicable; record audit count and requirement-state count; call twice; counts unchanged; after `SetApplicability(..., Applicable, "reason", Owner)` the suggestion is null.

**Done when:** `dotnet test --no-build --filter "ApplicabilityRulesTests|ApplicabilityScopeTests"` prints `Passed: 12`.

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 12.
- RENDER GATE: Requirements detail with and without a suggestion, light and dark, scratch data.
- Seed check (read-only): `python3 -I -c "import json,collections;d=json.load(open('seed/requirements.json'));print(collections.Counter(r.get('applicabilityHint') for r in d['requirements']))"` still prints always 402, if_compounding 36, if_indirect 7, if_prescribing 6 (this plan does not edit the seed).

## STOP conditions

- Any `ParseEnum` caller passes a value whose meaning would change when `_` or `-` is removed (run `grep -n "ParseEnum" src/QsppManager.Infrastructure/Seed/SeedImporter.cs` and inspect each argument source).
- The suggestion code path writes anything (audit count changes in the read-only test): fix, do not relax the test.
- Any command would read the real data dir.

## Rollback

`git revert` the commit. Hints corrected in a real database remain correct (they are seed metadata); no schema change.
