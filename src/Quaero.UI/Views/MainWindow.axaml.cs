using System.Linq;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Quaero.Core.Models;
using Quaero.UI.ViewModels;

namespace Quaero.UI.Views;

public partial class MainWindow : Window
{
    private bool _synchronizingSelections;
    private bool _isDataSourcesExpanded;

    public MainWindow()
    {
        InitializeComponent();
        RequestedThemeVariant = ThemeVariant.Light;
        DataSourcesHeaderButton.Click += OnDataSourcesHeaderClicked;
        Opened += OnOpened;
    }

    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private async void OnIndexAllClicked(object? sender, RoutedEventArgs e)
        => await VM.RunIndexingAsync();

    private void OnCompactClicked(object? sender, RoutedEventArgs e)
        => VM.CompactIndex();

    private async void OnAddDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var plugins = VM.DataSourcesVM.AvailablePluginTypes;
            VM.StatusText = $"DEBUG: Found {plugins.Count} plugins";
            
            if (plugins.Count == 0)
            {
                VM.StatusText = "No plugins found in the plugins folder. Place plugin DLLs there first.";
                return;
            }

            var editVM = new EditDataSourceViewModel(plugins);
            var dialog = new EditDataSourceDialog { DataContext = editVM };
            var result = await dialog.ShowDialog<bool>(this);
            if (result)
            {
                var source = editVM.ToDataSource();
                VM.DataSourcesVM.AddDataSource(source);
                VM.StatusText = $"Added data source: {editVM.Name}";
            }
        }
        catch (Exception ex)
        {
            VM.StatusText = $"ERROR: {ex.Message}";
        }
    }

    private async void OnEditDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;

        var plugins = VM.DataSourcesVM.AvailablePluginTypes;
        var editVM = new EditDataSourceViewModel(plugins, selected.Model);
        var dialog = new EditDataSourceDialog { DataContext = editVM };
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            VM.DataSourcesVM.UpdateDataSource(editVM.ToDataSource());
            VM.StatusText = $"Updated data source: {editVM.Name}";
            await VM.RefreshIndexedFilesAsync();
        }
    }

    private void OnRemoveDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.DataSourcesVM.RemoveDataSource(selected.Id);
        VM.StatusText = $"Removed data source: {selected.Name}";
    }

    private async void OnToggleDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.DataSourcesVM.ToggleDataSource(selected.Id);
        await VM.SelectDataSourceAsync(VM.DataSourcesVM.DataSources.FirstOrDefault(ds => ds.Id == selected.Id));
    }

    private async void OnRunSelectedClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        await VM.DataSourcesVM.RunSingleDataSourceAsync(selected.Id);
        await VM.RefreshSelectedDataSourceFilesAsync();
    }

    private async void OnRunAllClicked(object? sender, RoutedEventArgs e)
        => await VM.RunIndexingAsync();

    private async void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        VM.DataSourcesVM.RefreshDataSources();
        await VM.RefreshSelectedDataSourceFilesAsync();
    }

    private void OnToggleIndexerClicked(object? sender, RoutedEventArgs e)
        => VM.SettingsVM.ToggleIndexer();

    private async void OnSubmitPrimaryInputClicked(object? sender, RoutedEventArgs e)
    {
        await VM.SubmitPrimaryInputAsync();
        ApplyPaneVisibility();
    }

    private async void OnPrimaryInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await VM.SubmitPrimaryInputAsync();
        ApplyPaneVisibility();
    }

    private void OnSearchModeChecked(object? sender, RoutedEventArgs e)
    {
        VM.SelectWorkspace(useChatMode: false);
        ApplyPaneVisibility();
    }

    private void OnChatModeChecked(object? sender, RoutedEventArgs e)
    {
        VM.SelectWorkspace(useChatMode: true);
        ApplyPaneVisibility();
    }

    private async void OnDataSourcesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (DataSourcesList.SelectedItem is not DataSourceItemViewModel selected) return;
        ClearOtherSelections(DataSourcesList);
        await VM.SelectDataSourceAsync(selected);
        ApplyPaneVisibility();
    }

    private void OnDataSourcesHeaderClicked(object? sender, RoutedEventArgs e)
    {
        _isDataSourcesExpanded = !_isDataSourcesExpanded;
        ApplyDataSourcesSectionState();
    }

    private void OnSearchHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (SearchHistoryList.SelectedItem is not SearchHistoryEntry selected) return;
        ClearOtherSelections(SearchHistoryList);
        VM.SelectSearchHistory(selected);
        ApplyPaneVisibility();
    }

    private void OnChatHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (ChatHistoryList.SelectedItem is not ChatHistoryEntry selected) return;
        ClearOtherSelections(ChatHistoryList);
        VM.SelectChatHistory(selected);
        ApplyPaneVisibility();
    }

    private async void OnRefreshDataSourceFilesClicked(object? sender, RoutedEventArgs e)
        => await VM.RefreshSelectedDataSourceFilesAsync();

    private async void OnLoadMoreDataSourceFilesClicked(object? sender, RoutedEventArgs e)
        => await VM.LoadMoreSelectedDataSourceFilesAsync();

    private async void OnDataSourceFilesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        if (scrollViewer.Extent.Height <= 0)
            return;

        var nearBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 80;
        if (nearBottom)
            await VM.LoadMoreSelectedDataSourceFilesAsync();
    }

    private void OnDataSourceIndexedFilesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        if (listBox.SelectedItem is not SearchResult selected)
            return;

        VM.SelectIndexedFile(selected);
        listBox.SelectedItem = null;
        ApplyPaneVisibility();
    }

    private void OnBackToPreviousPaneClicked(object? sender, RoutedEventArgs e)
    {
        VM.GoBack();
        ApplyPaneVisibility();
    }

    private async void OnRerunSearchHistoryClicked(object? sender, RoutedEventArgs e)
    {
        await VM.RerunSelectedSearchHistoryAsync();
        ApplyPaneVisibility();
    }

    private void OnReuseChatPromptClicked(object? sender, RoutedEventArgs e)
    {
        VM.ReuseSelectedChatPrompt();
        ApplyPaneVisibility();
    }

    private void OnNewSearchClicked(object? sender, RoutedEventArgs e)
    {
        ClearOtherSelections(null);
        VM.StartNewSearch();
        ApplyPaneVisibility();
    }

    private void OnNewChatClicked(object? sender, RoutedEventArgs e)
    {
        ClearOtherSelections(null);
        VM.StartNewChat();
        ApplyPaneVisibility();
    }

    private void OnOpenSettingsClicked(object? sender, RoutedEventArgs e)
    {
        ClearOtherSelections(null);
        VM.SelectSettings();
        ApplyPaneVisibility();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        HookVm();
    }

    private void ClearOtherSelections(ListBox? keep)
    {
        _synchronizingSelections = true;
        try
        {
            if (keep != DataSourcesList) DataSourcesList.SelectedItem = null;
            if (keep != SearchHistoryList) SearchHistoryList.SelectedItem = null;
            if (keep != ChatHistoryList) ChatHistoryList.SelectedItem = null;
        }
        finally
        {
            _synchronizingSelections = false;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            vm.SettingsVM.Shutdown();
        }
        base.OnClosing(e);
    }

    private void HookVm()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        ApplyPaneVisibility();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ActivePane)
            or nameof(MainWindowViewModel.IsWorkspacePaneVisible)
            or nameof(MainWindowViewModel.IsDataSourcePaneVisible)
            or nameof(MainWindowViewModel.IsIndexedFilePaneVisible)
            or nameof(MainWindowViewModel.IsSearchHistoryPaneVisible)
            or nameof(MainWindowViewModel.IsChatHistoryPaneVisible)
            or nameof(MainWindowViewModel.IsSettingsPaneVisible))
        {
            Dispatcher.UIThread.Post(ApplyPaneVisibility);
        }
    }

    private void ApplyPaneVisibility()
    {
        if (!IsLoaded)
            return;

        if (DataContext is not MainWindowViewModel vm)
            return;

        if (WorkspacePane is null
            || DataSourcePane is null
            || IndexedFilePane is null
            || SearchHistoryPane is null
            || ChatHistoryPane is null
            || SettingsPane is null)
            return;

        WorkspacePane.IsVisible = vm.IsWorkspacePaneVisible;
        DataSourcePane.IsVisible = vm.IsDataSourcePaneVisible;
        IndexedFilePane.IsVisible = vm.IsIndexedFilePaneVisible;
        SearchHistoryPane.IsVisible = vm.IsSearchHistoryPaneVisible;
        ChatHistoryPane.IsVisible = vm.IsChatHistoryPaneVisible;
        SettingsPane.IsVisible = vm.IsSettingsPaneVisible;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        HookVm();
        ApplyPaneVisibility();
        ApplyDataSourcesSectionState();
    }

    private void ApplyDataSourcesSectionState()
    {
        if (DataSourcesSectionPanel is null)
            return;

        DataSourcesSectionPanel.MaxHeight = _isDataSourcesExpanded ? 1200 : 0;
        DataSourcesSectionPanel.Opacity = _isDataSourcesExpanded ? 1 : 0;
        DataSourcesSectionPanel.IsHitTestVisible = _isDataSourcesExpanded;        
    }
}
