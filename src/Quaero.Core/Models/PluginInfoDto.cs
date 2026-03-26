using Quaero.Plugins.Abstractions;

namespace Quaero.Core.Models;

/// <summary>
/// Serializable DTO describing a discovered plugin. Used by the Indexer API
/// to report available plugins to the UI.
/// </summary>
public class PluginInfoDto
{
    public string AssemblyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public List<PluginSettingDescriptorDto> Settings { get; set; } = new();
}

/// <summary>
/// Serializable DTO for a plugin setting descriptor.
/// </summary>
public class PluginSettingDescriptorDto
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SettingType { get; set; } = "Text";
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}