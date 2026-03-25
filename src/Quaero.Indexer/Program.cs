using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;
using Quaero.Plugins.Abstractions;
using Quaero.Plugins.Imap;
using Quaero.Plugins.Json;
using Quaero.Plugins.Markdown;
using Quaero.Plugins.Text;

namespace Quaero.Indexer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(LoadConfiguration());
                services.AddSingleton<IndexStore>();
                services.AddSingleton<IndexingService>();
                services.AddHostedService<IndexingBackgroundService>();
            })
            .Build();

        await host.RunAsync();
    }

    private static IndexConfiguration LoadConfiguration()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "config.json");

        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<IndexConfiguration>(json) ?? new IndexConfiguration();
        }

        return new IndexConfiguration();
    }
}

public class IndexingBackgroundService : BackgroundService
{
    private readonly IndexingService _indexingService;
    private readonly IndexConfiguration _config;
    private readonly ILogger<IndexingBackgroundService> _logger;

    public IndexingBackgroundService(
        IndexingService indexingService,
        IndexConfiguration config,
        ILogger<IndexingBackgroundService> logger)
    {
        _indexingService = indexingService;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quaero Indexer starting...");
        RegisterBuiltInPlugins();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _indexingService.RunIndexingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during indexing run");
            }

            _logger.LogInformation("Next indexing run in {Minutes} minutes", _config.IndexIntervalMinutes);
            await Task.Delay(TimeSpan.FromMinutes(_config.IndexIntervalMinutes), stoppingToken);
        }
    }

    private void RegisterBuiltInPlugins()
    {
        var pluginConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "plugins.json");

        var pluginConfigs = new Dictionary<string, PluginConfiguration>();
        if (File.Exists(pluginConfigPath))
        {
            var json = File.ReadAllText(pluginConfigPath);
            pluginConfigs = JsonSerializer.Deserialize<Dictionary<string, PluginConfiguration>>(json) ?? new();
        }

        var plugins = new ISearchPlugin[]
        {
            new MarkdownSearchPlugin(),
            new TextSearchPlugin(),
            new JsonSearchPlugin(),
            new ImapSearchPlugin()
        };

        foreach (var plugin in plugins)
        {
            var config = pluginConfigs.GetValueOrDefault(plugin.Metadata.Id) ?? new PluginConfiguration();
            if (!config.Enabled)
            {
                _logger.LogInformation("Plugin {Name} is disabled, skipping", plugin.Metadata.Name);
                continue;
            }

            plugin.InitializeAsync(config).GetAwaiter().GetResult();
            _indexingService.RegisterPlugin(plugin);
        }
    }
}
