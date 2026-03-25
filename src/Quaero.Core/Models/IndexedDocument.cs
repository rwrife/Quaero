namespace Quaero.Core.Models;

/// <summary>
/// Represents a document that has been indexed from a data source.
/// </summary>
public class IndexedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Machine { get; set; } = Environment.MachineName;
    public string Type { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, string> ExtendedData { get; set; } = new();
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
    public string ContentHash { get; set; } = string.Empty;
}
