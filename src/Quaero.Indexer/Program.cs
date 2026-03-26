using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;

namespace Quaero.Indexer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                var config = LoadConfiguration();
                services.AddSingleton(config);
                services.AddSingleton<IndexStore>();
                services.AddSingleton<DataSourceStore>();
                services.AddSingleton(sp => new PluginLoader(
                    config.PluginsDirectory,
                    sp.GetRequiredService<ILogger<PluginLoader>>()));
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
    private readonly PluginLoader _pluginLoader;
    private readonly ILogger<IndexingBackgroundService> _logger;

    public IndexingBackgroundService(
        IndexingService indexingService,
        IndexConfiguration config,
        PluginLoader pluginLoader,
        ILogger<IndexingBackgroundService> logger)
    {
        _indexingService = indexingService;
        _config = config;
        _pluginLoader = pluginLoader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quaero Indexer starting...");
        _logger.LogInformation("Plugins directory: {Dir}", _config.PluginsDirectory);
        _logger.LogInformation("Discovering available plugins...");

        var discovered = _pluginLoader.DiscoverPlugins();
        _logger.LogInformation("Found {Count} plugin type(s) in plugins folder", discovered.Count);

        // Main scheduling loop — evaluate cron schedules every minute
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _indexingService.EvaluateAndRunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during indexing evaluation");
            }

            // Check schedules every minute
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
