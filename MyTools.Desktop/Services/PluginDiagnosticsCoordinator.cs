using MyTools.Host.Core.Diagnostics;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed record PluginDiagnosticsPluginItem(
    string IdentityKey,
    IPlugin Plugin,
    NodePlugin? NodePlugin,
    PluginRuntimeDiagnosticsSnapshot? RuntimeSnapshot,
    string DisplayName,
    string PluginId,
    string? Version,
    string RuntimeKind,
    bool IsEnabled);

public sealed class PluginDiagnosticsCoordinator
{
    private readonly PluginLoader _pluginLoader;
    private readonly IPluginDiagnosticsService _diagnostics;

    public PluginDiagnosticsCoordinator(PluginLoader pluginLoader, IPluginDiagnosticsService diagnostics)
    {
        _pluginLoader = pluginLoader;
        _diagnostics = diagnostics;
    }

    public (IReadOnlyList<PluginDiagnosticsPluginItem> Plugins, PluginDiagnosticsSnapshot Snapshot) GetSnapshot()
    {
        var snapshot = _diagnostics.GetSnapshot();
        var byPluginId = snapshot.Plugins
            .ToDictionary(item => item.PluginId, StringComparer.OrdinalIgnoreCase);

        var plugins = _pluginLoader.LoadedPlugins
            .Select(plugin =>
            {
                if (plugin is NodePlugin nodePlugin)
                {
                    byPluginId.TryGetValue(nodePlugin.PluginId.Value, out var runtimeSnapshot);
                    if (runtimeSnapshot is not null
                        && ShouldSuppressRuntimeSnapshot(runtimeSnapshot, nodePlugin.BusSessionId))
                    {
                        runtimeSnapshot = null;
                    }

                    return new PluginDiagnosticsPluginItem(
                        $"{nodePlugin.PluginId.Value}|{nodePlugin.OverrideKey}",
                        nodePlugin,
                        nodePlugin,
                        runtimeSnapshot,
                        nodePlugin.GetDisplayName(),
                        nodePlugin.PluginId.Value,
                        string.IsNullOrWhiteSpace(nodePlugin.Version) ? null : nodePlugin.Version,
                        nodePlugin.Runtime,
                        nodePlugin.IsEnabled);
                }

                return new PluginDiagnosticsPluginItem(
                    $"{plugin.PluginId.Value}|{plugin.GetType().FullName}",
                    plugin,
                    null,
                    null,
                    plugin.Name,
                    plugin.PluginId.Value,
                    null,
                    "built-in",
                    plugin.IsEnabled);
            })
            .OrderBy(item => item.PluginId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.IdentityKey, StringComparer.Ordinal)
            .ToArray();

        return (plugins, snapshot);
    }

    private static bool ShouldSuppressRuntimeSnapshot(
        PluginRuntimeDiagnosticsSnapshot runtimeSnapshot,
        string? liveSessionId)
    {
        if (string.IsNullOrWhiteSpace(liveSessionId))
        {
            return false;
        }

        return !string.Equals(runtimeSnapshot.CurrentSessionId, liveSessionId, StringComparison.Ordinal);
    }

    public bool CanStop(PluginDiagnosticsPluginItem item)
        => item.NodePlugin is { IsEnabled: true, BusSessionId: not null };

    public bool CanRestart(PluginDiagnosticsPluginItem item)
        => item.NodePlugin is { IsEnabled: true };

    public Task StopAsync(PluginDiagnosticsPluginItem item, CancellationToken cancellationToken = default)
    {
        if (!CanStop(item))
        {
            return Task.CompletedTask;
        }

        return item.NodePlugin!.StopBackendAsync(cancellationToken);
    }

    public Task RestartAsync(PluginDiagnosticsPluginItem item, CancellationToken cancellationToken = default)
    {
        if (!CanRestart(item))
        {
            return Task.CompletedTask;
        }

        return item.NodePlugin!.RestartBackendAsync(cancellationToken);
    }
}
