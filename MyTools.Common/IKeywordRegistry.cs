using MyTools.Plugins;

namespace MyTools.Common;

public interface IKeywordRegistry
{
    void Register(string defaultKeyword, IPlugin puglin);
    void Unregister(string keyword);
    void Clear();
    string? GetKeyword(IPlugin plugin);
    bool TryFindPlugin(string searchText, out string searchTextWithoutPrefix, out IPlugin plugin);

    IEnumerable<(string keyword, IPlugin plugin)> Match(string searchText);
}