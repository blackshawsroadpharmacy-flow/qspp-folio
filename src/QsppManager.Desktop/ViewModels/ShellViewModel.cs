using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using QsppManager.Application;
using QsppManager.Desktop.Views;
using QsppManager.Domain;
using QsppManager.Infrastructure.Services;

namespace QsppManager.Desktop.ViewModels;

/// <summary>Navigation keys. One destination per owner-brief sidebar line.</summary>
public enum ShellPage { Loading, Onboarding, Login, Today, Requirements, Documents, Tasks, Registers, People, Assessments, Library, PrintCentre, Settings }

public class NavItem(string label, ShellPage page)
{
    public string Label { get; } = label;
    public ShellPage Page { get; } = page;
}

/// <summary>Shell state: navigation, session, palette, status bar. Pages are created
/// through the factory below so every page shares the same services and session.</summary>
public class ShellViewModel : ViewModelBase
{
    private Control _currentPage = new LoadingView();

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

    public Control CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

    public bool SignedIn { get; private set; }
    public string SessionLabel { get; private set; } = "";
    public string StatusRight { get; private set; } = "";
    public string DataViewLabel { get; private set; } = App.DemoMode ? "DEMO DATA — fictional pharmacy" : "Production data";

    public string StatusBar { get; private set; } = "Starting…";

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
        StatusBar = $"Corpus: imported at startup · Data: {Infrastructure.Runtime.AppData.Root} · Offline mode";
        RefreshSessionAwareUi();

        if (App.Auth.ListUsers().Count == 0)
            Navigate(ShellPage.Onboarding);
        else
            Navigate(ShellPage.Login);
    }

    public void SignIn(UserSession session)
    {
        App.Session = session;
        _session = session;
        SignedIn = true;
        SessionLabel = $"{session.DisplayName} · {RoleLabel(session.Role)}";
        RefreshSessionAwareUi();
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
    }

    public void Navigate(ShellPage page)
    {
        if (!SignedIn && page is not (ShellPage.Login or ShellPage.Onboarding)) page = _session == null ? ShellPage.Login : ShellPage.Today;

        CurrentPage = page switch
        {
            ShellPage.Onboarding => new OnboardingView(this),
            ShellPage.Login => new LoginView(this),
            ShellPage.Today => new TodayView(this),
            ShellPage.Requirements => new RequirementsView(this),
            ShellPage.Documents => new DocumentsView(this),
            ShellPage.Tasks => new TasksView(this),
            ShellPage.Registers => new PlaceholdersView("Registers", "Equipment, calibration and maintenance schedules, incidents, risks, suppliers and temperature logs arrive in Milestone 3. Recurring-task rules are drafted in docs/milestones.md."),
            ShellPage.People => new PlaceholdersView("People & training", "Staff profiles, induction checklists, qualifications, the training matrix and policy acknowledgements arrive in Milestone 3."),
            ShellPage.Assessments => new AssessmentsView(this),
            ShellPage.Library => new LibraryView(this),
            ShellPage.PrintCentre => new PrintCentreView(this),
            ShellPage.Settings => new SettingsView(this),
            _ => new LoadingView(),
        };

        StatusBar = $"Viewing {page} · {NowLabel()}";
        OnPropertyChanged(nameof(CurrentPage));
    }

    public void ClosePalette() { PaletteOpen = false; SearchText = ""; }
    public void OpenResult(SearchResultRow row)
    {
        ClosePalette();
        if (row.Kind is "Std" or "Uag" or "UagKeyTask" or "Rule" or "Tc" or "Policy" or "Obligation" or "Hitlist")
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

    private void RunSearch()
    {
        SearchResults.Clear();
        if (_searchText.Trim().Length < 2) return;
        foreach (var r in App.Search.Query(_searchText))
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

    private static string NowLabel() => DateTime.Now.ToString("dd MMM yyyy — h:mm tt");
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