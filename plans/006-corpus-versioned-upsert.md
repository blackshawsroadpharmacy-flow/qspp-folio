# Plan 006: new corpus content reaches installed pharmacies (insert-missing import, no schema change)

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-07, audit findings F-09 and F-25. Effort L.

## Summary

The seed importer is one-shot for requirements (`SeedImporter.cs:225`), interpretation issues (`:339`) and source documents (`:419`): once any row exists, new seed content is ignored. Only a wording refresh and the REM-item upsert reach existing installs. A subscription product must be able to ship Program Rules V1.1 or a new policy. This plan turns the three importers into **insert-missing** passes:

- A requirement whose deterministic id is absent is inserted (with evidence examples).
- A requirement whose (RefKind, Ref) already exists under a different id (the source document or version changed) is **re-sourced in place**: the row keeps its id, so states, owners, links and tasks stay attached; its source file, version, title, summary and wording are updated and its page anchor is reset to "unverified".
- An interpretation issue whose `Key` is absent is inserted; existing issues, including resolved ones, are never touched.
- A source document is imported only if its sha256 is not already stored.
- A failed import no longer leaves a half-corpus forever, because the next boot inserts what is missing. A failure is shown in About.

No schema change, no migration.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10, EF Core 10 on SQLite. Importer: `src/QsppManager.Infrastructure/Seed/SeedImporter.cs`. Seed files: `seed/requirements.json` (451 rows), `seed/issues.json` (3), `seed/documents.json` (22), `seed/remote-assessment.json` (19, already upserted on boot), `seed/page-anchors.json`.
- Identity of a requirement: `DeterministicId(kind, "<sourceDoc>:<ref>")` (`SeedImporter.cs:16-21`). A unique index on (RefKind, Ref) exists (`QsppDbContext.cs:38`), so inserting a row with a new id but an existing (kind, ref) would throw. That is why the re-source rule exists.
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- Baseline 231 tests at `5858779`; record N0 first.
- **Never** open `~/Library/Application Support/QsppManager/` or `QsppManager-backup-*`. Tests use the `TestBase` temp database and a temp repo with a three-row seed.
- Never verify a build through a pipe. Run bare, then `echo "exit=$?"`. No em dashes in text you write.

## Out of scope

Deleting or retiring requirements that disappear from the seed (they stay; this plan only adds and rewords). Creating review tasks for changed wording (worth doing next; record it as a follow-up in the commit message). Changing the seed JSON content. The REM upsert (already correct). Bundled PDF decisions (FIX-01).

## Step 1: requirements become insert-missing with re-source

**File:** `src/QsppManager.Infrastructure/Seed/SeedImporter.cs`, `ImportRequirements` (lines 223-290). Current head:

```csharp
    private (int reqs, int examples) ImportRequirements(CancellationToken ct)
    {
        if (db.Requirements.Any()) return WordingRefresh(ct);
        var path = Path.Combine(repoRoot, "seed", "requirements.json");
        if (!File.Exists(path)) return (0, 0);
        ...
        var examples = 0;
        foreach (var el in reqs)
        {
            var kind = ParseEnum<RefKind>(el.GetProperty("refKind").GetString() ?? "Std");
            var refKey = el.GetProperty("ref").GetString() ?? "";
            var id = el.TryGetProperty("id", out var idEl) && ... ? g : DeterministicId($"{kind}", $"{el.GetProperty("sourceDoc").GetString() ?? ""}:{refKey}");

            if (db.Requirements.IgnoreQueryFilters().Any(r => r.Id == id)) continue;

            var r = new Requirement
            {
```

**Change (a):** replace the first line of the method body with

```csharp
        var hadRows = db.Requirements.Any();
        if (hadRows) WordingRefresh(ct);   // keeps DD-35 behaviour: reword existing rows first
        var knownIds = db.Requirements.IgnoreQueryFilters().AsNoTracking().Select(r => r.Id).ToHashSet();
        var byKindRef = db.Requirements.IgnoreQueryFilters()
            .Select(r => new { r.RefKind, r.Ref, r.Id }).ToList()
            .ToDictionary(x => (x.RefKind, x.Ref), x => x.Id);
        var added = 0; var resourced = 0;
```

**Change (b):** replace `if (db.Requirements.IgnoreQueryFilters().Any(r => r.Id == id)) continue;` with

```csharp
            if (knownIds.Contains(id)) continue;
            if (byKindRef.TryGetValue((kind, refKey), out var existingId))
            {
                // Same requirement, new source document or version: update in place, keep the id.
                var cur = db.Requirements.IgnoreQueryFilters().First(x => x.Id == existingId);
                var newDoc = S(el, "sourceDoc"); var newVer = S(el, "sourceVersion");
                if (cur.SourceDocFile != newDoc || cur.SourceVersion != newVer)
                {
                    audit.Append("system", null, "corpus.resourced", "requirement", cur.Id.ToString(),
                        $"{kind} {refKey}: '{cur.SourceVersion}' -> '{newVer}'");
                    cur.SourceDocFile = newDoc; cur.SourceVersion = newVer;
                    cur.Title = S(el, "title"); cur.PlainSummary = S(el, "summary"); cur.WordingExcerpt = S(el, "wording");
                    cur.AnchorPage = null; cur.AnchorVerified = false; cur.AnchorApproximate = true;
                    resourced++;
                }
                continue;
            }
```

**Change (c):** after `db.Requirements.Add(r);` add `knownIds.Add(id); byKindRef[(kind, refKey)] = id; added++;`.

**Change (d):** replace the tail (`db.SaveChanges(); if (db.Requirements.Any()) audit.Append(... "corpus.imported" ...); return (...)`) with

```csharp
        db.SaveChanges();
        if (added > 0)
            audit.Append("system", null, hadRows ? "corpus.added" : "corpus.imported", "requirements", "*",
                $"{(hadRows ? "Added" : "Imported")} {added} requirements" + (resourced > 0 ? $"; {resourced} re-sourced" : ""));
        else if (resourced > 0)
            audit.Append("system", null, "corpus.resourced", "requirements", "*", $"{resourced} requirements re-sourced");
        return (db.Requirements.Count(), db.RequirementEvidenceExamples.Count());
```

The existing first-run audit text (`"Imported {n} requirements, {m} evidence examples from seed ..."`) may be asserted by an existing test; run `grep -rn "corpus.imported" tests src` and keep that action name and a compatible message for the `!hadRows` case, adjusting only what is needed.

**Done when:** BUILD GATE passes and the existing `SeedImporterTests` (in `WorkspaceWorkflowTests.cs`, tests `Import_IsIdempotent_SameRequirementsTwice_StillThree` and `Requirement_Ids_AreDeterministic_AcrossDatabaseInstances`) pass unchanged.

## Step 2: issues become insert-missing

**File:** same, `ImportIssues` (lines 337-364). Current head: `if (db.InterpretationIssues.Any()) return db.InterpretationIssues.Count();`.

**Change:** delete that line. Before the `foreach` over the JSON array add `var haveKeys = db.InterpretationIssues.AsNoTracking().Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);`. Inside the loop compute `var key = S(el, "key") != "" ? S(el, "key") : S(el, "id");` and `if (haveKeys.Contains(key)) continue;`, use `Key = key`, and add the key to the set after `db.InterpretationIssues.Add(...)`. Add `if (added > 0) audit.Append("system", null, "issues.added", "interpretation-issue", "*", $"{added} new interpretation issue(s) from seed");` after `SaveChanges`. The hard-coded fallback set below the JSON branch must now run only when `issues.json` is missing **and** `db.InterpretationIssues.Any()` is false: wrap it in `if (db.InterpretationIssues.Any()) return db.InterpretationIssues.Count();` placed at the top of the fallback section.

**Done when:** test `Reimport_AddsNewIssue_KeepsResolvedStatus` (step 5) passes.

## Step 3: documents import only what is new, without re-reading the pack

**File:** same, `ImportDocuments` (lines 417-451). Current head: `if (db.SourceDocuments.Any()) return db.SourceDocuments.Count();` and the local function:

```csharp
        int ImportOne(string absPath, string display, string role, string version, string expectedSha, string provenance)
            => File.Exists(absPath) ? ImportFromBytes(absPath, File.ReadAllBytes(absPath), display, role, version, expectedSha, provenance) : 0;
```

**Change:** delete the early `return`. Add `var haveSha = db.SourceDocuments.AsNoTracking().Select(d => d.Sha256Hex).ToHashSet(StringComparer.OrdinalIgnoreCase);` at the top. Change `ImportOne` to

```csharp
        int ImportOne(string absPath, string display, string role, string version, string expectedSha, string provenance)
        {
            if (!string.IsNullOrWhiteSpace(expectedSha) && haveSha.Contains(expectedSha)) return 0;   // already stored: do not read 35 MB every boot
            return File.Exists(absPath) ? ImportFromBytes(absPath, File.ReadAllBytes(absPath), display, role, version, expectedSha, provenance) : 0;
        }
```

In the manifest branch (`else if (File.Exists(manifestPath))`) add the same pre-check using the manifest `sha` before `File.ReadAllBytes(packFile)`. `ImportFromBytes` already skips by sha (line 485) and stays as the final guard. Return value: keep returning `count` (new imports this run); callers use it only for the tuple.

**Done when:** BUILD GATE passes. Manual proof (scratch only): two consecutive boots with `QSPP_DATA_DIR=$(mktemp -d)` both finish; the second boot logs no `document.imported` audit lines (count `AuditEvents` where `Action = 'document.imported'` before and after with `sqlite3` on the scratch DB).

## Step 4: a failed import is visible

**File:** `src/QsppManager.Desktop/App.axaml.cs`. Current (lines 126-137):

```csharp
        try
        {
            Seeds.ImportAll();
            ...
        }
        catch (Exception ex)
        {
            Log.Error(ex, "seed import partially failed");
        }
```

**Change:** add a static `public static string? SeedImportError { get; private set; }` to `App`, and in the catch assign `SeedImportError = "The program reference content did not finish loading (" + ex.GetType().Name + "). It will retry on the next start. See the log in the data folder.";`. In `src/QsppManager.Desktop/MainWindow.axaml.cs`, About dialog (around line 517, after the `Sources pack` info line) add `if (App.SeedImportError is { } seedErr) stack.Children.Add(Ui.InfoLine("Corpus import", seedErr));`.

**Done when:** BUILD GATE passes.

## Step 5: tests (five new)

**File:** new `tests/QsppManager.Integration.Tests/CorpusUpsertTests.cs : TestBase`. `TestBase` (in `WorkspaceWorkflowTests.cs`) writes a three-row `seed/requirements.json` into `TempRepo` and imports it. In each test rewrite `Path.Combine(TempRepo, "seed", "requirements.json")` with `File.WriteAllText` (use the same anonymous-object shape as `WriteSeedRequirements`: `refKind`, `sourceDoc`, `sourceVersion`, `@ref`, `title`, `domain`, `summary`, `evidenceExamples`, `interpretationFlags`), then call `Seeds.ImportAll()`.

1. `Reimport_AddsNewRequirement_KeepsExistingStateAndLinks`: set readiness InProgress on `Rule 8`; write a seed with the three rows plus `Rule 11`; import; count is 4, `Rule 8` state is still InProgress.
2. `Reimport_NewSourceVersion_UpdatesInPlace_KeepsIdAndState`: record `Rule 8`'s `Id` and set owner; write a seed where `Rule 8` has `sourceDoc = "QSPP-Program-Rules-V1.1-Dec2026.txt"`, `sourceVersion = "V1.1, Dec 2026"` and new `summary`; import; same `Id` (query by Ref), `SourceVersion == "V1.1, Dec 2026"`, `AnchorVerified == false`, owner unchanged, one audit row `corpus.resourced`, requirement count unchanged at 3.
3. `Reimport_IsIdempotent_ZeroWritesWhenUnchanged`: count `AuditEvents` before; `Seeds.ImportAll()` twice more; count equal.
4. `Reimport_AddsNewIssue_KeepsResolvedStatus`: resolve `ISSUE-CANCELWINDOW` through `Workspace.ResolveIssue`; write `seed/issues.json` with the original key plus `ISSUE-NEW`; import; two issues exist, the resolved one is still `resolved`. (The test seed has no `issues.json`; the first `ImportAll` used the fallback set, so create the file with three entries keyed `ISSUE-CANCELWINDOW`, `ISSUE-MIDCYCLE`, `ISSUE-POTENCY` plus the new key.)
5. `Reimport_RetriesAfterPartialFailure`: delete all requirements except `Rule 8` directly through `Db` (simulating a first run that died), call `Seeds.ImportAll()`, expect all three back.

**Done when:** `dotnet test --no-build --filter CorpusUpsertTests` prints `Passed: 5`.

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 5.
- Boot-time check on a scratch dir: time two consecutive cold boots; the second must not be slower than the first by more than 1 second (record both in the commit message).
- `git diff --stat`: `SeedImporter.cs`, `App.axaml.cs`, `MainWindow.axaml.cs`, the new test file only. No `seed/*.json` change, no migration.

## STOP conditions

- A step would require a schema change or migration.
- An existing test asserting `corpus.imported` text fails and the fix is not a one-line message adjustment.
- The re-source rule would touch a row whose `RefKind` is `Surveillance` (the REM upsert owns that namespace: skip those kinds in the re-source branch with `if (kind == RefKind.Surveillance) continue;` before the lookup, and report if you see any).
- Any command would read the real data dir.

## Rollback

`git revert` the commit. Rows added or re-sourced by earlier runs stay; they are valid corpus rows and carry audit lines (`corpus.added`, `corpus.resourced`).
