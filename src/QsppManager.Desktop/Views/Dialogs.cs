using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using QsppManager.Domain;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

/// <summary>Small modal dialogs used by the workspace. Each returns results via awaited
/// ShowDialog; nothing here performs writes — the workspace services do.</summary>
public static class Dialogs
{
    public static async Task<string?> Prompt(Control owner, string title, string watermark, string initial = "")
    {
        var win = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var box = new TextBox { Watermark = watermark, Text = initial, Margin = new(20, 16) };
        var tcs = new TaskCompletionSource<string?>();
        var ok = new Button { Content = "OK", Classes = { "primary" }, MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        ok.Click += (_, _) => { tcs.TrySetResult(box.Text); win.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(null); win.Close(); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Return) { tcs.TrySetResult(box.Text); win.Close(); }
            if (e.Key == Avalonia.Input.Key.Escape) { tcs.TrySetResult(null); win.Close(); }
        };
        win.Content = new StackPanel
        {
            Children =
            {
                new TextBlock { Classes = { "h3" }, Text = title, Margin = new(20, 14, 20, 0) },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                    Margin = new(0, 0, 20, 16), Children = { cancel, ok },
                },
            },
        };
        await win.ShowDialog(TopLevel.GetTopLevel(owner) as Window ?? owner as Window ?? throw new InvalidOperationException("No window host for the dialog"));
        return tcs.Task.Status == TaskStatus.RanToCompletion ? tcs.Task.Result : null;
    }
}

/// <summary>Attach-evidence dialog: title, kind, effective period, patient-data gate.</summary>
public sealed class EvidenceDialog : Window
{
    private readonly TextBox _title = new() { Watermark = "Evidence title (required)" };
    private readonly ComboBox _kind = new() { SelectedIndex = 0, MinWidth = 220, ItemsSource = Enum.GetNames<EvidenceKind>() };
    private readonly TextBox _scope = new() { Watermark = "Scope (e.g. compounding facility, retail floor)", MinWidth = 240 };
    private readonly DatePicker _from = new();
    private readonly DatePicker _to = new();
    private readonly CheckBox _patientData = new() { Content = "Contains patient-identifying data (redaction attestation required)" };
    private readonly TextBox _attestation = new() { Watermark = "Redaction attestation: what was redacted, who reviewed it, when", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 54 };
    private readonly TextBox _rationale = new() { Watermark = "Why this document supports the requirement (required for evidence not yet verified)", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 54 };
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
    private readonly TaskCompletionSource<bool> _tcs = new();

    public EvidenceDialog(Control owner)
    {
        Title = "Attach evidence";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = new Button { Content = "Attach and link", Classes = { "primary" }, MinWidth = 130 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_title.Text)) { _message.Text = "An evidence title is required."; return; }
            if (_patientData.IsChecked == true && string.IsNullOrWhiteSpace(_attestation.Text))
            {
                _message.Text = "Describe the redaction attestation (what was removed, who reviewed it). Evidence containing patient data without an attestation is deleted unread by the program (Rule 31).";
                return;
            }
            _tcs.TrySetResult(true);
            Close();
        };
        cancel.Click += (_, _) => { _tcs.TrySetResult(false); Close(); };

        Content = new StackPanel
        {
            Margin = new(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Classes = { "h3" }, Text = "Attach evidence" },
                _title,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _kind, _scope } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 12,
                    Children =
                    {
                        Ui.FormField("EFFECTIVE FROM", _from),
                        Ui.FormField("EFFECTIVE TO (OPTIONAL)", _to),
                    },
                },
                _patientData,
                _attestation,
                _rationale,
                _message,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } },
            },
        };
        _message.Text = "Uploading a file never sets compliance. Patient-identifying content must be removed from the document itself — not hidden behind a drawn black box from an unverified tool.";
    }

    public bool Confirmed => _tcs.Task.Status == TaskStatus.RanToCompletion && _tcs.Task.Result;
    public string TitleText => _title.Text?.Trim() ?? "";
    public EvidenceKind Kind => (EvidenceKind)Math.Max(_kind.SelectedIndex, 0);
    public DateOnly? EffectiveFrom => _from.SelectedDate.HasValue ? DateOnly.FromDateTime(_from.SelectedDate.Value.DateTime) : null;
    public DateOnly? EffectiveTo => _to.SelectedDate.HasValue ? DateOnly.FromDateTime(_to.SelectedDate.Value.DateTime) : null;
    public bool HasPatientData => _patientData.IsChecked == true;
    public string PatientDataAttestation => _attestation.Text ?? "";
    public string Rationale => _rationale.Text ?? "";
    public string Scope => _scope.Text ?? "";
}

/// <summary>Pick an existing evidence item to link.</summary>
public sealed class EvidencePickerDialog : Window
{
    private readonly List<EvidenceItem> _all;
    private readonly DataGrid _grid = new();
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
    private readonly TaskCompletionSource<EvidenceItem?> _tcs = new();

    public EvidencePickerDialog(Control owner)
    {
        Title = "Link existing evidence";
        Width = 700; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _all = App.Db.EvidenceItems.AsNoTracking().OrderByDescending(e => e.CreatedAtUtc).ToList();

        _grid.ItemsSource = _all.Select(e => new
        {
            e.Id, e.Title,
            Kind = e.Kind.ToString(),
            Review = e.ReviewState.ToString(),
            e.FileName,
            Effective = (e.EffectiveFrom?.ToString("dd MMM yyyy") ?? "—") + (e.EffectiveTo is null ? "" : " → " + e.EffectiveTo?.ToString("dd MMM yyyy")),
        }).ToList();
        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(220) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Kind", Binding = new Avalonia.Data.Binding("Kind"), Width = new DataGridLength(100) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Review", Binding = new Avalonia.Data.Binding("Review"), Width = new DataGridLength(120) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Effective", Binding = new Avalonia.Data.Binding("Effective"), Width = new DataGridLength(160) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new Avalonia.Data.Binding("FileName"), Width = new DataGridLength(130) });

        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 8, 0) };
        cancel.Click += (_, _) => _tcs.TrySetResult(null);
        var ok = new Button { Content = "Link selected", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) =>
        {
            if (_grid.SelectedItem is null || _grid.SelectedItem.GetType().GetProperty("Id") is not { } prop || prop.GetValue(_grid.SelectedItem) is not Guid id)
            { _message.Text = "Select an evidence item."; return; }
            _tcs.TrySetResult(_all.FirstOrDefault(e => e.Id == id));
            Close();
        };
        _message.Text = "Retired (superseded) items are excluded by the service; drafts need a rationale when linked.";

        Content = new StackPanel { Margin = new(20), Spacing = 12, Children = { new TextBlock { Classes = { "h3" }, Text = "Choose an evidence item" }, _grid, _message, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } } } };
    }

    public EvidenceItem? SelectedItem => _tcs.Task.Status == TaskStatus.RanToCompletion ? _tcs.Task.Result : null;
}

/// <summary>Create-task dialog.</summary>
public sealed class TaskDialog : Window
{
    private readonly Guid? _requirementId;
    private readonly TextBox _title = new() { Watermark = "Task title (required)" };
    private readonly TextBox _details = new() { Watermark = "Details", AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _owner = new() { MinWidth = 200 };
    private readonly ComboBox _priority = new() { ItemsSource = new[] { "Normal", "High", "Critical" }, SelectedIndex = 0, MinWidth = 140 };
    private readonly DatePicker _due = new();
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };

    public TaskDialog(Control owner, Guid? requirementId)
    {
        _requirementId = requirementId;
        Title = "Create task"; Width = 580; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var users = App.Auth.ListUsers();
        _owner.ItemsSource = users.Select(u => u.DisplayName).ToList();
        _owner.SelectedIndex = users.Count > 0 ? 0 : -1;

        var create = new Button { Content = "Create task", Classes = { "primary" } };
        create.Click += (_, _) =>
        {
            var assignee = users.ElementAtOrDefault(Math.Max(_owner.SelectedIndex, 0));
            if (assignee == null) { _message.Text = "Add a user first (Settings)."; return; }
            var res = App.Workspace.CreateTask(_title.Text ?? "", _details.Text ?? "", _requirementId, assignee.Id,
                (TaskPriority)Math.Max(_priority.SelectedIndex, 0), _due.SelectedDate.HasValue ? DateOnly.FromDateTime(_due.SelectedDate.Value.DateTime) : null, App.Session!);
            _message.Text = res.Ok ? "Task created." : res.Error;
            if (res.Ok) Close();
        };

        var cancelTask = new Button { Content = "Cancel", MinWidth = 80 };
        cancelTask.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new(20), Spacing = 12,
            Children =
            {
                new TextBlock { Classes = { "h3" }, Text = "Create task" },
                _title, _details,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _owner, _priority, Ui.FormField("DUE (OPTIONAL)", _due) } },
                _message,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancelTask, create } },
            },
        };
    }
}

/// <summary>Complete-task dialog.</summary>
public sealed class CompleteTaskDialog : Window
{
    public CompleteTaskDialog(Control owner, Guid taskId)
    {
        Title = "Complete task"; Width = 540; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var note = new TextBox { Watermark = "Completion note (what was done, where the evidence landed)", AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
        var message = new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
        var done = new Button { Content = "Mark complete", Classes = { "primary" } };
        done.Click += (_, _) =>
        {
            var res = App.Workspace.CompleteTask(taskId, note.Text ?? "", App.Session!);
            if (res.Ok) Close();
            else message.Text = res.Error;
        };
        Content = new StackPanel { Margin = new(20), Spacing = 12, Children = { new TextBlock { Classes = { "h3" }, Text = "Complete task" }, note, message, done } };
    }
}

internal static class DateOnlyHelpers
{
    public static DateOnly ToDateOnlyOrNull(this DateTime dt) => DateOnly.FromDateTime(dt);
}
