using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Plugins;

public class PluginLoader(ILogger<PluginLoader> logger, IKeywordRegistry keywordRegistry, IGlobalSearchRegistry globalSearchRegistry, IActionRegistry actionRegistry, IEnumerable<IPlugin> plugins, Searcher searcher, NodePluginCatalog nodePluginCatalog, NodePluginFactory nodePluginFactory) : IDisposable
{
    private readonly List<IPlugin> dynamicPlugins = [];
    private bool actionsRegistered;
    private bool disposed;

    public IReadOnlyList<IPlugin> InitPlugins()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        DisposeDynamicPlugins();
        dynamicPlugins.Clear();
        dynamicPlugins.AddRange(nodePluginFactory.CreatePlugins(nodePluginCatalog.Plugins));

        RegisterPlugins();
        _ = Task.Run(async () =>
        {
            await InitializeAsync();
            await searcher.WarmupHomePageAsync();
        });

        return GetAllPlugins().ToList();
    }

    public async Task<IReadOnlyList<IPlugin>> InitPluginsAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await DisposeDynamicPluginsAsync();
        dynamicPlugins.Clear();
        dynamicPlugins.AddRange(nodePluginFactory.CreatePlugins(nodePluginCatalog.Plugins));

        RegisterPlugins();
        _ = Task.Run(async () =>
        {
            await InitializeAsync();
            await searcher.WarmupHomePageAsync();
        });

        return GetAllPlugins().ToList();
    }

    public async Task<IReadOnlyList<NodePlugin>> ReloadPluginAsync(string parentPluginId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPluginId);

        var replacements = nodePluginFactory.CreatePlugins(
            nodePluginCatalog.Plugins.Where(manifest =>
                string.Equals(manifest.ParentId, parentPluginId, StringComparison.OrdinalIgnoreCase)));
        var replaced = dynamicPlugins
            .OfType<NodePlugin>()
            .Where(plugin => string.Equals(plugin.ParentId, parentPluginId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var plugin in replaced)
        {
            keywordRegistry.UnregisterPlugin(plugin);
            globalSearchRegistry.UnregisterPlugin(plugin);
            dynamicPlugins.Remove(plugin);
        }

        await DisposePluginsAsync(replaced);
        dynamicPlugins.AddRange(replacements);
        RegisterNodePlugins(replacements);
        searcher.InvalidateHomePageCache();
        _ = Task.Run(() => InitializeAsync(replacements));

        return replacements;
    }

    private async Task InitializeAsync(IEnumerable<IPlugin>? pluginsToInitialize = null)
    {
        var tasks = new List<Task>();
        foreach (var plugin in pluginsToInitialize ?? GetAllPlugins())
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await plugin.InitializeAsync();
                    logger.LogInformation("Plugins {pluginName} initialized.", plugin.Name);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error initializing plugin {pluginName}", plugin.Name);
                }
            }));
        }
        
        await Task.WhenAll(tasks);
    }

    private void RegisterPlugins()
    { 
        keywordRegistry.Clear();
        globalSearchRegistry.Clear();

        // register keyword plugins
        var clipboard = plugins.OfType<ClipBoardPlugin>().First();
        keywordRegistry.Register(PluginConstants.ClipboardHistory, clipboard);

        RegisterNodePlugins(dynamicPlugins.OfType<NodePlugin>());
        
        foreach (var plugin in plugins)
        {
            if (plugin.IsGlobalSearchPlugin)
            {
                globalSearchRegistry.Register(plugin);
            }
        }
        
        // register actions
        if (!actionsRegistered)
        {
            actionRegistry.Register("Ctrl+Enter", WellKnownActions.AdminExecute);
            actionRegistry.Register("Ctrl+O", WellKnownActions.OpenInExplorer);
            actionsRegistered = true;
        }
    }

    private IEnumerable<IPlugin> GetAllPlugins()
    {
        return plugins.Concat(dynamicPlugins);
    }

    /// <summary>
    /// 获取当前已加载的全部插件（含内置插件和动态加载的 Node 插件）。
    /// </summary>
    public IReadOnlyList<IPlugin> LoadedPlugins => GetAllPlugins().ToList();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        DisposeDynamicPlugins();
        dynamicPlugins.Clear();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private void DisposeDynamicPlugins()
    {
        DisposePlugins(dynamicPlugins.OfType<IDisposable>());
    }

    private Task DisposeDynamicPluginsAsync() =>
        DisposePluginsAsync(dynamicPlugins.OfType<NodePlugin>());

    private void RegisterNodePlugins(IEnumerable<NodePlugin> nodePlugins)
    {
        foreach (var nodePlugin in nodePlugins)
        {
            foreach (var keyword in nodePlugin.Keywords)
            {
                keywordRegistry.Register(keyword, nodePlugin);
            }

            // Node plugins stay in the registry so settings can opt them into global results.
            globalSearchRegistry.Register(nodePlugin);
        }
    }

    private void DisposePlugins(IEnumerable<IDisposable> disposablePlugins)
    {
        foreach (var disposable in disposablePlugins)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing dynamic plugin {PluginType}.", disposable.GetType().FullName);
            }
        }
    }

    private async Task DisposePluginsAsync(IEnumerable<NodePlugin> disposablePlugins)
    {
        foreach (var plugin in disposablePlugins)
        {
            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing dynamic plugin {PluginType}.", plugin.GetType().FullName);
            }
        }
    }
}
