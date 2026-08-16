using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Plugins;

public class PluginLoader(ILogger<PluginLoader> logger, IKeywordRegistry keywordRegistry, IGlobalSearchRegistry globalSearchRegistry, IActionRegistry actionRegistry, IEnumerable<IPlugin> plugins, Searcher searcher, NodePluginCatalog nodePluginCatalog, NodePluginFactory nodePluginFactory) : IDisposable
{
    private readonly List<IPlugin> dynamicPlugins = [];
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

    private async Task InitializeAsync()
    {
        var tasks = new List<Task>();
        foreach (var plugin in GetAllPlugins())
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
        // register keyword plugins
        var clipboard = plugins.OfType<ClipBoardPlugin>().First();
        keywordRegistry.Register(PluginConstants.ClipboardHistory, clipboard);

        var DllInterfaceReader = plugins.OfType<DllInterfaceReaderPlugin>().First();
        keywordRegistry.Register(PluginConstants.DllInterfaceReaderKeyword, DllInterfaceReader);

        foreach (var nodePlugin in dynamicPlugins.OfType<NodePlugin>())
        {
            foreach (var keyword in nodePlugin.Keywords)
            {
                keywordRegistry.Register(keyword, nodePlugin);
            }
        }
        
        foreach(var plugin in GetAllPlugins())
        {
            // Node plugins stay in the registry so settings can opt them into global
            // results later; Searcher still filters on IsGlobalSearchPlugin.
            if (plugin.IsGlobalSearchPlugin || plugin is NodePlugin)
            {
                globalSearchRegistry.Register(plugin);
            }
        }
        
        // register actions
        actionRegistry.Register("Ctrl+Enter", WellKnownActions.AdminExecute);
        actionRegistry.Register("Ctrl+O", WellKnownActions.OpenInExplorer);
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
        foreach (var disposable in dynamicPlugins.OfType<IDisposable>())
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
}