using Avalonia.Controls;
using Avalonia.Interactivity;
using Quaero.Core.Models;

namespace Quaero.UI.Views.Panes;

public partial class DataSourcePaneView : UserControl
{
    public event Action? EditDataSourceRequested;
    public event Action? RemoveDataSourceRequested;
    public event Action? ToggleDataSourceRequested;
    public event Action? RunSelectedRequested;
    public event Action? RefreshDataSourceFilesRequested;
    public event Action? LoadMoreDataSourceFilesRequested;
    public event Action<SearchResult>? IndexedFileSelected;

    public DataSourcePaneView()
    {
        InitializeComponent();
    }

    private void OnEditDataSourceClicked(object? sender, RoutedEventArgs e)
        => EditDataSourceRequested?.Invoke();

    private void OnRemoveDataSourceClicked(object? sender, RoutedEventArgs e)
        => RemoveDataSourceRequested?.Invoke();

    private void OnToggleDataSourceClicked(object? sender, RoutedEventArgs e)
        => ToggleDataSourceRequested?.Invoke();

    private void OnRunSelectedClicked(object? sender, RoutedEventArgs e)
        => RunSelectedRequested?.Invoke();

    private void OnRefreshDataSourceFilesClicked(object? sender, RoutedEventArgs e)
        => RefreshDataSourceFilesRequested?.Invoke();

    private void OnLoadMoreDataSourceFilesClicked(object? sender, RoutedEventArgs e)
        => LoadMoreDataSourceFilesRequested?.Invoke();

    private void OnDataSourceFilesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        if (scrollViewer.Extent.Height <= 0)
            return;

        var nearBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 80;
        if (nearBottom)
            LoadMoreDataSourceFilesRequested?.Invoke();
    }

    private void OnDataSourceIndexedFilesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        if (listBox.SelectedItem is not SearchResult selected)
            return;

        IndexedFileSelected?.Invoke(selected);
        listBox.SelectedItem = null;
    }
}
