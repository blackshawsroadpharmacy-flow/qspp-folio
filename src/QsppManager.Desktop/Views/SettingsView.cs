using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Media;

using Avalonia.Layout;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Services;
using QsppManager.Infrastructure.Runtime;

namespace QsppManager.Desktop.Views;

/// <summary>Settings v1: data location and integrity, users and roles, pharmacy profile +
/// cycle dates (with honest "unknown → follow-up task"), text enlargement, and the real
/// deadline consequences — including planning markers for the unresolved source conflicts.</summary>
public partial class SettingsView : UserControl
{
    private readonly DataGrid _users = new() { Height = 160, AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBox _name = new();
    private readonly ComboBox _jur = new() { SelectedIndex = 0, MinWidth = 260, ItemsSource = new[] { "VIC · Australia/Melbourne", "NSW · Australia/Sydney", "QLD · Australia/Brisbane", "WA · Australia/Perth", "SA · Australia/Adelaide", "TAS · Australia/Hobart", "ACT · Australia/Sydney", "NT · Australia/Darwin" } };
    private readonly CheckBox _complex = new() { Content = "Complex compounding" };
    private readonly CheckBox _sterile = new() { Content = "Sterile compounding (cleanroom)" };
    private readonly CheckBox _prescribing = new() { Content = "Prescribing by pharmacists" };
    private readonly CheckBox _indirect = new() { Content = "Indirect supply (delivery / RACF / online)" };
    private readonly DatePicker _cycleStart = new();
    private readonly DatePicker _expiry = new();
    private readonly DatePicker _onsite = new();
    private readonly ComboBox _scale = new() { SelectedIndex = 1, MinWidth = 200, ItemsSource = new[] { "Normal", "Larger (115%)", "Largest (130%)" } };
    private readonly ComboBox _appearance = new() { MinWidth = 200, ItemsSource = new[] { "Follow system", "Light", "Dark" } };
    private readonly ListBox _deadlineBox = new() { Height = 220, Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0) };
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };

    public SettingsView(ShellViewModel shell)
    {
        Content = Build();
        Load();
        _appearance.SelectedIndex = (App.Config.Appearance) switch { "dark" => 2, "light" => 1, _ => 0 };
        _appearance.SelectionChanged += (_, _) =>
        {
            var mode = _appearance.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            App.Config.Appearance = mode;
            App.Config.Save();
            App.ApplyAppearance(mode);
            Helpers.FireThemeChanged();
        };
        _scale.SelectionChanged += (_, _) =>
            Helpers.SetTextScale(_scale.SelectedIndex switch { 0 => 1.0, 1 => 1.15, _ => 1.30 });
    }

    private Control Build()
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 18, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Settings" });

        // Pharmacy profile
        var profile = new StackPanel { Spacing = 14 };
        profile.Children.Add(Ui.FormField("PHARMACY NAME", _name));
        profile.Children.Add(Ui.FormField("JURISDICTION · TIME ZONE", _jur));
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        flags.Children.Add(_complex); flags.Children.Add(_sterile); flags.Children.Add(_prescribing); flags.Children.Add(_indirect);
        profile.Children.Add(flags);
        var dates = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16,
            Children =
            {
                Ui.FormField("CYCLE START (OPTIONAL)", _cycleStart),
                Ui.FormField("ACCREDITATION EXPIRY (OPTIONAL)", _expiry),
                Ui.FormField("ONSITE ASSESSMENT DATE (OPTIONAL)", _onsite),
            } };
        profile.Children.Add(dates);
        var save = new Button { Content = "Save profile", Classes = { "primary" }, Height = 36 };
        save.Click += (_, _) => SaveProfile();
        profile.Children.Add(save);
        profile.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Text = "Leave dates blank if unknown — Hugh's QSPP Manager will not invent a deadline; it lists obligations that do not depend on the missing date." });
        sp.Children.Add(Ui.Section("Pharmacy profile", profile));

        // Deadlines
        sp.Children.Add(Ui.Section("Resulting known deadlines from the program sources", _deadlineBox));

        // Users
        _users.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(200) });
        _users.Columns.Add(new DataGridTextColumn { Header = "User name", Binding = new Avalonia.Data.Binding("UserName"), Width = new DataGridLength(140) });
        _users.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Avalonia.Data.Binding("Role"), Width = new DataGridLength(220) });
        var users = new StackPanel { Spacing = 10 };
        users.Children.Add(_users);
        var addUser = new Button { Content = "Add user…" };
        addUser.Click += (_, _) => AddUser();
        users.Children.Add(addUser);
        sp.Children.Add(Ui.Section("Users and roles", users));

        // Accessibility
        var a11y = new StackPanel { Spacing = 10 };
        a11y.Children.Add(_scale);
        a11y.Children.Add(new StackPanel { Spacing = 6, Children = { new TextBlock { Classes = { "label" }, Text = "APPEARANCE" }, _appearance } });
        a11y.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Text = "Applies immediately across every screen; the choice is remembered." });
        sp.Children.Add(Ui.Section("Text size", a11y));

        // Data + integrity
        var data = new StackPanel { Spacing = 10 };
        data.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Text = $"Database, attachments, rendered PDFs and backups live under: {AppData.Root}\nManaged attachments are content-addressed (sha256). The audit trail is append-only and hash-chained — tamper-EVIDENCE, not tamper-proofing: a machine administrator can still touch files." });
        var verify = new Button { Content = "Verify audit chain now" };
        verify.Click += (_, _) =>
        {
            var (valid, broken) = App.Audit.VerifyChain();
            _message.Text = valid ? "Audit chain verified: every entry links to the previous digest." : $"AUDIT CHAIN BROKEN at entry {broken}. Stop and investigate before trusting recorded actions.";
        };
        var backup = new Button { Content = "Back up database + attachments…" };
        backup.Click += (_, _) => { _ = Backup(); };
        var restoreBtn = new Button { Content = "Restore from backup…" };
        restoreBtn.Click += (_, _) => { _ = RestoreAsync(); };
        data.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { verify, backup, restoreBtn } });
        data.Children.Add(_message);
        sp.Children.Add(Ui.Section("Data location and integrity", data));

        // Reference corpus
        var corpus = new StackPanel { Spacing = 8 };
        corpus.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Text = $"Import summary is refreshed at startup: {App.Db.Requirements.Count()} requirements · {App.Db.SourceDocuments.Count()} source documents · {App.Db.InterpretationIssues.Count()} interpretation issues. Capacity-toolkit.pdf was rejected at import — it is an HTML page, not a PDF." });
        corpus.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap, Text = "Authentication cookies material (website-scrape/cookies.json, browser profiles) is deliberately kept out of the application database, backups, exports and source control." });
        sp.Children.Add(Ui.Section("Reference corpus", corpus));

        return new ScrollViewer { Content = sp };
    }

    private void Load()
    {
        var ph = App.Workspace.EnsurePharmacy();
        _name.Text = ph.Name;
        var jurs = ((_jur.ItemsSource as System.Collections.IEnumerable) ?? Array.Empty<object>()).Cast<object?>().Select(x => x?.ToString() ?? "").ToList();
        var idx = jurs.FindIndex(j => j.StartsWith(ph.Jurisdiction));
        _jur.SelectedIndex = idx >= 0 ? idx : 0;
        var flags = (ServiceScopeFlag)ph.ServiceScopeFlags;
        _complex.IsChecked = flags.HasFlag(ServiceScopeFlag.ComplexCompounding);
        _sterile.IsChecked = flags.HasFlag(ServiceScopeFlag.SterileCompounding);
        _prescribing.IsChecked = flags.HasFlag(ServiceScopeFlag.Prescribing);
        _indirect.IsChecked = flags.HasFlag(ServiceScopeFlag.IndirectSupply);
        if (ph.CycleStartDate is { } cs) _cycleStart.SelectedDate = new DateTimeOffset(cs.ToDateTime(TimeOnly.MinValue));
        if (ph.AccreditationExpiryDate is { } ex) _expiry.SelectedDate = new DateTimeOffset(ex.ToDateTime(TimeOnly.MinValue));
        if (ph.OnsiteAssessmentDate is { } oa) _onsite.SelectedDate = new DateTimeOffset(oa.ToDateTime(TimeOnly.MinValue));
        LoadUsers();
        LoadDeadlines();
    }

    private void LoadUsers()
    {
        _users.ItemsSource = App.Auth.ListUsers().Select(u => new { Name = u.DisplayName, u.UserName, Role = u.Role.ToString() }).ToList();
    }

    private void LoadDeadlines()
    {
        _deadlineBox.ItemsSource = App.Workspace.PharmacyDeadlines()
            .Select(d => new TextBlock
            {
                Text = $"{d.Date:dd MMM yyyy} · {d.Source} · {(d.PlanningOnly ? "PLANNING ONLY (sources disagree)" : d.Kind switch { DeadlineKind.ProviderConfirmed => "confirmed by provider/program", _ => "calculated from recorded dates" })}\n   {d.Explanation}",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new(8, 6),
            }).ToList();
    }

    private void SaveProfile()
    {
        var ph = App.Workspace.EnsurePharmacy();
        ph.Name = string.IsNullOrWhiteSpace(_name.Text) ? ph.Name : _name.Text.Trim();
        if (_jur.SelectedItem is string s)
        {
            var parts = s.Split('·');
            if (parts.Length == 2) { ph.Jurisdiction = parts[0].Trim(); ph.TimeZoneId = parts[1].Trim(); }
        }
        long flags = 0;
        if (_complex.IsChecked == true) flags |= (long)ServiceScopeFlag.ComplexCompounding;
        if (_sterile.IsChecked == true) flags |= (long)ServiceScopeFlag.SterileCompounding;
        if (_prescribing.IsChecked == true) flags |= (long)ServiceScopeFlag.Prescribing;
        if (_indirect.IsChecked == true) flags |= (long)ServiceScopeFlag.IndirectSupply;
        ph.ServiceScopeFlags = flags;
        ph.CycleStartDate = _cycleStart.SelectedDate.HasValue ? DateOnly.FromDateTime(_cycleStart.SelectedDate.Value.DateTime) : null;
        ph.AccreditationExpiryDate = _expiry.SelectedDate.HasValue ? DateOnly.FromDateTime(_expiry.SelectedDate.Value.DateTime) : null;
        ph.OnsiteAssessmentDate = _onsite.SelectedDate.HasValue ? DateOnly.FromDateTime(_onsite.SelectedDate.Value.DateTime) : null;
        App.Db.SaveChanges();
        App.Audit.Append(App.Session?.DisplayName ?? "?", App.Session?.UserId, "pharmacy.profile", "pharmacy", ph.Id.ToString(), $"name={ph.Name}; scopeFlags={flags}; cycleStart={ph.CycleStartDate:O}; expiry={ph.AccreditationExpiryDate:O}; onsite={ph.OnsiteAssessmentDate:O}");
        _message.Text = "Profile saved. Deadlines below refresh immediately; planning markers remain clearly labelled.";
        LoadDeadlines();
    }

    private async void AddUser()
    {
        var userName = await Dialogs.Prompt(this, "Add user — user name", "Login name");
        if (string.IsNullOrWhiteSpace(userName)) return;
        var displayName = await Dialogs.Prompt(this, "Add user — full name", "Display name") ?? userName;
        var roleRaw = await Dialogs.Prompt(this, "Add user — role", "OwnerAdmin, ManagerReviewer, Contributor or ReviewerReadOnly", "Contributor");
        if (!Enum.TryParse<UserRole>(roleRaw, true, out var role))
        { _message.Text = "Role must be one of: OwnerAdmin, ManagerReviewer, Contributor, ReviewerReadOnly."; return; }
        var passphrase = await Dialogs.Prompt(this, "Add user — passphrase", "At least 8 characters");
        if (string.IsNullOrWhiteSpace(passphrase)) return;
        var (result, _) = App.Auth.AddUser(App.Session?.DisplayName ?? "?", App.Session?.UserId ?? Guid.Empty, userName, displayName, role, passphrase);
        _message.Text = result.Ok ? $"User '{userName}' added with role {role}." : result.Error;
        LoadUsers();
    }

    private async Task Backup()
    {
        var label = await Dialogs.Prompt(this, "Backup", "Label for this backup (optional)") ?? "";
        var svc = new QsppManager.Infrastructure.Backup.BackupService(App.Db, App.Store, AppData.BackupsDir);
        var (ok, zipPath, error, shaHead) = svc.CreateBackup(label);
        _message.Text = ok
            ? $"Backup written (zip): {zipPath}\nsha256 {shaHead}… (database at backup time). Restores validate every file hash before extracting anything."
            : $"Backup failed: {error}. Nothing was corrupted — the live data was only read.";
        return;
    }

    private async Task RestoreAsync()
    {
        var tl = TopLevel.GetTopLevel(this);
        if (tl == null) return;
        var files = await tl.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false, Title = "Choose a Hugh's QSPP Manager backup (.zip)",
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("QSPP backup") { Patterns = ["*.zip"] }],
        });
        if (files.Count == 0) return;
        var zipPath = files[0].Path.LocalPath;
        var confirm = await Dialogs.Prompt(this, "Restore", $"Restore from {Path.GetFileName(zipPath)}?\nType RESTORE to continue. The app will close after a verified restore — the restored copy replaces the data on next launch.");
        if (confirm != "RESTORE") { _message.Text = "Restore cancelled — nothing changed."; return; }
        var svc = new QsppManager.Infrastructure.Backup.BackupService(App.Db, App.Store, AppData.BackupsDir);
        var restoreDir = AppData.RestoreStaging();
        var (ok, error, restoredDb, restoredFiles) = svc.Restore(zipPath, restoreDir);
        if (!ok)
        {
            _message.Text = $"RESTORE REFUSED: {error}\nThe current data is untouched.";
            return;
        }
        _message.Text = $"Verified and extracted {restoredFiles} files into {restoreDir}. Restore completes after app restart.";
        await Dialogs.Prompt(this, "Restore ready", $"Files verified and extracted to {restoreDir}. Close the app now; on next launch it uses the restored copy. (Data dir swap is logged in the audit trail on restart.)");
    }
}