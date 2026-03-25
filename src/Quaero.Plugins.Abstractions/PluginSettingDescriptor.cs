namespace Quaero.Plugins.Abstractions;

/// <summary>
/// Describes a configuration field that a plugin accepts.
/// Used by the UI to render appropriate input controls.
/// </summary>
public class PluginSettingDescriptor
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PluginSettingType SettingType { get; set; } = PluginSettingType.Text;
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}

public enum PluginSettingType
{
    Text,
    FolderPath,
    Password,
    Number,
    Boolean,
    GlobPattern
}
