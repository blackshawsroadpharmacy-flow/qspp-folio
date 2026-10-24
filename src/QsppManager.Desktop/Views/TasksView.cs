using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

/// <summary>Open tasks with a tick-to-complete flow (§5): a joined segmented filter, "New task"
/// as a quiet + in the toolbar, the empty state top-aligned under it. Recurring schedules live
/// in Registers; missed occurrences stay visible here until done.</summary>
public partial class TasksView : UserControl, IPageSearchFocus
{
    private static readonly string[] _filterLabels = ["All open", "Overdue", "Due next 7 days", "No due date"];

    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    private readonly NoticeHost _resultBar = new();
    private readonly List<Button> _filterButtons = [];
    private Control _emptyState = null!;
    private int _filter;

    public TasksView(ShellViewModel shell)
    {
        Content = Build(shell);
        SetFilter(0);
        Load();
    }

    private Control Build(ShellViewModel shell)
    {
        var sp = new StackPanel { Margin = new(28, 24), Spacing = 12, MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Ui.PageTitle(ViewModels.ShellPage.Tasks, "One-off work and recurring occurrences, with owners and due dates.", "Missed occurrences stay open (overdue) until done; completing one records who and when. Schedules live in Registers."));

        // One toolbar row (§5): the segmented filter left, "New task" as a quiet + right.
        var toolbar = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var filterHost = new Border
        {
            CornerRadius = new(6),
            BorderBrush = Ui.Br("Brush.ControlBorder"),
            BorderThickness = new(1),
            Background = Ui.Br("Brush.Surface"),
            Padding = new(2, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < _filterLabels.Length; i++)
        {
            if (i > 0)
                filterRow.Children.Add(new Border { Width = 1, Margin = new(1, 5), VerticalAlignment = VerticalAlignment.Stretch, Background = Ui.Br("Brush.Divider") });
            var index = i;
            var b = new Button { Height = 24, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.Medium, Padding = new(9, 0), Content = _filterLabels[i] };
            b.Classes.Add("segcell");
            b.Click += (_, _) => { SetFilter(index); Load(); };
            _filterButtons.Add(b);
            filterRow.Children.Add(b);
        }
        filterHost.Child = filterRow;
        toolbar.Children.Add(filterHost);
        var newBtn = Btn("+ New task", "Create a one-off task or a recurring schedule occurrence",
            (_, _) => new TaskDialog(this, null).ShowDialog(App.Main), "ghost");
        Avalonia.Controls.Grid.SetColumn(newBtn, 1);
        toolbar.Children.Add(newBtn);
        sp.Children.Add(toolbar);

        _grid.Columns.Add(new DataGridTextColumn { Header = "Title", Binding = new Avalonia.Data.Binding("Title"), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Avalonia.Data.Binding("Priority"), Width = new DataGridLength(80) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Due", Binding = new Avalonia.Data.Binding("DueLabel"), Width = new DataGridLength(150) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Avalonia.Data.Binding("Owner"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(Ui.MonoCol("Requirement", "ReqRef", 110));
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Done",
            Width = new DataGridLength(54),
            CellTemplate = new FuncDataTemplate<object>((_, _) => BuildTickCell(), true),
        });
        Ui.Ax(_grid, "Open tasks", "Tick a row to record it as completed");

        // The empty state sits top-left directly under the toolbar — no tall void above or
        // below it (owner brief, Tasks); the grid sizes to its content up to a page-length cap.
        var listScroll = new ScrollViewer { Content = _grid, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _emptyState = Ui.EmptyState("list-checks", "No open tasks in this view", "Try All open, or create a task with the + New task button.", null, "Brush.Clay");
        _emptyState.IsVisible = false;
        var listHost = new Panel();
        listHost.Children.Add(listScroll);
        listHost.Children.Add(_emptyState);
        sp.Children.Add(listHost);
        sp.Children.Add(_resultBar);
        return new ScrollViewer { Content = sp };
    }

    /// <summary>The per-row completion tick (§5): ticking opens the completion record (who,
    /// when and the note); cancelling leaves the task open. The checkbox is an action, not
    /// state — the refreshed list is the state.</summary>
    private Control BuildTickCell()
    {
        var cb = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // Avalonia CheckBox carries no WPF-style Checked event: watch IsCheckedProperty, and
        // react only to true so the programmatic reset does not re-trigger.
        cb.PropertyChanged += (_, e) =>
        {
            if (e.Property != ToggleButton.IsCheckedProperty || e.NewValue is not true) return;
            if (cb.DataContext is not TaskUiRow row || row.Id == Guid.Empty) { cb.IsChecked = null; return; }
            _ = CompleteFromTick(cb, row.Id);
        };
        return cb;
    }

    private async Task CompleteFromTick(CheckBox cb, Guid id)
    {
        try
        {
            await new CompleteTaskDialog(this, id).ShowDialog(App.Main!);
        }
        finally
        {
            cb.IsChecked = false;
            Load();
        }
    }

    private void Load()
    {
        if (_grid.Columns.Count == 0) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var users = App.Auth.ListUsers().ToDictionary(u => u.Id, u => u.DisplayName);
        var reqs = App.Workspace.RequirementRows(App.Session).ToDictionary(r => r.Id, r => r.Ref);
        var tasks = App.Workspace.OpenTasks();
        tasks = _filter switch
        {
            1 => tasks.Where(t => t.DueDate is { } d && d < today).ToList(),
            2 => tasks.Where(t => t.DueDate is { } d && d >= today && d < today.AddDays(7)).ToList(),
            3 => tasks.Where(t => t.DueDate == null).ToList(),
            _ => tasks,
        };
        _grid.ItemsSource = tasks.Select(t => new TaskUiRow(
            t.Title + (t.RecurrenceDefId != null ? " (recurring)" : ""),
            t.Priority.ToString(),
            t.OccurrenceDate.HasValue && t.DueDate != t.OccurrenceDate
                ? $"{t.DueDate:dd MMM yyyy} (for {t.OccurrenceDate:dd MMM})"
                : t.DueDate?.ToString("dd MMM yyyy") ?? "no due date",
            users.GetValueOrDefault(t.AssignedToUserId, "?"),
            t.RequirementId.HasValue && reqs.TryGetValue(t.RequirementId.Value, out var r) ? r : "",
            t.Details,
            t.Id)).ToList();
        var count = tasks.Count;
        _grid.IsVisible = count > 0;
        _emptyState.IsVisible = count == 0;
    }

    private void SetFilter(int i)
    {
        _filter = i;
        for (var k = 0; k < _filterButtons.Count; k++)
        {
            if (k == _filter) _filterButtons[k].Classes.Add("on"); else _filterButtons[k].Classes.Remove("on");
        }
    }

    /// <summary>⌘F hook: focus the first filter cell; toolbar text maps onto the presets.</summary>
    public void FocusSearch() { if (_filterButtons.Count > 0) _filterButtons[0].Focus(); }
    public void SetFilter(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        SetFilter(
            t.Length == 0 || t.Contains("all") ? 0 :
            t.Contains("overdue") ? 1 :
            t.Contains("no due") ? 3 :
            t.Contains("next") || t.Contains("7 days") ? 2 :
            0);
        Load();
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
}