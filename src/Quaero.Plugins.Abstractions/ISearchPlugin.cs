namespace Quaero.Plugins.Abstractions;

/// <summary>
/// Interface that all search plugins must implement.
/// </summary>
public interface ISearchPlugin
{
    /// <summary>
    /// Gets metadata about this plugin.
    /// </summary>
    PluginMetadata Metadata { get; }

    /// <summary>
    /// Describes the settings this plugin accepts, used by the UI to render config forms.
    /// </summary>
    IReadOnlyList<PluginSettingDescriptor> SettingDescriptors { get; }

    /// <summary>
    /// Initializes the plugin with its configuration.
    /// </summary>
    Task InitializeAsync(PluginConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers and yields documents from the plugin's data source.
    /// </summary>
    IAsyncEnumerable<DiscoveredDocument> DiscoverDocumentsAsync(CancellationToken cancellationToken = default);
}
