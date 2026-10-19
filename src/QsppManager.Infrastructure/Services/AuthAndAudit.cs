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
}

public record UserSession(Guid UserId, string UserName, string DisplayName, UserRole Role)
{
    public bool CanVerify => Role is UserRole.OwnerAdmin or UserRole.ManagerReviewer;
    public bool IsAuthenticated => true;
    public bool ReadOnly => Role == UserRole.ReviewerReadOnly;
}