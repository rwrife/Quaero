using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;

namespace Quaero.UI.ViewModels;

/// <summary>
/// Simple in-memory logger for diagnostics
/// </summary>
public class SimpleLogger<T> : ILogger<T>
{
    private readonly Action<string> _onLog;
    private readonly LogLevel _minLevel;

    public SimpleLogger(Action<string> onLog, LogLevel minLevel = LogLevel.Information)
    {
        _onLog = onLog;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        _onLog($"[{logLevel}] {message}");
        if (exception != null)
            _onLog($"Exception: {exception.Message}");
    }
}

public enum DetailPaneKind
{
    Workspace,
    DataSource,
    IndexedFile,
    SearchHistory,
    ChatHistory,
    Settings
}

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IndexingService _indexingService;
    private readonly IndexStore _store;
    private readonly DataSourceStore _dataSourceStore;
    private readonly PluginLoader _pluginLoader;
    private string _primaryInputText = string.Empty;
    private string _statusText = "Ready";
    private bool _isIndexing;
    private bool _isChatMode;
    private DetailPaneKind _activePane = DetailPaneKind.Workspace;
    private DetailPaneKind? _previousPane;
    private SearchResult? _selectedResult;
    private DataSourceItemViewModel? _selectedDataSource;
    private bool _isLoadingDataSourceFiles;
    private bool _hasMoreDataSourceFiles;
    private int _dataSourceFilesOffset;
    private bool _useProviderFallbackForDataSourceFiles;
    private string? _providerFallbackValue;
    private bool _useUnfilteredFallbackForDataSourceFiles;
    private readonly Dictionary<string, string> _dataSourceDebugLogById = new();
    private string _dataSourceDebugLog = string.Empty;
    private int _selectedDataSourceIndexedItemCount;
    private SearchResult? _selectedIndexedFile;
    private SearchHistoryEntry? _selectedSearchHistory;
    private ChatHistoryEntry? _selectedChatHistory;

    public MainWindowViewModel()
    {
        var config = new IndexConfiguration();
        _store = new IndexStore(config);
        _dataSourceStore = new DataSourceStore();
        
        // Create a logger that outputs to StatusText
        var pluginLogger = new SimpleLogger<PluginLoader>(msg => StatusText = msg, LogLevel.Debug);
        var indexLogger = new SimpleLogger<IndexingService>(msg => StatusText = msg, LogLevel.Debug);
        
        _pluginLoader = new PluginLoader(config.PluginsDirectory, pluginLogger);
        _indexingService = new IndexingService(_store, _dataSourceStore, _pluginLoader, indexLogger);

        // Discover plugins from plugins folder
        var discovered = _pluginLoader.DiscoverPlugins();
        StatusText = $"Ready. Found {discovered.Count} plugin(s).";

        DataSourcesVM = new DataSourcesViewModel(_dataSourceStore, _indexingService, _store, _pluginLoader);
        SettingsVM = new SettingsViewModel(config);

        ChatMessages.Add(new ChatMessageEntry
        {
            Role = "Assistant",
            Message = "Ask a question and I will search your local index to build an answer."
        });

        _ = RefreshIndexedFilesAsync();
    }

    public DataSourcesViewModel DataSourcesVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public string PrimaryInputText
    {
        get => _primaryInputText;
        set => SetField(ref _primaryInputText, value);
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

    public DataSourceItemViewModel? SelectedDataSource
    {
        get => _selectedDataSource;
        private set => SetField(ref _selectedDataSource, value);
    }

    public SearchResult? SelectedIndexedFile
    {
        get => _selectedIndexedFile;
        private set => SetField(ref _selectedIndexedFile, value);
    }

    public SearchHistoryEntry? SelectedSearchHistory
    {
        get => _selectedSearchHistory;
        private set => SetField(ref _selectedSearchHistory, value);
    }

    public ChatHistoryEntry? SelectedChatHistory
    {
        get => _selectedChatHistory;
        private set => SetField(ref _selectedChatHistory, value);
    }

    public bool IsChatMode
    {
        get => _isChatMode;
        private set
        {
            if (SetField(ref _isChatMode, value))
            {
                OnPropertyChanged(nameof(IsSearchMode));
                OnPropertyChanged(nameof(PrimaryInputPlaceholder));
                OnPropertyChanged(nameof(PrimaryActionText));
            }
        }
    }

    public bool IsSearchMode => !IsChatMode;
    public string PrimaryInputPlaceholder => IsChatMode
        ? "Ask about your indexed content..."
        : "Search indexed files, emails, and notes...";
    public string PrimaryActionText => IsChatMode ? "Send" : "Search";
    public DetailPaneKind ActivePane => _activePane;
    public bool IsWorkspacePaneVisible => _activePane == DetailPaneKind.Workspace;
    public bool IsDataSourcePaneVisible => _activePane == DetailPaneKind.DataSource;
    public bool IsIndexedFilePaneVisible => _activePane == DetailPaneKind.IndexedFile;
    public bool IsSearchHistoryPaneVisible => _activePane == DetailPaneKind.SearchHistory;
    public bool IsChatHistoryPaneVisible => _activePane == DetailPaneKind.ChatHistory;
    public bool IsSettingsPaneVisible => _activePane == DetailPaneKind.Settings;
    public bool CanGoBack => _previousPane.HasValue;

    public bool IsLoadingDataSourceFiles
    {
        get => _isLoadingDataSourceFiles;
        private set => SetField(ref _isLoadingDataSourceFiles, value);
    }

    public bool HasMoreDataSourceFiles
    {
        get => _hasMoreDataSourceFiles;
        private set => SetField(ref _hasMoreDataSourceFiles, value);
    }

    public int SelectedDataSourceIndexedItemCount
    {
        get => _selectedDataSourceIndexedItemCount;
        private set
        {
            if (SetField(ref _selectedDataSourceIndexedItemCount, value))
                OnPropertyChanged(nameof(SelectedDataSourceIndexedItemCountText));
        }
    }

    public string SelectedDataSourceIndexedItemCountText => SelectedDataSourceIndexedItemCount.ToString("N0");
    public string DataSourceDebugLog
    {
        get => _dataSourceDebugLog;
        private set => SetField(ref _dataSourceDebugLog, value);
    }

    public ObservableCollection<SearchResult> IndexedFiles { get; } = new();
    public ObservableCollection<SearchResult> SelectedDataSourceIndexedFiles { get; } = new();
    public ObservableCollection<SearchHistoryEntry> SearchHistory { get; } = new();
    public ObservableCollection<ChatHistoryEntry> ChatHistory { get; } = new();
    public ObservableCollection<ChatMessageEntry> ChatMessages { get; } = new();
    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    private const int InitialDataSourceFilePageSize = 500;
    private const int DataSourceFilePageSize = 200;

    public void SelectWorkspace(bool useChatMode)
    {
        IsChatMode = useChatMode;
        _previousPane = null;
        OnPropertyChanged(nameof(CanGoBack));
        SetActivePane(DetailPaneKind.Workspace);
    }

    public void StartNewSearch()
    {
        _previousPane = null;
        OnPropertyChanged(nameof(CanGoBack));
        IsChatMode = false;
        PrimaryInputText = string.Empty;
        SearchResults.Clear();
        SelectedResult = null;
        SetActivePane(DetailPaneKind.Workspace);
        StatusText = "Ready for a new search.";
    }

    public void StartNewChat()
    {
        _previousPane = null;
        OnPropertyChanged(nameof(CanGoBack));
        IsChatMode = true;
        PrimaryInputText = string.Empty;
        ChatMessages.Clear();
        ChatMessages.Add(new ChatMessageEntry
        {
            Role = "Assistant",
            Message = "New chat started. Ask anything about your indexed content."
        });
        SetActivePane(DetailPaneKind.Workspace);
        StatusText = "New chat started.";
    }

    public async Task SelectDataSourceAsync(DataSourceItemViewModel? dataSource)
    {
        if (dataSource == null) return;
        _previousPane = null;
        OnPropertyChanged(nameof(CanGoBack));
        SelectedDataSource = dataSource;
        DataSourcesVM.SelectedDataSource = dataSource;
        SelectedIndexedFile = null;
        SelectedSearchHistory = null;
        SelectedChatHistory = null;
        SetActivePane(DetailPaneKind.DataSource);
        DataSourceDebugLog = _dataSourceDebugLogById.GetValueOrDefault(dataSource.Id, string.Empty);
        AppendDataSourceDebugLog($"Selected data source '{dataSource.Name}' ({dataSource.Id}).");
        await LoadSelectedDataSourceFilesAsync(reset: true);
    }

    public void SelectIndexedFile(SearchResult? indexedFile)
    {
        if (indexedFile == null) return;
        _previousPane = _activePane;
        OnPropertyChanged(nameof(CanGoBack));
        SelectedIndexedFile = indexedFile;
        SelectedSearchHistory = null;
        SelectedChatHistory = null;
        SetActivePane(DetailPaneKind.IndexedFile);
    }

    public void GoBack()
    {
        if (!_previousPane.HasValue)
            return;

        var pane = _previousPane.Value;
        _previousPane = null;
        OnPropertyChanged(nameof(CanGoBack));
        SetActivePane(pane);
    }

    public void SelectSearchHistory(SearchHistoryEntry? historyEntry)
    {
        if (historyEntry == null) return;
        SelectedSearchHistory = historyEntry;
        SelectedDataSource = null;
        SelectedIndexedFile = null;
        SelectedChatHistory = null;
        SetActivePane(DetailPaneKind.SearchHistory);
    }

    public void SelectChatHistory(ChatHistoryEntry? historyEntry)
    {
        if (historyEntry == null) return;
        SelectedChatHistory = historyEntry;
        SelectedDataSource = null;
        SelectedIndexedFile = null;
        SelectedSearchHistory = null;
        SetActivePane(DetailPaneKind.ChatHistory);
    }

    public void SelectSettings()
    {
        _previousPane = null;
        OnPropertyChanged(nameof(CanGoBack));
        SelectedDataSource = null;
        SelectedIndexedFile = null;
        SelectedSearchHistory = null;
        SelectedChatHistory = null;
        SetActivePane(DetailPaneKind.Settings);
    }

    public async Task SubmitPrimaryInputAsync()
    {
        var text = PrimaryInputText.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "Enter a search query or chat prompt.";
            return;
        }

        SetActivePane(DetailPaneKind.Workspace);
        if (IsChatMode)
            await RunChatPromptAsync(text);
        else
            await SearchAsync(text, trackHistory: true);
    }

    public async Task SearchAsync(string query, bool trackHistory)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults.Clear();
            SelectedResult = null;
            StatusText = "Ready";
            return;
        }

        try
        {
            var searchQuery = new SearchQuery { QueryText = query };
            var results = await _indexingService.SearchAsync(searchQuery);
            SearchResults.Clear();
            foreach (var result in results)
                SearchResults.Add(result);
            SelectedResult = SearchResults.FirstOrDefault();

            if (trackHistory)
            {
                SearchHistory.Insert(0, new SearchHistoryEntry
                {
                    Query = query,
                    RanAt = DateTime.Now,
                    ResultCount = results.Count
                });
                TrimToLimit(SearchHistory, 30);
            }

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
            await RefreshIndexedFilesAsync();
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

    public async Task RefreshIndexedFilesAsync()
    {
        if (SelectedDataSource != null)
            await LoadSelectedDataSourceFilesAsync(reset: true);
    }

    public async Task RefreshSelectedDataSourceFilesAsync()
        => await LoadSelectedDataSourceFilesAsync(reset: true);

    public async Task LoadMoreSelectedDataSourceFilesAsync()
    {
        if (!HasMoreDataSourceFiles || IsLoadingDataSourceFiles || SelectedDataSource == null)
            return;

        await LoadSelectedDataSourceFilesAsync(reset: false);
    }

    private async Task LoadSelectedDataSourceFilesAsync(bool reset)
    {
        if (SelectedDataSource == null)
            return;

        if (IsLoadingDataSourceFiles)
            return;

        IsLoadingDataSourceFiles = true;
        try
        {
            if (reset)
            {
                _dataSourceFilesOffset = 0;
                _useProviderFallbackForDataSourceFiles = false;
                _providerFallbackValue = null;
                _useUnfilteredFallbackForDataSourceFiles = false;
                SelectedDataSourceIndexedItemCount = 0;
                SelectedDataSourceIndexedFiles.Clear();
            }

            var pageSize = reset ? InitialDataSourceFilePageSize : DataSourceFilePageSize;

            var results = await _indexingService.SearchAsync(new SearchQuery
            {
                DataSourceId = SelectedDataSource.Id,
                MaxResults = pageSize,
                Offset = _dataSourceFilesOffset
            });
            AppendDataSourceDebugLog($"Query by DataSourceId returned {results.Count} item(s) at offset {_dataSourceFilesOffset}.");

            if (reset && results.Count == 0)
            {
                results = await _indexingService.SearchAsync(new SearchQuery
                {
                    DataSourceName = SelectedDataSource.Name,
                    MaxResults = pageSize,
                    Offset = 0
                });
                AppendDataSourceDebugLog($"Fallback query by DataSourceName returned {results.Count} item(s).");

                if (results.Count > 0)
                {
                    _useProviderFallbackForDataSourceFiles = true;
                    _useUnfilteredFallbackForDataSourceFiles = false;
                    _providerFallbackValue = SelectedDataSource.Name;
                }
                else
                {
                    var fallbackProviders = new[]
                    {
                        SelectedDataSource.Model.Settings.GetValueOrDefault("Provider"),
                        SelectedDataSource.PluginName,
                        SelectedDataSource.PluginName.ToLowerInvariant(),
                        SelectedDataSource.Model.PluginType,
                        SelectedDataSource.Model.PluginAssembly
                    }.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct();

                    foreach (var provider in fallbackProviders)
                    {
                        results = await _indexingService.SearchAsync(new SearchQuery
                        {
                            Provider = provider,
                            MaxResults = pageSize,
                            Offset = 0
                        });
                        AppendDataSourceDebugLog($"Fallback query by Provider '{provider}' returned {results.Count} item(s).");

                        if (results.Count > 0)
                        {
                            _useProviderFallbackForDataSourceFiles = false;
                            _useUnfilteredFallbackForDataSourceFiles = true;
                            _providerFallbackValue = provider;
                            break;
                        }
                    }
                }
            }
            else if (_useProviderFallbackForDataSourceFiles && !string.IsNullOrWhiteSpace(_providerFallbackValue))
            {
                results = await _indexingService.SearchAsync(new SearchQuery
                {
                    DataSourceName = _providerFallbackValue,
                    MaxResults = pageSize,
                    Offset = _dataSourceFilesOffset
                });
                AppendDataSourceDebugLog($"Paged fallback by DataSourceName returned {results.Count} item(s) at offset {_dataSourceFilesOffset}.");
            }
            else if (_useUnfilteredFallbackForDataSourceFiles && !string.IsNullOrWhiteSpace(_providerFallbackValue))
            {
                results = await _indexingService.SearchAsync(new SearchQuery
                {
                    Provider = _providerFallbackValue,
                    MaxResults = pageSize,
                    Offset = _dataSourceFilesOffset
                });
                AppendDataSourceDebugLog($"Paged fallback by Provider '{_providerFallbackValue}' returned {results.Count} item(s) at offset {_dataSourceFilesOffset}.");
            }

            var countQuery = new SearchQuery
            {
                DataSourceId = SelectedDataSource.Id
            };

            if (_useProviderFallbackForDataSourceFiles && !string.IsNullOrWhiteSpace(_providerFallbackValue))
            {
                countQuery = new SearchQuery
                {
                    DataSourceName = _providerFallbackValue
                };
            }
            else if (_useUnfilteredFallbackForDataSourceFiles && !string.IsNullOrWhiteSpace(_providerFallbackValue))
            {
                countQuery = new SearchQuery
                {
                    Provider = _providerFallbackValue
                };
            }

            SelectedDataSourceIndexedItemCount = await _store.GetDocumentCountAsync(countQuery);
            AppendDataSourceDebugLog($"Count query returned {SelectedDataSourceIndexedItemCount} total item(s).");

            foreach (var result in results)
                SelectedDataSourceIndexedFiles.Add(result);

            _dataSourceFilesOffset += results.Count;
            HasMoreDataSourceFiles = results.Count == pageSize;
            StatusText = $"Loaded {SelectedDataSourceIndexedFiles.Count} file(s) for {SelectedDataSource.Name}.";
            AppendDataSourceDebugLog(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Unable to load files for {SelectedDataSource.Name}: {ex.Message}";
            AppendDataSourceDebugLog(StatusText);
        }
        finally
        {
            IsLoadingDataSourceFiles = false;
        }
    }

    public void AppendDataSourceDebugLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (string.IsNullOrWhiteSpace(DataSourceDebugLog))
            DataSourceDebugLog = line;
        else
            DataSourceDebugLog += Environment.NewLine + line;

        var lines = DataSourceDebugLog.Split(Environment.NewLine);
        if (lines.Length > 300)
            DataSourceDebugLog = string.Join(Environment.NewLine, lines[^300..]);

        if (!string.IsNullOrWhiteSpace(SelectedDataSource?.Id))
            _dataSourceDebugLogById[SelectedDataSource.Id] = DataSourceDebugLog;
    }

    public async Task RerunSelectedSearchHistoryAsync()
    {
        if (SelectedSearchHistory == null) return;
        IsChatMode = false;
        PrimaryInputText = SelectedSearchHistory.Query;
        await SearchAsync(SelectedSearchHistory.Query, trackHistory: false);
        SetActivePane(DetailPaneKind.Workspace);
    }

    public void ReuseSelectedChatPrompt()
    {
        if (SelectedChatHistory == null) return;
        IsChatMode = true;
        PrimaryInputText = SelectedChatHistory.Prompt;
        SetActivePane(DetailPaneKind.Workspace);
        StatusText = "Prompt moved to chat input.";
    }

    private async Task RunChatPromptAsync(string prompt)
    {
        try
        {
            ChatMessages.Add(new ChatMessageEntry
            {
                Role = "You",
                Message = prompt
            });

            PrimaryInputText = string.Empty;

            var results = await _indexingService.SearchAsync(new SearchQuery
            {
                QueryText = prompt,
                MaxResults = 5
            });

            SearchResults.Clear();
            foreach (var result in results)
                SearchResults.Add(result);
            SelectedResult = SearchResults.FirstOrDefault();

            var assistantReply = BuildAssistantReply(results);
            ChatMessages.Add(new ChatMessageEntry
            {
                Role = "Assistant",
                Message = assistantReply
            });

            ChatHistory.Insert(0, new ChatHistoryEntry
            {
                Prompt = prompt,
                Response = assistantReply,
                RanAt = DateTime.Now,
                ResultCount = results.Count
            });
            TrimToLimit(ChatHistory, 30);

            StatusText = results.Count > 0
                ? $"Chat grounded in {results.Count} indexed result(s)."
                : "No matching indexed context found.";
        }
        catch (Exception ex)
        {
            StatusText = $"Chat error: {ex.Message}";
        }
    }

    private static string BuildAssistantReply(IReadOnlyList<SearchResult> results)
    {
        if (results.Count == 0)
            return "I could not find relevant indexed content. Try broader keywords or index more sources.";

        var lines = new List<string>
        {
            "I found relevant indexed content:"
        };

        foreach (var result in results.Take(3))
        {
            var summary = result.Document.Summary;
            if (summary.Length > 140)
                summary = summary[..137] + "...";

            lines.Add($"- {result.Document.Title} ({result.Document.Provider}): {summary}");
        }

        lines.Add("Select a file from the left panel to open full details.");
        return string.Join(Environment.NewLine, lines);
    }

    private void SetActivePane(DetailPaneKind pane)
    {
        if (_activePane == pane) return;
        _activePane = pane;
        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(IsWorkspacePaneVisible));
        OnPropertyChanged(nameof(IsDataSourcePaneVisible));
        OnPropertyChanged(nameof(IsIndexedFilePaneVisible));
        OnPropertyChanged(nameof(IsSearchHistoryPaneVisible));
        OnPropertyChanged(nameof(IsChatHistoryPaneVisible));
        OnPropertyChanged(nameof(IsSettingsPaneVisible));
    }

    private static void TrimToLimit<T>(ObservableCollection<T> collection, int max)
    {
        while (collection.Count > max)
            collection.RemoveAt(collection.Count - 1);
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

public class SearchHistoryEntry
{
    public string Query { get; set; } = string.Empty;
    public DateTime RanAt { get; set; }
    public int ResultCount { get; set; }
    public string ResultLabel => $"{ResultCount} result(s)";
}

public class ChatHistoryEntry
{
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public DateTime RanAt { get; set; }
    public int ResultCount { get; set; }
    public string ResultLabel => $"{ResultCount} source hit(s)";
}

public class ChatMessageEntry
{
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
