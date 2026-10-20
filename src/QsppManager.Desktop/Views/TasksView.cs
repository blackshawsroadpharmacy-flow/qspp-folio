using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

/// <summary>Open tasks with completion flow. Recurring schedules arrive in Milestone 3
/// (durable definitions; missed occurrences stay visible; no duplicate creation on reopen).</summary>
public partial class TasksView : UserControl, IPageSearchFocus
{
    private readonly ComboBox _filter = new() { SelectedIndex = 0, MinWidth = 180, ItemsSource = new[] { "All open", "Overdue", "Due next 7 days", "No due date" } };
    private readonly DataGrid _grid = new() { Height = 420, AutoGenerateColumns = false, IsReadOnly = true };
    private readonly TextBox _resultBar = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 34,
        Watermark = "Result messages appear here", Classes = { "result" },
    };
    private TableHost _tasksHost = null!;

    public TasksView(ShellViewModel shell)
    {
        _filter.SelectionChanged += (_, _) => Load();
        Content = Build(shell);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(28, 24), Spacing = 14, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Classes = { "h1" }, Text = "Tasks & calendar" });
        sp.Children.Add(new TextBlock
        {
            Classes = { "lead" }, TextWrapping = TextWrapping.Wrap,
            Text = "One-off work and recurring schedule occurrences, with owners and due dates. Missed occurrences stay open (overdue) until done; completing one records who and when. Schedules live in Registers.",
        });

        Ui.Ax(_filter, "Task filter", "Choose which open tasks are listed");
        var row = Ui.Row(10, _filter,
            PrimaryBtn("New task", "Create a one-off task or a recurring schedule occurrence",
                (_, _) => new TaskDialog(this, null).ShowDialog(App.Main)),
            Btn("Complete selected", "Record the selected task as completed", (_, _) =>
            {
                if (_grid.SelectedItem is not null && _grid.SelectedItem.GetType().GetProperty("Id") is { } p && p.GetValue(_grid.SelectedItem) is Guid id)
                    new CompleteTaskDialog(this, id).ShowDialog(App.Main);
                else _resultBar.Text = "Select a task.";
                Load();
            }));
        sp.Children.Add(row);

        _grid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(320) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Avalonia.Data.Binding("Priority"), Width = new DataGridLength(90) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Due", Binding = new Avalonia.Data.Binding("DueLabel"), Width = new DataGridLength(110) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Avalonia.Data.Binding("Owner"), Width = new DataGridLength(140) });
        _grid.Columns.Add(Ui.MonoCol("Requirement", "ReqRef", 140));
        _grid.Columns.Add(new DataGridTextColumn { Header = "Details", Binding = new Avalonia.Data.Binding("Details"), Width = new DataGridLength(240) });
        Ui.Ax(_grid, "Open tasks", "Select a task to complete it");
        _tasksHost = Host(_grid, 420, "list-checks", "No open tasks in this view", "Try All open, or create a task with the New task button.");
        sp.Children.Add(_tasksHost.Panel);

        sp.Children.Add(_resultBar);
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
        _tasksHost.Refresh(tasks.Count);
        _resultBar.Text = $"{tasks.Count} task(s) shown. Completing a task records who completed it and when — that record is the evidence of the action.";
    }

    /// <summary>⌘F hook: the page's filter is a preset ComboBox, so toolbar text maps
    /// onto the existing presets rather than introducing free-text filtering.</summary>
    public void FocusSearch() { _filter.Focus(); }
    public void SetFilter(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        _filter.SelectedIndex =
            t.Length == 0 || t.Contains("all") ? 0 :
            t.Contains("overdue") ? 1 :
            t.Contains("no due") ? 3 :
            t.Contains("next") || t.Contains("7 days") ? 2 :
            0;
    }

    public record TaskUiRow(string Title, string Priority, string DueLabel, string Owner, string ReqRef, string Details, Guid Id);

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
    private static TableHost Host(Control table, double minHeight, string icon, string title, string hint)
    {
        var empty = Ui.EmptyState(icon, title, hint);
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