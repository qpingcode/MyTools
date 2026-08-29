using System.Collections.ObjectModel;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Plugins;
using MyTools.Desktop.Serializers;

namespace MyTools.Desktop.Services;

public class ConfigurationRegistry(IConfigurationStorage storage) : IConfigurationRegistry
{
    private readonly ObservableCollection<ConfigurationCategory> _rootCategories = new();
    private readonly Dictionary<string, ConfigurationSetting> settingsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConfigurationCategory> categoriesByKey = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    public ConfigurationCategory AddCategory(
        string key,
        string name,
        string description,
        bool IsSelectable = true,
        PluginId? pluginId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var order =  _rootCategories.Count;
        var category = new ConfigurationCategory
        {
            Key = key,
            PluginId = pluginId,
            Name = name,
            Description = description,
            SortOrder = order,
            IsSelectable = IsSelectable
        };

        _rootCategories.Add(category);
        categoriesByKey.Add(category.Key, category);
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
            Key = $"{category.Key}.{name}",
            Name = name,
            PluginId = category.PluginId,
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
        if ((options & SettingOptions.DisplayOnly) == 0)
        {
            settingsByKey.Add(setting.Key, setting);
        }

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
    
    public ConfigurationCategory? FindCategory(string key)
    {
        return categoriesByKey.TryGetValue(key, out var category) ? category : null;
    }
    
    public ConfigurationSetting? FindSetting(string key)
    {
        return settingsByKey.TryGetValue(key, out var setting) ? setting : null;
    }

    public bool RemoveCategory(string key)
    {
        if (!categoriesByKey.TryGetValue(key, out var category))
        {
            return false;
        }
        foreach (var setting in category.Settings)
        {
            settingsByKey.Remove(setting.Key);
        }
        _rootCategories.Remove(category);
        categoriesByKey.Remove(category.Key);
        return true;
    }

    public void SaveChanges()
    {
        var modifiedSettings = settingsByKey.Values.Where(s => s.IsDirty).ToList();
        foreach (var setting in modifiedSettings)
        {
            SaveSetting(setting);
        }
    }

    private void SaveSetting(ConfigurationSetting setting)
    {
        if (setting.IsDisplayOnly)
        {
            setting.IsDirty = false;
            return;
        }

        var oldValue = GetStoredValue(setting) ?? setting.DefaultValue;
        var newValue = setting.CurrentValue;
        var changed = !ValuesEqual(setting, oldValue, newValue);

        if (newValue == null)
        {
            storage.Delete(setting.StorageKey, setting.PluginId);
        }
        else
        {
            var serializedString = setting.Serializer.Serialize(newValue);
            storage.Store(setting.StorageKey, serializedString, setting.PluginId);
        }
        
        setting.IsDirty = false;
        if (changed)
        {
            ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(setting, oldValue, newValue));
        }
    }

    private static bool ValuesEqual(ConfigurationSetting setting, object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            setting.Serializer.Serialize(left),
            setting.Serializer.Serialize(right),
            StringComparison.Ordinal);
    }
    
    public void Reload()
    {
        foreach (var setting in settingsByKey.Values)
        {
            Reload(setting);
        }
    }
    
    public void Reload(ConfigurationSetting setting)
    {
        if (setting.IsDisplayOnly)
        {
            return;
        }

        var oldValue = setting.CurrentValue;
        var storedValue = GetStoredValue(setting);
        if (storedValue != null)
        {
            setting.InitValueWithoutNotify(storedValue);
            if (!ValuesEqual(setting, oldValue, storedValue))
            {
                ConfigurationChanged?.Invoke(
                    this,
                    new ConfigurationChangedEventArgs(setting, oldValue, storedValue));
            }
        }
    }
    
    private object? GetStoredValue(ConfigurationSetting setting)
    {
        if (!storage.Exists(setting.StorageKey, setting.PluginId))
        {
            return default;
        }
        
        var storedBytes = storage.Retrieve(setting.StorageKey, setting.PluginId);
        if (storedBytes == null)
        {
            return default;
        }

        return setting.Serializer.Deserialize(storedBytes);
    }
}








