using MyTools.Common.Config.Interfaces;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Model.Plugins;
using MyTools.Plugins;
using ConfigurationCategory = MyTools.Common.Config.Models.ConfigurationCategory;

namespace MyTools.Common.Plugins;

public abstract class PluginBase : IPlugin
{
    /// <summary>
    /// Stable, non-localized identifier used for persistence and routing.
    /// Override when a public plugin id already exists.
    /// </summary>
    public virtual PluginId PluginId
    {
        get
        {
            var typeName = GetType().Name;
            var value = typeName.EndsWith("Plugin", StringComparison.Ordinal)
                ? typeName[..^"Plugin".Length]
                : typeName;
            return new PluginId(value);
        }
    }

    protected readonly PluginState pluginState = new();
    
    public bool IsEnabled => pluginState.IsEnabled;
    public virtual ViewModelType ViewModelType => ViewModelType.Basic;
    public virtual bool IsGlobalSearchPlugin => false;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract List<IActionWithHotkey> Actions { get; }

    protected static string GetCaption(string key, string defaultValue, object? values = null)
    {
        try
        {
            return ServiceLocator.GetService<ILocalizationService>()?.GetCaption(key, defaultValue, values)
                   ?? FormatFallback(defaultValue, values);
        }
        catch (InvalidOperationException)
        {
            return FormatFallback(defaultValue, values);
        }
    }

    private static string FormatFallback(string defaultValue, object? values) =>
        LocalizedMessage.Format(defaultValue, LocalizedMessage.ToDictionary(values), System.Globalization.CultureInfo.CurrentCulture);

    public abstract Task<Result> SearchAsync(string query, CancellationToken cancellationToken,
        SearchOptions? searchOptions = null);

    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual string SettingsCategoryName => Name;
    protected virtual string SettingsCategoryDescription => Description;

    public void RegisterSettings(IConfigurationRegistry configurationRegistry)
    {
        var thisPluginCategory = configurationRegistry.AddCategory(
            PluginId.Value,
            SettingsCategoryName,
            SettingsCategoryDescription,
            pluginId: PluginId);
        AddPluginSettings(thisPluginCategory, configurationRegistry);
        // If the plugin does not register any additional settings, remove this empty category and do not display it in the sidebar.
        if (thisPluginCategory.Settings.Count == 0)
        {
            configurationRegistry.RemoveCategory(thisPluginCategory.Key);
        }
    }

    protected virtual void AddPluginSettings(ConfigurationCategory pluginCategory, IConfigurationRegistry configurationRegistry)
    {
    }
}
