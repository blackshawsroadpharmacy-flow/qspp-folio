using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain;
using QsppManager.Domain.Entities;
using QsppManager.Domain.Services;

namespace QsppManager.Desktop.Views;

/// <summary>Registers: recurring schedules (the durable definitions), the equipment register
/// with calibration dates (2.1.1.4b–c territory) and the incident register (1.1.2.4).
/// Schedule occurrences materialise as ordinary tasks; misses stay visible.</summary>
public partial class RegistersView : UserControl
{
    private readonly DataGrid _defs = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _equipment = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _incidents = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _suppliers = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 32 };
    private readonly DataGrid _recalls = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 32 };
    private readonly DataGrid _potency = new() { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 32 };
    private readonly TextBlock _potencyNote = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
    // Result strip per contract rule 8 (was: bare TextBlock feedback line).
    private readonly NoticeHost _resultBar = new();
    private OneLineHost _defsHost = null!;
    private OneLineHost _equipmentHost = null!;
    private OneLineHost _incidentsHost = null!;
    private OneLineHost _suppliersHost = null!;
    private OneLineHost _recallsHost = null!;
    private OneLineHost _potencyHost = null!;
    private readonly List<Control> _sections = [];
    private readonly List<Button> _regButtons = [];
    private readonly List<(TextBlock Label, string Noun)> _headerCounts = [];
    private int _segment;

    /// <summary>Grid host for one register (§5): the grid sizes to its content up to a cap,
    /// the empty state is one quiet line, and no fixed-height void surrounds either.</summary>
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

    public RegistersView()
    {
        Content = Build();
        Load();
        SetRegister(0);
    }

    /// <summary>The register switch (§5): one register shows at a time; the cells carry
    /// per-register counts in the section headers beneath.</summary>
    private static readonly string[] _registerLabels = ["Schedules", "Equipment", "Incidents", "Suppliers", "Recalls", "Potency"];

    private Control SegmentedRegisterSwitch()
    {
        var host = new Border
        {
            CornerRadius = new(6),
            BorderBrush = Ui.Br("Brush.ControlBorder"),
            BorderThickness = new(1),
            Background = Ui.Br("Brush.Surface"),
            Padding = new(2, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var segRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < _registerLabels.Length; i++)
        {
            if (i > 0)
                segRow.Children.Add(new Border { Width = 1, Margin = new(1, 5), VerticalAlignment = VerticalAlignment.Stretch, Background = Ui.Br("Brush.Divider") });
            var index = i;
            var b = new Button { Height = 24, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.Medium, Padding = new(9, 0), Content = _registerLabels[i] };
            b.Classes.Add("segcell");
            b.Click += (_, _) => SetRegister(index);
            _regButtons.Add(b);
            segRow.Children.Add(b);
        }
        host.Child = segRow;
        return host;
    }

    private void SetRegister(int i)
    {
        _segment = i;
        for (var k = 0; k < _regButtons.Count; k++)
        {
            if (k == _segment) _regButtons[k].Classes.Add("on"); else _regButtons[k].Classes.Remove("on");
        }
        for (var k = 0; k < _sections.Count; k++) _sections[k].IsVisible = k == _segment;
    }

    private Control Build()
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 12, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.Registers, "Recurring obligations, the equipment register and the incident register.", "Schedules materialise as ordinary tasks with due dates; missed occurrences stay visible until someone does them, and generation never duplicates. Equipment calibration dates automatically get a monthly check schedule."));
        sp.Children.Add(_resultBar);
        sp.Children.Add(SegmentedRegisterSwitch());

        var first = RecurringSection();
        sp.Children.Add(first);
        _sections.Add(first);
        var second = EquipmentSection();
        sp.Children.Add(second);
        _sections.Add(second);
        var third = IncidentSection();
        sp.Children.Add(third);
        _sections.Add(third);
        var fourth = SuppliersSection();
        sp.Children.Add(fourth);
        _sections.Add(fourth);
        var fifth = RecallsSection();
        sp.Children.Add(fifth);
        _sections.Add(fifth);
        var sixth = PotencySection();
        sp.Children.Add(sixth);
        _sections.Add(sixth);
        return new ScrollViewer { Content = sp };
    }

    private Control RecurringSection()
    {
        _defs.Columns.Add(Ui.Col("Schedule", "Title", 340));
        _defs.Columns.Add(Ui.Col("Cadence", "Cadence", 100));
        _defs.Columns.Add(Ui.Col("From", "Anchor", 110));
        _defs.Columns.Add(Ui.Col("Owner", "Owner", 150));
        _defs.Columns.Add(Ui.Col("Equipment", "EquipmentName", 180));
        _defs.Columns.Add(Ui.Col("Status", "Status", 90));
        Ui.Ax(_defs, "Recurring schedules", "Pause, resume or generate occurrences for recurring work");
        _defsHost = Host(_defs, "No recurring schedules yet. Add schedule\u2026 creates the first; occurrences appear as ordinary tasks with due dates.");
        var strip = Strip(
            Ui.Btn("Pause").With(b => b.Click += (_, _) => ToggleDef(false)),
            Ui.Btn("Resume").With(b => b.Click += (_, _) => ToggleDef(true)));
        return Ui.Section("Recurring schedules", Ui.Col(10, _defsHost.Panel, strip),
            trailing: HeaderTrailing("schedules", Ui.Btn("Generate occurrences").With(b => b.Click += (_, _) => GenerateOccurrences()),
                Ui.Btn("Add schedule\u2026").With(b => b.Click += async (_, _) => { await NewScheduleAsync(); Load(); })));
    }

    private void GenerateOccurrences()
    {
        var n = App.Workspace.GenerateRecurringOccurrences(session: App.Session);
        _resultBar.Show(n > 0 ? NoticeTone.Ok : NoticeTone.Info, n > 0 ? $"{n} occurrence(s) generated (idempotent: reopening never duplicates)." : "Nothing new to generate.");
        Load();
    }

    /// <summary>The header trailing group (§5): the live count and the register's Add… and
    /// register-level actions, right-aligned on the section header row.</summary>
    private Control HeaderTrailing(string noun, params Control[] buttons)
    {
        var count = new TextBlock { Classes = { "small" }, Foreground = Ui.Br("Brush.Muted"), VerticalAlignment = VerticalAlignment.Center };
        _headerCounts.Add((count, noun));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(count);
        foreach (var x in buttons)
        {
            x.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(x);
        }
        return row;
    }

    /// <summary>The selection-only action strip (§5): quiet buttons in one wrapping row that
    /// shows itself only while a row is selected.</summary>
    private StackPanel Strip(params Control[] actions)
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

    private Control EquipmentSection()
    {
        _equipment.Columns.Add(Ui.Col("Name", "Name", 260));
        _equipment.Columns.Add(Ui.Col("Model", "Model", 150));
        _equipment.Columns.Add(Ui.Col("Serial", "Serial", 140));
        _equipment.Columns.Add(Ui.Col("Location", "Location", 160));
        _equipment.Columns.Add(Ui.Col("State", "State", 110));
        _equipment.Columns.Add(Ui.Col("Calibration due", "Calibration", 130));
        Ui.Ax(_equipment, "Equipment register", "Calibration dates and service state for each instrument");
        _equipmentHost = Host(_equipment, "No equipment recorded. Add equipment…; calibration dates create their monthly check schedules.");
        // 3F #4: actions read only when a row is selected; Retire lives in the strip's … menu
        // with its confirm sheet — never a permanent filled red button.
        var strip = Strip(
            Ui.Btn("Set calibration due…").With(b => b.Click += async (_, _) => await CalibrationAsync()),
            Ui.Btn("Send to maintenance").With(b => b.Click += (_, _) => SetState("Maintenance")),
            Ui.Btn("Back in service").With(b => b.Click += (_, _) => SetState("InService")));
        var more = new Button { Classes = { "ghost" }, MinWidth = 32, Content = "…" };
        Avalonia.Automation.AutomationProperties.SetAutomationId(more, "EquipmentMoreMenu");
        var retire = new MenuItem { Header = "Retire…" };
        retire.Click += async (_, _) =>
        {
            // Premium-polish 3.5: destructive actions confirm first — never a silent one-click retire.
            if (!await Dialogs.Confirm(this, "Retire", "Retire this equipment? Retired instruments stop contributing to readiness until brought back into service."))
                return;
            SetState("Retired");
        };
        more.Flyout = new Flyout { Content = new StackPanel { Children = { retire } } };
        strip.Children.Add(more);
        _equipment.SelectionChanged += (_, _) => strip.IsVisible = _equipment.SelectedItem is not null;
        return Ui.Section("Equipment register", Ui.Col(10, _equipmentHost.Panel, strip),
            trailing: HeaderTrailing("instruments", Ui.Btn("Add equipment…").With(b => b.Click += async (_, _) => await AddEquipmentAsync())));
    }

    private Control IncidentSection()
    {
        _incidents.Columns.Add(Ui.Col("Date", "DateLabel", 110));
        _incidents.Columns.Add(Ui.Col("Severity", "Severity", 90));
        _incidents.Columns.Add(Ui.Col("Description", "Description", 360));
        _incidents.Columns.Add(Ui.Col("State", "State", 90));
        _incidents.Columns.Add(Ui.Col("Corrective action", "Action", 280));
        _incidents.Columns.Add(Ui.Col("Reported by", "By", 120));
        Ui.Ax(_incidents, "Incident register", "Reported incidents with their corrective actions");
        _incidentsHost = Host(_incidents, "No incidents recorded. Report incident… links each to the reporting requirement 1.1.2.4.");
        var strip = Strip(
            Ui.Btn("Close with corrective action…").With(b => b.Click += async (_, _) => await CloseIncidentAsync()));
        _incidents.SelectionChanged += (_, _) => strip.IsVisible = _incidents.SelectedItem is not null;
        return Ui.Section("Incident register", Ui.Col(10,
            _incidentsHost.Panel, strip,
            new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
                Text = "Incidents link to requirement 1.1.2.4 (reporting to a person responsible) so the register is reachable from the clause. An incident cannot close without a recorded corrective action." }),
            trailing: HeaderTrailing("incidents", Ui.Btn("Report incident…").With(b => b.Click += async (_, _) => await ReportIncidentAsync())));
    }

    private Control SuppliersSection()
    {
        _suppliers.Columns.Add(Ui.Col("Supplier", "Name", 240));
        _suppliers.Columns.Add(Ui.Col("Supplies", "Supplies", 300));
        _suppliers.Columns.Add(Ui.Col("Registration note", "Note", 280));
        _suppliers.Columns.Add(Ui.Col("Last reviewed", "Reviewed", 120));
        Ui.Ax(_suppliers, "Registered suppliers", "Compounding suppliers and registration review dates");
        _suppliersHost = Host(_suppliers, "No suppliers recorded. Add supplier… with the registration detail you verified.");
        var strip = Strip(
            Ui.Btn("Mark reviewed today").With(b => b.Click += (_, _) =>
            {
                if (_suppliers.SelectedItem is not SupRow s) { _resultBar.Show(NoticeTone.Info, "Select a supplier first."); return; }
                var res = App.Workspace.SetSupplierReviewed(s.Id, null, DateOnly.FromDateTime(DateTime.Today), App.Session!);
                _resultBar.Show(res.Ok, "Supplier review date recorded.", res.Error);
                Load();
            }));
        _suppliers.SelectionChanged += (_, _) => strip.IsVisible = _suppliers.SelectedItem is not null;
        return Ui.Section("Compounding · registered suppliers", Ui.Col(10, _suppliersHost.Panel, strip),
            trailing: HeaderTrailing("suppliers", Ui.Btn("Add supplier…").With(b => b.Click += async (_, _) =>
            {
                var name = await Dialogs.Prompt(this, "Add supplier", "Supplier name");
                if (string.IsNullOrWhiteSpace(name)) return;
                var supplies = await Dialogs.Prompt(this, "Add supplier", "What you buy from them") ?? "";
                var note = await Dialogs.Prompt(this, "Add supplier", "Registration/licence note you verified") ?? "";
                var res = App.Workspace.AddSupplier(name, supplies, note, App.Session!);
                _resultBar.Show(res.Ok, "Supplier added (2.2.3.3 sourcing register).", res.Error);
                Load();
            })));
    }

    private Control RecallsSection()
    {
        _recalls.Columns.Add(Ui.Col("Product", "Product", 280));
        _recalls.Columns.Add(Ui.Col("Batch", "Batch", 140));
        _recalls.Columns.Add(Ui.Col("Received", "Received", 110));
        _recalls.Columns.Add(Ui.Col("Action taken", "Action", 320));
        _recalls.Columns.Add(Ui.Col("State", "State", 80));
        Ui.Ax(_recalls, "Product recalls", "Recalls received from suppliers and the stock action taken");
        _recallsHost = Host(_recalls, "No recalls recorded. Record recall… and close each one with the stock action taken.");
        var strip = Strip(
            Ui.Btn("Close recall…").With(b => b.Click += async (_, _) =>
            {
                if (_recalls.SelectedItem is not RecRow r) { _resultBar.Show(NoticeTone.Info, "Select a recall first."); return; }
                var action = await Dialogs.Prompt(this, "Close recall", "What was done with the affected stock (quarantined/returned/destroyed)?");
                var res = App.Workspace.CloseRecall(r.Id, action ?? "", App.Session!);
                _resultBar.Show(res.Ok, "Recall closed with the stock action recorded.", res.Error);
                Load();
            }));
        _recalls.SelectionChanged += (_, _) => strip.IsVisible = _recalls.SelectedItem is not null;
        return Ui.Section("Compounding · product recalls", Ui.Col(10, _recallsHost.Panel, strip),
            trailing: HeaderTrailing("recalls", Ui.Btn("Record recall…").With(b => b.Click += async (_, _) =>
            {
                var product = await Dialogs.Prompt(this, "Record recall", "Product name");
                if (string.IsNullOrWhiteSpace(product)) return;
                var batch = await Dialogs.Prompt(this, "Record recall", "Batch / lot") ?? "";
                var received = await Dialogs.PromptDate(this, "Record recall", "Date received (blank = today)")
                               ?? DateOnly.FromDateTime(DateTime.Today);
                var res = App.Workspace.AddRecall(product, batch, received, "", App.Session!);
                _resultBar.Show(res.Ok, "Recall recorded (open). Close it with the stock action once resolved.", res.Error);
                Load();
            })));
    }

    private Control PotencySection()
    {
        _potency.Columns.Add(Ui.Col("Person", "Name", 190));
        _potency.Columns.Add(Ui.Col("Distinct drugs this year", "Drugs", 230));
        _potency.Columns.Add(Ui.Col("Distinct dosage forms", "Forms", 190));
        _potency.Columns.Add(Ui.Col("Coverage", "Status", 420));
        Ui.Ax(_potency, "Potency testing coverage", "Per-person compounding coverage matrix");
        _potencyHost = Host(_potency, "No potency records yet. Record a potency test… per person to build the coverage matrix.");
        return Ui.Section("Compounding · potency testing coverage (per person)", Ui.Col(10, _potencyHost.Panel, _potencyNote),
            trailing: HeaderTrailing("people", Ui.Btn("Record a potency test…").With(b => b.Click += async (_, _) =>
            {
                var dlg = new PotencyDialog();
                if (!await dlg.Show(this)) return;
                var res = App.Workspace.AddPotencyRecord(dlg.StaffId, dlg.Role, dlg.Drug, dlg.Form, dlg.TestedOn, dlg.Outcome, App.Session!);
                _resultBar.Show(res.Ok, "Potency record added to the per-person matrix.", res.Error);
                Load();
            })));
    }

    private void Load()
    {
        App.Workspace.GenerateRecurringOccurrences(session: App.Session); // idempotent
        var users = App.Auth.ListUsers().ToDictionary(u => u.Id, u => u.DisplayName);
        var eqById = App.Workspace.Equipment().ToDictionary(e => e.Id, e => e.Name);
        _defs.ItemsSource = App.Workspace.RecurringDefs().Select(d => new DefRow(
            d.Id, d.Title, d.Cadence == RecurrenceCadence.CustomDays ? $"every {d.CustomDays} days" : d.Cadence.ToString(),
            d.AnchorDate.ToString("dd MMM yyyy"),
            users.GetValueOrDefault(d.AssignedToUserId, d.AssignedToUserId == Guid.Empty ? "(unassigned)" : "(unknown)"),
            d.EquipmentId.HasValue ? eqById.GetValueOrDefault(d.EquipmentId.Value, "?") : "",
            d.Active ? "active" : "paused")).ToList();
        _equipment.ItemsSource = App.Workspace.Equipment().Select(e => new EqRow(
            e.Id, e.Name, e.Model, e.Serial, e.Location, e.State, e.CalibrationDue?.ToString("dd MMM yyyy") ?? "—")).ToList();
        _incidents.ItemsSource = App.Workspace.Incidents().Select(i => new IncRow(
            i.Id, i.Date.ToLocalTime().ToString("dd MMM yyyy"), i.Severity, i.Description, i.State, i.CorrectiveAction, i.ReportedBy)).ToList();
        _suppliers.ItemsSource = App.Workspace.Suppliers().Select(s => new SupRow(
            s.Id, s.Name, s.Supplies, s.RegistrationNote, s.LastReviewed?.ToString("dd MMM yyyy") ?? "—")).ToList();
        _recalls.ItemsSource = App.Workspace.Recalls().Select(r => new RecRow(
            r.Id, r.Product, r.Batch, r.ReceivedDate.ToString("dd MMM yyyy"), r.Action, r.State)).ToList();
        var (potRows, potNoteText) = App.Workspace.PotencyCoverage();
        _potency.ItemsSource = potRows.Select(p => new PotRow(p.name, string.Join(", ", p.drugs) is var dl && dl.Length == 0 ? "—" : dl, string.Join(", ", p.forms) is var fl && fl.Length == 0 ? "—" : fl, p.status)).ToList();
        _potencyNote.Text = potNoteText;
        _defsHost.Refresh(_defs.ItemsSource?.Cast<object>().Count() ?? 0);
        _equipmentHost.Refresh(_equipment.ItemsSource?.Cast<object>().Count() ?? 0);
        _incidentsHost.Refresh(_incidents.ItemsSource?.Cast<object>().Count() ?? 0);
        _suppliersHost.Refresh(_suppliers.ItemsSource?.Cast<object>().Count() ?? 0);
        _recallsHost.Refresh(_recalls.ItemsSource?.Cast<object>().Count() ?? 0);
        _potencyHost.Refresh(_potency.ItemsSource?.Cast<object>().Count() ?? 0);
        UpdateHeaderCounts();
    }

    /// <summary>Header counts read "3 schedules" next to the Add… button; quiet when empty
    /// (the empty line carries it).</summary>
    private void UpdateHeaderCounts()
    {
        var grids = new[] { _defs, _equipment, _incidents, _suppliers, _recalls, _potency };
        for (var i = 0; i < _headerCounts.Count; i++)
        {
            var n = grids[i].ItemsSource?.Cast<object>().Count() ?? 0;
            _headerCounts[i].Label.Text = n == 0 ? "" : $"{n} {_headerCounts[i].Noun}";
        }
    }

    private async Task NewScheduleAsync()
    {
        var dlg = new ScheduleDialog();
        if (!await dlg.Show(this)) return;
        var res = App.Workspace.CreateRecurringDef(dlg.TitleText, dlg.Details, null, null,
            dlg.AssignedTo, dlg.Cadence, dlg.CustomDays, dlg.Anchor, TaskPriority.Normal, App.Session!);
                _resultBar.Show(res.Ok, "Schedule created; occurrences generate on refresh, and via 'Generate occurrences now'.", res.Error);
        Load();
    }

    private void ToggleDef(bool active)
    {
                if (_defs.SelectedItem is not DefRow row) { _resultBar.Show(NoticeTone.Info, "Select a schedule first."); return; }
        var res = App.Workspace.SetRecurringDefActive(row.Id, active, App.Session!);
                _resultBar.Show(res.Ok, (active ? "Schedule resumed." : "Schedule paused; no further occurrences generate until resumed."), res.Error);
        Load();
    }

    private async Task AddEquipmentAsync()
    {
        var name = await Dialogs.Prompt(this, "Equipment name", "Name");
        if (string.IsNullOrWhiteSpace(name)) return;
        var model = await Dialogs.Prompt(this, "Model", "Model") ?? "";
        var serial = await Dialogs.Prompt(this, "Serial number", "Serial") ?? "";
        var location = await Dialogs.Prompt(this, "Location in the pharmacy", "Location") ?? "";
        var res = App.Workspace.AddEquipment(name, model, serial, location, "", App.Session!);
                _resultBar.Show(res.Ok, "Equipment added to the register.", res.Error);
        Load();
    }

    private async Task CalibrationAsync()
    {
                if (_equipment.SelectedItem is not EqRow row) { _resultBar.Show(NoticeTone.Info, "Select equipment first."); return; }
        var due = await Dialogs.PromptDate(this, $"Calibration due · {row.Name}", "Due date", required: true);
        if (due is null) return;
        var res = App.Workspace.SetCalibrationDue(row.Id, due.Value, App.Session!);
                _resultBar.Show(res.Ok, "Calibration date recorded; a monthly check schedule now exists for this equipment.", res.Error);
        Load();
    }

    private void SetState(string state)
    {
                if (_equipment.SelectedItem is not EqRow row) { _resultBar.Show(NoticeTone.Info, "Select equipment first."); return; }
        var res = App.Workspace.SetEquipmentState(row.Id, state, App.Session!);
                _resultBar.Show(res.Ok, $"Equipment marked {state}.", res.Error);
        Load();
    }

    private async Task ReportIncidentAsync()
    {
        var dlg = new IncidentDialog();
        if (!await dlg.Show(this)) return;
        var res = App.Workspace.AddIncident(dlg.Date, dlg.Severity, dlg.Description, dlg.ImmediateAction, App.Session!.DisplayName, App.Session);
                _resultBar.Show(res.Ok, "Incident recorded and linked to the reporting requirement.", res.Error);
        Load();
    }

    private async Task CloseIncidentAsync()
    {
                if (_incidents.SelectedItem is not IncRow row) { _resultBar.Show(NoticeTone.Info, "Select an incident first."); return; }
        var action = await Dialogs.Prompt(this, "Corrective action (required to close)", "What was done so this doesn't recur?");
        if (string.IsNullOrWhiteSpace(action)) return;
        var res = App.Workspace.SetIncidentState(row.Id, "Closed", action, App.Session!);
                _resultBar.Show(res.Ok, "Incident closed with corrective action recorded.", res.Error);
        Load();
    }

    public record DefRow(Guid Id, string Title, string Cadence, string Anchor, string Owner, string EquipmentName, string Status);
    public record EqRow(Guid Id, string Name, string Model, string Serial, string Location, string State, string Calibration);
    public record IncRow(Guid Id, string DateLabel, string Severity, string Description, string State, string Action, string By);
    public record SupRow(Guid Id, string Name, string Supplies, string Note, string Reviewed);
    public record RecRow(Guid Id, string Product, string Batch, string Received, string Action, string State);
    public record PotRow(string Name, string Drugs, string Forms, string Status);
}