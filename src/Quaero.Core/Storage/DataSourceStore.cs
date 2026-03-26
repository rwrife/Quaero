using System.Text.Json;
using Quaero.Core.Models;

namespace Quaero.Core.Storage;

/// <summary>
/// Persists data source configurations to a JSON file.
/// This is the indexer configuration file shared between the UI and the Indexer service.
/// Runtime status (last run, doc count) is tracked in IndexRunLog in SQLite, not here.
/// </summary>
public class DataSourceStore
{
    private readonly string _filePath;
    private List<DataSource> _dataSources = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DataSourceStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "datasources.json");

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        Load();
    }

    public string FilePath => _filePath;
    public IReadOnlyList<DataSource> DataSources => _dataSources.AsReadOnly();

    public void Add(DataSource dataSource)
    {
        _dataSources.Add(dataSource);
        Save();
    }

    public void Update(DataSource dataSource)
    {
        var index = _dataSources.FindIndex(ds => ds.Id == dataSource.Id);
        if (index >= 0)
        {
            _dataSources[index] = dataSource;
            Save();
        }
    }

    public void Remove(string id)
    {
        _dataSources.RemoveAll(ds => ds.Id == id);
        Save();
    }

    public DataSource? GetById(string id)
        => _dataSources.FirstOrDefault(ds => ds.Id == id);

    public List<DataSource> GetEnabled()
        => _dataSources.Where(ds => ds.Enabled).ToList();

    public void Reload() => Load();

    private void Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                _dataSources = JsonSerializer.Deserialize<List<DataSource>>(json, JsonOptions) ?? new();
            }
            catch
            {
                _dataSources = new();
            }
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_dataSources, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
