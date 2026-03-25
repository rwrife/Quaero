using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;
using Quaero.Plugins.Abstractions;

namespace Quaero.UI.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IndexingService _indexingService;
    private readonly IndexStore _store;
    private string _searchText = string.Empty;
    private string _statusText = "Ready";
    private bool _isIndexing;
    private SearchResult? _selectedResult;

    public MainWindowViewModel()
    {
        var config = new IndexConfiguration();
        _store = new IndexStore(config);
        _indexingService = new IndexingService(_store,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexingService>.Instance);
    }

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
    public ObservableCollection<PluginMetadata> LoadedPlugins { get; } = new();

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
        StatusText = "Indexing...";
        try
        {
            await _indexingService.RunIndexingAsync();
            var count = await _store.GetDocumentCountAsync();
            StatusText = $"Indexing complete. {count} documents in index.";
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

    public void RegisterPlugin(ISearchPlugin plugin, PluginConfiguration config)
    {
        plugin.InitializeAsync(config).GetAwaiter().GetResult();
        _indexingService.RegisterPlugin(plugin);
        LoadedPlugins.Add(plugin.Metadata);
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
