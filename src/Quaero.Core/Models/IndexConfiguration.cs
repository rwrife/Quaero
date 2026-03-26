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
    /// Override with QUAERO_PLUGINS_DIR environment variable for debugging.
    /// </summary>
    public string PluginsDirectory { get; set; } =
        Environment.GetEnvironmentVariable("QUAERO_PLUGINS_DIR")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quaero", "plugins");

    public int IndexIntervalMinutes { get; set; } = 30;
}
