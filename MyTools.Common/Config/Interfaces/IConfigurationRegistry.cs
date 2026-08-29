using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Models;
using ConfigurationCategory = MyTools.Common.Config.Models.ConfigurationCategory;
using ConfigurationSetting = MyTools.Common.Config.Models.ConfigurationSetting;
using MyTools.Common.Plugins;

namespace MyTools.Common.Config.Interfaces;

public interface IConfigurationRegistry
{
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    ConfigurationCategory AddCategory(string key, string name, string description,
        bool IsSelectable = true, PluginId? pluginId = null);
    IEnumerable<ConfigurationCategory> GetRootCategories();
    ConfigurationSetting AddSetting<T>(ConfigurationCategory category, string name, string title, string description,
        T defaultValue, IRegistrySerializer? serializer = null, SettingOptions options = SettingOptions.None,
        SettingValueTypes? valueType = null);
    ConfigurationCategory? FindCategory(string name);
    ConfigurationSetting? FindSetting(string name);
    bool RemoveCategory(string name);
    void SaveChanges();
    void Reload();
    void Reload(ConfigurationSetting setting);
}
