using System.Linq;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Quaero.Core.Models;
using Quaero.UI.ViewModels;

namespace Quaero.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RequestedThemeVariant = ThemeVariant.Light;
        HookEvents();
        Opened += OnOpened;
    }

    private MainWindowViewModel VM => (MainWindowViewModel)DataContext!;

    private void HookEvents()
    {
        LeftNavPanel.NewSearchRequested += OnNewSearchRequested;
        LeftNavPanel.NewChatRequested += OnNewChatRequested;
        LeftNavPanel.OpenSettingsRequested += OnOpenSettingsRequested;
        LeftNavPanel.AddDataSourceRequested += OnAddDataSourceRequested;
        LeftNavPanel.DataSourceSelected += OnDataSourceSelected;
        LeftNavPanel.SearchHistorySelected += OnSearchHistorySelected;
        LeftNavPanel.ChatHistorySelected += OnChatHistorySelected;

        WorkspacePane.SearchModeRequested += OnSearchModeRequested;
        WorkspacePane.ChatModeRequested += OnChatModeRequested;
        WorkspacePane.SubmitPrimaryInputRequested += OnSubmitPrimaryInputRequested;

        DataSourcePane.EditDataSourceRequested += OnEditDataSourceRequested;
        DataSourcePane.RemoveDataSourceRequested += OnRemoveDataSourceRequested;
        DataSourcePane.ToggleDataSourceRequested += OnToggleDataSourceRequested;
        DataSourcePane.RunSelectedRequested += OnRunSelectedRequested;
        DataSourcePane.RefreshDataSourceFilesRequested += OnRefreshDataSourceFilesRequested;
        DataSourcePane.LoadMoreDataSourceFilesRequested += OnLoadMoreDataSourceFilesRequested;
        DataSourcePane.IndexedFileSelected += OnIndexedFileSelected;

        IndexedFilePane.BackRequested += OnBackRequested;
        SearchHistoryPane.RerunRequested += OnRerunSearchHistoryRequested;
        ChatHistoryPane.ReusePromptRequested += OnReuseChatPromptRequested;

        SettingsPane.ToggleIndexerRequested += OnToggleIndexerRequested;
        SettingsPane.CompactRequested += OnCompactRequested;
        SettingsPane.IndexAllRequested += OnIndexAllRequested;
        SettingsPane.RefreshRequested += OnRefreshRequested;
        SettingsPane.GoogleSignInRequested += OnGoogleSignInRequested;
    }

    private async void OnIndexAllRequested()
        => await VM.RunIndexingAsync();

    private void OnCompactRequested()
        => VM.CompactIndex();

    private async void OnAddDataSourceRequested()
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

    private async void OnEditDataSourceRequested()
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

    private void OnRemoveDataSourceRequested()
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.DataSourcesVM.RemoveDataSource(selected.Id);
        VM.StatusText = $"Removed data source: {selected.Name}";
    }

    private async void OnToggleDataSourceRequested()
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.DataSourcesVM.ToggleDataSource(selected.Id);
        await VM.SelectDataSourceAsync(VM.DataSourcesVM.DataSources.FirstOrDefault(ds => ds.Id == selected.Id));
    }

    private async void OnRunSelectedRequested()
    {
        var selected = VM.DataSourcesVM.SelectedDataSource;
        if (selected == null) return;
        VM.AppendDataSourceDebugLog($"Run requested for '{selected.Name}' ({selected.Id}).");
        await VM.DataSourcesVM.RunSingleDataSourceAsync(selected.Id);

        var refreshed = VM.DataSourcesVM.DataSources.FirstOrDefault(ds => ds.Id == selected.Id);
        if (refreshed?.LatestRun != null)
        {
            VM.AppendDataSourceDebugLog(
                $"Run status: {refreshed.LatestRun.Status}, docs indexed this run: {refreshed.LatestRun.DocumentCount}, error: {refreshed.LatestRun.ErrorMessage ?? "(none)"}.");
        }

        VM.AppendDataSourceDebugLog($"Run finished for '{selected.Name}'. Refreshing indexed files list.");
        await VM.RefreshSelectedDataSourceFilesAsync();
        VM.AppendDataSourceDebugLog("Refresh complete.");
    }

    private async void OnRefreshRequested()
    {
        VM.DataSourcesVM.RefreshDataSources();
        await VM.RefreshSelectedDataSourceFilesAsync();
    }

    private void OnToggleIndexerRequested()
        => VM.SettingsVM.ToggleIndexer();

    private async Task OnGoogleSignInRequested()
        => await VM.SettingsVM.SignInWithGoogleAsync();

    private async void OnSubmitPrimaryInputRequested()
    {
        await VM.SubmitPrimaryInputAsync();
        ApplyPaneVisibility();
    }

    private void OnSearchModeRequested()
    {
        VM.SelectWorkspace(useChatMode: false);
        ApplyPaneVisibility();
    }

    private void OnChatModeRequested()
    {
        VM.SelectWorkspace(useChatMode: true);
        ApplyPaneVisibility();
    }

    private async void OnDataSourceSelected(DataSourceItemViewModel selected)
    {
        await VM.SelectDataSourceAsync(selected);
        ApplyPaneVisibility();
    }

    private void OnSearchHistorySelected(SearchHistoryEntry selected)
    {
        VM.SelectSearchHistory(selected);
        ApplyPaneVisibility();
    }

    private void OnChatHistorySelected(ChatHistoryEntry selected)
    {
        VM.SelectChatHistory(selected);
        ApplyPaneVisibility();
    }

    private async void OnRefreshDataSourceFilesRequested()
        => await VM.RefreshSelectedDataSourceFilesAsync();

    private async void OnLoadMoreDataSourceFilesRequested()
        => await VM.LoadMoreSelectedDataSourceFilesAsync();

    private void OnIndexedFileSelected(SearchResult selected)
    {
        VM.SelectIndexedFile(selected);
        ApplyPaneVisibility();
    }

    private void OnBackRequested()
    {
        VM.GoBack();
        ApplyPaneVisibility();
    }

    private async void OnRerunSearchHistoryRequested()
    {
        await VM.RerunSelectedSearchHistoryAsync();
        ApplyPaneVisibility();
    }

    private void OnReuseChatPromptRequested()
    {
        VM.ReuseSelectedChatPrompt();
        ApplyPaneVisibility();
    }

    private void OnNewSearchRequested()
    {
        LeftNavPanel.ClearSelections();
        VM.StartNewSearch();
        ApplyPaneVisibility();
    }

    private void OnNewChatRequested()
    {
        LeftNavPanel.ClearSelections();
        VM.StartNewChat();
        ApplyPaneVisibility();
    }

    private void OnOpenSettingsRequested()
    {
        LeftNavPanel.ClearSelections();
        VM.SelectSettings();
        ApplyPaneVisibility();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        HookVm();
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
