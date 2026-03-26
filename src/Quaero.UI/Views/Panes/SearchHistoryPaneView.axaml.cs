using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Quaero.UI.Views.Panes;

public partial class SearchHistoryPaneView : UserControl
{
    public event Action? RerunRequested;

    public SearchHistoryPaneView()
    {
        InitializeComponent();
    }

    private void OnRerunSearchHistoryClicked(object? sender, RoutedEventArgs e)
        => RerunRequested?.Invoke();
}
