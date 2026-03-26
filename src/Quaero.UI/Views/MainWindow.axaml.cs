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

    public MainWindow()
    {
        InitializeComponent();
        RequestedThemeVariant = ThemeVariant.Light;
        Opened += OnOpened;
    }

    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private async void OnIndexAllClicked(object? sender, RoutedEventArgs e)
        => await VM.RunIndexingAsync();

    private void OnCompactClicked(object? sender, RoutedEventArgs e)
        => VM.CompactIndex();

    private async void OnAddDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var plugins = VM.DataSourcesVM.AvailablePluginTypes;
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

    private void OnToggleDataSourceClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.DataSourcesVM.ToggleDataSource(selected.Id);
        VM.SelectDataSource(VM.DataSourcesVM.DataSources.FirstOrDefault(ds => ds.Id == selected.Id));
    }

    private async void OnRunSelectedClicked(object? sender, RoutedEventArgs e)
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        await VM.DataSourcesVM.RunSingleDataSourceAsync(selected.Id);
        await VM.RefreshIndexedFilesAsync();
    }

    private async void OnRunAllClicked(object? sender, RoutedEventArgs e)
        => await VM.RunIndexingAsync();

    private async void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        VM.DataSourcesVM.RefreshDataSources();
        await VM.RefreshIndexedFilesAsync();
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

    private void OnDataSourcesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (DataSourcesList.SelectedItem is not DataSourceItemViewModel selected) return;
        ClearOtherSelections(DataSourcesList);
        VM.SelectDataSource(selected);
    }

    private void OnIndexedFilesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (IndexedFilesList.SelectedItem is not SearchResult selected) return;
        ClearOtherSelections(IndexedFilesList);
        VM.SelectIndexedFile(selected);
    }

    private void OnSearchHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (SearchHistoryList.SelectedItem is not SearchHistoryEntry selected) return;
        ClearOtherSelections(SearchHistoryList);
        VM.SelectSearchHistory(selected);
    }

    private void OnChatHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSelections) return;
        if (ChatHistoryList.SelectedItem is not ChatHistoryEntry selected) return;
        ClearOtherSelections(ChatHistoryList);
        VM.SelectChatHistory(selected);
    }

    private async void OnRefreshIndexedFilesClicked(object? sender, RoutedEventArgs e)
        => await VM.RefreshIndexedFilesAsync();

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
            if (keep != IndexedFilesList) IndexedFilesList.SelectedItem = null;
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
    }
}
