using Avalonia.Controls;
using Avalonia.Threading;
using QsppManager.Desktop.ViewModels;
using QsppManager.Domain.Services;

namespace QsppManager.Desktop.Views;

/// <summary>Hosts the code-built Today screen (TodayScreen). Refreshes every 30 s so
/// dates roll over across Melbourne midnight and activity recency stays honest; the
/// planning queries are cheap and read-only.</summary>
public partial class TodayView : UserControl
{
    private readonly TodayViewModel _vm;
    private readonly TodayScreen _screen;
    private DispatcherTimer? _timer;

    public TodayView(ShellViewModel shell)
    {
        InitializeComponent();
        _vm = new TodayViewModel(shell, this);
        DataContext = _vm;
        _screen = new TodayScreen(_vm);
        Root.Children.Add(_screen.Root);
        _vm.Refresh();
        _screen.PostRefresh(_vm);

        SizeChanged += (_, e) => _screen?.OnWidthChanged(e.NewSize.Width);
        AttachedToLogicalTree += (_, _) => StartTimer();
        DetachedFromLogicalTree += (_, _) => StopTimer();
    }

    public void Refresh()
    {
        _vm.Refresh();
        _screen.PostRefresh(_vm);
    }

    private void StartTimer()
    {
        StopTimer();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}