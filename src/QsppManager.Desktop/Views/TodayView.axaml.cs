using Avalonia.Controls;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop.Views;

public partial class TodayView : UserControl
{
    private readonly TodayViewModel _vm;

    public TodayView(ShellViewModel shell)
    {
        InitializeComponent();
        _vm = new TodayViewModel(shell, this);
        DataContext = _vm;
        _vm.Refresh();
    }

    public void Refresh() => _vm.Refresh();
}