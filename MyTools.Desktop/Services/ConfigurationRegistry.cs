using System.Collections.ObjectModel;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Serializers;

namespace MyTools.Desktop.Services;

public class ConfigurationRegistry(IConfigurationStorage storage) : IConfigurationRegistry
{
    private readonly ObservableCollection<ConfigurationCategory> _rootCategories = new();
    private readonly Dictionary<string, ConfigurationSetting> _settingsByName = new();
    private readonly Dictionary<string, ConfigurationCategory> _categoriesByPath = new();

    public ConfigurationCategory AddCategory(string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true)
    {
        return AddCategory(name, name, description, parent, IsSelectable);
    }

    public ConfigurationCategory AddCategory(string key, string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true)
    {
        
        var order = parent == null ? _rootCategories.Count : parent.Children.Count;
        
        var category = new ConfigurationCategory
        {
            Key = key,
            Name = name,
            Description = description,
            Parent = parent,
            SortOrder = order,
            IsSelectable = IsSelectable
        };

        if (parent == null)
        {
            _rootCategories.Add(category);
        }
        else
        {
            parent.Children.Add(category);
        }
        
        _categoriesByPath[category.FullPath] = category;
        return category;
    }
    
    public ConfigurationSetting AddSetting<T>(
        ConfigurationCategory category,
        string name,
        string title,
        string description, 
        T defaultValue, 
        IRegistrySerializer? serializer,
        SettingOptions options = SettingOptions.None,
        SettingValueTypes? valueType = null)
    {
        
        var serializerFoRegistry = serializer ?? GetSerializer(typeof(T));
        
        var setting = new ConfigurationSetting
        {
            Name = name,
            Title = title,
            Description = description,
            DefaultValue = defaultValue,
            Options = options,
            Category = category,
            ValueType = valueType ?? GetSettingType(typeof(T)),
            Serializer = serializerFoRegistry,
            SortOrder = category.Settings.Count
        };
        
        setting.InitValueWithoutNotify(defaultValue);
        category.AddSetting(setting);
        _settingsByName[setting.FullPath] = setting;

        return setting;
    }

    private SettingValueTypes GetSettingType(Type type)
    {
        if (type == typeof(string))
        {
            return SettingValueTypes.String;
        }

        if (type == typeof(int))
        {
            return SettingValueTypes.Integer;
        }

        if (type == typeof(double))
        {
            return SettingValueTypes.Double;
        }

        if (type == typeof(bool))
        {
            return SettingValueTypes.Bool;
        }

        return SettingValueTypes.Custom;
    }

    private static readonly Dictionary<Type, IRegistrySerializer> Serializers = new()
    {
        { typeof(string), new StringSerializer() },
        { typeof(int), new IntegerSerializer() },
        { typeof(double), new DoubleSerializer() },
        { typeof(bool), new BoolSerializer() }
    };

    private IRegistrySerializer GetSerializer(Type valueType)
    {
        if (Serializers.TryGetValue(valueType, out var serializer))
        {
            return serializer;
        }
        throw new ArgumentException("Unsupported type for configuration setting: " + valueType.Name);
    }

    public IEnumerable<ConfigurationCategory> GetRootCategories()
    {
        return _rootCategories;
    }
    
    public ConfigurationCategory? FindCategory(string path)
    {
        return _categoriesByPath.TryGetValue(path, out var category) ? category : null;
    }
    
    public ConfigurationSetting? FindSetting(string path)
    {
        return _settingsByName.TryGetValue(path, out var setting) ? setting : null;
    }
    
    public IEnumerable<object> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Enumerable.Empty<object>();
        }
        
        var results = new List<object>();
        var searchQuery = query.ToLowerInvariant();
        
        foreach (var category in GetAllCategories())
        {
            if (category.Name.ToLowerInvariant().Contains(searchQuery) ||
                category.Description.ToLowerInvariant().Contains(searchQuery))
            {
                results.Add(category);
            }
        }
        
        foreach (var setting in _settingsByName.Values)
        {
            if (setting.Name.ToLowerInvariant().Contains(searchQuery) ||
                setting.Title.ToLowerInvariant().Contains(searchQuery) ||
                setting.Description.ToLowerInvariant().Contains(searchQuery))
            {
                results.Add(setting);
            }
        }
        
        return results.OrderBy(x => x is ConfigurationCategory ? 0 : 1);
    }

    public IEnumerable<ConfigurationSetting> GetModifiedSettings()
    {
        return _settingsByName.Values.Where(s => s.IsDirty).ToList();
    }

    private IEnumerable<ConfigurationCategory> GetAllCategories()
    {
        var allCategories = new List<ConfigurationCategory>();
        
        void CollectCategories(ConfigurationCategory category)
        {
            allCategories.Add(category);
            foreach (var child in category.Children)
            {
                CollectCategories(child);
            }
        }
        
        foreach (var rootCategory in _rootCategories)
        {
            CollectCategories(rootCategory);
        }
        
        return allCategories;
    }
    
    public void SaveChanges()
    {
        foreach (var setting in GetModifiedSettings())
        {
            SaveSetting(setting);
        }
    }

    private void SaveSetting(ConfigurationSetting setting)
    {
        if (setting.CurrentValue == null)
        {
            storage.Delete(setting.FullPath);
        }
        else
        {
            var serializedString = setting.Serializer.Serialize(setting.CurrentValue);
            storage.Store(setting.FullPath, serializedString);
        }
        
        setting.IsDirty = false;
    }
    
    public void Reload()
    {
        foreach (var setting in _settingsByName.Values)
        {
            Reload(setting);
        }
    }
    
    public void Reload(ConfigurationSetting setting)
    {
        var storedValue = GetStoredValue(setting);
        if (storedValue != null)
        {
            setting.InitValueWithoutNotify(storedValue);
        }
    }
    
    private object? GetStoredValue(ConfigurationSetting setting)
    {
        if (!storage.Exists(setting.FullPath))
        {
            return default;
        }
        
        var storedBytes = storage.Retrieve(setting.FullPath);
        if (storedBytes == null)
        {
            return default;
        }

        return setting.Serializer.Deserialize(storedBytes);
    }
}








