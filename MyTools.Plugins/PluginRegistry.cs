using System.Diagnostics.CodeAnalysis;
using MyTools.Common;
using MyTools.Common.Utils;

namespace MyTools.Plugins;

public class PluginRegistry : IKeywordRegistry, IGlobalSearchRegistry, IActionRegistry
{
    private readonly Dictionary<string, IPlugin> _keywordMap = new();
    private readonly Dictionary<IAction, Hotkey> _actionHotkeys = new();
    private readonly List<IPlugin> _globalSearchPlugins = new();
    
    void IGlobalSearchRegistry.Register(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(plugin.Actions); 
        _globalSearchPlugins.Add(plugin);
    }

    IEnumerable<IPlugin> IGlobalSearchRegistry.Plugins => _globalSearchPlugins;

    void IGlobalSearchRegistry.UnregisterPlugin(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _globalSearchPlugins.RemoveAll(item => ReferenceEquals(item, plugin));
    }

    void IGlobalSearchRegistry.Clear() => _globalSearchPlugins.Clear();

    void IKeywordRegistry.Register(string keyword, IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(plugin.Actions);
        _keywordMap[keyword] = plugin;
    }

    void IKeywordRegistry.Unregister(string keyword)
    {
        if (!string.IsNullOrEmpty(keyword))
        {
            _keywordMap.Remove(keyword);
        }
    }

    void IKeywordRegistry.UnregisterPlugin(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var keys = _keywordMap
            .Where(kvp => ReferenceEquals(kvp.Value, plugin))
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keys)
        {
            _keywordMap.Remove(key);
        }
    }

    void IKeywordRegistry.Clear()
    {
        _keywordMap.Clear();
    }

    bool IKeywordRegistry.TryFindPlugin(string searchText, [NotNullWhen(true)] out string searchTextWithoutPrefix, [NotNullWhen(true)] out IPlugin plugin)
    {
        var prefix = GetPrefix(searchText);
        searchTextWithoutPrefix = GetQueryWithoutPrefix(searchText, prefix);
        if (!_keywordMap.ContainsKey(prefix))
        {
            plugin = null!;
            return false;
        }
        else
        {
            plugin = _keywordMap[prefix];
            return true;
        }
    }

    IEnumerable<(string keyword, IPlugin plugin)> IKeywordRegistry.Match(string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return _keywordMap
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        var plugins = _keywordMap
            .Where(kvp => kvp.Key.StartsWith(searchText) || StringUtils.IsSubsequence(searchText, kvp.Value.Name)) 
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();

        return plugins;
    }

    string GetPrefix(string searchText)
    {
        var separatorIndex = IndexOfKeywordSeparator(searchText);
        if (separatorIndex == -1)
        {
            return string.Empty;
        }
        var prefix = searchText.Substring(0, separatorIndex);
        return prefix;
    }

    string GetQueryWithoutPrefix(string searchText, string prefix)
    {
        if (prefix == string.Empty)
        {
            return searchText;
        }

        return searchText.Substring(prefix.Length).TrimStart();
    }

    static int IndexOfKeywordSeparator(string searchText)
    {
        for (var index = 0; index < searchText.Length; index++)
        {
            if (char.IsWhiteSpace(searchText[index]))
            {
                return index;
            }
        }

        return -1;
    }

    void IActionRegistry.Register(Hotkey hotkey, IAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _actionHotkeys.Add(action, hotkey);
    }

    IAction? IActionRegistry.GetAction(Hotkey hotkey, IEnumerable<IAction> allowedActions)
    {
        var registry = this as IActionRegistry;
        return allowedActions.FirstOrDefault(a => registry.GetHotkey(a) == hotkey);
    }

    Hotkey? IActionRegistry.GetHotkey(IAction action)
    {
        return _actionHotkeys.TryGetValue(action, out var hotkey) ? hotkey : null;
    }

    public string? GetKeyword(IPlugin plugin)
    {
        var keyword = _keywordMap
            .Where(kvp => kvp.Value == plugin) 
            .Select(kvp => kvp.Key)
            .FirstOrDefault();

        return keyword;
    }
}
