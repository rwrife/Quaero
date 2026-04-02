namespace Quaero.Core.Models;

/// <summary>
/// Configuration for the index store including encryption settings.
/// </summary>
public class IndexConfiguration
{
    public string DatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Quaero", "index.db");

    public bool EncryptionEnabled { get; set; } = false;
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Directory where plugin assemblies are loaded from.
    /// Resolution order: plugins/ next to exe → QUAERO_PLUGINS_DIR env var → %LocalAppData%/Quaero/plugins
    /// </summary>
    public string PluginsDirectory { get; set; } = ResolvePluginsDirectory();

    private static string ResolvePluginsDirectory()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (Directory.Exists(bundled)) return bundled;

        var envOverride = Environment.GetEnvironmentVariable("QUAERO_PLUGINS_DIR");
        if (!string.IsNullOrEmpty(envOverride)) return envOverride;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "plugins");
    }

    public int IndexIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Base URL for the background server / indexer API.
    /// Defaults to localhost, but can point at another host.
    /// </summary>
    public string ServerBaseUrl { get; set; } = ResolveServerBaseUrl();

    private static string ResolveServerBaseUrl()
    {
        var envOverride = Environment.GetEnvironmentVariable("QUAERO_SERVER_BASE_URL");
        if (!string.IsNullOrWhiteSpace(envOverride))
            return envOverride.Trim();

        return "http://localhost:5055";
    }
}
