using CommunityToolkit.Mvvm.ComponentModel;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;

namespace MyTools.Common.Config.Models;

public partial class ConfigurationSetting : ObservableObject
{
    private bool isInitialized = false;
    
    [ObservableProperty] 
    public object? currentValue;

    partial void OnCurrentValueChanged(object? value)
    {
        isInitialized = true;
        IsDirty = true;
    }

    public string Name { get; init; } = string.Empty;
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
    public bool IsDirty { get; set; }

    public string FullPath => Category != null ? $"{Category.FullPath}.{Name}" : Name;
    
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
}