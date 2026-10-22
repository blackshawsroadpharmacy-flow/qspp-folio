using QsppManager.Domain;
using QsppManager.Infrastructure.Services;
using Xunit;

namespace QsppManager.Integration.Tests;

/// <summary>M7.3: passphrase resets. The passphrase is PBKDF2-hash-only — it can never be
/// shown again, only replaced. Three paths pin the contract: signed-in change (requires the
/// CURRENT passphrase), owner reset of a staff account (Owner role gate), and the login-screen
/// forgot-passphrase safety net (typed confirmation phrase + loud audit). None of them may
/// touch roles, data, or any other account; all write visible audit lines.</summary>
public class PasswordResetTests : TestBase
{
    [Fact]
    public void ChangeOwn_RequiresCurrentPassphrase_OldStopsWorking()
    {
        var wrong = Auth.ChangeOwnPassphrase(Owner, "not-the-one", "new-passphras9");
        Assert.False(wrong.Ok);

        var ok = Auth.ChangeOwnPassphrase(Owner, "passphras8", "new-passphras9");
        Assert.True(ok.Ok, ok.Error);

        var (loginOld, _) = Auth.Login("owner", "passphras8");
        Assert.False(loginOld.Ok);
        var (loginNew, session) = Auth.Login("owner", "new-passphras9");
        Assert.True(loginNew.Ok);
        Assert.Equal(Owner.UserId, session!.UserId);

        // exactly one passphrase-change audit line; the passphrases themselves never appear
        Assert.Equal(1, Db.AuditEvents.AsEnumerable().Count(e => e.Action == "user.passphrase.changed"));
        var line = Db.AuditEvents.AsEnumerable().LastOrDefault(e => e.Action == "user.passphrase.changed");
        Assert.NotNull(line);
        Assert.Equal("Test Owner", line!.ActorName);
    }

    [Fact]
    public void OwnerCanResetStaff_NonOwnerCannot()
    {
        var (added, staff) = Auth.AddUser(Owner.DisplayName, Owner.UserId, "pharmacist", "Rosa Intern", UserRole.Contributor, "staff-passphras1");
        Assert.True(added.Ok, added.Error);
        var staffSession = new UserSession(staff!.Id, staff.UserName, staff.DisplayName, staff.Role);

        var asStaff = Auth.ResetStaffPassphrase(staffSession, "owner", "hacked-passphras7");
        Assert.False(asStaff.Ok);   // only the owner can set others' passphrases

        var asOwner = Auth.ResetStaffPassphrase(Owner, "pharmacist", "fresh-passphras5");
        Assert.True(asOwner.Ok, asOwner.Error);
        var (login, _) = Auth.Login("pharmacist", "fresh-passphras5");
        Assert.True(login.Ok);

        // the reset touched ONLY the target account
        var ownerUser = Auth.GetUser(Owner.UserId)!;
        var (ownerLogin, _) = Auth.Login("owner", "passphras8");
        Assert.True(ownerLogin.Ok);
        Assert.Equal(UserRole.Contributor, Db.Users.AsEnumerable().First(u => u.UserName == "pharmacist").Role);
        Assert.Equal(1, Db.AuditEvents.AsEnumerable().Count(e => e.Action == "user.passphrase.reset"));
    }

    [Fact]
    public void ForgotReset_NeedsTypedConfirmation_ThenSignInWorks()
    {
        // wrong confirmation phrase refused
        Assert.False(Auth.ForgotPassphraseReset("owner", "just let me in", "brand-passphras1").Ok);
        Assert.False(Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, "short").Ok);
        Assert.False(Auth.ForgotPassphraseReset("nobody", AuthService.ForgotConfirmPhrase, "brand-passphras1").Ok);
        var (stillOld, _) = Auth.Login("owner", "passphras8");
        Assert.True(stillOld.Ok);   // nothing changed by the refused attempts

        // correct confirmation phrase replaces the passphrase
        var ok = Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, "brand-passphras1");
        Assert.True(ok.Ok, ok.Error);
        var (oldGone, _) = Auth.Login("owner", "passphras8");
        Assert.False(oldGone.Ok);
        var (newIn, session) = Auth.Login("owner", "brand-passphras1");
        Assert.True(newIn.Ok);
        Assert.Equal(Owner.UserId, session!.UserId);
        Assert.Equal(UserRole.OwnerAdmin, session.Role);

        // loud, distinct, unambiguous audit line
        var line = Db.AuditEvents.AsEnumerable().LastOrDefault(e => e.Action == "user.passphrase.forgot_reset");
        Assert.NotNull(line);
        Assert.Equal("workstation", line!.ActorName);
        Assert.Contains("'owner'", line.Details);
    }

    [Fact]
    public void ResetPaths_LeaveDataAndIntactness_Untouched()
    {
        var reqsBefore = Db.Requirements.Count();
        var usersBefore = Db.Users.Count();
        Assert.True(Auth.ForgotPassphraseReset("owner", AuthService.ForgotConfirmPhrase, "brand-passphras1").Ok);
        Assert.Equal(3, Db.Requirements.Count());
        Assert.Equal(usersBefore, Db.Users.Count());
        Assert.Equal(1, Db.Users.Select(u => u.Role == UserRole.OwnerAdmin).Count());
    }
}