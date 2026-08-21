using System.IO;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Plugins;
using MyTools.Plugins.Param;
using Newtonsoft.Json;

namespace MyTools.Plugins;

public sealed class SearchEnginePlugin : PluginBase, IPlugin
{
    public override string PluginId => "SearchEngine";
    private List<SearchEngineConfig> _searchEngines = new();

    public override string Name => GetCaption("Plugin.SearchEngine.Name", "Web Search");
    public override string Description => GetCaption("Plugin.SearchEngine.Description", "Search using different search engines");
    public override List<IActionWithCommand> Actions => [WellKnownActions.OpenInBrowser.WithDefaultCommand()];

    private Icon _icon = new StringIcon("🌐");
    public override bool IsGlobalSearchPlugin => true;
    
    public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(Result.CreateEmpty());
        }

        var items = _searchEngines.Select(engine =>
        {
            return new ResultItem(
                _icon,
                $"Search {engine.Name}: {query} ",
                $"Search using {engine.Name}",
                ActionStringParam.From(GetUrl(engine, query)),
                ResultItemPriorities.Low);
        });
        var result = Result.CreateSuccessResult(items);
        return Task.FromResult(result);
    }

    private string GetUrl(SearchEngineConfig engineConfig, string query)
    {
        if (!string.IsNullOrEmpty(engineConfig.Url))
        {
            return CreateUrl(engineConfig.Url, query);
        }

        if (engineConfig.Urls != null)
        {
            return string.Join(OpenInBrowser.SplitStr, engineConfig.Urls.Select(url => CreateUrl(url, query)));
        }

        return string.Empty;
    }

    private string CreateUrl(string url, string query)
    {
        return url.Replace("{query}", Uri.EscapeDataString(query));
    }

    public override async Task InitializeAsync()
    {
        var configPath = Path.Combine(ConfigPath.Base, "SearchEnginePlugin.json");
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            _searchEngines = JsonConvert.DeserializeObject<List<SearchEngineConfig>>(json) ??
                             new List<SearchEngineConfig>();
        }

        SetDefaultBrowserIconIfPossible();
        
        pluginState.IsEnabled = _searchEngines.Count > 0;
    }

    private void SetDefaultBrowserIconIfPossible()
    {
        var defaultBrowserPath = DefaultBrowserHelper.GetBrowserExecutePath();
        if (defaultBrowserPath != null)
        {
            var imageData = FileIconHelper.GetFileIconData(defaultBrowserPath);
            if (imageData != null)
            {
                _icon = new ImageIcon(imageData);
            }
        }
    }
}