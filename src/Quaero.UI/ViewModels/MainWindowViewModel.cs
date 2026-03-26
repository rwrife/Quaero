using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;

namespace Quaero.UI.ViewModels;

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
    private SearchResult? _selectedResult;
    private DataSourceItemViewModel? _selectedDataSource;
    private SearchResult? _selectedIndexedFile;
    private SearchHistoryEntry? _selectedSearchHistory;
    private ChatHistoryEntry? _selectedChatHistory;

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

    public ObservableCollection<SearchResult> IndexedFiles { get; } = new();
    public ObservableCollection<SearchHistoryEntry> SearchHistory { get; } = new();
    public ObservableCollection<ChatHistoryEntry> ChatHistory { get; } = new();
    public ObservableCollection<ChatMessageEntry> ChatMessages { get; } = new();
    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    public void SelectWorkspace(bool useChatMode)
    {
        IsChatMode = useChatMode;
        SetActivePane(DetailPaneKind.Workspace);
    }

    public void StartNewSearch()
    {
        IsChatMode = false;
        PrimaryInputText = string.Empty;
        SearchResults.Clear();
        SelectedResult = null;
        SetActivePane(DetailPaneKind.Workspace);
        StatusText = "Ready for a new search.";
    }

    public void StartNewChat()
    {
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

    public void SelectDataSource(DataSourceItemViewModel? dataSource)
    {
        if (dataSource == null) return;
        SelectedDataSource = dataSource;
        DataSourcesVM.SelectedDataSource = dataSource;
        SelectedIndexedFile = null;
        SelectedSearchHistory = null;
        SelectedChatHistory = null;
        SetActivePane(DetailPaneKind.DataSource);
    }

    public void SelectIndexedFile(SearchResult? indexedFile)
    {
        if (indexedFile == null) return;
        SelectedIndexedFile = indexedFile;
        SelectedDataSource = null;
        SelectedSearchHistory = null;
        SelectedChatHistory = null;
        SetActivePane(DetailPaneKind.IndexedFile);
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
        try
        {
            var results = await _indexingService.SearchAsync(new SearchQuery { MaxResults = 100 });
            IndexedFiles.Clear();
            foreach (var result in results)
                IndexedFiles.Add(result);
        }
        catch (Exception ex)
        {
            StatusText = $"Unable to load indexed files: {ex.Message}";
        }
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
