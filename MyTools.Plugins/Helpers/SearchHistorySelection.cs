using MyTools.Common.Plugins;

namespace MyTools.Plugins;

public sealed record SearchHistorySelection(
    string PluginId, string ResultKey, string Query, SearchFrom SearchFrom,
    long SelectionCount, DateTime LastSelectedAt, SearchResultSnapshot? Snapshot = null);
