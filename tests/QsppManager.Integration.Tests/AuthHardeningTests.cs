using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Runtime;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>FIX-05 / plan 004: sign-in locks out after five failures (in-memory, restart
/// clears), hashes upgrade to 600k iterations on the next successful sign-in, and the
/// login-screen reset requires the owner's one-time recovery code (Step 0 answer: Hugh
/// accepts that an owner without a code cannot reset from the login screen).</summary>
public class AuthHardeningTests : TestBase
{
    [Fact]
    public void NewHashes_Use600kIterations()
    {
        // TestBase lowers the static for speed; the PRODUCTION constant is what ships.
        Assert.Equal(600_000, AuthService.DefaultIterations);
    }

    [Fact]
    public void Login_UpgradesOldHashIterations()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var oldHash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes("old-passphras3"), salt, 500, HashAlgorithmName.SHA256, 32);
        Db.Users.Add(new AppUser
        {
            UserName = "legacy", DisplayName = "Legacy User", Role = UserRole.Contributor,
            PassphraseSalt = Convert.ToHexString(salt).ToLowerInvariant(),
            PassphraseHash = Convert.ToHexString(oldHash).ToLowerInvariant(),
            PassphraseIterations = 500,
        });
        Db.SaveChanges();

        var (login, _) = Auth.Login("legacy", "old-passphras3");
        Assert.True(login.Ok, login.Error);

        var upgraded = Db.Users.AsNoTracking().First(u => u.UserName == "legacy");
        Assert.Equal(AuthService.Iterations, upgraded.PassphraseIterations);
        Assert.Contains(Db.AuditEvents.AsEnumerable(), e => e.Action == "user.passphrase.rehash");

        var (again, _) = Auth.Login("legacy", "old-passphras3");
        Assert.True(again.Ok);
    }

    [Fact]
    public void Login_LocksAfterFiveFailures_ThenRecovers()
    {
        var now = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
        Auth.UtcNow = () => now;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var (bad, _) = Auth.Login("owner", "wrong-passphras!");
                Assert.False(bad.Ok);
            }

            // sixth attempt, even with the CORRECT passphrase, is throttled
            var (sixth, _) = Auth.Login("owner", "passphras8");
            Assert.False(sixth.Ok);
            Assert.Contains("Too many attempts", sixth.Error);

            now = now.AddSeconds(31);
            var (recovered, session) = Auth.Login("owner", "passphras8");
            Assert.True(recovered.Ok, recovered.Error);
            Assert.Equal(Owner.UserId, session!.UserId);
            Assert.Equal(1, Db.AuditEvents.AsEnumerable().Count(e => e.Action == "user.login.locked"));
        }
        finally { Auth.UtcNow = () => DateTime.UtcNow; }
    }

    [Fact]
    public void Login_SuccessClearsFailureCount()
    {
        var now = new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
        Auth.UtcNow = () => now;
        try
        {
            for (var i = 0; i < 4; i++) Auth.Login("owner", "wrong-passphras!");
            var (ok, _) = Auth.Login("owner", "passphras8");
            Assert.True(ok.Ok);
            for (var i = 0; i < 4; i++) Auth.Login("owner", "wrong-passphras!");
            var (still, s2) = Auth.Login("owner", "passphras8");
            Assert.True(still.Ok, still.Error);   // no lockout: success cleared the count
        }
        finally { Auth.UtcNow = () => DateTime.UtcNow; }
    }

    [Fact]
    public void RecoveryCode_OnlyOwnerCanCreate()
    {
        var (added, contrib) = Auth.AddUser(Owner.DisplayName, Owner.UserId, "assist", "Assist", UserRole.Contributor, "assist-pass1");
        Assert.True(added.Ok, added.Error);
        var session = new UserSession(contrib!.Id, contrib.UserName, contrib.DisplayName, contrib.Role);

        var cfg = new AppConfig();
        var (res, code) = Auth.CreateRecoveryCode(session, cfg);
        Assert.False(res.Ok);
        Assert.Null(code);
        Assert.Null(cfg.RecoveryHash);
    }

    [Fact]
    public void ForgotReset_RefusedWithoutRecoveryCode()
    {
        var cfg = new AppConfig();   // no code on file

        var res = Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, "BBBB-CCCC-DDDD-EEEE-FFFF", "brand-passphras1", cfg);
        Assert.False(res.Ok);
        Assert.Contains("No recovery code", res.Error);

        var (oldOk, _) = Auth.Login("owner", "passphras8");
        Assert.True(oldOk.Ok);   // refusal changed nothing
    }

    [Fact]
    public void ForgotReset_RefusedWithWrongCode()
    {
        var cfg = new AppConfig();
        var (created, code) = Auth.CreateRecoveryCode(Owner, cfg);
        Assert.True(created.Ok, created.Error);

        var wrong = Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, "ZZZZ-YYYY-XXXX-WWWW-VVVV", "brand-passphras1", cfg);
        Assert.False(wrong.Ok);
        Assert.Contains("not correct", wrong.Error);
        var (stillOld, _) = Auth.Login("owner", "passphras8");
        Assert.True(stillOld.Ok);

        // five wrong codes lock recovery:owner; a sixth attempt is throttled
        for (var i = 0; i < 4; i++)
            Assert.False(Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, "ZZZZ-YYYY-XXXX-WWWW-VVVV", "brand-passphras1", cfg).Ok);
        var fifth = Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, "ZZZZ-YYYY-XXXX-WWWW-VVVV", "brand-passphras1", cfg);
        Assert.False(fifth.Ok);   // fifth failure sets the lock
        var sixth = Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, code!, "brand-passphras1", cfg);
        Assert.False(sixth.Ok);
        Assert.Contains("Too many attempts", sixth.Error);
    }
}