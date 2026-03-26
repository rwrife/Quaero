using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;
using Quaero.Plugins.Abstractions;

namespace Quaero.UI.ViewModels;

public class DataSourcesViewModel : INotifyPropertyChanged
{
    private readonly DataSourceStore _dataSourceStore;
    private readonly IndexingService _indexingService;
    private readonly IndexStore _indexStore;
    private readonly PluginLoader _pluginLoader;
    private DataSourceItemViewModel? _selectedDataSource;
    private string _indexerStatusText = "Idle";
    private bool _isIndexing;

    public DataSourcesViewModel(
        DataSourceStore dataSourceStore,
        IndexingService indexingService,
        IndexStore indexStore,
        PluginLoader pluginLoader)
    {
        _dataSourceStore = dataSourceStore;
        _indexingService = indexingService;
        _indexStore = indexStore;
        _pluginLoader = pluginLoader;
        RefreshDataSources();
    }

    public ObservableCollection<DataSourceItemViewModel> DataSources { get; } = new();

    public DataSourceItemViewModel? SelectedDataSource
    {
        get => _selectedDataSource;
        set => SetField(ref _selectedDataSource, value);
    }

    public string IndexerStatusText
    {
        get => _indexerStatusText;
        set => SetField(ref _indexerStatusText, value);
    }

    public bool IsIndexing
    {
        get => _isIndexing;
        set => SetField(ref _isIndexing, value);
    }

    /// <summary>
    /// All plugin types discovered from the plugins folder.
    /// </summary>
    public List<PluginTypeInfo> AvailablePluginTypes
    {
        get
        {
            var discovered = _pluginLoader.DiscoverPlugins();
            return discovered.Select(d => new PluginTypeInfo
            {
                AssemblyName = d.AssemblyName,
                TypeName = d.TypeName,
                Metadata = d.Prototype.Metadata,
                SettingDescriptors = d.Prototype.SettingDescriptors
            }).ToList();
        }
    }

    public void RefreshDataSources()
    {
        _dataSourceStore.Reload();
        DataSources.Clear();
        foreach (var ds in _dataSourceStore.DataSources)
        {
            var prototype = _pluginLoader.GetPluginPrototype(ds.PluginAssembly, ds.PluginType);
            var pluginName = prototype?.Metadata.Name ?? ds.PluginType;
            var latestRun = _indexStore.GetLatestRunAsync(ds.Id).GetAwaiter().GetResult();
            DataSources.Add(new DataSourceItemViewModel(ds, pluginName, latestRun));
        }
        UpdateIndexerStatus();
    }

    public void AddDataSource(DataSource dataSource)
    {
        _dataSourceStore.Add(dataSource);
        RefreshDataSources();
    }

    public void UpdateDataSource(DataSource dataSource)
    {
        _dataSourceStore.Update(dataSource);
        RefreshDataSources();
    }

    public void RemoveDataSource(string id)
    {
        _dataSourceStore.Remove(id);
        RefreshDataSources();
    }

    public void ToggleDataSource(string id)
    {
        var ds = _dataSourceStore.GetById(id);
        if (ds == null) return;
        ds.Enabled = !ds.Enabled;
        _dataSourceStore.Update(ds);
        RefreshDataSources();
    }

    public async Task RunIndexingAsync()
    {
        IsIndexing = true;
        IndexerStatusText = "Indexing all sources...";
        try
        {
            await _indexingService.RunAllAsync();
            var count = await _indexStore.GetDocumentCountAsync();
            IndexerStatusText = $"Complete. {count} document(s) in index.";
            RefreshDataSources();
        }
        catch (Exception ex)
        {
            IndexerStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsIndexing = false;
        }
    }

    public async Task RunSingleDataSourceAsync(string dataSourceId)
    {
        var ds = _dataSourceStore.GetById(dataSourceId);
        if (ds == null) return;

        IsIndexing = true;
        IndexerStatusText = $"Indexing {ds.Name}...";
        try
        {
            await _indexingService.RunDataSourceAsync(ds);
            IndexerStatusText = $"Finished indexing {ds.Name}.";
            RefreshDataSources();
        }
        catch (Exception ex)
        {
            IndexerStatusText = $"Error indexing {ds.Name}: {ex.Message}";
        }
        finally
        {
            IsIndexing = false;
        }
    }

    private void UpdateIndexerStatus()
    {
        var total = _dataSourceStore.DataSources.Count;
        var enabled = _dataSourceStore.GetEnabled().Count;
        var lastRun = _indexingService.LastRunTime;
        var lastRunText = lastRun.HasValue ? lastRun.Value.ToLocalTime().ToString("g") : "Never";
        IndexerStatusText = $"{enabled}/{total} source(s) enabled · Last evaluation: {lastRunText}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public class PluginTypeInfo
{
    public string AssemblyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public PluginMetadata Metadata { get; set; } = new();
    public IReadOnlyList<PluginSettingDescriptor> SettingDescriptors { get; set; } = [];
    public string DisplayName => Metadata.Name;
}

public class DataSourceItemViewModel : INotifyPropertyChanged
{
    public DataSourceItemViewModel(DataSource model, string pluginName, IndexRunLog? latestRun)
    {
        Model = model;
        PluginName = pluginName;
        LatestRun = latestRun;
    }

    public DataSource Model { get; }
    public IndexRunLog? LatestRun { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string PluginName { get; }
    public bool Enabled => Model.Enabled;
    public string CronSchedule => Model.CronSchedule;

    public string StatusText
    {
        get
        {
            if (LatestRun == null) return "Not yet indexed";
            return LatestRun.Status switch
            {
                DataSourceStatus.Success => $"Last run: {LatestRun.CompletedAt?.ToLocalTime():g} ({LatestRun.DocumentCount} docs)",
                DataSourceStatus.Error => $"Error: {LatestRun.ErrorMessage ?? "Unknown"}",
                DataSourceStatus.Indexing => "Indexing...",
                _ => "Idle"
            };
        }
    }

    public string EnabledText => Model.Enabled ? "Enabled" : "Disabled";

    public event PropertyChangedEventHandler? PropertyChanged;
}
