using Avalonia.Controls;
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
    private readonly DataGrid _defs = new() { Height = 170, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _equipment = new() { Height = 200, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly DataGrid _incidents = new() { Height = 200, AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 34 };
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };

    public RegistersView()
    {
        Content = Build();
        Load();
    }

    private Control Build()
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 12, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Registers" });
        sp.Children.Add(new TextBlock { Classes = { "lead" }, TextWrapping = TextWrapping.Wrap,
            Text = "Recurring obligations, the equipment register and the incident register. Schedules materialise as ordinary tasks with due dates — missed occurrences stay visible until someone does them, and generation never duplicates. Equipment calibration dates automatically get a monthly check schedule." });

        sp.Children.Add(new TextBlock { Text = "Recurring schedules", Classes = { "h3" } });
        _defs.Columns.Add(new DataGridTextColumn { Header = "Schedule", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(340) });
        _defs.Columns.Add(new DataGridTextColumn { Header = "Cadence", Binding = new Avalonia.Data.Binding("Cadence"), Width = new DataGridLength(100) });
        _defs.Columns.Add(new DataGridTextColumn { Header = "From", Binding = new Avalonia.Data.Binding("Anchor"), Width = new DataGridLength(110) });
        _defs.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Avalonia.Data.Binding("Owner"), Width = new DataGridLength(150) });
        _defs.Columns.Add(new DataGridTextColumn { Header = "Equipment", Binding = new Avalonia.Data.Binding("EquipmentName"), Width = new DataGridLength(180) });
        _defs.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Avalonia.Data.Binding("Status"), Width = new DataGridLength(90) });
        sp.Children.Add(_defs);
        var defRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        defRow.Children.Add(new Button { Content = "New schedule…", Classes = { "primary" }, Height = 34 }.With(b => b.Click += async (_, _) => await NewScheduleAsync()));
        defRow.Children.Add(new Button { Content = "Pause selected", Height = 34 }.With(b => b.Click += (_, _) => ToggleDef(false)));
        defRow.Children.Add(new Button { Content = "Resume selected", Height = 34 }.With(b => b.Click += (_, _) => ToggleDef(true)));
        defRow.Children.Add(new Button { Content = "Generate occurrences now", Height = 34 }.With(b => b.Click += (_, _) =>
        {
            var n = App.Workspace.GenerateRecurringOccurrences(session: App.Session);
            _message.Text = n > 0 ? $"{n} occurrence(s) generated (idempotent — reopening never duplicates)." : "Nothing new to generate.";
            Load();
        }));
        sp.Children.Add(defRow);

        sp.Children.Add(new TextBlock { Text = "Equipment register", Classes = { "h3" }, Margin = new(0, 8, 0, 0) });
        _equipment.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding("Name"), Width = new DataGridLength(260) });
        _equipment.Columns.Add(new DataGridTextColumn { Header = "Model", Binding = new Avalonia.Data.Binding("Model"), Width = new DataGridLength(150) });
        _equipment.Columns.Add(new DataGridTextColumn { Header = "Serial", Binding = new Avalonia.Data.Binding("Serial"), Width = new DataGridLength(140) });
        _equipment.Columns.Add(new DataGridTextColumn { Header = "Location", Binding = new Avalonia.Data.Binding("Location"), Width = new DataGridLength(160) });
        _equipment.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(110) });
        _equipment.Columns.Add(new DataGridTextColumn { Header = "Calibration due", Binding = new Avalonia.Data.Binding("Calibration"), Width = new DataGridLength(130) });
        sp.Children.Add(_equipment);
        var eqRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        eqRow.Children.Add(new Button { Content = "Add equipment…", Classes = { "primary" }, Height = 34 }.With(b => b.Click += async (_, _) => await AddEquipmentAsync()));
        eqRow.Children.Add(new Button { Content = "Set calibration due…", Height = 34 }.With(b => b.Click += async (_, _) => await CalibrationAsync()));
        eqRow.Children.Add(new Button { Content = "Send to maintenance", Height = 34 }.With(b => b.Click += (_, _) => SetState("Maintenance")));
        eqRow.Children.Add(new Button { Content = "Back in service", Height = 34 }.With(b => b.Click += (_, _) => SetState("InService")));
        eqRow.Children.Add(new Button { Content = "Retire", Height = 34 }.With(b => b.Click += (_, _) => SetState("Retired")));
        sp.Children.Add(eqRow);

        sp.Children.Add(new TextBlock { Text = "Incident register", Classes = { "h3" }, Margin = new(0, 8, 0, 0) });
        _incidents.Columns.Add(new DataGridTextColumn { Header = "Date", Binding = new Avalonia.Data.Binding("DateLabel"), Width = new DataGridLength(110) });
        _incidents.Columns.Add(new DataGridTextColumn { Header = "Severity", Binding = new Avalonia.Data.Binding("Severity"), Width = new DataGridLength(90) });
        _incidents.Columns.Add(new DataGridTextColumn { Header = "Description", Binding = new Avalonia.Data.Binding("Description"), Width = new DataGridLength(360) });
        _incidents.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Avalonia.Data.Binding("State"), Width = new DataGridLength(90) });
        _incidents.Columns.Add(new DataGridTextColumn { Header = "Corrective action", Binding = new Avalonia.Data.Binding("Action"), Width = new DataGridLength(280) });
        _incidents.Columns.Add(new DataGridTextColumn { Header = "Reported by", Binding = new Avalonia.Data.Binding("By"), Width = new DataGridLength(120) });
        sp.Children.Add(_incidents);
        var incRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        incRow.Children.Add(new Button { Content = "Report incident…", Classes = { "primary" }, Height = 34 }.With(b => b.Click += async (_, _) => await ReportIncidentAsync()));
        incRow.Children.Add(new Button { Content = "Close with corrective action…", Height = 34 }.With(b => b.Click += async (_, _) => await CloseIncidentAsync()));
        sp.Children.Add(incRow);
        sp.Children.Add(new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Incidents link to requirement 1.1.2.4 (reporting to a person responsible) so the register is reachable from the clause. An incident cannot close without a recorded corrective action." });

        sp.Children.Add(_message);
        return new ScrollViewer { Content = sp };
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
    }

    private async Task NewScheduleAsync()
    {
        var dlg = new ScheduleDialog();
        if (!await dlg.Show(this)) return;
        var res = App.Workspace.CreateRecurringDef(dlg.TitleText, dlg.Details, null, null,
            dlg.AssignedTo, dlg.Cadence, dlg.CustomDays, dlg.Anchor, TaskPriority.Normal, App.Session!);
        _message.Text = res.Ok ? "Schedule created — occurrences generate on refresh, and via 'Generate occurrences now'." : res.Error;
        Load();
    }

    private void ToggleDef(bool active)
    {
        if (_defs.SelectedItem is not DefRow row) { _message.Text = "Select a schedule first."; return; }
        var res = App.Workspace.SetRecurringDefActive(row.Id, active, App.Session!);
        _message.Text = res.Ok ? (active ? "Schedule resumed." : "Schedule paused — no further occurrences generate until resumed.") : res.Error;
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
        _message.Text = res.Ok ? "Equipment added to the register." : res.Error;
        Load();
    }

    private async Task CalibrationAsync()
    {
        if (_equipment.SelectedItem is not EqRow row) { _message.Text = "Select equipment first."; return; }
        var text = await Dialogs.Prompt(this, $"Calibration due — {row.Name}", "Due date (yyyy-MM-dd)");
        if (string.IsNullOrWhiteSpace(text) || !DateOnly.TryParse(text, out var due))
        { _message.Text = "Enter the date as yyyy-MM-dd."; return; }
        var res = App.Workspace.SetCalibrationDue(row.Id, due, App.Session!);
        _message.Text = res.Ok ? "Calibration date recorded; a monthly check schedule now exists for this equipment." : res.Error;
        Load();
    }

    private void SetState(string state)
    {
        if (_equipment.SelectedItem is not EqRow row) { _message.Text = "Select equipment first."; return; }
        var res = App.Workspace.SetEquipmentState(row.Id, state, App.Session!);
        _message.Text = res.Ok ? $"Equipment marked {state}." : res.Error;
        Load();
    }

    private async Task ReportIncidentAsync()
    {
        var dlg = new IncidentDialog();
        if (!await dlg.Show(this)) return;
        var res = App.Workspace.AddIncident(dlg.Date, dlg.Severity, dlg.Description, dlg.ImmediateAction, App.Session!.DisplayName, App.Session);
        _message.Text = res.Ok ? "Incident recorded and linked to the reporting requirement." : res.Error;
        Load();
    }

    private async Task CloseIncidentAsync()
    {
        if (_incidents.SelectedItem is not IncRow row) { _message.Text = "Select an incident first."; return; }
        var action = await Dialogs.Prompt(this, "Corrective action (required to close)", "What was done so this doesn't recur?");
        if (string.IsNullOrWhiteSpace(action)) return;
        var res = App.Workspace.SetIncidentState(row.Id, "Closed", action, App.Session!);
        _message.Text = res.Ok ? "Incident closed with corrective action recorded." : res.Error;
        Load();
    }

    public record DefRow(Guid Id, string Title, string Cadence, string Anchor, string Owner, string EquipmentName, string Status);
    public record EqRow(Guid Id, string Name, string Model, string Serial, string Location, string State, string Calibration);
    public record IncRow(Guid Id, string DateLabel, string Severity, string Description, string State, string Action, string By);
}