using Avalonia.Controls;
using QsppManager.Desktop.ViewModels;

namespace QsppManager.Desktop.Views;

public record PlaceholderModel(string Title, string Text);

public partial class PlaceholdersView : UserControl
{
    public PlaceholdersView(string title, string text)
    {
        InitializeComponent();
        DataContext = new PlaceholderModel(title, text);
    }
}