using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;

namespace Quaero.UI.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IndexingService _indexingService;
    private readonly IndexStore _store;
    private readonly DataSourceStore _dataSourceStore;
    private readonly PluginLoader _pluginLoader;
    private string _searchText = string.Empty;
    private string _statusText = "Ready";
    private bool _isIndexing;
    private SearchResult? _selectedResult;

    public MainWindowViewModel()
    {
        var config = new IndexConfiguration();
        _store = new IndexStore(config);
        _dataSourceStore = new DataSourceStore();
        _pluginLoader = new PluginLoader(config.PluginsDirectory,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginLoader>.Instance);
        _indexingService = new IndexingService(_store, _dataSourceStore, _pluginLoader,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexingService>.Instance);

        // Discover plugins from plugins folder
        _pluginLoader.DiscoverPlugins();

        DataSourcesVM = new DataSourcesViewModel(_dataSourceStore, _indexingService, _store, _pluginLoader);
    }

    public DataSourcesViewModel DataSourcesVM { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                _ = SearchAsync();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsIndexing
    {
        get => _isIndexing;
        set => SetField(ref _isIndexing, value);
    }

    public SearchResult? SelectedResult
    {
        get => _selectedResult;
        set => SetField(ref _selectedResult, value);
    }

    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.Clear();
            StatusText = "Ready";
            return;
        }

        try
        {
            var query = new SearchQuery { QueryText = SearchText };
            var results = await _indexingService.SearchAsync(query);
            SearchResults.Clear();
            foreach (var result in results)
                SearchResults.Add(result);
            StatusText = $"Found {results.Count} result(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Search error: {ex.Message}";
        }
    }

    public async Task RunIndexingAsync()
    {
        IsIndexing = true;
        StatusText = "Indexing all sources...";
        try
        {
            await _indexingService.RunAllAsync();
            var count = await _store.GetDocumentCountAsync();
            StatusText = $"Indexing complete. {count} documents in index.";
            DataSourcesVM.RefreshDataSources();
        }
        catch (Exception ex)
        {
            StatusText = $"Indexing error: {ex.Message}";
        }
        finally
        {
            IsIndexing = false;
        }
    }

    public void CompactIndex()
    {
        try
        {
            _store.Compact();
            StatusText = "Index compacted successfully.";
        }
        catch (Exception ex)
        {
            StatusText = $"Compact error: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
