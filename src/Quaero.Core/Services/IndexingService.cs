using System.Security.Cryptography;
using System.Text;
using Cronos;
using Quaero.Core.Models;
using Quaero.Core.Storage;
using Quaero.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace Quaero.Core.Services;

public enum IndexerState
{
    Idle,
    Running
}

/// <summary>
/// Orchestrates data-source-driven indexing with cron scheduling, incremental runs,
/// dynamic plugin loading, and run logging.
/// </summary>
public class IndexingService
{
    private readonly IndexStore _store;
    private readonly DataSourceStore _dataSourceStore;
    private readonly PluginLoader _pluginLoader;
    private readonly ILogger<IndexingService> _logger;

    public IndexingService(
        IndexStore store,
        DataSourceStore dataSourceStore,
        PluginLoader pluginLoader,
        ILogger<IndexingService> logger)
    {
        _store = store;
        _dataSourceStore = dataSourceStore;
        _pluginLoader = pluginLoader;
        _logger = logger;
    }

    public IndexerState State { get; private set; } = IndexerState.Idle;
    public DateTime? LastRunTime { get; private set; }
    public string? LastError { get; private set; }
    public PluginLoader PluginLoader => _pluginLoader;
    public IndexStore Store => _store;
    public DataSourceStore DataSourceStore => _dataSourceStore;

    /// <summary>
    /// Checks all enabled data sources and runs any whose cron schedule indicates they are due.
    /// </summary>
    public async Task EvaluateAndRunAsync(CancellationToken ct = default)
    {
        _dataSourceStore.Reload();
        var dataSources = _dataSourceStore.GetEnabled();

        foreach (var ds in dataSources)
        {
            if (ct.IsCancellationRequested) break;

            if (await IsDueAsync(ds, ct))
            {
                await RunDataSourceAsync(ds, ct);
            }
            else
            {
                _logger.LogDebug("Data source {Name} not due yet, skipping", ds.Name);
            }
        }

        LastRunTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Forces an immediate indexing run for a specific data source, regardless of schedule.
    /// </summary>
    public async Task RunDataSourceAsync(DataSource ds, CancellationToken ct = default)
    {
        State = IndexerState.Running;
        LastError = null;

        var logId = await _store.StartRunLogAsync(ds.Id, ct);
        var docCount = 0;

        try
        {
            var plugin = _pluginLoader.CreatePlugin(ds.PluginAssembly, ds.PluginType);
            if (plugin == null)
            {
                var msg = $"Could not load plugin {ds.PluginAssembly}::{ds.PluginType}";
                _logger.LogError(msg);
                await _store.CompleteRunLogAsync(logId, DataSourceStatus.Error, 0, msg, ct);
                return;
            }

            // Get last successful run time for incremental indexing
            var lastSuccess = await _store.GetLastSuccessfulRunAsync(ds.Id, ct);

            var config = new PluginConfiguration
            {
                Enabled = true,
                Settings = ds.Settings,
                LastSuccessfulRun = lastSuccess
            };

            await plugin.InitializeAsync(config, ct);

            _logger.LogInformation("Indexing data source: {Name} (plugin: {Plugin}, last success: {Last})",
                ds.Name, plugin.Metadata.Name, lastSuccess?.ToString("g") ?? "never");

            await foreach (var discovered in plugin.DiscoverDocumentsAsync(ct))
            {
                var hash = ComputeHash(discovered.Content);
                if (!await _store.HasChangedAsync(discovered.Location, hash, ct))
                    continue;

                var doc = new IndexedDocument
                {
                    Machine = Environment.MachineName,
                    Type = discovered.Type,
                    Provider = discovered.Provider,
                    Location = discovered.Location,
                    Title = discovered.Title,
                    Summary = discovered.Summary,
                    Content = discovered.Content,
                    ExtendedData = discovered.ExtendedData,
                    ContentHash = hash,
                    IndexedAt = DateTime.UtcNow
                };

                await _store.UpsertDocumentAsync(doc, ct);
                docCount++;
            }

            await _store.CompleteRunLogAsync(logId, DataSourceStatus.Success, docCount, ct: ct);
            _logger.LogInformation("Data source {Name} indexed {Count} new/changed documents", ds.Name, docCount);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogError(ex, "Error indexing data source {Name}", ds.Name);
            await _store.CompleteRunLogAsync(logId, DataSourceStatus.Error, docCount, ex.Message, ct);
        }
        finally
        {
            State = IndexerState.Idle;
        }
    }

    /// <summary>
    /// Runs all enabled data sources regardless of schedule.
    /// </summary>
    public async Task RunAllAsync(CancellationToken ct = default)
    {
        _dataSourceStore.Reload();
        foreach (var ds in _dataSourceStore.GetEnabled())
        {
            if (ct.IsCancellationRequested) break;
            await RunDataSourceAsync(ds, ct);
        }
        LastRunTime = DateTime.UtcNow;
    }

    public Task<List<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
        => _store.SearchAsync(query, ct);

    private async Task<bool> IsDueAsync(DataSource ds, CancellationToken ct)
    {
        try
        {
            var cron = CronExpression.Parse(ds.CronSchedule);
            var lastRun = await _store.GetLastSuccessfulRunAsync(ds.Id, ct);

            if (lastRun == null)
                return true; // Never run before

            var nextDue = cron.GetNextOccurrence(lastRun.Value, TimeZoneInfo.Utc);
            return nextDue.HasValue && nextDue.Value <= DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid cron expression '{Cron}' for data source {Name}, running anyway",
                ds.CronSchedule, ds.Name);
            return true;
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
