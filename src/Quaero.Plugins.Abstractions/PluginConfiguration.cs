namespace Quaero.Plugins.Abstractions;

/// <summary>
/// Configuration for a plugin instance, typically loaded from a data source settings.
/// Includes runtime context like when the plugin was last successfully run.
/// </summary>
public class PluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = new();

    /// <summary>
    /// The last time this data source was successfully indexed.
    /// Plugins should use this to perform incremental indexing (only fetch content newer than this).
    /// Null means this is the first run — index everything.
    /// </summary>
    public DateTime? LastSuccessfulRun { get; set; }
}
