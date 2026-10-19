using Avalonia.Controls;
using Avalonia.Interactivity;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Entities;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Desktop.Views;

public partial class OnboardingView : UserControl
{
    private readonly ShellViewModel _shell;

    public OnboardingView(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        CreateBtn.Click += OnCreate;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        Message.Text = "";
        var userName = UserName.Text?.Trim();
        var passphrase = Passphrase.Text ?? "";
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(passphrase))
        {
            Message.Text = "A user name and passphrase are required.";
            return;
        }
        var (result, user) = App.Auth.CreateFirstUser(userName, DisplayName.Text?.Trim() ?? "", passphrase);
        if (!result.Ok || user == null)
        {
            Message.Text = result.Error;
            return;
        }
        var ph = App.Workspace.EnsurePharmacy();
        ph.Name = string.IsNullOrWhiteSpace(PharmacyName.Text) ? ph.Name : PharmacyName.Text.Trim();
        if (Jur.SelectedItem is ComboBoxItem { Content: string s })
        {
            var parts = s.Split('·');
            if (parts.Length == 2)
            {
                ph.Jurisdiction = parts[0].Trim();
                ph.TimeZoneId = parts[1].Trim();
            }
        }
        App.Db.SaveChanges();
        App.Audit.Append(user.DisplayName, user.Id, "pharmacy.profile", "pharmacy", ph.Id.ToString(), $"{ph.Name} · {ph.Jurisdiction} · {ph.TimeZoneId}");
        _shell.SignIn(new UserSession(user.Id, user.UserName, user.DisplayName, user.Role));
    }
}