using Avalonia.Controls;
using Avalonia.Interactivity;
using Quaero.Core.Models;
using Quaero.UI.ViewModels;

namespace Quaero.UI.Views.Components;

public partial class LeftNavPanel : UserControl
{
    private bool _synchronizingSelections;
    private bool _isDataSourcesExpanded;

    public event Action? NewSearchRequested;
    public event Action? NewChatRequested;
    public event Action? OpenSettingsRequested;
    public event Action? AddDataSourceRequested;
    public event Action<DataSourceItemViewModel>? DataSourceSelected;
    public event Action<SearchHistoryEntry>? SearchHistorySelected;
    public event Action<ChatHistoryEntry>? ChatHistorySelected;

    public LeftNavPanel()
    {
        InitializeComponent();
        DataSourcesHeaderButton.Click += OnDataSourcesHeaderClicked;
        ApplyDataSourcesSectionState();
    }

    public void ClearSelections()
    {
        _synchronizingSelections = true;
        try
        {
            DataSourcesList.SelectedItem = null;
            SearchHistoryList.SelectedItem = null;
            ChatHistoryList.SelectedItem = null;
        }
        finally
        {
            _synchronizingSelections = false;
        }
    }

    private void OnDataSourcesHeaderClicked(object? sender, RoutedEventArgs e)
    {
        _isDataSourcesExpanded = !_isDataSourcesExpanded;
        ApplyDataSourcesSectionState();
    }

    private void OnNewSearchClicked(object? sender, RoutedEventArgs e)
        => NewSearchRequested?.Invoke();

    private void OnNewChatClicked(object? sender, RoutedEventArgs e)
        => NewChatRequested?.Invoke();

    private void OnOpenSettingsClicked(object? sender, RoutedEventArgs e)
        => OpenSettingsRequested?.Invoke();

    private void OnAddDataSourceClicked(object? sender, RoutedEventArgs e)
        => AddDataSourceRequested?.Invoke();

    private void OnDataSourcesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (DataSourcesList.SelectedItem is not DataSourceItemViewModel selected) return;

        _synchronizingSelections = true;
        try
        {
            SearchHistoryList.SelectedItem = null;
            ChatHistoryList.SelectedItem = null;
        }
        finally
        {
            _synchronizingSelections = false;
        }

        DataSourceSelected?.Invoke(selected);
    }

    private void OnSearchHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (SearchHistoryList.SelectedItem is not SearchHistoryEntry selected) return;

        _synchronizingSelections = true;
        try
        {
            DataSourcesList.SelectedItem = null;
            ChatHistoryList.SelectedItem = null;
        }
        finally
        {
            _synchronizingSelections = false;
        }

        SearchHistorySelected?.Invoke(selected);
    }

    private void OnChatHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (ChatHistoryList.SelectedItem is not ChatHistoryEntry selected) return;

        _synchronizingSelections = true;
        try
        {
            DataSourcesList.SelectedItem = null;
            SearchHistoryList.SelectedItem = null;
        }
        finally
        {
            _synchronizingSelections = false;
        }

        ChatHistorySelected?.Invoke(selected);
    }

    private void ApplyDataSourcesSectionState()
    {
        DataSourcesSectionPanel.MaxHeight = _isDataSourcesExpanded ? 1200 : 0;
        DataSourcesSectionPanel.Opacity = _isDataSourcesExpanded ? 1 : 0;
        DataSourcesSectionPanel.IsHitTestVisible = _isDataSourcesExpanded;
    }
}
