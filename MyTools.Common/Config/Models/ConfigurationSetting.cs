using CommunityToolkit.Mvvm.ComponentModel;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Plugins;

namespace MyTools.Common.Config.Models;

public partial class ConfigurationSetting : ObservableObject
{
    private bool isInitialized;

    [ObservableProperty] public object? currentValue;

    partial void OnCurrentValueChanged(object? value)
    {
        isInitialized = true;
        IsDirty = true;
    }

    /// <summary>Stable global key used for registry lookup and DTO round trips.</summary>
    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public PluginId? PluginId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public SettingValueTypes ValueType { get; init; }
    public object? DefaultValue { get; init; }

    public SettingOptions Options { get; init; } = SettingOptions.None;
    public ConfigurationCategory? Category { get; init; }
    public required IRegistrySerializer Serializer { get; init; }
    public int SortOrder { get; init; }
    public string? UiHint { get; set; }
    public string? Visibility { get; set; }
    public SettingSchema? Schema { get; set; }
    public bool IsVisible => (Options & SettingOptions.Hidden) != 0;
    public bool IsDisplayOnly => (Options & SettingOptions.DisplayOnly) != 0;
    public bool IsDirty { get; set; }

    /// <summary>
    /// Key used in the backing store. Plugin settings are stored under their relative
    /// name because the storage is already scoped by <see cref="PluginId"/>.
    /// </summary>
    public string StorageKey => PluginId == null ? Key : Name;

    public void InitValueWithoutNotify(object? value)
    {
        isInitialized = true;
#pragma warning disable MVVMTK0034 // We don't want to trigger OnPropertyChanged here
        currentValue = value;
#pragma warning restore MVVMTK0034
    }

    public T? GetValue<T>()
    {
        if (isInitialized)
        {
            return CurrentValue is T typedValue ? typedValue : default;
        }

        throw new InvalidOperationException("Setting value is not initialized.");
    }

    public override string ToString() => Key;
}
