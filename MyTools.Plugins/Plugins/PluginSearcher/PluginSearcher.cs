using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

public class PluginSearcher(IKeywordRegistry keywordRegistry) : PluginBase
{
    public override string PluginId => "PluginSearcher";
    public override string Name => GetCaption("Plugin.PluginSearcher.Name", "Plugin Searcher");
    public override string Description => GetCaption("Plugin.PluginSearcher.Description", "All available plugins");
    public override List<IActionWithCommand> Actions => [];
    
    public override bool IsGlobalSearchPlugin => false;
    
    public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        var list = keywordRegistry
            .Match(query)
            .Select(tuple =>
            {
                var (keyword, p) = tuple;
                return new ResultItem(new StringIcon("⚙️"), p.Name, p.Description, ActionStringParam.From(keyword), ResultItemPriorities.High);
            }).ToList();
        return Task.FromResult(Result.CreateSuccessResult(list));
    }
}

