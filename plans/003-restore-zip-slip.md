# Plan 003: backup restore rejects path traversal, unlisted and duplicate entries

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-04, audit finding F-06. Effort S.

## Summary

`BackupService.Restore` hash-checks only the files named in `manifest.json`, then extracts **every** archive entry to `Path.Combine(targetDir, entry.FullName)`. A zip entry named `../../x`, an entry the manifest never mentions, or a second entry with the same name as a verified one is written unchecked. After this plan, restore refuses any archive that contains an entry outside the manifest, a manifest path that leaves the target directory, or duplicate entry names, before anything is extracted.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10. Backup code: `src/QsppManager.Infrastructure/Backup/BackupService.cs`. Tests: `tests/QsppManager.Integration.Tests`.
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- Baseline: 231 tests pass at `5858779`. Write down the total N0 from a TEST GATE run before you change anything; other plans may have landed.
- **Never** open `~/Library/Application Support/QsppManager/` or `QsppManager-backup-*`. All test archives are generated in temp dirs.
- Never verify a build through a pipe. Run bare, then `echo "exit=$?"`. No em dashes in text you write.

## Out of scope

Encrypting backups (FIX-20). Signing the manifest (an attacker who controls the zip controls the manifest; this plan stops file-system escape and substitution, not a fully forged backup). Safety copy and integrity check of the database (plan 001).

## Step 1: validate the archive before extracting anything

**File:** `src/QsppManager.Infrastructure/Backup/BackupService.cs`, `Restore` (lines 85-137). Current code from the manifest read to the end of extraction:

```csharp
            // Validate every listed file hash before trusting the archive.
            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
                expected[f.GetProperty("path").GetString()!] = f.GetProperty("sha256").GetString()!;

            foreach (var (entryPath, sha) in expected)
            {
                var entry = archive.GetEntry(entryPath);
                if (entry == null)
                    throw new InvalidOperationException($"manifest lists '{entryPath}' but it is missing from the archive");
                var tmp = Path.Combine(targetDir, ".hashcheck");
                entry.ExtractToFile(tmp, overwrite: true);
                var actual = HashFile(tmp);
                File.Delete(tmp);
                if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                    return (false, $"Hash mismatch for {entryPath} ...", "", 0);
            }

            // Extract everything (hashes already proven).
            var restored = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var dest = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                restored++;
            }
```

**Change:** insert, between the `expected` dictionary build and the hash loop:

```csharp
            var targetFull = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            bool Contained(string rel)
            {
                if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel) || rel.Contains('\0')) return false;
                var full = Path.GetFullPath(Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar)));
                return full.StartsWith(targetFull, StringComparison.Ordinal);
            }

            foreach (var p in expected.Keys)
                if (!Contained(p))
                    return (false, $"Unsafe path in backup manifest ('{p}'). Nothing was restored.", "", 0);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in archive.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;             // directory entry
                if (!seen.Add(e.FullName))
                    return (false, $"Backup contains two entries named '{e.FullName}'. Nothing was restored.", "", 0);
                if (e.FullName == "manifest.json") continue;
                if (!expected.ContainsKey(e.FullName) || !Contained(e.FullName))
                    return (false, $"Backup contains '{e.FullName}', which its manifest does not list. Nothing was restored.", "", 0);
            }
```

**Change the extraction loop** to extract only verified, contained entries and wrap the hash temp file in `try/finally`:

```csharp
            var restored = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var dest = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                if (!Path.GetFullPath(dest).StartsWith(targetFull, StringComparison.Ordinal))
                    return (false, "Unsafe entry path. Restore stopped.", "", restored);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                restored++;
            }
```

and in the hash loop replace the three lines `entry.ExtractToFile(tmp...)`, `var actual = HashFile(tmp);`, `File.Delete(tmp);` with:

```csharp
                string actual;
                try { entry.ExtractToFile(tmp, overwrite: true); actual = HashFile(tmp); }
                finally { try { File.Delete(tmp); } catch { } }
```

**Done when:** BUILD GATE passes and `grep -n "Contained(" src/QsppManager.Infrastructure/Backup/BackupService.cs` shows the helper and its three uses.

## Step 2: tests (five new)

**File:** new `tests/QsppManager.Integration.Tests/RestoreArchiveSafetyTests.cs`. These tests do **not** need a database: `BackupService`'s constructor takes `(QsppDbContext db, AttachmentStore store, string backupsDir)` but `Restore` uses none of them. Build the service with an in-memory SQLite context: `new QsppDbContext(new DbContextOptionsBuilder<QsppDbContext>().UseSqlite("Data Source=:memory:").Options)` and `new AttachmentStore(Path.GetTempPath())`.

Add a private helper `BuildZip(string zipPath, params (string name, byte[] bytes)[] entries)` that writes the entries with `ZipArchiveMode.Create` and also writes a `manifest.json` entry (`format: "qspp-backup-1"`, `files` = a JSON array of `{path, sha256, bytes}` for the entries passed in the optional `manifestListed` argument, defaulting to all of them). Compute sha256 with `Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()`.

1. `Restore_RejectsEntryNotInManifest`: entries `qspp.db` (listed) and `extra.txt` (not listed). Expect `ok == false`, error contains `does not list`, and `Directory.Exists(target)` may be true but contains no `extra.txt` and no `qspp.db`.
2. `Restore_RejectsTraversalEntry_NotListed`: entry name `../escaped-1.txt`. Expect refusal and `File.Exists(Path.Combine(Path.GetDirectoryName(target)!, "escaped-1.txt")) == false`.
3. `Restore_RejectsTraversalPath_ListedInManifest_WithMatchingHash`: manifest lists `../escaped-2.txt` with the correct hash. Expect refusal containing `Unsafe path`, and the file absent.
4. `Restore_RejectsDuplicateEntryNames`: two entries both named `qspp.db` with different bytes (`ZipArchive.CreateEntry` allows this), manifest lists the first. Expect refusal containing `two entries`.
5. `Restore_AcceptsNestedAttachmentPaths`: entries `qspp.db` and `attachments/ab/cd/abcd.txt`, both listed. Expect `ok == true`, `filesRestored == 3` (db, attachment, manifest), and the nested file exists under `target`.

Use a fresh `target` per test: `Path.Combine(Path.GetTempPath(), "qspp-zs-" + Guid.NewGuid().ToString("N"), "t")` so a parent directory exists to assert that nothing escaped into it.

**Done when:** `dotnet test --no-build --filter RestoreArchiveSafetyTests` prints `Passed: 5`; the existing `BackupRestore_Roundtrip_PreservesData_AndDetectsTampering` still passes (it restores a real backup, which has only listed files).

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 5.
- `git diff --stat` shows only `BackupService.cs` and the new test file.

## STOP conditions

- The existing round-trip test fails (a real backup contains an entry the manifest does not list; report the entry name, do not loosen the rule).
- You find yourself needing the real data dir or a real backup file.

## Rollback

`git revert` the commit (`fix(FIX-04): restore rejects traversal, unlisted and duplicate entries`). No data or schema is touched.
