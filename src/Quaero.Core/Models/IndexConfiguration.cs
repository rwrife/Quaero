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
    /// Resolution order: QUAERO_PLUGINS_DIR env var → plugins/ next to exe → %LocalAppData%/Quaero/plugins
    /// </summary>
    public string PluginsDirectory { get; set; } = ResolvePluginsDirectory();

    private static string ResolvePluginsDirectory()
    {
        var envOverride = Environment.GetEnvironmentVariable("QUAERO_PLUGINS_DIR");
        if (!string.IsNullOrEmpty(envOverride)) return envOverride;

        // Check for bundled plugins/ folder alongside the executable
        var bundled = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (Directory.Exists(bundled)) return bundled;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "plugins");
    }

    public int IndexIntervalMinutes { get; set; } = 30;
}
