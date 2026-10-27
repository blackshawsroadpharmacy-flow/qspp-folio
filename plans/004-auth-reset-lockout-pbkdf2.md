# Plan 004: login reset needs a recovery code, sign-in locks out, hashes upgrade to 600,000 iterations

Written against commit `5858779` (branch `design/premium-polish`). Backlog item FIX-05, audit findings F-04 and F-05. Effort M.

## Summary

Today anyone at the login screen can reset any account, including the owner, by typing a fixed public phrase (`AuthAndAudit.cs:163`), and sign-in has no attempt limit. After this plan:

1. The owner creates a one-time **recovery code** in Settings. The login-screen reset requires that code. With no code on file the reset is refused and the screen says why.
2. Five wrong sign-ins (or wrong recovery codes) for one user name lock that name out for 30 seconds, doubling each time up to 15 minutes.
3. New hashes use PBKDF2-SHA256 at 600,000 iterations; existing hashes are upgraded silently on the next successful sign-in.

## Decision needed before you start (Step 0, blocking)

Ask Hugh, quote exactly, and record the answer in the commit message:

> "After this change the login-screen reset only works if the owner has created a recovery code first. An owner who forgets their passphrase and never created a code cannot reset from the login screen. Is that acceptable, and will you create the code the moment this ships?"

If Hugh says no, STOP and report. Do not weaken the gate.

## Read this first (you have never seen this repo)

- Repo `~/AI/qspp-folio`, solution `qspp-manager.slnx`, .NET 10, Avalonia UI, SQLite/EF Core. Auth code: `src/QsppManager.Infrastructure/Services/AuthAndAudit.cs`. Login screen: `src/QsppManager.Desktop/Views/LoginView.axaml` and `.axaml.cs`. Settings: `src/QsppManager.Desktop/Views/SettingsView.cs`. Config file class: `src/QsppManager.Infrastructure/Runtime/AppConfig.cs`.
- Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.
- Baseline 231 tests at `5858779`. Write down N0 from a TEST GATE run before changing anything.
- **Never** open `~/Library/Application Support/QsppManager/` or `QsppManager-backup-*`. Real accounts live there. Tests use temp databases; any app run uses `QSPP_DATA_DIR=$(mktemp -d)`.
- Never print or log a passphrase or recovery code. Audit lines record the action, never the secret.
- Never verify a build through a pipe. Run bare, then `echo "exit=$?"`. No em dashes in text you write.

## Out of scope

Idle auto-lock of a signed-in session. Persisting lockout counters across app restarts (they are in memory by design; a restart clears them, and that limit is stated in the code comment). Two-factor. Changing role rules. Password complexity beyond the existing 8-character minimum.

## Step 1: iteration constant and rehash on login

**File:** `src/QsppManager.Infrastructure/Services/AuthAndAudit.cs`. Current code:

```csharp
    public static (string salt, string hash, int iterations) HashPassphrase(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 120_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToHexString(salt).ToLowerInvariant(), Convert.ToHexString(hash).ToLowerInvariant(), iterations);
    }
```

**Change:** replace with

```csharp
    /// <summary>OWASP-aligned work factor for PBKDF2-HMAC-SHA256. Tests lower <see cref="Iterations"/>
    /// for speed; production never changes it.</summary>
    public const int DefaultIterations = 600_000;
    public static int Iterations = DefaultIterations;

    public static (string salt, string hash, int iterations) HashPassphrase(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var iterations = Iterations;
        var hash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToHexString(salt).ToLowerInvariant(), Convert.ToHexString(hash).ToLowerInvariant(), iterations);
    }
```

Also in `src/QsppManager.Domain/Entities/CoreEntities.cs:153` leave `PassphraseIterations = 120_000` as is (it is only a default for rows created without a hash; `HashPassphrase` always supplies the real value).

In `Login`, after the success check (currently lines 101-108):

```csharp
        var user = db.Users.FirstOrDefault(u => u.UserName == userName && u.IsActive);
        if (user is null || !VerifyPassphrase(passphrase, user.PassphraseSalt, user.PassphraseHash, user.PassphraseIterations))
            return (ServiceResult.Fail("Sign-in failed. Check the user name and passphrase."), null);
        audit.Append(user.DisplayName, user.Id, "user.login", "user", user.Id.ToString(), "");
```

insert before the `audit.Append(... "user.login" ...)` line:

```csharp
        if (user.PassphraseIterations < Iterations)
        {
            var (s2, h2, i2) = HashPassphrase(passphrase);
            user.PassphraseSalt = s2; user.PassphraseHash = h2; user.PassphraseIterations = i2;
            db.SaveChanges();
            audit.Append(user.DisplayName, user.Id, "user.passphrase.rehash", "user", user.Id.ToString(), $"work factor raised to {i2}");
        }
```

**Done when:** BUILD GATE passes; test `NewHashes_Use600kIterations` and `Login_UpgradesOldHashIterations` (step 5) pass.

## Step 2: lockout

**File:** same, class `AuthService`. Add fields and helpers at the top of the class:

```csharp
    private readonly Dictionary<string, (int Fails, DateTime LockedUntil)> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _attemptLock = new();
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Per-name throttle. In memory only: a restart clears it. It slows a person at the
    /// keyboard; it is not a defence against someone who can copy the database file.</summary>
    private string? LockMessage(string key)
    {
        lock (_attemptLock)
            if (_attempts.TryGetValue(key, out var a) && a.LockedUntil > UtcNow())
                return $"Too many attempts. Try again in {Math.Ceiling((a.LockedUntil - UtcNow()).TotalSeconds):0} seconds.";
        return null;
    }

    private void RecordFailure(string key, string display)
    {
        lock (_attemptLock)
        {
            _attempts.TryGetValue(key, out var a);
            var fails = a.Fails + 1;
            var until = a.LockedUntil;
            if (fails >= 5)
            {
                var step = Math.Min(fails - 5, 5);                   // 0..5
                until = UtcNow().AddSeconds(Math.Min(30 * Math.Pow(2, step), 900));
                audit.Append("workstation", null, "user.login.locked", "user", display, $"{fails} failed attempts");
            }
            _attempts[key] = (fails, until);
        }
    }

    private void ClearFailures(string key) { lock (_attemptLock) _attempts.Remove(key); }
```

In `Login`, first line: `var locked = LockMessage(userName.Trim()); if (locked != null) return (ServiceResult.Fail(locked), null);`. On the failure branch call `RecordFailure(userName.Trim(), userName.Trim())` before returning; after success call `ClearFailures(userName.Trim())`. Keep the generic failure text unchanged.

**Done when:** tests `Login_LocksAfterFiveFailures_ThenRecovers` and `Login_SuccessClearsFailureCount` pass.

## Step 3: recovery code

**File:** `src/QsppManager.Infrastructure/Runtime/AppConfig.cs`. Add three properties after `ReduceMotion`:

```csharp
    public string? RecoverySalt { get; set; }
    public string? RecoveryHash { get; set; }
    public int RecoveryIterations { get; set; }
```

(`app-config.json` sits beside the database; it holds a hash, never the code. It is not part of backups, so a restored machine needs a new code.)

**File:** `AuthAndAudit.cs`. Add to `AuthService`:

```csharp
    /// <summary>Owner only. Returns the code once; only a PBKDF2 hash is stored.</summary>
    public (ServiceResult result, string? code) CreateRecoveryCode(UserSession session, QsppManager.Infrastructure.Runtime.AppConfig cfg)
    {
        if (session.Role != UserRole.OwnerAdmin)
            return (ServiceResult.Fail("Only the pharmacy owner can create a recovery code."), null);
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";   // no 0 O 1 I L
        var chars = new char[20];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        var code = string.Join("-", Enumerable.Range(0, 5).Select(g => new string(chars, g * 4, 4)));
        var (salt, hash, iters) = HashPassphrase(code.Replace("-", ""));
        cfg.RecoverySalt = salt; cfg.RecoveryHash = hash; cfg.RecoveryIterations = iters;
        // The caller persists cfg (App.Config.Save()). The service never writes to disk, so tests cannot touch a real data dir.
        audit.Append(session.DisplayName, session.UserId, "recovery.created", "pharmacy", "recovery-code", "new recovery code issued (previous code, if any, no longer works)");
        return (ServiceResult.Succeed(), code);
    }
```

Change `ForgotPassphraseReset` (currently `(string userName, string confirmation, string newPassphrase)`, lines 163-176) to `(string userName, string confirmation, string recoveryCode, string newPassphrase, QsppManager.Infrastructure.Runtime.AppConfig cfg)`. Keep the existing confirmation-phrase and length checks. Add, before looking the user up:

```csharp
        var key = "recovery:" + (userName ?? "").Trim();
        var locked = LockMessage(key);
        if (locked != null) return ServiceResult.Fail(locked);
        if (string.IsNullOrEmpty(cfg.RecoveryHash) || string.IsNullOrEmpty(cfg.RecoverySalt))
            return ServiceResult.Fail("No recovery code has been created on this computer. Sign in as the owner and create one in Settings, or ask the owner to set your passphrase.");
        if (!VerifyPassphrase((recoveryCode ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant(), cfg.RecoverySalt, cfg.RecoveryHash, cfg.RecoveryIterations))
        {
            RecordFailure(key, (userName ?? "").Trim());
            return ServiceResult.Fail("That recovery code is not correct.");
        }
        ClearFailures(key);
```

Update the doc comment above the method: it is no longer a bypass; it requires the recovery code.

**Done when:** tests `ForgotReset_RefusedWithoutRecoveryCode`, `ForgotReset_RefusedWithWrongCode`, `RecoveryCode_OnlyOwnerCanCreate` and the rewritten existing reset tests pass.

## Step 4: UI

**File:** `src/QsppManager.Desktop/Views/LoginView.axaml`, in `ResetPanel` add, after the `ACCOUNT TO RESET` block (before `TYPE TO CONFIRM`):

```xml
          <StackPanel Spacing="6">
            <TextBlock Classes="label">RECOVERY CODE</TextBlock>
            <TextBox x:Name="RecoveryCode" Watermark="XXXX-XXXX-XXXX-XXXX-XXXX (from the owner)" Height="34" />
          </StackPanel>
```

**File:** `LoginView.axaml.cs`, `OnReset` (currently calls `App.Auth.ForgotPassphraseReset(ResetUserName.Text?.Trim() ?? "", ConfirmPhrase.Text ?? "", NewPass.Text ?? "")`). Change to pass `RecoveryCode.Text ?? ""` as the third argument and `App.Config` as the last; clear `RecoveryCode.Text` together with the other fields after success.

**File:** `SettingsView.cs`, after the `resetPass` button (line 93, `users.Children.Add(resetPass);`) add:

```csharp
        var recovery = new Button { Content = "Create a recovery code…" };
        recovery.Click += async (_, _) =>
        {
            if (App.Session is null) { _message.Text = "Sign in first."; return; }
            var (res, code) = App.Auth.CreateRecoveryCode(App.Session, App.Config);
            if (!res.Ok || code is null) { _message.Text = res.Error; return; }
            App.Config.Save();
            await Dialogs.Prompt(this, "Recovery code (shown once)", "Write this down and keep it somewhere safe. It lets someone reset a passphrase from the login screen.", code);
            _message.Text = "Recovery code created. The previous code, if any, no longer works.";
        };
        users.Children.Add(recovery);
```

**Done when:** BUILD GATE passes; with `QSPP_DATA_DIR=$(mktemp -d)`, the login reset panel shows the recovery field and refuses with the "No recovery code" message before one exists. Capture screenshots of Login (reset mode) and Settings > Users.

## Step 5: tests

**File:** `tests/QsppManager.Integration.Tests/PasswordResetTests.cs`. Existing tests use `Auth.ForgotPassphraseReset(user, phrase, newPass)`. Rewrite the two that call it:

- `ForgotReset_NeedsTypedConfirmation_ThenSignInWorks`: call `Auth.CreateRecoveryCode(Owner, cfg)` first with `var cfg = new AppConfig();`. Never call `cfg.Save()` or `AppConfig.Load()` in a test: `AppConfig.ConfigPath` resolves to the real data directory when `QSPP_DATA_DIR` is unset. Pass the returned code. Keep every assertion.
- `ResetPaths_LeaveDataAndIntactness_Untouched`: same, and replace the tautology `Assert.Equal(1, Db.Users.Select(u => u.Role == UserRole.OwnerAdmin).Count())` with `Assert.Equal(1, Db.Users.Count(u => u.Role == UserRole.OwnerAdmin))`.

Speed: in `TestBase`'s constructor (`WorkspaceWorkflowTests.cs`, top of the constructor) add `AuthService.Iterations = 2_000;` so the suite does not pay 600,000 iterations per user. Tests below that need the production value assert the constant, not a hash.

**Seven new tests** (same file or new `AuthHardeningTests.cs : TestBase`):

1. `NewHashes_Use600kIterations`: `Assert.Equal(600_000, AuthService.DefaultIterations)`.
2. `Login_UpgradesOldHashIterations`: insert a user whose hash was computed with 500 iterations (use `Rfc2898DeriveBytes.Pbkdf2(... 500 ...)`), `PassphraseIterations = 500`; `Auth.Login` succeeds; reload; `PassphraseIterations == AuthService.Iterations`; audit contains `user.passphrase.rehash`; second login still works.
3. `Login_LocksAfterFiveFailures_ThenRecovers`: set `Auth.UtcNow` to a variable clock; five wrong passphrases for `owner`; sixth attempt with the CORRECT passphrase fails with text containing `Too many attempts`; advance clock 31 s; correct passphrase succeeds; `Db.AuditEvents` contains one `user.login.locked`.
4. `Login_SuccessClearsFailureCount`: four failures, one success, four more failures: no lockout.
5. `RecoveryCode_OnlyOwnerCanCreate`: a Contributor session gets `Ok == false`.
6. `ForgotReset_RefusedWithoutRecoveryCode`: fresh `new AppConfig()`; valid phrase; expect refusal containing `No recovery code`; owner can still sign in with the old passphrase.
7. `ForgotReset_RefusedWithWrongCode`: after creating a code, a wrong code is refused; old passphrase still works; five wrong codes lock `recovery:owner`.

**Done when:** `dotnet test --no-build --filter "PasswordResetTests|AuthHardeningTests"` shows `Failed: 0`.

## Plan gates

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build qspp-manager.slnx -t:Rebuild -p:DesignTimeBuild=false; echo "exit=$?"
dotnet test --no-build; echo "exit=$?"
```

- BUILD GATE: `0 Error(s)`, `exit=0`.
- TEST GATE: `Failed: 0`, `exit=0`, total = N0 + 7.
- RENDER GATE: Login (sign-in and reset modes) and Settings, light and dark, scratch data only.
- `rg -n "RESET MY PASSPHRASE" src` still finds the confirmation constant (kept as a second step, not removed).

## STOP conditions

- Hugh has not answered Step 0.
- `ForgotPassphraseReset` has callers outside `LoginView.axaml.cs` and the tests (`grep -rn ForgotPassphraseReset src tests`); list them and stop.
- Any command would touch real accounts or the real data dir.
- Test run time grows by more than 30 s (it means `AuthService.Iterations` was not lowered in `TestBase`).

## Rollback

`git revert` the commit. `app-config.json` may now hold `RecoverySalt/RecoveryHash/RecoveryIterations`; older builds ignore unknown JSON fields, so leaving them is harmless. Users upgraded to 600,000 iterations still sign in on the older build (the iteration count is stored per user).
