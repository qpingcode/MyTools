using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Models;
using ConfigurationCategory = MyTools.Common.Config.Models.ConfigurationCategory;
using ConfigurationSetting = MyTools.Common.Config.Models.ConfigurationSetting;

namespace MyTools.Common.Config.Interfaces;

public interface IConfigurationRegistry
{
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    ConfigurationCategory AddCategory(string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true);
    ConfigurationCategory AddCategory(string key, string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true);
    IEnumerable<ConfigurationCategory> GetRootCategories();
    ConfigurationSetting AddSetting<T>(ConfigurationCategory category, string name, string title, string description,
        T defaultValue, IRegistrySerializer? serializer = null, SettingOptions options = SettingOptions.None,
        SettingValueTypes? valueType = null);
    ConfigurationCategory? FindCategory(string path);
    ConfigurationSetting? FindSetting(string path);
    bool RemoveCategory(string path);
    IEnumerable<object> Search(string query);
    IEnumerable<ConfigurationSetting> GetModifiedSettings();
    void SaveChanges();
    void Reload();
    void Reload(ConfigurationSetting setting);
}


