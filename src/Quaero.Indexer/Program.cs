using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quaero.Core.Models;
using Quaero.Core.Services;
using Quaero.Core.Storage;
using Quaero.Plugins.Abstractions;

namespace Quaero.Indexer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var config = LoadConfiguration();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<IndexStore>();
        builder.Services.AddSingleton<DataSourceStore>();
        builder.Services.AddSingleton(sp => new PluginLoader(
            config.PluginsDirectory,
            sp.GetRequiredService<ILogger<PluginLoader>>()));
        builder.Services.AddSingleton<PluginRegistry>();
        builder.Services.AddSingleton<IndexingService>();
        builder.Services.AddHostedService<IndexingBackgroundService>();

        var app = builder.Build();

        // ── API Endpoints ──────────────────────────────────────────────

        app.MapGet("/", () => Results.Ok(new
        {
            name = "Quaero Indexer Service",
            baseUrl = config.ServerBaseUrl
        }));

        app.MapGet("/api/config", (IndexConfiguration cfg) => Results.Ok(new
        {
            serverBaseUrl = cfg.ServerBaseUrl,
            pluginsDirectory = cfg.PluginsDirectory,
            databasePath = cfg.DatabasePath
        }));

        app.MapPut("/api/config/server-base-url", (IndexConfiguration cfg, ServerBaseUrlRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.ServerBaseUrl))
                return Results.BadRequest("ServerBaseUrl is required.");

            cfg.ServerBaseUrl = request.ServerBaseUrl.Trim();
            SaveConfiguration(cfg);
            return Results.Ok(new { serverBaseUrl = cfg.ServerBaseUrl });
        });

        app.MapGet("/api/plugins", (PluginRegistry registry) =>
        {
            return Results.Ok(registry.GetDiscoveredPlugins());
        });

        await app.RunAsync();
    }

    private static void SaveConfiguration(IndexConfiguration config)
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "config.json");

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record ServerBaseUrlRequest(string ServerBaseUrl);

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

/// <summary>
/// Holds the list of discovered plugins so they can be served by the API.
/// Populated at startup by <see cref="IndexingBackgroundService"/>.
/// </summary>
public class PluginRegistry
{
    private List<PluginInfoDto> _plugins = new();

    public void Register(List<PluginInfoDto> plugins)
    {
        _plugins = plugins;
    }

    public IReadOnlyList<PluginInfoDto> GetDiscoveredPlugins() => _plugins.AsReadOnly();
}

public class IndexingBackgroundService : BackgroundService
{
    private readonly IndexingService _indexingService;
    private readonly IndexConfiguration _config;
    private readonly PluginLoader _pluginLoader;
    private readonly PluginRegistry _pluginRegistry;
    private readonly ILogger<IndexingBackgroundService> _logger;

    public IndexingBackgroundService(
        IndexingService indexingService,
        IndexConfiguration config,
        PluginLoader pluginLoader,
        PluginRegistry pluginRegistry,
        ILogger<IndexingBackgroundService> logger)
    {
        _indexingService = indexingService;
        _config = config;
        _pluginLoader = pluginLoader;
        _pluginRegistry = pluginRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quaero Indexer starting...");
        _logger.LogInformation("Plugins directory: {Dir}", _config.PluginsDirectory);
        _logger.LogInformation("Discovering available plugins...");

        // Discover and log all plugin assemblies from the plugins folder
        var discovered = _pluginLoader.DiscoverPlugins();
        _logger.LogInformation("Found {Count} plugin type(s) in plugins folder:", discovered.Count);

        var pluginDtos = new List<PluginInfoDto>();
        foreach (var (assemblyName, typeName, prototype) in discovered)
        {
            _logger.LogInformation("  ✓ {Name} (v{Version}) — {Description}  [{Assembly} → {Type}]",
                prototype.Metadata.Name,
                prototype.Metadata.Version,
                prototype.Metadata.Description,
                assemblyName,
                typeName);

            pluginDtos.Add(new PluginInfoDto
            {
                AssemblyName = assemblyName,
                TypeName = typeName,
                Id = prototype.Metadata.Id,
                Name = prototype.Metadata.Name,
                Description = prototype.Metadata.Description,
                Version = prototype.Metadata.Version,
                Settings = prototype.SettingDescriptors.Select(s => new PluginSettingDescriptorDto
                {
                    Key = s.Key,
                    DisplayName = s.DisplayName,
                    Description = s.Description,
                    SettingType = s.SettingType.ToString(),
                    DefaultValue = s.DefaultValue,
                    IsRequired = s.IsRequired
                }).ToList()
            });
        }

        // Register discovered plugins so the API can serve them
        _pluginRegistry.Register(pluginDtos);
        _logger.LogInformation("Plugin discovery complete. {Count} plugin(s) available via /api/plugins", pluginDtos.Count);

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