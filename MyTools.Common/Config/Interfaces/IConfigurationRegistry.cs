using MyTools.Common.Config.Enums;
using ConfigurationCategory = MyTools.Common.Config.Models.ConfigurationCategory;
using ConfigurationSetting = MyTools.Common.Config.Models.ConfigurationSetting;

namespace MyTools.Common.Config.Interfaces;

public interface IConfigurationRegistry
{
    ConfigurationCategory AddCategory(string name, string description, ConfigurationCategory? parent = null, bool IsSelectable = true);
    IEnumerable<ConfigurationCategory> GetRootCategories();
    ConfigurationSetting AddSetting<T>(ConfigurationCategory category, string name, string title, string description,
        T defaultValue, IRegistrySerializer? serializer = null, SettingOptions options = SettingOptions.None);
    ConfigurationCategory? FindCategory(string path);
    ConfigurationSetting? FindSetting(string path);
    IEnumerable<object> Search(string query);
    IEnumerable<ConfigurationSetting> GetModifiedSettings();
    void SaveChanges();
    void Reload();
    void Reload(ConfigurationSetting setting);
}


