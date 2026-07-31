using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyTools.Common.Config.Models;

/// <summary>
/// 配置分类
/// </summary>
public partial class ConfigurationCategory : ObservableObject, ICloneable
{
    public string Name { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    
    public ConfigurationCategory? Parent { get; set; }
    
    public ObservableCollection<ConfigurationCategory> Children { get; set; } = new();
    
    public ObservableCollection<ConfigurationSetting> Settings { get; set; } = new();
    
    public string? Icon { get; set; }
    
    public int SortOrder { get; set; }
    
    public bool IsVisible { get; set; } = true;
    
    public bool IsSelectable { get; set; } = true;
    
    [ObservableProperty]
    private bool isExpanded = false;
    
    public string FullPath => Parent != null ? $"{Parent.FullPath}.{Name}" : Name;
    
    public void AddSetting(ConfigurationSetting setting)
    {
        Settings.Add(setting);
    }
    
    public object Clone()
    {
        return new ConfigurationCategory()
        {
            Name = Name,
            Description = Description,
            Icon = Icon,
            SortOrder = SortOrder,
            IsVisible = IsVisible,
            IsSelectable = IsSelectable,
            Parent = Parent,
            IsExpanded = true,
        };
    }
}


