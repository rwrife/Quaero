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
        // 1. Prefer bundled plugins/ folder alongside the executable (most reliable)
        var bundled = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (Directory.Exists(bundled)) return bundled;

        // 2. Allow env var override for custom deployments
        var envOverride = Environment.GetEnvironmentVariable("QUAERO_PLUGINS_DIR");
        if (!string.IsNullOrEmpty(envOverride)) return envOverride;

        // 3. Fall back to user-local folder
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "plugins");
    }

    public int IndexIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Base URL for the Indexer API. The UI uses this to fetch discovered plugins
    /// and other indexer status information.
    /// </summary>
    public string IndexerApiBaseUrl { get; set; } = "http://localhost:5199";
}
