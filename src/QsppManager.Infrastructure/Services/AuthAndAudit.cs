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
    public static (string salt, string hash, int iterations) HashPassphrase(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 120_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToHexString(salt).ToLowerInvariant(), Convert.ToHexString(hash).ToLowerInvariant(), iterations);
    }

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
        var user = db.Users.FirstOrDefault(u => u.UserName == userName && u.IsActive);
        if (user is null || !VerifyPassphrase(passphrase, user.PassphraseSalt, user.PassphraseHash, user.PassphraseIterations))
            return (ServiceResult.Fail("Sign-in failed. Check the user name and passphrase."), null);
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

    /// <summary>The forgotten-passphrase safety net (no session, login screen). A passphrase
    /// is PBKDF2-hash-only — it can never be shown again, only replaced. This deliberately
    /// carries friction: the actor must type an exact confirmation phrase, and the audit
    /// records the event loudly (actor 'workstation', distinct action) so any reset that
    /// happened without the owner present is visible. Threat model: the database lives on
    /// the same workstation, so this replaces access; it does NOT bypass hash-chain
    /// verification or change roles/data.</summary>
    public const string ForgotConfirmPhrase = "RESET MY PASSPHRASE";
    public ServiceResult ForgotPassphraseReset(string userName, string confirmation, string newPassphrase)
    {
        if (confirmation.Trim().ToUpperInvariant() != ForgotConfirmPhrase)
            return ServiceResult.Fail($"Type {ForgotConfirmPhrase} to confirm the reset.");
        if (string.IsNullOrWhiteSpace(newPassphrase) || newPassphrase.Length < 8)
            return ServiceResult.Fail("The new passphrase must be at least 8 characters.");
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