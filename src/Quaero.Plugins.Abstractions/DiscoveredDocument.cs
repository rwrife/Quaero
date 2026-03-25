namespace Quaero.Plugins.Abstractions;

/// <summary>
/// Represents a document discovered by a plugin, ready for indexing.
/// </summary>
public class DiscoveredDocument
{
    public string Type { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, string> ExtendedData { get; set; } = new();
    public string ContentHash { get; set; } = string.Empty;
}
