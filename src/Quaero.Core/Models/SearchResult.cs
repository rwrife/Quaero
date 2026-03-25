namespace Quaero.Core.Models;

/// <summary>
/// Represents a search result with relevance ranking.
/// </summary>
public class SearchResult
{
    public IndexedDocument Document { get; set; } = null!;
    public double Rank { get; set; }
}
