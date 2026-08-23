using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Enums;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Theming;
using MyTools.Common.Utils;
using MyTools.Desktop.Models;
using MyTools.Desktop.Services;
using MyTools.Desktop.Services.WindowNativeHandler;
using MyTools.Desktop.Themes;
using MyTools.Desktop.Utils;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Clipboard = System.Windows.Clipboard;


namespace MyTools.Desktop;

public class AppBootstrapper : IDisposable
{
    private readonly NativeMessageWindowHost nativeMessageWindowHost;
    private readonly GestureRegistry gestureRegistry;
    private readonly GestureConfigProvider gestureConfigProvider;
    private readonly MouseHelper mouseHelper;
    private readonly HotKeyManager hotKeyManager;
    private readonly IReadOnlyList<IPlugin> plugins;
    private readonly NodePluginCatalog nodePluginCatalog;
    private readonly PluginLoader pluginLoader;
    private readonly ILogger<AppBootstrapper> logger;
    private readonly LogLevelService logLevelService;
    private readonly IPluginLauncher pluginLauncher;
    private readonly PluginHotKeyService pluginHotKeyService;
    private readonly PluginKeymapService pluginKeymapService;
    private readonly IConfigurationRegistry registry;
    private readonly ILocalizationService localization;
    private readonly IThemeService themeService;
    private readonly DevelopmentPluginService developmentPluginService;
    private readonly PluginWindowManager pluginWindowManager;
    private readonly IKeyboardHelper keyboardHelper;
    private readonly SemaphoreSlim nodePluginReloadLock = new(1, 1);

    public AppBootstrapper(
        NativeMessageWindowHost nativeMessageWindowHost,
        GestureRegistry gestureRegistry,
        GestureConfigProvider gestureConfigProvider,
        MouseHelper mouseHelper,
        HotKeyManager hotKeyManager,
        IEnumerable<IPlugin> plugins,
        NodePluginCatalog nodePluginCatalog,
        PluginLoader pluginLoader,
        ILogger<AppBootstrapper> logger,
        LogLevelService logLevelService,
        IPluginLauncher pluginLauncher,
        PluginHotKeyService pluginHotKeyService,
        PluginKeymapService pluginKeymapService,
        IConfigurationRegistry registry,
        ILocalizationService localization,
        IThemeService themeService,
        DevelopmentPluginService developmentPluginService,
        PluginWindowManager pluginWindowManager,
        IKeyboardHelper keyboardHelper)
    {
        this.nativeMessageWindowHost = nativeMessageWindowHost;
        this.gestureRegistry = gestureRegistry;
        this.gestureConfigProvider = gestureConfigProvider;
        this.mouseHelper = mouseHelper;
        this.hotKeyManager = hotKeyManager;
        this.plugins = plugins.ToArray();
        this.nodePluginCatalog = nodePluginCatalog;
        this.pluginLoader = pluginLoader;
        this.logger = logger;
        this.logLevelService = logLevelService;
        this.pluginLauncher = pluginLauncher;
        this.pluginHotKeyService = pluginHotKeyService;
        this.pluginKeymapService = pluginKeymapService;
        this.registry = registry;
        this.localization = localization;
        this.themeService = themeService;
        this.developmentPluginService = developmentPluginService;
        this.pluginWindowManager = pluginWindowManager;
        this.keyboardHelper = keyboardHelper;
    }

    public void Init()
    {
        // Ensure that NativeMessageWindowHost has been loaded and Windows messages have been properly monitored
        // which is a prerequisite for the clipboard / hotkey
        EnsureNativeMessageWindowHost();

        developmentPluginService.ReloadRequested += OnDevelopmentPluginsReloadRequested;
        developmentPluginService.Initialize();

        // Node plugins (and their plugin.json configuration) must exist before settings
        // are registered, otherwise Tools categories like Snippet never appear.
        var nodePlugins = LoadNodePlugins();

        InitializeConfigurationData();

        // Apply the user-configured log level now that settings have been loaded.
        logLevelService.ApplyFromSettings(registry);

        var searchHotKeyText = registry.FindSetting(GeneralSettings.SearchHotKeyPath)?.GetValue<string>()
                               ?? GeneralSettings.DefaultSearchHotKey;
        RegisterGlobalHotKey(new HotKeyConfig(searchHotKeyText));
        
        InitializeGestureDetection();

        RegisterNodePluginHostCallHandlers(nodePlugins);
        RegisterNodePluginOverrides(nodePlugins);

        // Apply the user-configured theme and keep WPF in sync on hot-swap.
        ThemeManager.ApplyTheme(themeService.CurrentTheme);
        themeService.ThemeChanged += OnThemeChanged;
    }

    private void OnDevelopmentPluginsReloadRequested(object? sender, DevelopmentPluginReloadRequestedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        _ = dispatcher.InvokeAsync(() => _ = ReloadNodePluginsAsync(e.PluginId));
    }

    private async Task ReloadNodePluginsAsync(string? parentPluginId)
    {
        await nodePluginReloadLock.WaitAsync();
        try
        {
            var previousConfigurations = CaptureNodePluginConfigurations(nodePluginCatalog.Plugins);
            nodePluginCatalog.Reload();
            var currentConfigurations = CaptureNodePluginConfigurations(nodePluginCatalog.Plugins);
            if (string.IsNullOrWhiteSpace(parentPluginId))
            {
                var nodePlugins = (await pluginLoader.InitPluginsAsync()).OfType<NodePlugin>().ToList();
                RefreshNodePluginSettings(
                    FindChangedPluginConfigurations(previousConfigurations, currentConfigurations),
                    nodePlugins);
                RegisterNodePluginHostCallHandlers(nodePlugins);
                pluginKeymapService.ApplyOverrides(nodePlugins);
                pluginHotKeyService.ReRegisterAll(nodePlugins, OpenNodePluginDetail);
                pluginKeymapService.ReRegisterKeywords(pluginLoader.LoadedPlugins);
                pluginWindowManager.RefreshOpenPlugins(nodePlugins);
                logger.LogInformation("Reloaded all {Count} Node plugins.", nodePlugins.Count);
                return;
            }

            var replacements = (await pluginLoader.ReloadPluginAsync(parentPluginId)).ToList();
            var configurationChanged = ConfigurationChanged(
                parentPluginId, previousConfigurations, currentConfigurations);
            if (configurationChanged)
            {
                RefreshNodePluginSettings([parentPluginId], replacements);
            }
            RegisterNodePluginHostCallHandlers(replacements);
            pluginKeymapService.ApplyOverrides(replacements);
            pluginKeymapService.ReRegisterKeywords(replacements);
            pluginHotKeyService.ReRegisterPlugin(parentPluginId, replacements, OpenNodePluginDetail);
            pluginWindowManager.RefreshOpenPlugin(parentPluginId, replacements);
            if (configurationChanged)
            {
                RefreshOpenSettingsPlugin();
            }
            logger.LogInformation(
                "Reloaded development plugin {PluginId} with {Count} entries.",
                parentPluginId, replacements.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reload development plugins.");
        }
        finally
        {
            nodePluginReloadLock.Release();
        }
    }

    private static void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        ThemeManager.ApplyTheme(e.CurrentTheme);
    }
    
    private void EnsureNativeMessageWindowHost()
    {
        nativeMessageWindowHost.EnsureCreated();
    }

    private void InitializeGestureDetection()
    {
        var enableGesture = registry.FindSetting("Gestures.EnableGesture")?.GetValue<bool>() ?? false;
        if (!enableGesture)
        {
            return;
        }

        var configs = gestureConfigProvider.GetAll();
        gestureRegistry.ReloadFromConfigs(configs, mouseHelper);

        var gestureThread = new Thread(() =>
        {
            gestureRegistry.StartListening();
        })
        { Name = "Gesture Thread", IsBackground = true };

        gestureThread.SetApartmentState(ApartmentState.STA);
        gestureThread.Start();
    }

    private void RegisterGlobalHotKey(HotKeyConfig SearchHotKey)
    {
        hotKeyManager.RegisterySearchHotKey(SearchHotKey);

        var clipboardHotKeyText = registry.FindSetting("ClipBoard.HotKey")?.GetValue<string>()
                                  ?? ClipBoardPlugin.DefaultHotKey;
        var clipboardHotKey = new HotKeyConfig(clipboardHotKeyText);
        hotKeyManager.RegisterClipboardHotKey(clipboardHotKey,
            () => pluginLauncher.Open("ClipBoard"));

        var sequentialPasteHotKeyText = registry.FindSetting("ClipBoard.SequentialPasteHotKey")?.GetValue<string>()
                                        ?? ClipBoardPlugin.DefaultSequentialPasteHotKey;
        var sequentialPasteHotKey = new HotKeyConfig(sequentialPasteHotKeyText);
        var clipboardPlugin = plugins.OfType<ClipBoardPlugin>().First();
        hotKeyManager.RegisterClipboardSequentialPasteHotKey(sequentialPasteHotKey,
            () => _ = clipboardPlugin.PasteLatestAndRemoveAsync(keyboardHelper));
    }
    
    private List<NodePlugin> LoadNodePlugins()
    {
        CopyExampleWhenConfigNotExists();
        nodePluginCatalog.Reload();
        return pluginLoader.InitPlugins().OfType<NodePlugin>().ToList();
    }

    /// <summary>
    /// 为声明了可处理 capability 的 Node 插件注册 hostCall handler。
    /// </summary>
    private void RegisterNodePluginHostCallHandlers(IEnumerable<NodePlugin> nodePlugins)
    {
        var router = ServiceLocator.GetRequiredService<Services.NodePluginHostCallRouter>();
        foreach (var nodePlugin in nodePlugins)
        {
            if (router.HasHandlerForPlugin(nodePlugin))
            {
                nodePlugin.RegisterHostCallHandler(router.HandleAsync);
            }
        }
    }

    private void RegisterNodePluginOverrides(IEnumerable<NodePlugin> nodePlugins)
    {
        var plugins = nodePlugins.ToList();
        pluginKeymapService.ApplyOverrides(plugins);
        pluginHotKeyService.RegisterAll(plugins, OpenNodePluginDetail);
        pluginKeymapService.ReRegisterKeywords(pluginLoader.LoadedPlugins);
    }

    private void OpenNodePluginDetail(NodePlugin nodePlugin)
    {
        pluginLauncher.Open(nodePlugin);
    }

    /// <summary>
    /// 打开 settings 插件窗口（供托盘菜单调用）。
    /// </summary>
    public void OpenSettings()
    {
        var settingsPlugin = pluginLoader.LoadedPlugins.OfType<NodePlugin>().FirstOrDefault(p => p.ParentId == "settings");
        if (settingsPlugin != null)
        {
            OpenNodePluginDetail(settingsPlugin);
            return;
        }

        // settings 插件未加载时，退回到搜索窗口
        WindowHelper.ShowSearchWindow();
    }
    
    private void CopyExampleWhenConfigNotExists()
    {
        var configPath = ConfigPath.Base;
        var examplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples");
        var pluginTargetPath = Path.Combine(configPath, "plugins");
        
        if (Directory.Exists(examplePath))
        {
            Directory.CreateDirectory(pluginTargetPath);
            foreach (var sourcePluginDirectory in Directory.GetDirectories(examplePath))
            {
                // Skip non-plugin directories (sdk-v3, common, node_modules, etc.) that have no dist.
                var distDir = Path.Combine(sourcePluginDirectory, "dist");
                if (!File.Exists(Path.Combine(distDir, "plugin.json")))
                {
                    continue;
                }

                var targetPluginDirectory = Path.Combine(pluginTargetPath, Path.GetFileName(sourcePluginDirectory));
                SyncDirectory(GetExamplePluginSourceDirectory(sourcePluginDirectory), targetPluginDirectory);
                GenerateThemeHtmlFiles(targetPluginDirectory);
            }
        }
    }

    private void RefreshNodePluginSettings(
        IEnumerable<string> pluginIds,
        IEnumerable<NodePlugin> currentPlugins)
    {
        var ids = pluginIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return;

        foreach (var pluginId in ids)
        {
            registry.RemoveCategory(pluginId);
        }
        foreach (var plugin in currentPlugins.Where(plugin => ids.Contains(plugin.ParentId)))
        {
            plugin.RegisterSettings(registry);
        }
        foreach (var pluginId in ids)
        {
            var category = registry.FindCategory(pluginId);
            if (category is null) continue;
            foreach (var setting in category.Settings)
            {
                registry.Reload(setting);
            }
        }
    }

    private void RefreshOpenSettingsPlugin()
    {
        var settingsPlugins = pluginLoader.LoadedPlugins
            .OfType<NodePlugin>()
            .Where(plugin => string.Equals(plugin.ParentId, "settings", StringComparison.OrdinalIgnoreCase))
            .ToList();
        pluginWindowManager.RefreshOpenPlugin("settings", settingsPlugins);
    }

    internal static IReadOnlyDictionary<string, string> CaptureNodePluginConfigurations(
        IEnumerable<NodePluginManifest> manifests) =>
        manifests
            .GroupBy(manifest => manifest.ParentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => JsonSerializer.Serialize(group.First().Configuration),
                StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlySet<string> FindChangedPluginConfigurations(
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> current) =>
        previous.Keys.Concat(current.Keys)
            .Where(pluginId => ConfigurationChanged(pluginId, previous, current))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool ConfigurationChanged(
        string pluginId,
        IReadOnlyDictionary<string, string> previous,
        IReadOnlyDictionary<string, string> current) =>
        !previous.TryGetValue(pluginId, out var oldConfiguration)
        || !current.TryGetValue(pluginId, out var newConfiguration)
        || !string.Equals(oldConfiguration, newConfiguration, StringComparison.Ordinal);

    /// <summary>
    /// For every index.html under the plugin directory, generate a theme-specific
    /// variant (index.dark.html, index.light.html) with inline CSS variable
    /// definitions. The runtime selects the right one based on the active theme,
    /// so the variables exist at first paint — no flash.
    /// </summary>
    private static void GenerateThemeHtmlFiles(string pluginDirectory)
    {
        foreach (var htmlFile in Directory.GetFiles(pluginDirectory, "index.html", SearchOption.AllDirectories))
        {
            var html = File.ReadAllText(htmlFile);
            var dir = Path.GetDirectoryName(htmlFile)!;

            foreach (var theme in Enum.GetValues<ThemeKind>())
            {
                var themed = WebThemeTokens.InjectThemeStyle(html, theme);
                var themedPath = Path.Combine(dir, WebThemeTokens.ThemeHtmlFileName("index.html", theme));
                File.WriteAllText(themedPath, themed);
            }
        }
    }

    private static string GetExamplePluginSourceDirectory(string sourcePluginDirectory)
    {
        var distDirectory = Path.Combine(sourcePluginDirectory, "dist");
        if (File.Exists(Path.Combine(distDirectory, "plugin.json")))
        {
            return distDirectory;
        }
        throw new Exception("Missing dist folder, please compile plugin first. directory: " + distDirectory);
    }

    private static void SyncDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            if (!File.Exists(targetFile)
                || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(targetFile)
                || new FileInfo(file).Length != new FileInfo(targetFile).Length)
            {
                File.Copy(file, targetFile, true);
            }
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            SyncDirectory(directory, Path.Combine(targetDirectory, Path.GetFileName(directory)));
        }
    }
    
    private void InitializeConfigurationData()
    {
        try
        {
            var generalCategory = registry.AddCategory(
                "General",
                localization.GetCaption("Configuration.General.Name", "General"),
                localization.GetCaption("Configuration.General.Description", "General Settings"));
            
            registry.AddSetting(generalCategory, "Language",
                localization.GetCaption("Configuration.General.Language.Title", "Language"),
                localization.GetCaption("Configuration.General.Language.Description", "Select the application display language"),
                localization.CurrentLocale,
                options: SettingOptions.RequiresRestart,
                valueType: SettingValueTypes.Language);

            registry.AddSetting(generalCategory, "Theme",
                localization.GetCaption("Configuration.General.Theme.Title", "Theme"),
                localization.GetCaption("Configuration.General.Theme.Description", "Choose the application color theme"),
                themeService.CurrentTheme.ToWireString(),
                valueType: SettingValueTypes.Theme);

            registry.AddSetting(generalCategory, "SearchHotKey",
                localization.GetCaption("Configuration.General.SearchHotKey.Title", "Search hotkey"),
                localization.GetCaption("Configuration.General.SearchHotKey.Description", "Keyboard shortcut that opens MyTools search"),
                GeneralSettings.DefaultSearchHotKey,
                valueType: SettingValueTypes.HotKey);
            
            registry.AddSetting(generalCategory, "AutoStart",
                localization.GetCaption("Configuration.General.AutoStart.Title", "Auto start"),
                localization.GetCaption("Configuration.General.AutoStart.Description", "Run MyTools when the system starts"), false);
            
            registry.AddSetting(generalCategory, "SearchDelay",
                localization.GetCaption("Configuration.General.SearchDelay.Title", "Search delay"),
                localization.GetCaption("Configuration.General.SearchDelay.Description", "Search debounce delay in milliseconds"), 250.0);
            
            registry.AddSetting(generalCategory, "UpdateUrl",
                localization.GetCaption("Configuration.General.UpdateUrl.Title", "Update URL"),
                localization.GetCaption("Configuration.General.UpdateUrl.Description", "HTTPS or local path containing Velopack releases"), UpdateService.DefaultUpdateUrl);
            
            registry.AddSetting(generalCategory, "UpdateChannel",
                localization.GetCaption("Configuration.General.UpdateChannel.Title", "Update channel"),
                localization.GetCaption("Configuration.General.UpdateChannel.Description", "Choose between stable releases and beta previews"),
                UpdateService.DefaultChannel,
                valueType: SettingValueTypes.UpdateChannel);
            
            registry.AddSetting(generalCategory, "UpdateProxyUrl",
                localization.GetCaption("Configuration.General.UpdateProxyUrl.Title", "Update proxy"),
                localization.GetCaption("Configuration.General.UpdateProxyUrl.Description", "Optional proxy URL; leave empty for a direct connection"), string.Empty);
            registry.AddSetting(generalCategory, "LogLevel",
                localization.GetCaption("Configuration.General.LogLevel.Title", "Log level"),
                localization.GetCaption("Configuration.General.LogLevel.Description", "Minimum level of messages written to the log file"),
                "Debug",
                valueType: SettingValueTypes.LogLevel);

            // Mouse Gestures category (rendered as a dedicated list editor in the settings UI)
            var gesturesCategory = registry.AddCategory(
                "Gestures",
                localization.GetCaption("Configuration.Gestures.Name", "Gestures"),
                localization.GetCaption("Configuration.Gestures.Description", "Hold the right mouse button and draw a gesture, then release it to run the assigned action. Each gesture can be limited to specific processes and mapped to a keyboard shortcut or mouse button."),
                IsSelectable: true);

            registry.AddSetting(gesturesCategory, "EnableGesture",
                localization.GetCaption("Configuration.Gestures.Enable.Title", "Enable"),
                string.Empty,
                false,
                options: SettingOptions.RequiresRestart);

            // Add Plugin Settings
            registry.AddCategory(
                "Plugins",
                localization.GetCaption("Configuration.Plugins.Name", "Plugins"),
                localization.GetCaption("Configuration.Plugins.Description", "Enable plugins, assign hotkeys, and set aliases used in global search."),
                IsSelectable: true);
             
            foreach (var plugin in pluginLoader.LoadedPlugins)
            {
                try
                {
                    plugin.RegisterSettings(registry);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to register settings for plugin {Plugin}.", plugin.Name);
                }
            }
            
            // Load configuration from file if exists
            registry.Reload();
       
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize configuration test data: {ex.Message}");
        }
    }

    private string GetSelectedTextFromActiveWindow()
    {
        try
        {
            // 获取当前活动窗口
            var foregroundWindow = Native.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return string.Empty;
            
            // 尝试将窗口带到前台，这可能会帮助获取焦点
            Native.SetForegroundWindow(foregroundWindow);
            
            // 等待一小段时间让窗口获得焦点
            Thread.Sleep(50);
            
            // 再次尝试模拟 Ctrl+C
            KeyboardHelper.SimulateKeyPress(ModifierKeys.Control, Key.C);
            
            // 等待剪贴板操作完成
            Thread.Sleep(100);
            
            // 再次尝试获取剪贴板内容
            var retryText = Clipboard.GetText();
            if (!string.IsNullOrEmpty(retryText))
            {
                return retryText;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in fallback method: {ex.Message}");
        }
        
        return string.Empty;
    }

    public void Dispose()
    {
        developmentPluginService.ReloadRequested -= OnDevelopmentPluginsReloadRequested;
        themeService.ThemeChanged -= OnThemeChanged;
        hotKeyManager?.UnregisterAllHotKeys();
        nativeMessageWindowHost.Dispose();
    }
}
