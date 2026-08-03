using MyTools.Common;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Plugins;

namespace MyTools.Plugins;

public interface IPlugin
{
    string PluginId { get; }
    string Name { get; }
    string Description { get; }
    public List<IActionWithCommand> Actions { get; }
    public bool IsEnabled { get; }
    ViewModelType ViewModelType { get;}
    public bool IsGlobalSearchPlugin { get; }

    Task<Result>  SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null);
    public Task InitializeAsync();
    public void RegisterSettings(IConfigurationRegistry configurationRegistry);
}