using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop.Views;

public partial class LoginView : UserControl
{
    private readonly ShellViewModel _shell;

    public LoginView(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        PharmacyLine.Text = shell.PharmacyName;   // DB-backed profile name, never a hardcoded pharmacy
        LoginBtn.Click += OnLogin;
        ForgotBtn.Click += (_, _) => EnterResetMode();
        BackBtn.Click += (_, _) => ExitResetMode();
        ResetBtn.Click += OnReset;
        Passphrase.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Return) OnLogin(sender: null, e: null!);
        };
        AttachedToVisualTree += (_, _) => UserName.Focus();
    }

    private void OnLogin(object? sender, RoutedEventArgs? e)
    {
        Message.Text = "";
        SuccessMessage.IsVisible = false;
        var (result, session) = App.Auth.Login(UserName.Text?.Trim() ?? "", Passphrase.Text ?? "");
        if (!result.Ok || session == null)
        {
            Message.Text = result.Error;
            return;
        }
        _shell.SignIn(session);
    }

    // M7.3: forgotten-passphrase reset, straight from the login screen. The auth service
    // carries the full contract (typed confirmation phrase, 8+ characters, loud audit).
    private void EnterResetMode()
    {
        ResetUserName.Text = UserName.Text;
        ResetMessage.Text = "";
        SignInPanel.IsVisible = false;
        ResetPanel.IsVisible = true;
        ResetUserName.Focus();
    }

    private void ExitResetMode()
    {
        ResetPanel.IsVisible = false;
        SignInPanel.IsVisible = true;
        Message.Text = "";
        UserName.Focus();
    }

    private void OnReset(object? sender, RoutedEventArgs? e)
    {
        ResetMessage.Text = "";
        if (NewPass.Text != NewPass2.Text)
        {
            ResetMessage.Text = "The two new passphrases do not match.";
            return;
        }
        var result = App.Auth.ForgotPassphraseReset(ResetUserName.Text?.Trim() ?? "", ConfirmPhrase.Text ?? "", NewPass.Text ?? "");
        if (!result.Ok)
        {
            ResetMessage.Text = result.Error;
            return;
        }
        ResetMessage.Text = "";
        SuccessMessage.Text = $"Passphrase reset for '{ResetUserName.Text?.Trim()}'. Sign in with the new passphrase.";
        SuccessMessage.IsVisible = true;
        NewPass.Text = NewPass2.Text = ConfirmPhrase.Text = "";
        ExitResetMode();
    }
}