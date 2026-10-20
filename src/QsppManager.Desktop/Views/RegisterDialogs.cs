using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using QsppManager.Domain;
using QsppManager.Domain.Entities;

namespace QsppManager.Desktop.Views;

// Compact modal dialogs for registers and people. They follow the same mechanics as
// Dialogs.Prompt: await the (non-generic) ShowDialog, then read TCS-backed properties —
// no reliance on generic dialog-result plumbing.

public abstract class RegisterDialogBase : Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    protected RegisterDialogBase(string title, double width = 560)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = new Button { Content = "Save", Classes = { "primary" }, MinWidth = 110 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80 };
        var message = new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap };
        save.Click += (_, _) => { if (OnSave(message)) { _tcs.TrySetResult(true); Close(); } };
        cancel.Click += (_, _) => { _tcs.TrySetResult(false); Close(); };

        Content = new StackPanel { Margin = new(20), Spacing = 12, Children =
        {
            new TextBlock { Classes = { "h3" }, Text = title },
            BuildBody(),
            message,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } },
        } };
    }

    protected abstract Control BuildBody();

    /// <summary>Validate + capture values; return false (with message) to keep the dialog open.</summary>
    protected abstract bool OnSave(TextBlock message);

    /// <summary>Show modally over the owner and report whether Save completed.</summary>
    public async Task<bool> Show(Control owner)
    {
        _ = ShowDialog(TopLevel.GetTopLevel(owner) as Window ?? owner as Window ?? throw new InvalidOperationException("No window host for the dialog"));
        return await _tcs.Task;
    }
}

public sealed class ScheduleDialog : RegisterDialogBase
{
    public string TitleText = "";
    public string Details = "";
    public RecurrenceCadence Cadence = RecurrenceCadence.Monthly;
    public int CustomDays = 30;
    public DateOnly Anchor = DateOnly.FromDateTime(DateTime.Today);
    public Guid AssignedTo;

    private readonly TextBox _title = new() { Watermark = "e.g. Fridge temperature checks, cleaning log" };
    private readonly TextBox _details = new() { Watermark = "Carried onto every occurrence", AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _cadence = new() { SelectedIndex = 2, MinWidth = 170, ItemsSource = new[] { "Weekly", "Fortnightly", "Monthly", "Quarterly", "Yearly", "Custom days" } };
    private readonly TextBox _custom = new() { Text = "30", MinWidth = 90 };
    private readonly DatePicker _anchor = new() { SelectedDate = DateTimeOffset.Now };

    public ScheduleDialog() : base("New recurring schedule") { }

    protected override Control BuildBody() => new StackPanel { Spacing = 12, Children =
    {
        Ui.FormField("SCHEDULE TITLE", _title),
        Ui.FormField("WHAT TO DO EACH TIME", _details),
        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children =
        {
            Ui.FormField("CADENCE", _cadence),
            Ui.FormField("CUSTOM DAYS", _custom),
            Ui.FormField("FIRST DUE", _anchor),
        } },
        new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Occurrence dates keep the anchor day through shorter months (31 Jan monthly → 28/29 Feb → 31 Mar). The task is assigned to you; reassign in Tasks. Generation is idempotent — reopening the app never duplicates." },
    } };

    protected override bool OnSave(TextBlock message)
    {
        if (string.IsNullOrWhiteSpace(_title.Text)) { message.Text = "The schedule needs a title."; return false; }
        if (_cadence.SelectedIndex == 5 && (!int.TryParse(_custom.Text, out var cd) || cd < 1))
        { message.Text = "Custom cadence needs an interval of at least 1 day."; return false; }
        TitleText = _title.Text;
        Details = _details.Text ?? "";
        Cadence = _cadence.SelectedIndex switch
        {
            0 => RecurrenceCadence.Weekly, 1 => RecurrenceCadence.Fortnightly, 3 => RecurrenceCadence.Quarterly,
            4 => RecurrenceCadence.PerAnnum, 5 => RecurrenceCadence.CustomDays, _ => RecurrenceCadence.Monthly,
        };
        if (Cadence == RecurrenceCadence.CustomDays)
        {
            _ = int.TryParse(_custom.Text, out var days);
            CustomDays = Math.Max(1, days);
        }
        if (_anchor.SelectedDate is { } dt) Anchor = DateOnly.FromDateTime(dt.DateTime);
        AssignedTo = App.Session!.UserId;
        return true;
    }
}

public sealed class IncidentDialog : RegisterDialogBase
{
    public DateOnly Date = DateOnly.FromDateTime(DateTime.Today);
    public string Severity = "";
    public string Description = "";
    public string ImmediateAction = "";

    private readonly DatePicker _date = new() { SelectedDate = DateTimeOffset.Now };
    private readonly TextBox _severity = new() { Watermark = "Severity (free text — the register doesn't invent a taxonomy)" };
    private readonly TextBox _desc = new() { Watermark = "Describe what happened as it occurred", AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _action = new() { Watermark = "Optional now (required to close)", AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };

    public IncidentDialog() : base("Report incident") { }

    protected override Control BuildBody() => new StackPanel { Spacing = 12, Children =
    {
        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children =
        {
            Ui.FormField("DATE", _date),
            Ui.FormField("SEVERITY (FREE TEXT)", _severity),
        } },
        Ui.FormField("WHAT HAPPENED", _desc),
        Ui.FormField("IMMEDIATE ACTION TAKEN", _action),
        new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "The record links to requirement 1.1.2.4 (incidents reported to a person responsible). Closing requires a corrective action." },
    } };

    protected override bool OnSave(TextBlock message)
    {
        if (string.IsNullOrWhiteSpace(_desc.Text)) { message.Text = "Describe the incident — the register is the evidence that reporting happened."; return false; }
        if (_date.SelectedDate is { } dt) Date = DateOnly.FromDateTime(dt.DateTime);
        Severity = _severity.Text ?? "";
        Description = _desc.Text;
        ImmediateAction = _action.Text ?? "";
        return true;
    }
}

public sealed class PotencyDialog : RegisterDialogBase
{
    public Guid StaffId;
    public string Role = "Compounding technician";
    public string Drug = "", Form = "", Outcome = "";
    public DateOnly TestedOn = DateOnly.FromDateTime(DateTime.Today);

    private readonly ComboBox _staff = new() { MinWidth = 240 };
    private readonly ComboBox _role = new() { SelectedIndex = 0, MinWidth = 220, ItemsSource = new[] { "Compounding technician", "Compounding pharmacist" } };
    private readonly TextBox _drug = new() { Watermark = "Drug tested (e.g. hydrocortisone)" };
    private readonly TextBox _form = new() { Watermark = "Dosage form (e.g. cream, suspension, ointment)" };
    private readonly DatePicker _date = new() { SelectedDate = DateTimeOffset.Now };
    private readonly TextBox _outcome = new() { Watermark = "pass, fail + what was done" };

    public PotencyDialog() : base("Record potency testing") { }

    protected override Control BuildBody() => new StackPanel { Spacing = 12, Children =
    {
        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children =
        {
            Ui.FormField("PERSON", _staff),
            Ui.FormField("ROLE", _role),
        } },
        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children =
        {
            Ui.FormField("DRUG", _drug),
            Ui.FormField("DOSAGE FORM", _form),
            Ui.FormField("TESTED ON", _date),
        } },
        Ui.FormField("OUTCOME / ACTION", _outcome),
        new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Add staff first under People & training. The cadence interpretation stays open (ISSUE-POTENCY): the app tracks per-person coverage — two different drugs across two different dosage forms per year is the conservative planning reading, not a settled rule." },
    } };

    protected override bool OnSave(TextBlock message)
    {
        var staff = App.Workspace.Staff().ToList();
        _staff.ItemsSource = staff.Select(s => s.Name).ToList();
        if (_staff.SelectedIndex < 0 && staff.Count > 0) _staff.SelectedIndex = 0;
        if (_staff.SelectedIndex < 0) { message.Text = "No staff recorded yet — add people under People & training first."; return false; }
        if (string.IsNullOrWhiteSpace(_drug.Text) || string.IsNullOrWhiteSpace(_form.Text))
        { message.Text = "Record WHICH drug and WHICH dosage form were tested."; return false; }
        StaffId = staff[_staff.SelectedIndex].Id;
        Role = _role.SelectedItem as string ?? Role;
        Drug = _drug.Text;
        Form = _form.Text;
        if (_date.SelectedDate is { } dt) TestedOn = DateOnly.FromDateTime(dt.DateTime);
        Outcome = _outcome.Text ?? "";
        return true;
    }
}

public sealed class TrainingDialog : RegisterDialogBase
{
    public DateOnly? FirstSupply, Initial, Refresher;

    private readonly DatePicker _supply = new();
    private readonly DatePicker _initial = new();
    private readonly DatePicker _refresher = new();

    public TrainingDialog() : base("Record S2/S3 training") { }

    protected override Control BuildBody() => new StackPanel { Spacing = 12, Children =
    {
        Ui.FormField("FIRST SCHEDULE 2/3 SUPPLY DATE", _supply),
        Ui.FormField("INITIAL TRAINING COMPLETED", _initial),
        Ui.FormField("REFRESHER COMPLETED", _refresher),
        new TextBlock { Classes = { "small" }, TextWrapping = TextWrapping.Wrap,
            Text = "Fill only dates you hold evidence for; a picker left untouched keeps its current value. Status lines update immediately." },
    } };

    protected override bool OnSave(TextBlock message)
    {
        FirstSupply = _supply.SelectedDate?.DateTime.Date is { } a ? DateOnly.FromDateTime(a) : null;
        Initial = _initial.SelectedDate?.DateTime.Date is { } b ? DateOnly.FromDateTime(b) : null;
        Refresher = _refresher.SelectedDate?.DateTime.Date is { } c ? DateOnly.FromDateTime(c) : null;
        return true;
    }
}