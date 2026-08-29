using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTools.Common.Plugins;

namespace MyTools.Common.Config.Models;

/// <summary>
/// 配置分类
/// </summary>
public partial class ConfigurationCategory : ObservableObject, ICloneable
{
    public PluginId? PluginId { get; init; }

    /// <summary>Stable, non-localized key used by the registry and UI.</summary>
    public string Key { get; init; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    public ObservableCollection<ConfigurationSetting> Settings { get; set; } = new();
    
    public string? Icon { get; set; }
    
    public int SortOrder { get; set; }
    
    public bool IsVisible { get; set; } = true;
    
    public bool IsSelectable { get; set; } = true;
    
    [ObservableProperty]
    private bool isExpanded;
    
    public void AddSetting(ConfigurationSetting setting)
    {
        if (PluginId != setting.PluginId)
        {
            throw new ArgumentException($"Cannot add settings with different plugin id, setting: {setting}, category: {this}");
        }
        Settings.Add(setting);
    }

    public override string ToString()
    {
        return Key;
    }

    public object Clone()
    {
        return new ConfigurationCategory()
        {
            PluginId = PluginId,
            Key = Key,
            Name = Name,
            Description = Description,
            Icon = Icon,
            SortOrder = SortOrder,
            IsVisible = IsVisible,
            IsSelectable = IsSelectable,
            IsExpanded = true,
        };
    }
}

