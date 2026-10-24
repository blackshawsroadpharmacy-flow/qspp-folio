using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
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
    private readonly DataGrid _staff = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _acks = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly NoticeHost _resultBar = new();
    private OneLineHost _staffHost = null!;
    private OneLineHost _acksHost = null!;

    /// <summary>Grid host (§5): content-height grid under a cap, a one-line empty state, no
    /// fixed-height void around either.</summary>
    private sealed record OneLineHost(Control Scroll, Control Empty, Panel Panel)
    {
        public void Refresh(int rowCount)
        {
            Scroll.IsVisible = rowCount > 0;
            Empty.IsVisible = rowCount == 0;
        }
    }

    private static OneLineHost Host(DataGrid grid, string line)
    {
        var empty = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(4, 6), IsVisible = false,
            Children =
            {
                new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Ui.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Classes = { "small" }, Foreground = Ui.Br("Brush.Muted"), Text = line, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var scroll = new ScrollViewer { Content = grid, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new Panel();
        panel.Children.Add(scroll);
        panel.Children.Add(empty);
        return new OneLineHost(scroll, empty, panel);
    }

    private static StackPanel Strip(params Control[] actions)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var a in actions)
        {
            a.Margin = new(0, 0, 8, 0);
            a.VerticalAlignment = VerticalAlignment.Center;
            wrap.Children.Add(a);
        }
        return new StackPanel { Children = { wrap }, IsVisible = false };
    }

    public PeopleView()
    {
        Content = Build();
        Load();
    }

    private Control Build()
    {
        var sp = new StackPanel { Margin = new(28, 24), Spacing = 12, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.People, "The staff register and each person's Schedule 2/3 training position.", "Initial training is due within six months of first supply; refresher runs on the anniversary of the last completed training (initial or refresher). Acknowledgements are recorded per policy VERSION."));

        _staff.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(200) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "Role", Binding = new Avalonia.Data.Binding("Role"), Width = new DataGridLength(150) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "First supply", Binding = new Avalonia.Data.Binding("FirstSupply"), Width = new DataGridLength(110) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "Initial status", Binding = new Avalonia.Data.Binding("InitialStatus"), Width = new DataGridLength(280) });
        _staff.Columns.Add(new DataGridTextColumn { Header = "Refresher status", Binding = new Avalonia.Data.Binding("RefresherStatus"), Width = new DataGridLength(300) });
        Ui.Ax(_staff, "Staff members", "Select a staff member to record training or acknowledgements");
        _staffHost = Host(_staff, "No staff recorded yet. Add staff member… starts the register.");
        var staffStrip = Strip(
            Btn("Record training…", "Record Schedule 2/3 training for the selected staff member",
                async (_, _) => await RecordTrainingAsync()));
        _staff.SelectionChanged += (_, _) => staffStrip.IsVisible = _staff.SelectedItem is not null;
        var staffTrailing = Btn("Add staff member…", "Add a staff member to the register (app users are managed elsewhere)",
            async (_, _) => await AddStaffAsync());
        sp.Children.Add(Ui.Section("Staff & S2/S3 training", Ui.Col(8, _staffHost.Panel, staffStrip), staffTrailing));

        _acks.Columns.Add(new DataGridTextColumn { Header = "Staff", Binding = new Avalonia.Data.Binding("Staff"), Width = new DataGridLength(200) });
        _acks.Columns.Add(new DataGridTextColumn { Header = "Policy", Binding = new Avalonia.Data.Binding("Policy"), Width = new DataGridLength(340) });
        _acks.Columns.Add(new DataGridTextColumn { Header = "Version", Binding = new Avalonia.Data.Binding("Version"), Width = new DataGridLength(120) });
        _acks.Columns.Add(new DataGridTextColumn { Header = "Acknowledged", Binding = new Avalonia.Data.Binding("On"), Width = new DataGridLength(140) });
        Ui.Ax(_acks, "Policy acknowledgements", "Version-tied acknowledgements of pharmacy policies");
        _acksHost = Host(_acks, "No acknowledgements recorded. Record acknowledgement… ties each policy version to a person.");
        var ackBtn = Btn("Record acknowledgement…", "Record a version-tied policy acknowledgement for the selected staff member",
            async (_, _) => await AckAsync());
        sp.Children.Add(Ui.Section("Policy acknowledgements", Ui.Col(8, _acksHost.Panel), ackBtn));

        sp.Children.Add(_resultBar);
        return new ScrollViewer { Content = sp };
    }

    private void Load()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var staffRows = App.Workspace.Staff().Select(s =>
        {
            var (iDue, iStatus) = TrainingRules.InitialStatus(s, today);
            var (rDue, rStatus) = TrainingRules.RefresherStatus(s, today);
            return new StaffRow(s.Id, s.Name, s.Role,
                s.S2S3FirstSupplyDate?.ToString("dd MMM yyyy") ?? "—", $"{iStatus}{(iDue.HasValue ? $" (by {iDue:dd MMM yyyy})" : "")}", rStatus);
        }).ToList();
        _staff.ItemsSource = staffRows;
        _staffHost.Refresh(staffRows.Count);

        var ackRows = App.Workspace.Acknowledgements().Select(a => new AckRow(
            App.Workspace.Staff().First(s => s.Id == a.StaffMemberId).Name,
            a.PolicyName, a.PolicyVersion, a.AcknowledgedOn.ToString("dd MMM yyyy"))).ToList();
        _acks.ItemsSource = ackRows;
        _acksHost.Refresh(ackRows.Count);
    }

    private async Task AddStaffAsync()
    {
        var name = await Dialogs.Prompt(this, "Add staff member", "Name");
        if (string.IsNullOrWhiteSpace(name)) return;
        var role = await Dialogs.Prompt(this, "Role in the pharmacy", "Role (e.g. Pharmacist Assistant)") ?? "";
        var res = App.Workspace.AddStaffMember(name, role, App.Session!);
                _resultBar.Show(res.Ok, "Staff member added.", res.Error);
        Load();
    }

    private async Task RecordTrainingAsync()
    {
                if (_staff.SelectedItem is not StaffRow row) { _resultBar.Show(NoticeTone.Info, "Select a staff member first."); return; }
        var dlg = new TrainingDialog();
        if (!await dlg.Show(this)) return;
        var res = App.Workspace.SetStaffTraining(row.Id, dlg.FirstSupply, dlg.Initial, dlg.Refresher, App.Session!);
                _resultBar.Show(res.Ok, "Training record updated.", res.Error);
        Load();
    }

    private async Task AckAsync()
    {
                if (_staff.SelectedItem is not StaffRow row) { _resultBar.Show(NoticeTone.Info, "Select a staff member first."); return; }
        var policy = await Dialogs.Prompt(this, "Record acknowledgement", "Policy name (e.g. QSPP Assessments Policy)");
        if (string.IsNullOrWhiteSpace(policy)) return;
        var version = await Dialogs.Prompt(this, "Record acknowledgement", "Policy version (from the document itself)");
                if (string.IsNullOrWhiteSpace(version)) { _resultBar.Show(NoticeTone.Info, "The version matters; acknowledgement without a version can't be checked later."); return; }
        var res = App.Workspace.AcknowledgePolicy(row.Id, policy, version, null, App.Session!);
                _resultBar.Show(res.Ok, "Acknowledgement recorded against that policy version.", res.Error);
        Load();
    }

    public record StaffRow(Guid Id, string Name, string Role, string FirstSupply, string InitialStatus, string RefresherStatus);
    public record AckRow(string Staff, string Policy, string Version, string On);

    // ---------------- design-system helpers (local, view-level) ----------------
    private static Button Btn(string label, string axHelp, EventHandler<RoutedEventArgs> onClick, string classes = "")
    {
        var b = new Button { Content = label };
        foreach (var cls in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(cls);
        Ui.Ax(b, label, axHelp);
        b.Click += onClick;
        return b;
    }
    private static Button PrimaryBtn(string label, string axHelp, EventHandler<RoutedEventArgs> onClick) => Btn(label, axHelp, onClick, "primary");

    /// <summary>Table with an in-place empty state; the visible child tracks the row count.</summary>
    private static TableHost Host(Control table, double minHeight, string icon, string title, string hint, string hue = "Brush.Stone")
    {
        var empty = Ui.EmptyState(icon, title, hint, null, hue);
        empty.IsVisible = false;
        var panel = new Panel { MinHeight = minHeight };
        panel.Children.Add(table);
        panel.Children.Add(empty);
        return new TableHost(table, empty, panel);
    }

    private sealed record TableHost(Control Table, Control Empty, Panel Panel)
    {
        public void Refresh(int rowCount)
        {
            Table.IsVisible = rowCount > 0;
            Empty.IsVisible = rowCount == 0;
        }
    }
}