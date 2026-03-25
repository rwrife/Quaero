namespace Quaero.Plugins.Abstractions;

/// <summary>
/// Metadata describing a search plugin.
/// </summary>
public class PluginMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string[] SupportedFileExtensions { get; set; } = [];
}
