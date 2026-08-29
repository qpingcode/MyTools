using MyTools.Common;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Plugins;

namespace MyTools.Plugins;

public interface IPlugin
{
    PluginId PluginId { get; }
    string Name { get; }
    string Description { get; }
    public List<IActionWithHotkey> Actions { get; }
    public bool IsEnabled { get; }
    ViewModelType ViewModelType { get;}
    public bool IsGlobalSearchPlugin { get; }

    Task<Result>  SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null);
    public Task InitializeAsync();
    public void RegisterSettings(IConfigurationRegistry configurationRegistry);
}
