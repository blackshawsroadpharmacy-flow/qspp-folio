using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Services;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

/// <summary>People & training: staff register (not app users), S2/S3 training status per the
/// fact-sheet rules, and version-tied policy acknowledgements.</summary>
public partial class PeopleView : UserControl
{
    private readonly DataGrid _staff = new() { Height = 220, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _acks = new() { Height = 180, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };

    public PeopleView()
    {
        Content = Build();
        Load();
    }

    private Control Build()
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 12, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "People & training" });
        sp.Children.Add(new TextBlock { Classes = { "lead" }, TextWrapping = TextWrapping.Wrap,
            Text = "The staff register and each person's Schedule 2/3 training position. Initial training is due within six months of first supply; refresher runs on the anniversary of the last completed training (initial or refresher). Acknowledgements are recorded per policy VERSION." });

        sp.Children.Add(new TextBlock { Text = "Staff & S2/S3 training", Classes = { "h3" } });
        _staff.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(200) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Avalonia.Data.Binding("Role"), Width = new DataGridLength(150) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "First supply", Binding = new Avalonia.Data.Binding("FirstSupply"), Width = new DataGridLength(110) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "Initial status", Binding = new Avalonia.Data.Binding("InitialStatus"), Width = new DataGridLength(280) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "Refresher status", Binding = new Avalonia.Data.Binding("RefresherStatus"), Width = new DataGridLength(300) });
        sp.Children.Add(_staff);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Button { Content = "Add staff member…", Classes = { "primary" }, Height = 34 }.With(b => b.Click += async (_, _) => await AddStaffAsync()));
        row.Children.Add(new Button { Content = "Record training…", Height = 34 }.With(b => b.Click += async (_, _) => await RecordTrainingAsync()));
        sp.Children.Add(row);

        sp.Children.Add(new TextBlock { Text = "Policy acknowledgements", Classes = { "h3" }, Margin = new(0, 8, 0, 0) });
        _acks.Columns.Add(new DataGridTextColumn { Header = "Staff", Binding = new Avalonia.Data.Binding("Staff"), Width = new DataGridLength(200) });
        _acks.Columns.Add(new DataGridTextColumn { Header = "Policy", Binding = new Avalonia.Data.Binding("Policy"), Width = new DataGridLength(340) });
        _acks.Columns.Add(new DataGridTextColumn { Header = "Version", Binding = new Avalonia.Data.Binding("Version"), Width = new DataGridLength(120) });
        _acks.Columns.Add(new DataGridTextColumn { Header = "Acknowledged", Binding = new Avalonia.Data.Binding("On"), Width = new DataGridLength(140) });
        sp.Children.Add(_acks);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row2.Children.Add(new Button { Content = "Record acknowledgement…", Classes = { "primary" }, Height = 34 }.With(b => b.Click += async (_, _) => await AckAsync()));
        sp.Children.Add(row2);

        sp.Children.Add(_message);
        return new ScrollViewer { Content = sp };
    }

    private void Load()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _staff.ItemsSource = App.Workspace.Staff().Select(s =>
        {
            var (iDue, iStatus) = TrainingRules.InitialStatus(s, today);
            var (rDue, rStatus) = TrainingRules.RefresherStatus(s, today);
            return new StaffRow(s.Id, s.Name, s.Role,
                s.S2S3FirstSupplyDate?.ToString("dd MMM yyyy") ?? "—", $"{iStatus}{(iDue.HasValue ? $" (by {iDue:dd MMM yyyy})" : "")}", rStatus);
        }).ToList();
        _acks.ItemsSource = App.Workspace.Acknowledgements().Select(a => new AckRow(
            App.Workspace.Staff().First(s => s.Id == a.StaffMemberId).Name,
            a.PolicyName, a.PolicyVersion, a.AcknowledgedOn.ToString("dd MMM yyyy"))).ToList();
    }

    private async Task AddStaffAsync()
    {
        var name = await Dialogs.Prompt(this, "Add staff member", "Name");
        if (string.IsNullOrWhiteSpace(name)) return;
        var role = await Dialogs.Prompt(this, "Role in the pharmacy", "Role (e.g. Pharmacist Assistant)") ?? "";
        var res = App.Workspace.AddStaffMember(name, role, App.Session!);
        _message.Text = res.Ok ? "Staff member added." : res.Error;
        Load();
    }

    private async Task RecordTrainingAsync()
    {
        if (_staff.SelectedItem is not StaffRow row) { _message.Text = "Select a staff member first."; return; }
        var dlg = new TrainingDialog();
        if (!await dlg.Show(this)) return;
        var res = App.Workspace.SetStaffTraining(row.Id, dlg.FirstSupply, dlg.Initial, dlg.Refresher, App.Session!);
        _message.Text = res.Ok ? "Training record updated." : res.Error;
        Load();
    }

    private async Task AckAsync()
    {
        if (_staff.SelectedItem is not StaffRow row) { _message.Text = "Select a staff member first."; return; }
        var policy = await Dialogs.Prompt(this, "Record acknowledgement", "Policy name (e.g. QSPP Assessments Policy)");
        if (string.IsNullOrWhiteSpace(policy)) return;
        var version = await Dialogs.Prompt(this, "Record acknowledgement", "Policy version (from the document itself)");
        if (string.IsNullOrWhiteSpace(version)) { _message.Text = "The version matters — acknowledgement without a version can't be checked later."; return; }
        var res = App.Workspace.AcknowledgePolicy(row.Id, policy, version, null, App.Session!);
        _message.Text = res.Ok ? "Acknowledgement recorded against that policy version." : res.Error;
        Load();
    }

    public record StaffRow(Guid Id, string Name, string Role, string FirstSupply, string InitialStatus, string RefresherStatus);
    public record AckRow(string Staff, string Policy, string Version, string On);
}