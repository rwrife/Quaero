using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Quaero.UI.Views.Panes;

public partial class IndexedFilePaneView : UserControl
{
    public event Action? BackRequested;

    public IndexedFilePaneView()
    {
        InitializeComponent();
    }

    private void OnBackToPreviousPaneClicked(object? sender, RoutedEventArgs e)
        => BackRequested?.Invoke();
}
