namespace Quaero.Plugins.Abstractions;

/// <summary>
/// Configuration for a plugin, typically loaded from a settings file.
/// </summary>
public class PluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = new();
}
