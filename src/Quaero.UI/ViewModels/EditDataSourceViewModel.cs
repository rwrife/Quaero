using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Quaero.Core.Models;
using Quaero.Plugins.Abstractions;

namespace Quaero.UI.ViewModels;

/// <summary>
/// ViewModel for the Add/Edit Data Source dialog.
/// Dynamically generates settings fields based on the selected plugin's SettingDescriptors.
/// </summary>
public class EditDataSourceViewModel : INotifyPropertyChanged
{
    private PluginTypeInfo? _selectedPluginType;
    private string _name = string.Empty;
    private string _cronSchedule = "0 * * * *";
    private bool _isNew;

    public EditDataSourceViewModel(IReadOnlyList<PluginTypeInfo> availablePlugins, DataSource? existing = null)
    {
        AvailablePluginTypes = availablePlugins;
        _isNew = existing == null;

        if (existing != null)
        {
            DataSourceId = existing.Id;
            Name = existing.Name;
            Enabled = existing.Enabled;
            CronSchedule = existing.CronSchedule;

            // Find the matching plugin type
            var match = availablePlugins.FirstOrDefault(p =>
                p.AssemblyName == existing.PluginAssembly && p.TypeName == existing.PluginType);
            if (match != null)
            {
                _selectedPluginType = match;
                BuildSettingFields(existing.Settings);
            }
        }
    }

    public string DataSourceId { get; private set; } = Guid.NewGuid().ToString("N");
    public bool IsNew => _isNew;
    public string DialogTitle => _isNew ? "Add Data Source" : "Edit Data Source";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string CronSchedule
    {
        get => _cronSchedule;
        set => SetField(ref _cronSchedule, value);
    }

    public bool Enabled { get; set; } = true;

    public IReadOnlyList<PluginTypeInfo> AvailablePluginTypes { get; }

    public PluginTypeInfo? SelectedPluginType
    {
        get => _selectedPluginType;
        set
        {
            if (SetField(ref _selectedPluginType, value))
            {
                BuildSettingFields(null);
                OnPropertyChanged(nameof(SettingFields));
                OnPropertyChanged(nameof(HasPlugin));
                OnPropertyChanged(nameof(IsGmailPluginSelected));
                if (_isNew && _selectedPluginType != null && string.IsNullOrWhiteSpace(Name))
                    Name = _selectedPluginType.Metadata.Name;
            }
        }
    }

    public bool HasPlugin => _selectedPluginType != null;
    public bool IsGmailPluginSelected => string.Equals(_selectedPluginType?.Metadata.Id, "quaero.plugins.gmail", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<SettingFieldViewModel> SettingFields { get; } = new();

    private void BuildSettingFields(Dictionary<string, string>? existingSettings)
    {
        SettingFields.Clear();
        if (_selectedPluginType == null) return;

        foreach (var descriptor in _selectedPluginType.SettingDescriptors)
        {
            var value = existingSettings?.GetValueOrDefault(descriptor.Key) ?? descriptor.DefaultValue;
            SettingFields.Add(new SettingFieldViewModel(descriptor, value));
        }
    }

    public DataSource ToDataSource()
    {
        var settings = new Dictionary<string, string>();
        foreach (var field in SettingFields)
        {
            if (!string.IsNullOrEmpty(field.Value))
                settings[field.Key] = field.Value;
        }

        return new DataSource
        {
            Id = DataSourceId,
            Name = Name,
            PluginAssembly = _selectedPluginType?.AssemblyName ?? string.Empty,
            PluginType = _selectedPluginType?.TypeName ?? string.Empty,
            Enabled = Enabled,
            CronSchedule = CronSchedule,
            Settings = settings
        };
    }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Name is required.";
            return false;
        }
        if (_selectedPluginType == null)
        {
            error = "Please select a plugin type.";
            return false;
        }
        foreach (var field in SettingFields)
        {
            if (field.IsRequired && string.IsNullOrWhiteSpace(field.Value))
            {
                error = $"{field.DisplayName} is required.";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public class SettingFieldViewModel : INotifyPropertyChanged
{
    private string _value;

    public SettingFieldViewModel(PluginSettingDescriptor descriptor, string value)
    {
        Key = descriptor.Key;
        DisplayName = descriptor.DisplayName;
        Description = descriptor.Description;
        SettingType = descriptor.SettingType;
        IsRequired = descriptor.IsRequired;
        _value = value;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public PluginSettingType SettingType { get; }
    public bool IsRequired { get; }
    public bool IsPassword => SettingType == PluginSettingType.Password;

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
