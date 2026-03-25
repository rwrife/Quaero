namespace Quaero.Core.Models;

/// <summary>
/// Represents a configured data source — a named instance of a plugin with specific settings,
/// cron schedule, and runtime status tracking.
/// </summary>
public class DataSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>Assembly filename (without .dll) to load from the plugins folder.</summary>
    public string PluginAssembly { get; set; } = string.Empty;

    /// <summary>Full type name of the ISearchPlugin implementation.</summary>
    public string PluginType { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Cron expression for scheduling (5-part, e.g. "0 */6 * * *" = every 6 hours).</summary>
    public string CronSchedule { get; set; } = "0 * * * *"; // default: every hour

    public Dictionary<string, string> Settings { get; set; } = new();
}

public enum DataSourceStatus
{
    Idle,
    Indexing,
    Success,
    Error
}

