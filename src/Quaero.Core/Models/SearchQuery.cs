namespace Quaero.Core.Models;

/// <summary>
/// Represents a search query with optional filters.
/// </summary>
public class SearchQuery
{
    public string QueryText { get; set; } = string.Empty;
    public string? DataSourceId { get; set; }
    public string? Provider { get; set; }
    public string? Type { get; set; }
    public string? Machine { get; set; }
    public int MaxResults { get; set; } = 50;
    public int Offset { get; set; } = 0;
}
