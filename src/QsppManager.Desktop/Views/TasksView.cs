using Avalonia.Controls;
using Avalonia.Media;

using Avalonia.Layout;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

/// <summary>Open tasks with completion flow. Recurring schedules arrive in Milestone 3
/// (durable definitions; missed occurrences stay visible; no duplicate creation on reopen).</summary>
public partial class TasksView : UserControl
{
    private readonly ComboBox _filter = new() { SelectedIndex = 0, MinWidth = 180, ItemsSource = new[] { "All open", "Overdue", "Due next 7 days", "No due date" } };
    private readonly DataGrid _grid = new() { Height = 420, AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBlock _message = new() { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };

    public TasksView(ShellViewModel shell)
    {
        _filter.SelectionChanged += (_, _) => Load();
        Content = Build(shell);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(32, 28), Spacing = 14, MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Tasks & calendar" });
        sp.Children.Add(new TextBlock { Classes = { "lead" }, Text = "One-off work and recurring schedule occurrences, with owners and due dates. Missed occurrences stay open (overdue) until done; completing one records who and when. Schedules live in Registers." });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_filter);
        row.Children.Add(new Button { Content = "New task", Classes = { "primary" }, Height = 34 }.With(_ => _.Click += (_, _) => new TaskDialog(this, null).ShowDialog(App.Main)));
        row.Children.Add(new Button { Content = "Complete selected", Height = 34 }.With(_ => _.Click += (_, _) =>
        {
            if (_grid.SelectedItem is not null && _grid.SelectedItem.GetType().GetProperty("Id") is { } p && p.GetValue(_grid.SelectedItem) is Guid id)
                new CompleteTaskDialog(this, id).ShowDialog(App.Main);
            else _message.Text = "Select a task.";
            Load();
        }));
        sp.Children.Add(row);

        _grid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(320) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Avalonia.Data.Binding("Priority"), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Due", Binding = new Avalonia.Data.Binding("DueLabel"), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Avalonia.Data.Binding("Owner"), Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Requirement", Binding = new Avalonia.Data.Binding("ReqRef"), Width = new DataGridLength(140) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Details", Binding = new Avalonia.Data.Binding("Details"), Width = new DataGridLength(240) });
        sp.Children.Add(_grid);
        sp.Children.Add(_message);
        return new ScrollViewer { Content = sp };
    }

    private void Load()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var users = App.Auth.ListUsers().ToDictionary(u => u.Id, u => u.DisplayName);
        var reqs = App.Workspace.RequirementRows(App.Session).ToDictionary(r => r.Id, r => r.Ref);
        var tasks = App.Workspace.OpenTasks();
        tasks = _filter.SelectedIndex switch
        {
            1 => tasks.Where(t => t.DueDate is { } d && d < today).ToList(),
            2 => tasks.Where(t => t.DueDate is { } d && d >= today && d < today.AddDays(7)).ToList(),
            3 => tasks.Where(t => t.DueDate == null).ToList(),
            _ => tasks,
        };
        _grid.ItemsSource = tasks.Select(t => new
        {
            Title = t.Title + (t.RecurrenceDefId != null ? " (recurring)" : ""),
            Priority = t.Priority.ToString(),
            DueLabel = t.OccurrenceDate.HasValue && t.DueDate != t.OccurrenceDate
                ? $"{t.DueDate:dd MMM yyyy} (for {t.OccurrenceDate:dd MMM})"
                : t.DueDate?.ToString("dd MMM yyyy") ?? "no due date",
            Owner = users.GetValueOrDefault(t.AssignedToUserId, "?"),
            ReqRef = t.RequirementId.HasValue && reqs.TryGetValue(t.RequirementId.Value, out var r) ? r : "",
            t.Details,
            t.Id,
        }).ToList();
        _message.Text = $"{tasks.Count} task(s) shown. Completing a task records who completed it and when — that record is the evidence of the action.";
    }

    public record TaskUiRow(string Title, string Priority, string DueLabel, string Owner, string ReqRef, string Details, Guid Id);
}