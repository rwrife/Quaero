using System.Security.Cryptography;
using System.Text;
using Quaero.Core.Models;
using Quaero.Core.Storage;
using Quaero.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace Quaero.Core.Services;

/// <summary>
/// Orchestrates plugin discovery and document indexing.
/// </summary>
public class IndexingService
{
    private readonly IndexStore _store;
    private readonly ILogger<IndexingService> _logger;
    private readonly List<ISearchPlugin> _plugins = new();

    public IndexingService(IndexStore store, ILogger<IndexingService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public void RegisterPlugin(ISearchPlugin plugin)
    {
        _plugins.Add(plugin);
        _logger.LogInformation("Registered plugin: {PluginName} ({PluginId})",
            plugin.Metadata.Name, plugin.Metadata.Id);
    }

    public IReadOnlyList<ISearchPlugin> Plugins => _plugins.AsReadOnly();

    public async Task RunIndexingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting indexing run with {Count} plugins", _plugins.Count);
        var totalIndexed = 0;

        foreach (var plugin in _plugins)
        {
            try
            {
                _logger.LogInformation("Indexing with plugin: {PluginName}", plugin.Metadata.Name);
                var pluginCount = 0;

                await foreach (var discovered in plugin.DiscoverDocumentsAsync(cancellationToken))
                {
                    var hash = ComputeHash(discovered.Content);

                    if (!await _store.HasChangedAsync(discovered.Location, hash, cancellationToken))
                    {
                        _logger.LogDebug("Skipping unchanged document: {Location}", discovered.Location);
                        continue;
                    }

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

                    await _store.UpsertDocumentAsync(doc, cancellationToken);
                    pluginCount++;
                    totalIndexed++;
                }

                _logger.LogInformation("Plugin {PluginName} indexed {Count} documents",
                    plugin.Metadata.Name, pluginCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing with plugin {PluginName}", plugin.Metadata.Name);
            }
        }

        _logger.LogInformation("Indexing run complete. Total documents indexed: {Count}", totalIndexed);
    }

    public Task<List<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
        => _store.SearchAsync(query, cancellationToken);

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
