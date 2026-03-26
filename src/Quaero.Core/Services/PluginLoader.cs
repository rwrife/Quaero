using System.Reflection;
using System.Runtime.Loader;
using Quaero.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace Quaero.Core.Services;

/// <summary>
/// Dynamically loads plugin assemblies from a plugins folder and instantiates ISearchPlugin types.
/// Assemblies are loaded into isolated AssemblyLoadContexts so plugins don't conflict.
/// </summary>
public class PluginLoader
{
    private readonly string _pluginsDirectory;
    private readonly ILogger<PluginLoader> _logger;
    private readonly Dictionary<string, Assembly> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ISearchPlugin> _pluginCache = new();

    public PluginLoader(string pluginsDirectory, ILogger<PluginLoader> logger)
    {
        // Resolve relative paths against the app base directory
        _pluginsDirectory = Path.IsPathRooted(pluginsDirectory)
            ? pluginsDirectory
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, pluginsDirectory));
        _logger = logger;

        if (!Directory.Exists(_pluginsDirectory))
            Directory.CreateDirectory(_pluginsDirectory);
    }

    /// <summary>
    /// Creates a fresh ISearchPlugin instance for the given assembly name and type name.
    /// Loads the assembly from the plugins folder if not already loaded.
    /// </summary>
    public ISearchPlugin? CreatePlugin(string assemblyName, string typeName)
    {
        try
        {
            var assembly = LoadAssembly(assemblyName);
            if (assembly == null)
            {
                _logger.LogError("Could not load assembly {Assembly} from plugins folder", assemblyName);
                return null;
            }

            var type = assembly.GetType(typeName);
            if (type == null)
            {
                _logger.LogError("Type {Type} not found in assembly {Assembly}", typeName, assemblyName);
                return null;
            }

            if (!typeof(ISearchPlugin).IsAssignableFrom(type))
            {
                _logger.LogError("Type {Type} does not implement ISearchPlugin", typeName);
                return null;
            }

            var instance = (ISearchPlugin?)Activator.CreateInstance(type);
            if (instance == null)
            {
                _logger.LogError("Failed to create instance of {Type}", typeName);
                return null;
            }

            _logger.LogDebug("Created plugin instance: {Type} from {Assembly}", typeName, assemblyName);
            return instance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating plugin {Type} from {Assembly}", typeName, assemblyName);
            return null;
        }
    }

    /// <summary>
    /// Gets a cached prototype instance for reading metadata/setting descriptors.
    /// </summary>
    public ISearchPlugin? GetPluginPrototype(string assemblyName, string typeName)
    {
        var key = $"{assemblyName}::{typeName}";
        if (_pluginCache.TryGetValue(key, out var cached))
            return cached;

        var plugin = CreatePlugin(assemblyName, typeName);
        if (plugin != null)
            _pluginCache[key] = plugin;
        return plugin;
    }

    /// <summary>
    /// Discovers all ISearchPlugin implementations from all assemblies in the plugins folder.
    /// Returns (assemblyName, typeName, prototype) tuples.
    /// </summary>
    public List<(string AssemblyName, string TypeName, ISearchPlugin Prototype)> DiscoverPlugins()
    {
        var results = new List<(string, string, ISearchPlugin)>();

        if (!Directory.Exists(_pluginsDirectory))
            return results;

        foreach (var dllPath in Directory.EnumerateFiles(_pluginsDirectory, "*.dll"))
        {
            try
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                var assembly = LoadAssembly(assemblyName);
                if (assembly == null) continue;

                foreach (var type in assembly.GetExportedTypes())
                {
                    if (typeof(ISearchPlugin).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                    {
                        try
                        {
                            var instance = (ISearchPlugin?)Activator.CreateInstance(type);
                            if (instance != null)
                            {
                                results.Add((assemblyName, type.FullName!, instance));
                                _pluginCache[$"{assemblyName}::{type.FullName}"] = instance;
                                _logger.LogInformation("Discovered plugin: {Name} ({Id}) in {Assembly}",
                                    instance.Metadata.Name, instance.Metadata.Id, assemblyName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not instantiate {Type} from {Assembly}",
                                type.FullName, assemblyName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not scan assembly {Path}", dllPath);
            }
        }

        return results;
    }

    private Assembly? LoadAssembly(string assemblyName)
    {
        if (_loadedAssemblies.TryGetValue(assemblyName, out var cached))
            return cached;

        // Search for the assembly DLL in the plugins folder
        var dllPath = Path.Combine(_pluginsDirectory, $"{assemblyName}.dll");
        if (!File.Exists(dllPath))
        {
            // Also try loading from the app's own directory (built-in plugins)
            var appDir = AppContext.BaseDirectory;
            var appDllPath = Path.Combine(appDir, $"{assemblyName}.dll");
            if (File.Exists(appDllPath))
                dllPath = appDllPath;
            else
            {
                _logger.LogWarning("Assembly not found: {Path} (also checked {AppPath})", dllPath, appDllPath);
                return null;
            }
        }

        try
        {
            var context = new PluginAssemblyLoadContext(dllPath);
            var assembly = context.LoadFromAssemblyPath(dllPath);
            _loadedAssemblies[assemblyName] = assembly;
            _logger.LogInformation("Loaded assembly: {Path}", dllPath);
            return assembly;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load assembly: {Path}", dllPath);
            return null;
        }
    }
}

/// <summary>
/// Isolated load context for plugin assemblies. Falls back to the default context for shared types
/// (like Quaero.Plugins.Abstractions).
/// </summary>
internal class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginAssemblyLoadContext(string pluginPath) : base(isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Let shared framework assemblies and plugin abstractions resolve from default context
        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolved != null)
            return LoadFromAssemblyPath(resolved);
        return null; // Fall back to default context
    }
}
