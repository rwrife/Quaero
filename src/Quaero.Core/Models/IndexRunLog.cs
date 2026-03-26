namespace Quaero.Core.Models;

/// <summary>
/// Records the result of an indexing run for a single data source.
/// Persisted in the SQLite index database.
/// </summary>
public class IndexRunLog
{
    public long Id { get; set; }
    public string DataSourceId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DataSourceStatus Status { get; set; } = DataSourceStatus.Indexing;
    public int DocumentCount { get; set; }
    public string? ErrorMessage { get; set; }
}
