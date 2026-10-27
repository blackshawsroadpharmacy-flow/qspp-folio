# Plan 005: retiring evidence marks its links Superseded, raises review work, and needs a verifier role

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-06, audit finding F-08. Effort S.

## Summary

`SetEvidenceReview(id, Retired, ...)` is a legal transition from `ManagerVerified` (`DomainServices.cs:170`). It changes only the evidence row: its links stay `Active`, no task is raised, and `MayVerifyEvidence` lets a Contributor do it (`DomainServices.cs:204-207`). No screen offers Retire today (grep of `src/QsppManager.Desktop/Views` finds none), so this is reachable through the service only. After this plan, a retire requires an Owner or Manager, flips every non-superseded link to `Superseded`, and creates one re-link task per affected requirement, exactly as `SupersedeEvidence` does. `SupersedeEvidence` also refuses a replacement that is itself retired.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10. Workspace service: `src/QsppManager.Infrastructure/Services/QsppWorkspace.cs`. State machines: `src/QsppManager.Domain/Services/DomainServices.cs`. Tests: `tests/QsppManager.Integration.Tests/WorkspaceWorkflowTests.cs`.
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- Baseline 231 tests at `5858779`. Record N0 from a TEST GATE run first; other plans may have landed.
- **Never** open `~/Library/Application Support/QsppManager/` or `QsppManager-backup-*`. Tests use temp databases.
- Never verify a build through a pipe. Run bare, then `echo "exit=$?"`. No em dashes in text you write.

## Out of scope

Adding a Retire button to any screen. Evidence expiry handling (FIX-21). The four-eyes rule (FIX-12). Changing the evidence state table.

## Step 1: Retire needs a verifier role

**File:** `src/QsppManager.Domain/Services/DomainServices.cs`, lines 204-207. Current code:

```csharp
    public static bool MayVerifyEvidence(EvidenceReviewState target, UserRole actor)
        => target == EvidenceReviewState.ManagerVerified
            ? actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer
            : actor is not UserRole.ReviewerReadOnly;
```

**Change:**

```csharp
    public static bool MayVerifyEvidence(EvidenceReviewState target, UserRole actor)
        => target is EvidenceReviewState.ManagerVerified or EvidenceReviewState.Retired
            ? actor is UserRole.OwnerAdmin or UserRole.ManagerReviewer
            : actor is not UserRole.ReviewerReadOnly;
```

**Done when:** BUILD GATE passes and the test `Retire_ByContributor_IsRefused` (step 4) passes. The existing error text in `SetEvidenceReview` already reads "Only Owner/Admin or Manager/Reviewer accounts can mark evidence 'Retired'."

## Step 2: one shared helper for "flag the links"

**File:** `src/QsppManager.Infrastructure/Services/QsppWorkspace.cs`. In `SupersedeEvidence` (lines 309-344) the link-flagging block is:

```csharp
        var affected = db.EvidenceLinks.Where(l => l.EvidenceItemId == oldId && l.State != LinkState.Superseded).ToList();
        foreach (var l in affected)
        {
            l.State = LinkState.Superseded;
            var req = db.Requirements.FirstOrDefault(r => r.Id == l.RequirementId);
            var st = db.RequirementStates.FirstOrDefault(s => s.RequirementId == l.RequirementId);
            var task = new TaskItem
            {
                Title = $"Evidence superseded — re-link current version for {req?.Ref ?? "requirement"}",
                Details = $"Superseded evidence '{old.Title}' was replaced ({reason}). Choose the current version, link it with a rationale, and mark the link reviewed so readiness reflects reality.",
                RequirementId = l.RequirementId,
                EvidenceItemId = oldId,
                AssignedToUserId = st?.OwnerUnassigned == false ? st.OwnerUserId : EnsurePharmacyOwner(),
                Priority = TaskPriority.High,
                CreatedByUserId = session.UserId,
            };
            db.Tasks.Add(task);
        }
```

**Change:** move that block verbatim into a new private method placed right after `SupersedeEvidence`:

```csharp
    /// <summary>Marks every live link of an evidence item Superseded and raises one re-link task per
    /// requirement. Caller saves. Returns the number of links changed.</summary>
    private int FlagLinksAfterRetire(EvidenceItem old, string what, string reason, UserSession session)
    {
        var affected = db.EvidenceLinks.Where(l => l.EvidenceItemId == old.Id && l.State != LinkState.Superseded).ToList();
        foreach (var l in affected)
        {
            l.State = LinkState.Superseded;
            var req = db.Requirements.FirstOrDefault(r => r.Id == l.RequirementId);
            var st = db.RequirementStates.FirstOrDefault(s => s.RequirementId == l.RequirementId);
            db.Tasks.Add(new TaskItem
            {
                Title = $"Evidence {what}: re-link current version for {req?.Ref ?? "requirement"}",
                Details = $"Evidence '{old.Title}' was {what} ({reason}). Choose the current version, link it with a rationale, and mark the link reviewed so readiness reflects reality.",
                RequirementId = l.RequirementId,
                EvidenceItemId = old.Id,
                AssignedToUserId = st?.OwnerUnassigned == false ? st.OwnerUserId : EnsurePharmacyOwner(),
                Priority = TaskPriority.High,
                CreatedByUserId = session.UserId,
            });
        }
        return affected.Count;
    }
```

(The em dash in the old title is replaced with a colon; the string is user-visible and the project avoids em dashes in new text. The existing test `Supersede_RetiresOld_FlagsLinks_CreatesRelinkTask` asserts only task state and count, so the title change is safe; confirm by running it.)

In `SupersedeEvidence` replace the block with `var affectedCount = FlagLinksAfterRetire(old, "superseded", reason, session);` and use `affectedCount` in the audit line (`affectedLinks={affectedCount}`). Also add, after the `newId == oldId` check:

```csharp
        if (nu.ReviewState == EvidenceReviewState.Retired) return ServiceResult.Fail("The replacement is itself retired. Choose a current version.");
```

**Done when:** BUILD GATE passes and the existing `Supersede_RetiresOld_FlagsLinks_CreatesRelinkTask` still passes unchanged.

## Step 3: SetEvidenceReview(Retired) flags the links

**File:** same, `SetEvidenceReview` (lines 278-304). Its order today is: read-only check, load item, `CanTransitionEvidence` check, `MayVerifyEvidence` check, the patient-data privacy gate (`if (target == EvidenceReviewState.ReadyForReview && item.ContainsPatientData && ...) return ...Fail("Privacy gate: ...")`), then mutation starting at `item.ReviewState = target;`.

**Change (a), validation before any mutation:** immediately after the privacy-gate `if`, insert

```csharp
        if (target == EvidenceReviewState.Retired && string.IsNullOrWhiteSpace(note))
            return ServiceResult.Fail("Say why this evidence is being retired; the reason is kept with its history.");
```

**Change (b), mutation:** immediately after the line `item.ReviewNote = note ?? item.ReviewNote;` and before `if (target == EvidenceReviewState.ManagerVerified)`, insert

```csharp
        var retiredLinks = 0;
        if (target == EvidenceReviewState.Retired)
        {
            item.SupersededReason = note.Trim();
            retiredLinks = FlagLinksAfterRetire(item, "retired", note.Trim(), session);
        }
```

**Change (c), audit line:** change `$"to={target}; note={note}"` to `$"to={target}; note={note}" + (retiredLinks > 0 ? $"; links={retiredLinks}" : "")`. The existing `db.SaveChanges()` after the `ManagerVerified` block saves the link and task changes together with the evidence row.

**Done when:** tests `Retire_FlagsLinks_AndRaisesTasks` and `Retire_RequiresReason` (step 4) pass.

## Step 4: tests (five new)

**File:** new `tests/QsppManager.Integration.Tests/EvidenceRetireTests.cs`, `public class EvidenceRetireTests : TestBase`. Copy the small `ImportEvidence(string title, string content, ...)` helper from `WorkspaceWorkflowTests.cs` (lines 135-142 in the class that follows `TestBase`; it writes `%PDF-1.4 fake` bytes into `TempRepo` and calls `Workspace.ImportEvidence`). Setup helper `VerifiedLinked()` creates an item, links it to the requirement with Ref `2.2.3.8`, moves it Draft -> ReadyForReview -> ManagerVerified.

1. `Retire_FlagsLinks_AndRaisesTasks`: Owner retires with note `"obsolete"`; expect `Ok`, link state `Superseded`, one open task for the requirement, evidence `ReviewState == Retired`.
2. `Retire_RequiresReason`: empty note; expect `Ok == false`; evidence still `ManagerVerified`; link still `Active`.
3. `Retire_ByContributor_IsRefused`: add a Contributor with `Auth.AddUser(Owner.DisplayName, Owner.UserId, "contrib", "Contrib", UserRole.Contributor, "passphras8")`, build a `UserSession`, call retire; expect refusal and unchanged state.
4. `Supersede_RefusesRetiredReplacement`: retire item B, then supersede A with B; expect `Ok == false`, message contains `retired`.
5. `Retire_IsIdempotentOnSecondCall`: retiring an already Retired item fails the transition table (`Retired` has no exits) and creates no extra tasks (count tasks before and after).

**Done when:** `dotnet test --no-build --filter EvidenceRetireTests` prints `Passed: 5`, and the pre-existing supersede test still passes.

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 5.
- `git diff --stat` touches `DomainServices.cs`, `QsppWorkspace.cs` and the new test file only.

## STOP conditions

- Any other caller of `SetEvidenceReview(..., Retired, ...)` appears (`grep -rn "Retired" src --include=*.cs` outside the files named here): list it and stop.
- The existing supersede test fails after the helper extraction (the helper must be a pure move).

## Rollback

`git revert` the commit. No schema change, no data change.
