# Plan 002: a requirement cannot be Manager-verified without reviewed evidence

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-02, audit findings F-03 and part of F-20. Effort M.

## Summary

`QsppWorkspace.SetReadiness` lets a Manager move a requirement to `ManagerVerified` with zero evidence. After this plan, that transition needs at least one Active link to Manager-verified, unexpired evidence, or an explicit written reason ("no evidence file needed because ...") that is stored and audited. The Today readiness count and the assessor pack count only requirements that satisfy this. Rows already verified in real data are **not** downgraded; they simply stop counting until they qualify.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10, Avalonia UI, SQLite via EF Core 10. Projects: `QsppManager.Domain`, `.Application`, `.Infrastructure`, `.Desktop`; tests in `tests/QsppManager.Domain.Tests` and `tests/QsppManager.Integration.Tests`.
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`. EF tool: `~/.dotnet/tools/dotnet-ef` (installed; confirm with `~/.dotnet/tools/dotnet-ef --version`).
- Baseline: 231 tests pass at `5858779`.
- **Never** open `~/Library/Application Support/QsppManager/` or `QsppManager-backup-*`. Use temp dirs; `QSPP_DATA_DIR=$(mktemp -d)` for any run.
- Never verify a build through a pipe. Run bare, then `echo "exit=$?"`.
- Internal names stay `QsppManager`; display text says `QSPP Folio`. No em dashes in text you write.

## Out of scope

Four-eyes rule (importer cannot verify own evidence; FIX-12). Evidence expiry creating tasks or suspending links (FIX-21). Changing the progress denominator (451 includes Not applicable). Any redesign of the Requirements screen. Downgrading existing rows.

## Step 1: add the stored reason (schema)

**File:** `src/QsppManager.Domain/Entities/CoreEntities.cs`, class `RequirementState` (currently has `ExternalOutcomeRef` then `ExternalOutcomeAt`):

```csharp
    /// <summary>For AssessorAccepted only: the external outcome reference (report, letter ref).</summary>
    public string ExternalOutcomeRef { get; set; } = "";
    public DateTime? ExternalOutcomeAt { get; set; }
```

**Change:** add after `ExternalOutcomeAt`:

```csharp
    /// <summary>Set when a requirement was Manager-verified with no evidence file: why none is needed.</summary>
    public string NoEvidenceReason { get; set; } = "";
```

**File:** `src/QsppManager.Infrastructure/Persistence/QsppDbContext.cs`, in the `RequirementState` block (it already has `e.Property(x => x.ExternalOutcomeRef).HasMaxLength(200);` near line 50). Add `e.Property(x => x.NoEvidenceReason).HasMaxLength(500);`.

**Then run:**

```bash
~/.dotnet/tools/dotnet-ef migrations add ReadinessNoEvidenceReason --project src/QsppManager.Infrastructure; echo "exit=$?"
```

**Done when:** exit 0; a new file `src/QsppManager.Infrastructure/Persistence/Migrations/*_ReadinessNoEvidenceReason.cs` exists; its `Up` contains exactly one `AddColumn<string>` for table `RequirementStates`, column `NoEvidenceReason`, `nullable: false`, `defaultValue: ""`; `git diff --stat` shows changes only in `CoreEntities.cs`, `QsppDbContext.cs`, the two new migration files and `QsppDbContextModelSnapshot.cs`. If the migration touches any other table, STOP.

## Step 2: the evidence test and the gate

**File:** `src/QsppManager.Infrastructure/Services/QsppWorkspace.cs`. Current `SetReadiness` (lines 120-144):

```csharp
    public ServiceResult SetReadiness(Guid requirementId, RequirementReadiness target, UserSession session, string externalOutcomeRef = "")
    {
        if (session.ReadOnly) return ServiceResult.Fail("Read-only reviewer accounts cannot change readiness.");
        var ph = EnsurePharmacy();
        var st = GetState(requirementId, ph.Id);
        if (!StateMachines.CanTransitionReadiness(st.Readiness, target))
            return ServiceResult.Fail($"Requirement cannot move from '{st.Readiness}' to '{target}'.");
        if (!StateMachines.MayApplyReadiness(target, session.Role))
            return ServiceResult.Fail($"Only Owner/Admin or Manager/Reviewer accounts can set readiness to '{target}'.");
        if (target == RequirementReadiness.AssessorAccepted)
        {
            ...
        }
        st.Readiness = target;
        st.UpdatedAtUtc = DateTime.UtcNow;
        db.SaveChanges();
        audit.Append(session.DisplayName, session.UserId, "requirement.readiness", "requirement", requirementId.ToString(),
            $"to={target}" + (target == RequirementReadiness.AssessorAccepted ? $"; externalRef={externalOutcomeRef}" : ""));
        return ServiceResult.Succeed();
    }
```

**Change (a):** new signature `SetReadiness(Guid requirementId, RequirementReadiness target, UserSession session, string externalOutcomeRef = "", string noEvidenceReason = "")`. Existing callers pass positional args up to the 4th, so they still compile.

**Change (b):** add this public method next to it:

```csharp
    /// <summary>True when the requirement has at least one Active link to Manager-verified
    /// evidence whose EffectiveTo (if any) has not passed. Proposed, Suspended and
    /// Superseded links never count.</summary>
    public bool HasQualifyingEvidence(Guid requirementId)
    {
        var today = Clock.Today();
        var ends = (from l in db.EvidenceLinks.AsNoTracking()
                    where l.RequirementId == requirementId && l.State == LinkState.Active
                    join e in db.EvidenceItems.AsNoTracking() on l.EvidenceItemId equals e.Id
                    where e.ReviewState == EvidenceReviewState.ManagerVerified
                    select e.EffectiveTo).ToList();
        return ends.Any(d => d is null || d.Value >= today);
    }
```

**Change (c):** after the `MayApplyReadiness` check and before the `AssessorAccepted` block, insert:

```csharp
        if (target == RequirementReadiness.ManagerVerified)
        {
            var reason = (noEvidenceReason ?? "").Trim();
            if (!HasQualifyingEvidence(requirementId))
            {
                if (reason.Length < 10)
                    return ServiceResult.Fail("Manager verification needs at least one linked, Manager-verified, in-date evidence item. If this requirement genuinely needs no evidence file, give the reason (10 characters or more) and it will be recorded in the audit trail.");
                st.NoEvidenceReason = reason[..Math.Min(reason.Length, 500)];
                audit.Append(session.DisplayName, session.UserId, "requirement.readiness.noevidence", "requirement", requirementId.ToString(), $"reason={st.NoEvidenceReason}");
            }
            else st.NoEvidenceReason = "";
        }
```

**Done when:** BUILD GATE passes (`0 Error(s)`, `exit=0`) and `grep -n "HasQualifyingEvidence" src/QsppManager.Infrastructure/Services/QsppWorkspace.cs` shows the definition and the call.

## Step 3: count only qualifying verified rows

**File:** same. Current `ReadinessSummary` (line 958):

```csharp
    public (int verified, int total) ReadinessSummary()
    {
        var ph = EnsurePharmacy();
        var total = db.Requirements.Count();
        var verified = db.RequirementStates.AsNoTracking().Count(s => s.PharmacyId == ph.Id && s.Readiness == RequirementReadiness.ManagerVerified);
        return (verified, total);
    }
```

**Change:** replace `verified` with

```csharp
        var verified = db.RequirementStates.AsNoTracking()
            .Where(s => s.PharmacyId == ph.Id && s.Readiness == RequirementReadiness.ManagerVerified)
            .Select(s => new { s.RequirementId, s.NoEvidenceReason }).ToList()
            .Count(s => s.NoEvidenceReason != "" || HasQualifyingEvidence(s.RequirementId));
```

Apply the same predicate to `Journey()` (line 1051, `var verified = states.Count(s => s.Readiness == RequirementReadiness.ManagerVerified);`): replace with `states.Count(s => s.Readiness == RequirementReadiness.ManagerVerified && (s.NoEvidenceReason != "" || HasQualifyingEvidence(s.RequirementId)))`.

**File:** `src/QsppManager.Infrastructure/Backup/AssessorPackService.cs`, current filter (lines 34-36):

```csharp
            var inScope = rows
                .Where(r => r.Readiness is RequirementReadiness.ManagerVerified or RequirementReadiness.AssessorAccepted
                            && r.Applicability != Domain.Applicability.NotApplicable)
```

**Change:** append `.Where(r => workspace.HasQualifyingEvidence(r.Id) || workspace.RequirementDetail(r.Id, session)?.State.NoEvidenceReason is { Length: > 0 })` after the existing `.Where(...)` and before `.OrderBy`. The empty-scope refusal text stays.

**Done when:** tests in step 5 pass.

## Step 4: UI asks for the reason

**File:** `src/QsppManager.Desktop/Views/RequirementsView.axaml.cs`, `TransitionTo` (lines 731-743). Current:

```csharp
        string? ext = "";
        if (target == RequirementReadiness.AssessorAccepted)
        {
            ext = await Dialogs.Prompt(this, "Assessor outcome reference", "Report / letter reference (required; only record an outcome that actually arrived)");
            if (string.IsNullOrWhiteSpace(ext)) return;
        }
        var r = App.Workspace.SetReadiness(d.Requirement.Id, target, session, ext);
```

**Change:** add before the `SetReadiness` call:

```csharp
        string? noEv = "";
        if (target == RequirementReadiness.ManagerVerified && !App.Workspace.HasQualifyingEvidence(d.Requirement.Id))
        {
            noEv = await Dialogs.Prompt(this, "No verified evidence linked",
                "Link and verify evidence first. If this requirement needs no evidence file, say why (recorded in the audit trail).");
            if (string.IsNullOrWhiteSpace(noEv)) return;
        }
```

and pass `noEv` as the fifth argument: `SetReadiness(d.Requirement.Id, target, session, ext, noEv)`.

**Done when:** BUILD GATE passes. In a scratch run (`QSPP_DATA_DIR=$(mktemp -d)`), taking a requirement to Manager-verified with no evidence shows the prompt, and cancelling leaves it at ReadyForReview. Capture a screenshot for the commit message.

## Step 5: tests

**Fix three existing tests** that verify without evidence. Add this helper to `TestBase` in `tests/QsppManager.Integration.Tests/WorkspaceWorkflowTests.cs` (after `Dispose`):

```csharp
    protected void AttachVerifiedEvidence(Guid requirementId, string title = "Supporting SOP")
    {
        var path = Path.Combine(TempRepo, $"ev-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fake " + Guid.NewGuid()));
        var (res, item) = Workspace.ImportEvidence(path, title, EvidenceKind.PolicyOrSop, null, null, false, "", Owner);
        Assert.True(res.Ok, res.Error);
        Assert.True(Workspace.LinkEvidence(item!.Id, requirementId, "", null, null, "supports the requirement", Owner).Ok);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ReadyForReview, Owner, "").Ok);
        Assert.True(Workspace.SetEvidenceReview(item.Id, EvidenceReviewState.ManagerVerified, Owner, "ok").Ok);
    }
```

Then call `AttachVerifiedEvidence(<requirement id>)` immediately before the `ManagerVerified` line in:
1. `WorkspaceWorkflowTests.cs` `Readiness_RejectsJumpingToVerified` (before line 162, with `req.Id`).
2. `WorkflowM2Tests.cs` `Journey_CountsReflectRealChanges` (before line 70, with `ruleRow.Id`).
3. `AssessorPackTests.cs` (lines 46-47): the loop sets all three transitions in one go. Change the loop to set `InProgress` and `ReadyForReview`, then keep the existing evidence import/link/verify block for `evA2` further down, and set `ManagerVerified` **after** `Workspace.SetEvidenceReview(evA2.Id, ... ManagerVerified ...)` (the line after the block shown at `AssessorPackTests.cs:58`). `Assert.True(Workspace.SetReadiness(row.Id, RequirementReadiness.ManagerVerified, Owner).Ok);`

**Add six new tests** in a new file `tests/QsppManager.Integration.Tests/ReadinessEvidenceGateTests.cs`, class `ReadinessEvidenceGateTests : TestBase`:

1. `Verify_WithoutEvidence_IsRefused`: move `Rule 8` to ReadyForReview, call with no reason, expect `Ok == false`, error contains `Manager-verified`, readiness unchanged.
2. `Verify_WithReason_IsAllowed_AndAudited`: reason `"Definition only, nothing to evidence"`; expect Ok, `NoEvidenceReason` stored, and one audit row with action `requirement.readiness.noevidence`.
3. `Verify_WithOnlyProposedLink_IsRefused`: import evidence, link it, leave evidence in Draft; expect refusal.
4. `Verify_WithExpiredEvidence_IsRefused`: verified evidence whose `EffectiveTo` is `Clock.Today().AddDays(-1)` (set via `Db` after verifying, then `Db.SaveChanges()`); expect refusal. Call `Clock.Pin(...)` only if you need a fixed date, and `Clock.Unpin()` in a `finally`.
5. `ReadinessSummary_DoesNotCountLegacyVerifiedWithoutEvidence`: set a `RequirementState.Readiness = ManagerVerified` directly through `Db` with empty `NoEvidenceReason`; expect `ReadinessSummary().verified == 0`; then attach evidence, expect 1.
6. `AssessorPack_ExcludesVerifiedWithoutEvidence`: as test 5 setup; `Export` returns `ok == false` with `Nothing is in scope yet`.

**Done when:** `dotnet test --no-build --filter "ReadinessEvidenceGateTests|Readiness_RejectsJumpingToVerified|Journey_CountsReflectRealChanges"` shows `Failed: 0`.

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 6 (237 when this is the first plan applied).
- N0 is the total printed by a TEST GATE run on a clean checkout before you change anything. Write it down first; other plans in `plans/` may have landed and moved it above 231.
- RENDER GATE: Requirements page only, light and dark, scratch data.
- `git diff --stat` touches only the files named in this plan plus the two migration files and the snapshot.

## STOP conditions

- The generated migration touches any table or column other than the one named.
- Any step would write to the real data dir.
- More than the three named existing tests fail after the change (it means another path verifies without evidence; report it, do not patch around it).
- Hugh has not agreed that already-verified real rows stop counting until they qualify. This is a visible change to the Today number; ask once before the commit and record the answer in the commit message.

## Rollback

`git revert` the commit. The extra column is harmless if left (`NoEvidenceReason` defaults to empty). To drop it: `~/.dotnet/tools/dotnet-ef migrations remove --project src/QsppManager.Infrastructure` only if the migration has not been applied to any real database; otherwise leave the column.
