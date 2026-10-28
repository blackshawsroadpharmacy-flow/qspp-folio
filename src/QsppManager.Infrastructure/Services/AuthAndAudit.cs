using QsppManager.Application;
using QsppManager.Domain;
using QsppManager.Infrastructure.Persistence;
using System.Security.Cryptography;
using QsppManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace QsppManager.Infrastructure.Services;

/// <summary>Hash-chained append-only audit trail. Tamper-EVIDENCE, not tamper-proofing:
/// a machine administrator can still alter storage; the chain makes unnoticed edits detectable.</summary>
public class AuditService(QsppDbContext db)
{
    private readonly object _lock = new();

    private static string DigestFor(string previous, string actorName, string action, string entityType, string entityId, string details)
    {
        // AtUtc is deliberately NOT part of the digest: SQLite DateTime round-trips change
        // Kind (Utc↔Unspecified) which makes timestamp-based chain verification brittle.
        var payload = $"{previous}|{actorName}|{action}|{entityType}|{entityId}|{details}";
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    public void Append(string actorName, Guid? actorId, string action, string entityType, string entityId, string details)
    {
        lock (_lock)
        {
            var last = db.AuditEvents.AsNoTracking().OrderByDescending(a => a.Id).FirstOrDefault();
            var prev = last?.Digest ?? "";
            var storedDetails = details[..Math.Min(details.Length, 1800)];
            var digest = DigestFor(prev, actorName, action, entityType, entityId, storedDetails);
            db.AuditEvents.Add(new AuditEvent
            {
                ActorName = actorName,
                ActorUserId = actorId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = storedDetails,
                PreviousDigest = prev,
                Digest = digest,
            });
            db.SaveChanges();
        }
    }

    public List<AuditEvent> Recent(int count) =>
        db.AuditEvents.AsNoTracking().OrderByDescending(a => a.Id).Take(count).ToList();

    /// <summary>Verifies the whole chain; returns (valid, firstBrokenId).</summary>
    public (bool valid, long? brokenAt) VerifyChain()
    {
        var prev = "";
        foreach (var ev in db.AuditEvents.AsNoTracking().OrderBy(a => a.Id))
        {
            var digest = DigestFor(prev, ev.ActorName, ev.Action, ev.EntityType, ev.EntityId, ev.Details);
            if (digest != ev.Digest || ev.PreviousDigest != prev) return (false, ev.Id);
            prev = ev.Digest;
        }
        return (true, null);
    }
}

/// <summary>Local identity: PBKDF2-SHA256 passphrases + role checks. Machine-local
/// authentication for a single-machine deployment (honest scope per docs/decisions.md DD-13).</summary>
public class AuthService(QsppDbContext db, AuditService audit)
{
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

    public static bool VerifyPassphrase(string passphrase, string saltHex, string hashHex, int iterations)
    {
        try
        {
            var salt = Convert.FromHexString(saltHex);
            var hash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(hashHex));
        }
        catch { return false; }
    }

    public (ServiceResult result, AppUser? user) CreateFirstUser(string userName, string displayName, string passphrase)
    {
        if (db.Users.Any(u => u.UserName == userName))
            return (ServiceResult.Fail("That user name is already in use."), null);
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 8)
            return (ServiceResult.Fail("Passphrase must be at least 8 characters."), null);
        var (salt, hash, iters) = HashPassphrase(passphrase);
        var user = new AppUser { UserName = userName.Trim(), DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName, Role = UserRole.OwnerAdmin, PassphraseSalt = salt, PassphraseHash = hash, PassphraseIterations = iters };
        db.Users.Add(user);
        db.SaveChanges();
        audit.Append(displayName, user.Id, "user.created", "user", user.Id.ToString(), "First owner/admin created");
        return (ServiceResult.Succeed(user.Id), user);
    }

    public (ServiceResult result, UserSession? session) Login(string userName, string passphrase)
    {
        var locked = LockMessage(userName.Trim());
        if (locked != null) return (ServiceResult.Fail(locked), null);
        var user = db.Users.FirstOrDefault(u => u.UserName == userName && u.IsActive);
        if (user is null || !VerifyPassphrase(passphrase, user.PassphraseSalt, user.PassphraseHash, user.PassphraseIterations))
        {
            RecordFailure(userName.Trim(), userName.Trim());
            return (ServiceResult.Fail("Sign-in failed. Check the user name and passphrase."), null);
        }
        ClearFailures(userName.Trim());
        if (user.PassphraseIterations < Iterations)
        {
            var (s2, h2, i2) = HashPassphrase(passphrase);
            user.PassphraseSalt = s2; user.PassphraseHash = h2; user.PassphraseIterations = i2;
            db.SaveChanges();
            audit.Append(user.DisplayName, user.Id, "user.passphrase.rehash", "user", user.Id.ToString(), $"work factor raised to {i2}");
        }
        audit.Append(user.DisplayName, user.Id, "user.login", "user", user.Id.ToString(), "");
        return (ServiceResult.Succeed(user.Id), new UserSession(user.Id, user.UserName, user.DisplayName, user.Role));
    }

    public AppUser? GetUser(Guid id) => db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);

    public (ServiceResult result, AppUser? user) AddUser(string actorName, Guid actorId, string userName, string displayName, UserRole role, string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 8)
            return (ServiceResult.Fail("Passphrase must be at least 8 characters."), null);
        if (db.Users.Any(u => u.UserName == userName))
            return (ServiceResult.Fail("That user name is already in use."), null);
        var (salt, hash, iters) = HashPassphrase(passphrase);
        var user = new AppUser { UserName = userName.Trim(), DisplayName = displayName, Role = role, PassphraseSalt = salt, PassphraseHash = hash, PassphraseIterations = iters };
        db.Users.Add(user);
        db.SaveChanges();
        audit.Append(actorName, actorId, "user.added", "user", user.Id.ToString(), $"role={role}");
        return (ServiceResult.Succeed(user.Id), user);
    }

    public List<AppUser> ListUsers() => db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName).ToList();

    /// <summary>Signed-in self-service passphrase change: the CURRENT passphrase must verify
    /// (this is the ordinary "change password", not a reset). Old passphrase stops working
    /// immediately; audit records the change without ever recording the passphrase itself.</summary>
    public ServiceResult ChangeOwnPassphrase(UserSession session, string current, string newPassphrase)
    {
        if (string.IsNullOrWhiteSpace(newPassphrase) || newPassphrase.Length < 8)
            return ServiceResult.Fail("The new passphrase must be at least 8 characters.");
        var user = db.Users.FirstOrDefault(u => u.Id == session.UserId && u.IsActive);
        if (user is null) return ServiceResult.Fail("Account not found.");
        if (!VerifyPassphrase(current, user.PassphraseSalt, user.PassphraseHash, user.PassphraseIterations))
            return ServiceResult.Fail("The current passphrase is not correct.");
        return ApplyNewPassphrase(user, session.DisplayName, session.UserId, "user.passphrase.changed", "changed their own passphrase", newPassphrase);
    }

    /// <summary>Owner-admin resets any staff account's passphrase from Settings. Owner role
    /// required; the reset never touches the account's role, data or links.</summary>
    public ServiceResult ResetStaffPassphrase(UserSession session, string targetUserName, string newPassphrase)
    {
        if (session.Role != UserRole.OwnerAdmin)
            return ServiceResult.Fail("Only the pharmacy owner can set another account's passphrase.");
        if (string.IsNullOrWhiteSpace(newPassphrase) || newPassphrase.Length < 8)
            return ServiceResult.Fail("The new passphrase must be at least 8 characters.");
        var user = db.Users.FirstOrDefault(u => u.UserName == targetUserName.Trim() && u.IsActive);
        if (user is null) return ServiceResult.Fail("No active account with that user name.");
        return ApplyNewPassphrase(user, session.DisplayName, session.UserId, "user.passphrase.reset", $"reset the passphrase for '{user.UserName}'", newPassphrase);
    }

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

    /// <summary>Forgotten-passphrase path (no session, login screen). A passphrase is
    /// PBKDF2-hash-only: it can never be shown again, only replaced. It is NOT a bypass: the
    /// actor needs the owner's one-time recovery code, plus the typed confirmation phrase.
    /// The audit records the event loudly (actor 'workstation', distinct action) so any reset
    /// is visible; the recovery code itself is never logged. Threat model: the database lives
    /// on the same workstation, so this replaces access; it does NOT bypass hash-chain
    /// verification or change roles/data. No code on file: the reset is refused and the
    /// screen says why.</summary>
    public const string ForgotConfirmPhrase = "RESET MY PASSPHRASE";
    public ServiceResult ForgotPassphraseReset(string userName, string confirmation, string recoveryCode, string newPassphrase, QsppManager.Infrastructure.Runtime.AppConfig cfg)
    {
        var key = "recovery:" + (userName ?? "").Trim();
        var locked = LockMessage(key);
        if (locked != null) return ServiceResult.Fail(locked);
        if (confirmation.Trim().ToUpperInvariant() != ForgotConfirmPhrase)
            return ServiceResult.Fail($"Type {ForgotConfirmPhrase} to confirm the reset.");
        if (string.IsNullOrWhiteSpace(newPassphrase) || newPassphrase.Length < 8)
            return ServiceResult.Fail("The new passphrase must be at least 8 characters.");
        if (string.IsNullOrEmpty(cfg.RecoveryHash) || string.IsNullOrEmpty(cfg.RecoverySalt))
            return ServiceResult.Fail("No recovery code has been created on this computer. Sign in as the owner and create one in Settings, or ask the owner to set your passphrase.");
        if (!VerifyPassphrase((recoveryCode ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant(), cfg.RecoverySalt, cfg.RecoveryHash, cfg.RecoveryIterations))
        {
            RecordFailure(key, (userName ?? "").Trim());
            return ServiceResult.Fail("That recovery code is not correct.");
        }
        ClearFailures(key);
        var user = db.Users.FirstOrDefault(u => u.UserName == userName.Trim() && u.IsActive);
        if (user is null) return ServiceResult.Fail("No active account with that user name.");
        return ApplyNewPassphrase(user, "workstation", null, "user.passphrase.forgot_reset", $"forgot-passphrase reset for '{user.UserName}' (roles and data unchanged)", newPassphrase);
    }

    private ServiceResult ApplyNewPassphrase(AppUser user, string actorName, Guid? actorId, string action, string detailsPhrase, string newPassphrase)
    {
        // HashPassphrase draws a fresh random salt every call (same path as CreateFirstUser).
        var (salt, hash, iters) = HashPassphrase(newPassphrase);
        user.PassphraseSalt = salt;
        user.PassphraseHash = hash;
        user.PassphraseIterations = iters;
        db.SaveChanges();
        audit.Append(actorName, actorId, action, "user", user.Id.ToString(), detailsPhrase);
        return ServiceResult.Succeed(user.Id);
    }
}

public record UserSession(Guid UserId, string UserName, string DisplayName, UserRole Role)
{
    public bool CanVerify => Role is UserRole.OwnerAdmin or UserRole.ManagerReviewer;
    public bool IsAuthenticated => true;
    public bool ReadOnly => Role == UserRole.ReviewerReadOnly;
}