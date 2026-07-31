using MyTools.Common.Config.Interfaces;
using MyTools.Common.Model.Plugins;
using MyTools.Plugins;
using ConfigurationCategory = MyTools.Common.Config.Models.ConfigurationCategory;

namespace MyTools.Common.Plugins;

public abstract class PluginBase : IPlugin
{
    protected readonly PluginState pluginState = new();
    public IPluginState PluginState => pluginState;
    
    public bool IsEnabled => pluginState.IsEnabled;
    public virtual ViewModelType ViewModelType => ViewModelType.Basic;
    public virtual bool IsGlobalSearchPlugin => false;

    protected void DisablePlugin()
    {
        pluginState.IsEnabled = false;
    }
    
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract List<IActionWithCommand> Actions { get; }

    public abstract Task<Result> SearchAsync(string query, CancellationToken cancellationToken,
        SearchOptions? searchOptions = null);

    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public void RegisterSettings(IConfigurationRegistry configurationRegistry)
    {
        var pluginsCategory = configurationRegistry.FindCategory("Plugins");
        var thisPluginCategory = configurationRegistry.AddCategory(Name, Description, pluginsCategory);
        AddPluginSettings(thisPluginCategory, configurationRegistry);
    }

    protected string GetSettingFullPath(string settingName)
    {
        return $"Plugins.{Name}.{settingName}";
    }
    protected virtual void AddPluginSettings(ConfigurationCategory pluginCategory, IConfigurationRegistry configurationRegistry)
    {
        configurationRegistry.AddSetting(pluginCategory, "IsEnabled", "启用插件", "启用或禁用此插件", true);
    }
}