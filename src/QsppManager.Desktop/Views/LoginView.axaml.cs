using Avalonia.Controls;
using Avalonia.Interactivity;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop.Views;

public partial class LoginView : UserControl
{
    private readonly ShellViewModel _shell;

    public LoginView(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        LoginBtn.Click += OnLogin;
    }

    private void OnLogin(object? sender, RoutedEventArgs e)
    {
        Message.Text = "";
        var (result, session) = App.Auth.Login(UserName.Text?.Trim() ?? "", Passphrase.Text ?? "");
        if (!result.Ok || session == null)
        {
            Message.Text = result.Error;
            return;
        }
        _shell.SignIn(session);
    }
}