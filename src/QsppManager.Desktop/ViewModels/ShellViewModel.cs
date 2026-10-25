using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using QsppManager.Application;
using QsppManager.Desktop.Views;
using QsppManager.Domain;
using QsppManager.Infrastructure.Services;
using QsppManager.Infrastructure.Runtime;

namespace QsppManager.Desktop.ViewModels;

/// <summary>Navigation keys. One destination per owner-brief sidebar line.</summary>
public enum ShellPage { Loading, Onboarding, Login, Today, Requirements, Documents, Tasks, Registers, People, Assessments, Library, PrintCentre, Settings }

public class NavItem(string label, ShellPage page) : ViewModelBase
{
    public string Label { get; } = label;
    public ShellPage Page { get; } = page;

    private bool _isActive;
    /// <summary>Sidebar selected state (premium-polish 3.3): SelBg row, Semibold
    /// label only on the active destination. Raised through INPC so grouped rows
    /// repaint on navigate.</summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    /// <summary>Destination hue class (3D): one of hueOchre/hueForest/hueTeal/hueClay/
    /// hueUmber/hueBerry/hueViolet/hueStone, or none for Library (Ink). The sidebar
    /// styles key on these class flags so brushes stay DynamicResource (theme-safe).</summary>
    public bool IsHueOchre => Page == ShellPage.Today;
    public bool IsHueForest => Page == ShellPage.Requirements;
    public bool IsHueTeal => Page == ShellPage.Documents;
    public bool IsHueClay => Page == ShellPage.Tasks;
    public bool IsHueUmber => Page == ShellPage.Registers;
    public bool IsHueBerry => Page == ShellPage.People;
    public bool IsHueViolet => Page == ShellPage.Assessments;
    public bool IsHueStone => Page is ShellPage.PrintCentre or ShellPage.Settings;

    private string? _badge;
    /// <summary>Optional count badge (premium-polish 3.3): Today only, for overdue
    /// items, and only when the count is above 0.</summary>
    public string? Badge { get => _badge; internal set => SetProperty(ref _badge, value); }

    /// <summary>Lucide icon name for the sidebar (bundled assets/Icons, ISC).</summary>
    public string Icon { get; } = page switch
    {
        ShellPage.Today => "house",
        ShellPage.Requirements => "list-checks",
        ShellPage.Documents => "files",
        ShellPage.Tasks => "calendar-days",
        ShellPage.Registers => "clipboard-list",
        ShellPage.People => "users",
        ShellPage.Assessments => "clipboard-check",
        ShellPage.Library => "book-open",
        ShellPage.PrintCentre => "printer",
        ShellPage.Settings => "settings",
        _ => "circle-dashed",
    };
}

/// <summary>Sidebar group (premium-polish 3.3): Prepare / Operate / Assess, each an
/// eyebrow label over its destinations. Settings is not grouped; it is pinned at the
/// bottom of the sidebar.</summary>
public sealed record NavGroup(string Label, NavItem[] Items);

/// <summary>Shell state: navigation, session, palette, sidebar. Pages are created
/// through the factory below so every page shares the same services and session.
/// The macOS shell exposes no permanent status bar — per-screen message lines and
/// toasts carry state; the toolbar shows the current destination and context search.</summary>
public class ShellViewModel : ViewModelBase
{
    private Control _currentPage = new LoadingView();
    private ShellPage _currentPageEnum = ShellPage.Loading;

    public ObservableCollection<NavItem> NavItems { get; } =
    [
        new("Today", ShellPage.Today),
        new("Requirements", ShellPage.Requirements),
        new("Evidence & documents", ShellPage.Documents),
        new("Tasks & calendar", ShellPage.Tasks),
        new("Registers", ShellPage.Registers),
        new("People & training", ShellPage.People),
        new("Assessments", ShellPage.Assessments),
        new("Library", ShellPage.Library),
        new("Print centre", ShellPage.PrintCentre),
        new("Settings", ShellPage.Settings),
    ];

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (SetProperty(ref _selectedNav, value) && value != null && App.Session != null)
                Navigate(value.Page);
        }
    }
    private NavItem? _selectedNav;

    /// <summary>The sidebar's three quiet groups (premium-polish 3.3): Prepare,
    /// Operate, Assess. Exactly the same ten destinations; Settings renders pinned
    /// at the bottom instead of inside a group.</summary>
    public IReadOnlyList<NavGroup> NavGroups
    {
        get
        {
            var items = NavItems;
            return
            [
                new("Prepare", [items[0], items[1], items[2], items[3]]),   // Today, Requirements, Evidence, Tasks
                new("Operate", [items[4], items[5]]),                       // Registers, People
                new("Assess", [items[6], items[7], items[8]]),              // Assessments, Library, Print centre
            ];
        }
    }

    public NavItem SettingsItem => NavItems[9];

    /// <summary>Pinned Settings row highlight (premium-polish 3.3).</summary>
    public bool IsSettingsActive => _currentPageEnum == ShellPage.Settings;

    /// <summary>Pharmacy name for the sidebar header (the DB-backed profile name; the
    /// Today eyebrow reads the same source).</summary>
    public string PharmacyName
    {
        get
        {
            try { return App.Workspace.EnsurePharmacy().Name; }
            catch { return "Your pharmacy"; }
        }
    }

    /// <summary>Initials for the toolbar user chip (24pt circle), e.g. "HN".</summary>
    public string Initials
    {
        get
        {
            var name = _session?.DisplayName ?? "";
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length switch
            {
                0 => "",
                1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                _ => (parts[0][0].ToString() + parts[^1][0]).ToUpperInvariant(),
            };
        }
    }

    /// <summary>Sidebar rows navigate directly (grouped ItemsControl, not a ListBox).</summary>
    public ICommand NavigateCommand => _navigateCommand ??= new RelayCommand(p => { if (p is ShellPage page) Navigate(page); });
    private ICommand? _navigateCommand;

    public Control CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

    /// <summary>Heading shown in the toolbar for the current destination.</summary>
    public string PageTitle { get; private set; } = "Today";

    /// <summary>Collapsible sidebar (macOS standard). Persisted in app-config.json.</summary>
    public bool SidebarCollapsed
    {
        get => _sidebarCollapsed;
        set
        {
            if (SetProperty(ref _sidebarCollapsed, value))
            {
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(SidebarExpanded));
            }
        }
    }
    private bool _sidebarCollapsed = App.Config.SidebarCollapsed;

    public double SidebarWidth => _sidebarCollapsed ? 52 : 232;
    public bool SidebarExpanded => !_sidebarCollapsed;
    public bool IsDemo => App.DemoMode;

    public ICommand ToggleSidebarCommand => _toggleSidebar ??= new RelayCommand(_ => ToggleSidebar());
    private ICommand? _toggleSidebar;

    public ICommand TogglePaletteCommand => _togglePalette ??= new RelayCommand(_ => TogglePalette());
    private ICommand? _togglePalette;

    public void ToggleSidebar()
    {
        SidebarCollapsed = !SidebarCollapsed;
        App.Config.SidebarCollapsed = SidebarCollapsed;
        App.Config.Save();
    }

    public bool SignedIn { get; private set; }
    public string SessionLabel { get; private set; } = "";
    public string DataViewLabel { get; private set; } = App.DemoMode ? "DEMO DATA · fictional pharmacy" : "Production data";

    // Command palette
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) RunSearch(); } }
    private string _searchText = "";
    public ObservableCollection<SearchResultRow> SearchResults { get; } = [];
    public SearchResultRow? SelectedSearchResult { get; set; }
    public bool PaletteOpen { get => _paletteOpen; private set => SetProperty(ref _paletteOpen, value); }
    private bool _paletteOpen;

    public void TogglePalette() { PaletteOpen = !PaletteOpen; if (PaletteOpen) RunSearch(); }

    private UserSession? _session;

    public ShellViewModel()
    {
        RefreshSessionAwareUi();
        RefreshTodayBadge();

        if (App.Auth.ListUsers().Count == 0)
            Navigate(ShellPage.Onboarding);
        else
            Navigate(ShellPage.Login);
    }

    /// <summary>Today's overdue count on the sidebar badge (premium-polish 3.3):
    /// shown only when above zero; same query as the Today position card.</summary>
    public void RefreshTodayBadge()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var overdue = App.Workspace.OpenTasks().Count(t => t.DueDate is { } d && d < today);
            NavItems[0].Badge = overdue > 0 ? overdue.ToString() : null;
        }
        catch { NavItems[0].Badge = null; }
    }

    public void SignIn(UserSession session)
    {
        App.Session = session;
        _session = session;
        SignedIn = true;
        SessionLabel = $"{session.DisplayName} · {RoleLabel(session.Role)}" + (App.DemoMode ? " · DEMO DATA" : "");
        RefreshSessionAwareUi();
        try
        {
            App.Workspace.GenerateRecurringOccurrences(session: session); // idempotent; brings schedules live at sign-in
        }
        catch { /* generation failures must never block sign-in; the audit trail still logs */ }
        RefreshTodayBadge();
        if (SelectedNav == null) SelectedNav = NavItems[0];
        Navigate(ShellPage.Today);
    }

    public void SignOut()
    {
        App.Session = null;
        _session = null;
        SignedIn = false;
        SessionLabel = "";
        RefreshSessionAwareUi();
        Navigate(ShellPage.Login);
    }

    public ICommand SignOutCommand => new RelayCommand(_ => SignOut());

    private void RefreshSessionAwareUi()
    {
        OnPropertyChanged(nameof(SignedIn));
        OnPropertyChanged(nameof(SessionLabel));
        OnPropertyChanged(nameof(DataViewLabel));
    }

    public void Navigate(ShellPage page)
    {
        if (!SignedIn && page is not (ShellPage.Login or ShellPage.Onboarding)) page = _session == null ? ShellPage.Login : ShellPage.Today;

        _currentPageEnum = page;
        // Keep the sidebar selection in sync for programmatic navigation (⌘1–⌘0, palette, menus).
        var navMatch = NavItems.FirstOrDefault(n => n.Page == page);
        if (navMatch != null && !ReferenceEquals(_selectedNav, navMatch)) _selectedNav = navMatch;
        OnPropertyChanged(nameof(SelectedNav));
        // Grouped sidebar: exactly one row reads active (SelBg + Semibold).
        var settingsActive = page == ShellPage.Settings;
        foreach (var item in NavItems)
            item.IsActive = item.Page == page && !settingsActive;
        OnPropertyChanged(nameof(IsSettingsActive));
        CurrentPage = page switch
        {
            ShellPage.Onboarding => new OnboardingView(this),
            ShellPage.Login => new LoginView(this),
            ShellPage.Today => new TodayView(this),
            ShellPage.Requirements => new RequirementsView(this),
            ShellPage.Documents => new DocumentsView(this),
            ShellPage.Tasks => new TasksView(this),
            ShellPage.Registers => new RegistersView(),
            ShellPage.People => new PeopleView(),
            ShellPage.Assessments => new AssessmentsView(this),
            ShellPage.Library => new LibraryView(this),
            ShellPage.PrintCentre => new PrintCentreView(this),
            ShellPage.Settings => new SettingsView(this),
            _ => new LoadingView(),
        };

        PageTitle = page switch
        {
            ShellPage.Login => "Sign in",
            ShellPage.Onboarding => "Welcome",
            ShellPage.Documents => "Evidence & documents",
            ShellPage.Tasks => "Tasks & calendar",
            ShellPage.People => "People & training",
            ShellPage.PrintCentre => "Print centre",
            _ => NavItems.FirstOrDefault(n => n.Page == page)?.Label ?? "QSPP Manager",
        };
        OnPropertyChanged(nameof(PageTitle));
    }

    /// <summary>Rebuild the current page from scratch — used by theme/appearance change
    /// (code-built views capture brushes at build time) and by explicit refreshes.</summary>
    public void RebuildCurrentPage() => Navigate(_currentPageEnum);

    /// <summary>⌘F: focus the current screen's filter/search box when it has one,
    /// falling back to the command palette.</summary>
    public void RequestFind()
    {
        if (CurrentPage is IPageSearchFocus searchable)
        {
            searchable.FocusSearch();
            return;
        }
        TogglePalette();
    }

    /// <summary>⌘P: hand the current context to the print centre, or re-render there
    /// when already showing it.</summary>
    public void RequestPrint()
    {
        if (CurrentPage is not IPagePrintRequest printable)
        {
            if (SelectedNav?.Page != ShellPage.PrintCentre)
            {
                SelectedNav = NavItems.FirstOrDefault(n => n.Page == ShellPage.PrintCentre);
                Navigate(ShellPage.PrintCentre);
                SelectedNav = NavItems.FirstOrDefault(n => n.Page == ShellPage.PrintCentre);
            }
            return;
        }
        printable.OpenPrintRequest();
    }

    public void ClosePalette() { PaletteOpen = false; SearchText = ""; }
    public void OpenResult(SearchResultRow row)
    {
        ClosePalette();
        if (row.Kind is "Std" or "Uag" or "UagKeyTask" or "Rule" or "Tc" or "Policy" or "Obligation" or "Hitlist" or "Surveillance")
        {
            if (SelectedNav?.Page != ShellPage.Requirements) SelectedNav = NavItems.FirstOrDefault(n => n.Page == ShellPage.Requirements);
            if (CurrentPage is RequirementsView rv) rv.OpenById(row.Id);
        }
        else if (row.Kind is "Issue" or "Source")
        {
            if (SelectedNav?.Page != ShellPage.Library) SelectedNav = NavItems.FirstOrDefault(n => n.Page == ShellPage.Library);
        }
        else
        {
            if (SelectedNav?.Page != ShellPage.Documents) SelectedNav = NavItems.FirstOrDefault(n => n.Page == ShellPage.Documents);
            if (CurrentPage is DocumentsView dv) dv.OpenEvidence(row.Id);
        }
    }

    /// <summary>M7: deep-link from Today's surveillance card — opens Requirements with
    /// the search pre-filtered to the round's items. DD-29: navigation is synchronous,
    /// so the filter applies as soon as the Requirements pane is the current page.</summary>
    public void OpenRequirementsFiltered(string filterText)
    {
        if (SelectedNav?.Page != ShellPage.Requirements) SelectedNav = NavItems.FirstOrDefault(n => n.Page == ShellPage.Requirements);
        if (CurrentPage is RequirementsView rv) rv.SetFilter(filterText);
    }

    private void RunSearch()
    {
        SearchResults.Clear();
        if (_searchText.Trim().Length < 2) return;
        foreach (var r in App.Search.Query(_searchText, hidePatientDataEvidence: App.Session?.ReadOnly == true))
            SearchResults.Add(r);
    }

    private static string RoleLabel(UserRole r) => r switch
    {
        UserRole.OwnerAdmin => "Owner / Admin",
        UserRole.ManagerReviewer => "Manager / Reviewer",
        UserRole.Contributor => "Contributor",
        UserRole.ReviewerReadOnly => "Reviewer (read-only)",
        _ => r.ToString(),
    };
}

public class ViewModelBase : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        return true;
    }
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

public class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void RaiseCanExecute() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}