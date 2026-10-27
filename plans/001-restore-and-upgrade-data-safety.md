# Plan 001: restore and schema upgrade cannot destroy live data

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-03, audit finding F-02. Effort M.

## Summary

Today a staged restore overwrites the live `qspp.db` with no safety copy and no integrity check, and a schema migration runs with no backup. After this plan: a restore first copies the live DB aside, verifies the incoming DB with `PRAGMA integrity_check` and a migration-history check, swaps it in with an atomic move, and rolls back on any failure. A migration that has pending steps first writes a `VACUUM INTO` copy of the DB into `pre-migration/`, keeping the newest 5.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10, Avalonia UI, SQLite via EF Core. Four projects: `QsppManager.Domain`, `.Application`, `.Infrastructure`, `.Desktop`; tests in `tests/QsppManager.Domain.Tests` and `tests/QsppManager.Integration.Tests`.
- Shell setup for every command: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- Baseline: 231 tests pass at `5858779`.
- **Never** read, copy or open `~/Library/Application Support/QsppManager/` or any `QsppManager-backup-*`. That is the live pharmacy record. Every run and test uses a temp directory (`mktemp -d` or `Path.GetTempPath()`), and you may set `QSPP_DATA_DIR` to one.
- Do not verify a build through a pipe. Run the command bare, then `echo "exit=$?"`.
- Internal names stay `QsppManager`; user-visible text says `QSPP Folio`.
- No em dashes in any text you write.

## Out of scope

Zip Slip hardening of `BackupService.Restore` (plan 003). Backup encryption (FIX-20). WAL and quick_check at boot (FIX-15). Any change to the backup zip format. Any UI redesign.

## Step 1: extract the promote logic into a testable class

**File:** `src/QsppManager.Desktop/App.axaml.cs`, method `RestoreStagedBackupIfAny` (lines 520-540). Current code:

```csharp
    private static void RestoreStagedBackupIfAny()
    {
        var staging = AppData.RestoreStaging();
        var stagedDb = Path.Combine(staging, "qspp.db");
        if (!File.Exists(stagedDb)) return;
        var liveDb = AppData.DatabasePath(App.DemoMode);
        File.Copy(stagedDb, liveDb, overwrite: true);
        var stagedAtt = Path.Combine(staging, "attachments");
        if (Directory.Exists(stagedAtt))
        {
            var attDir = AppData.AttachmentsDir;
            foreach (var f in Directory.EnumerateFiles(stagedAtt, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(attDir, Path.GetRelativePath(stagedAtt, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
            }
        }
        try { Directory.Delete(staging, recursive: true); } catch { }
        Console.WriteLine($"[qspp] restored backup promoted into {AppData.Root}");
    }
```

**Change:** create `src/QsppManager.Infrastructure/Backup/RestorePromoter.cs` containing

```csharp
namespace QsppManager.Infrastructure.Backup;

public static class RestorePromoter
{
    public sealed record Outcome(bool Promoted, bool Attempted, string Message);

    /// <summary>staging/qspp.db + staging/attachments -> live db path + attachments dir.
    /// Never leaves the live DB half-written; never deletes staging unless promotion succeeded.</summary>
    public static Outcome Promote(string stagingDir, string liveDbPath, string attachmentsDir, string dataRoot,
        IReadOnlyCollection<string> knownMigrationIds) { /* steps 2-4 */ }
}
```

and replace the body of `RestoreStagedBackupIfAny` with a call to `RestorePromoter.Promote(staging, AppData.DatabasePath(App.DemoMode), AppData.AttachmentsDir, AppData.Root, <ids>)`. The `<ids>` come from a `QsppDbContext` created for design purposes: `new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>().UseSqlite("Data Source=:memory:").Options).Database.GetMigrations().ToList()`. If `Promote` returns `Attempted: true, Promoted: false`, write `Message` to `<data>/restore-failed.txt` and log it with Serilog (`Log.Warning`) after the logger exists; the app continues to boot on the untouched live DB.

**Done when:** `dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false` prints `0 Error(s)` and `exit=0`; `grep -n "File.Copy(stagedDb" src/QsppManager.Desktop/App.axaml.cs` returns nothing.

## Step 2: safety copy of the live DB

**File:** `RestorePromoter.cs`. Inside `Promote`, before touching the live DB:

1. If `liveDbPath` exists, create `Path.Combine(dataRoot, "pre-restore", DateTime.Now.ToString("yyyyMMdd-HHmmss"))` and `File.Copy(liveDbPath, <dir>/qspp.db)`. Also copy `liveDbPath + "-wal"` and `liveDbPath + "-shm"` if they exist.
2. Verify the copy with SHA-256 (use `System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(..))`) equal to the source. If not equal, return `Outcome(false, true, "Safety copy did not verify; restore not applied. Live data untouched.")`.
3. Keep only the newest 5 directories under `pre-restore/` (sort by name descending, delete the rest).

Attachments need no safety copy: the store is content-addressed (`AttachmentStore.cs:19-26`, path is the file's own SHA-256), so overwriting a path with the same name writes identical bytes.

**Done when:** test `Promote_CopiesLiveDbAside_BeforeSwap` (step 5) passes.

## Step 3: verify the incoming DB before swapping

**File:** `RestorePromoter.cs`. Using `Microsoft.Data.Sqlite` (already referenced by Infrastructure through EF Sqlite):

1. Copy `stagingDir/qspp.db` to `liveDbPath + ".incoming"`.
2. Open it read-only (`Data Source=<incoming>;Mode=ReadOnly`), run `PRAGMA integrity_check;`. Require the single row `ok`.
3. Read `SELECT MigrationId FROM __EFMigrationsHistory`. If any id is not in `knownMigrationIds`, the backup is from a newer app: delete `.incoming`, return `Outcome(false, true, "This backup was made by a newer version of QSPP Folio. Update the app, then restore again. Live data untouched.")`.
4. If integrity or history reading fails (exception), same: delete `.incoming`, return failure with the exception message.

**Done when:** tests `Promote_RefusesCorruptDb` and `Promote_RefusesBackupFromNewerSchema` pass.

## Step 4: atomic swap and cleanup

**File:** `RestorePromoter.cs`. After step 3 succeeds:

1. Delete `liveDbPath + "-wal"` and `-shm` if present (a stale journal from the old DB must not replay onto the new file).
2. `File.Move(liveDbPath + ".incoming", liveDbPath, overwrite: true)`.
3. Copy staged attachments exactly as the current loop does, but only when the destination does not exist or the sizes differ.
4. Delete `stagingDir` only now. Return `Outcome(true, true, "Restore applied. Previous data kept in pre-restore/<stamp>.")`.

If any exception is thrown between 1 and 3, move the safety copy back (`File.Copy(safety, liveDbPath, overwrite: true)`), keep staging, and return a failure outcome.

**Done when:** test `Promote_SwapsDb_AndRemovesStagingOnlyOnSuccess` passes.

## Step 5: tests (five new)

**File:** new `tests/QsppManager.Integration.Tests/RestorePromoterTests.cs`. Each test builds its own temp root with a small SQLite file created through `QsppDbContext` and `Database.EnsureCreated()` plus a table row, so it can tell old from new data.

1. `Promote_CopiesLiveDbAside_BeforeSwap`: after success, `pre-restore/*/qspp.db` exists and has the OLD row.
2. `Promote_RefusesCorruptDb`: staged `qspp.db` is the first 100 bytes of a valid DB. Expect `Promoted == false`, live DB bytes unchanged (compare SHA-256 before and after), staging still exists.
3. `Promote_RefusesBackupFromNewerSchema`: insert a fake `MigrationId` (`29990101000000_Future`) into the staged DB's `__EFMigrationsHistory` (create the table if absent). Expect refusal and unchanged live DB.
4. `Promote_SwapsDb_AndRemovesStagingOnlyOnSuccess`: success case, live DB has the NEW row, staging deleted.
5. `Promote_KeepsOnlyNewestFiveSafetyCopies`: run six promotions with different timestamps (sleep 1.1 s or inject the stamp via an optional parameter), assert 5 directories remain.

**Done when:** `dotnet test --no-build --filter RestorePromoterTests` prints `Passed: 5`.

## Step 6: pre-migration backup

**File:** `src/QsppManager.Infrastructure/Persistence/DatabaseBootstrapper.cs`, `Prepare` (lines 21-50). Current end of the method:

```csharp
        finally { conn.Close(); }
        db.Database.Migrate();
    }
```

**Change:** replace the last statement with

```csharp
        finally { conn.Close(); }
        var pending = db.Database.GetPendingMigrations().ToList();
        var dbFile = db.Database.GetDbConnection().DataSource;
        if (pending.Count > 0 && File.Exists(dbFile) && new FileInfo(dbFile).Length > 0 && HasUserTables(db))
            PreMigrationBackup(db, dbFile, pending[^1]);
        db.Database.Migrate();
```

Add two private static methods. `HasUserTables` runs `SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Requirements'` through the same `Scalar` helper and returns `> 0` (a fresh install has nothing worth saving). `PreMigrationBackup` creates `Path.Combine(Path.GetDirectoryName(dbFile)!, "pre-migration")`, runs `VACUUM INTO '<dir>/qspp-<yyyyMMdd-HHmmss>-before-<migrationId>.db'` (escape single quotes as `BackupService.cs:33` does), then keeps the newest 5 `*.db` files in that folder. If the VACUUM throws, **let it throw**: do not migrate without the safety copy. Catch it in `App.axaml.cs` only to show a message and stop the boot (`Log.Fatal`, then `desktop.Shutdown()`), because silently continuing is the bug.

**Done when:** test below passes.

## Step 7: migration safety test (one new)

**File:** `RestorePromoterTests.cs` (or a new `DatabaseBootstrapperTests.cs`). `Prepare_BacksUpBeforePendingMigration`: create a temp DB with `EnsureCreated()`, delete the `__EFMigrationsHistory` table, insert a row in `Requirements` is not needed; call `DatabaseBootstrapper.Prepare(db)`; assert `pre-migration/` beside the DB holds exactly one `.db` file and that the migrated DB still opens. (This also gives the legacy stamp path its first test.)

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`, warnings not above the 83 baseline without explanation.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 6 (237 when this is the first plan applied).
- N0 is the total printed by a TEST GATE run on a clean checkout before you change anything. Write it down first; other plans in `plans/` may have landed and moved it above 231.
- Manual proof (scratch only): `QSPP_DATA_DIR=$(mktemp -d) dotnet run --project src/QsppManager.Desktop` boots; a `pre-migration/` folder does **not** appear on a fresh install.

## STOP conditions

- Any command would read or write the real data dir.
- `dotnet test` count differs from 237 for a reason you cannot explain.
- `PRAGMA integrity_check` is not available through the SQLite build in use (it is standard; if it errors, stop and report).
- Step 6 makes any existing test fail, which would mean a test relied on migrating without a backup.

## Rollback

`git revert` the single commit (`fix(FIX-03): restore and upgrade cannot destroy live data`). The new `pre-restore/` and `pre-migration/` folders are inert data and can be deleted by the user.
