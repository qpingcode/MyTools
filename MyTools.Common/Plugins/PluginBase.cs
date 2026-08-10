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
    public virtual string PluginId
    {
        get
        {
            var typeName = GetType().Name;
            return typeName.EndsWith("Plugin", StringComparison.Ordinal)
                ? typeName[..^"Plugin".Length]
                : typeName;
        }
    }

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

    public void RegisterSettings(IConfigurationRegistry configurationRegistry)
    {
        // 插件设置分类作为顶层分类（不再嵌套在 Plugins 下），呈扁平列表展示。
        var thisPluginCategory = configurationRegistry.AddCategory(PluginId, Name, Description);
        AddPluginSettings(thisPluginCategory, configurationRegistry);

        // 如果插件没有注册任何额外设置项，则移除这个空分类，不在侧栏中显示。
        if (thisPluginCategory.Settings.Count == 0)
        {
            var rootCategories = configurationRegistry.GetRootCategories().ToList();
            var parent = thisPluginCategory.Parent;
            if (parent != null)
            {
                parent.Children.Remove(thisPluginCategory);
            }
        }
    }

    protected string GetSettingFullPath(string settingName)
    {
        return $"Plugins.{PluginId}.{settingName}";
    }

    /// <summary>
    /// 子类重写以添加插件特有的设置项。基类不再注册默认的 IsEnabled——
    /// 启用状态统一在 Plugins 分类中管理。
    /// </summary>
    protected virtual void AddPluginSettings(ConfigurationCategory pluginCategory, IConfigurationRegistry configurationRegistry)
    {
    }
}