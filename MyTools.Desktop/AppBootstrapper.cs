using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.DependencyInjection;
using MyTools.Desktop.Models;
using MyTools.Desktop.Services;
using MyTools.Desktop.Services.WindowNativeHandler;
using MyTools.Desktop.Utils;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Clipboard = System.Windows.Clipboard;


namespace MyTools.Desktop;

public class AppBootstrapper : IDisposable
{
    private GestureRegistry? gestureRegistry;
    private HotKeyManager? hotKeyManager;
    private ServiceProvider? serviceProvider;

    public void Init()
    {
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        
        serviceProvider = serviceCollection.BuildServiceProvider();
        ServiceLocator.ServiceProvider = serviceProvider;

        var appConfigService = serviceProvider.GetRequiredService<AppConfigService>();
        var appConfig = appConfigService.AppConfig;
        
        // Ensure that MessageOnlyWindow has been loaded and Windows messages have been properly monitored
        // which is a prerequisite for the clipboard / hotkey
        EnsureMessageOnlyWindow();
        
        InitializeConfigurationData();
        
        RegisterGlobalHotKey(appConfig.SearchHotKey);
        EnableGestureDetection(appConfig.EnableGesture);
        LoadPlugins();
    }
    
    private void EnsureMessageOnlyWindow()
    {
        var messageOnlyWindow = ServiceLocator.GetRequiredService<MessageOnlyWindow>();
        messageOnlyWindow.Show();
    }

    private void EnableGestureDetection(bool enableGesture)
    {
        if (!enableGesture)
        {
            return;
        }
        
        gestureRegistry = ServiceLocator.GetRequiredService<GestureRegistry>();
        var mouseHelper = ServiceLocator.GetRequiredService<MouseHelper>();

        MoveDirection[] downRight = [MoveDirection.Down, MoveDirection.Right];
        gestureRegistry.RegisterGesture(downRight, _ => KeyboardHelper.SimulateKeyPress(ModifierKeys.Control, Key.W), "Close Tab");
        gestureRegistry.RegisterGesture(downRight, ["rider", "rider64", "devenv"], _ => KeyboardHelper.SimulateKeyPress(ModifierKeys.Control, Key.F4), "Close Tab");

        MoveDirection[] UpRight = [MoveDirection.Up, MoveDirection.Right];
        gestureRegistry.RegisterGesture(UpRight, _ => KeyboardHelper.SimulateKeyPress(ModifierKeys.Control, Key.N), "Create New");
        gestureRegistry.RegisterGesture(UpRight, ["chrome", "firefox", "edge"], _ => KeyboardHelper.SimulateKeyPress(ModifierKeys.Control, Key.T), "Create New Tab");

        gestureRegistry.RegisterGesture(MoveDirection.Left, args => mouseHelper.XButton1Click(args.LastPoint), "Back");
        gestureRegistry.RegisterGesture(MoveDirection.Right, args => mouseHelper.XButton2Click(args.LastPoint), "Forward");

        MoveDirection[] UpDown = [MoveDirection.Up, MoveDirection.Down];
        gestureRegistry.RegisterGesture(UpDown, ["chrome", "firefox", "edge"], _ => KeyboardHelper.SimulateKeyPress(Key.F5), "Refresh Page");
        
        MoveDirection[] downRightUpLeft = [MoveDirection.Down, MoveDirection.Right, MoveDirection.Up, MoveDirection.Left];
        gestureRegistry.RegisterGesture(downRightUpLeft, args => KeyboardHelper.SimulateKeyPress(ModifierKeys.Alt | ModifierKeys.Shift, Key.O), "Close Other Tabes");
        
        MoveDirection[] LeftRight = [MoveDirection.Left, MoveDirection.Right];
        MoveDirection[] RightLeft = [MoveDirection.Right, MoveDirection.Left];
        gestureRegistry.RegisterGesture(LeftRight, ["rider", "rider64", "devenv"], rgs => KeyboardHelper.SimulateKeyPress(ModifierKeys.Control | ModifierKeys.Shift, Key.F12), "Full Screen Switch");
        gestureRegistry.RegisterGesture(RightLeft, ["rider", "rider64", "devenv"], args => KeyboardHelper.SimulateKeyPress(ModifierKeys.Control | ModifierKeys.Shift, Key.F12), "Full Screen Switch");
        
        var gestureThread = new Thread(() =>
        {
            gestureRegistry.StartListening();
        })
        { Name = "Gesture Thread", IsBackground = true };
        
        gestureThread.SetApartmentState(ApartmentState.STA);
        gestureThread.Start();
    }

    private void ConfigureServices(ServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<MouseGestureDetector>();
        
        // register plugins
        serviceCollection.AddPluginServices();
        
        // register windows & services
        serviceCollection.AddDesktopServices();
        
        // register configuration system
        serviceCollection.AddConfigurationSystem();
        
        serviceCollection.AddLog();
        
        serviceCollection.AddMemoryCache();
    }
    
    private void RegisterGlobalHotKey(HotKeyConfig SearchHotKey)
    {
        hotKeyManager = ServiceLocator.GetRequiredService<HotKeyManager>();
        hotKeyManager.RegisterySearchHotKey(SearchHotKey);

        hotKeyManager.RegisterHotKey(Key.V, ModifierKeys.Control | ModifierKeys.Shift, () =>
        {
            var clipboardPlugin = ServiceLocator.GetServices<IPlugin>().OfType<ClipBoardPlugin>().First();
            WindowHelper.ShowSearchWindow(clipboardPlugin);
        });
    }
    
    private void LoadPlugins()
    {
        CopyExampleWhenConfigNotExists();

        var nodePluginCatalog = ServiceLocator.GetRequiredService<NodePluginCatalog>();
        nodePluginCatalog.Reload();
        
        var pluginLoader = ServiceLocator.GetRequiredService<PluginLoader>();
        var loadedPlugins = pluginLoader.InitPlugins();
        RegisterNodePluginHotKeys(loadedPlugins.OfType<NodePlugin>());
    }

    private void RegisterNodePluginHotKeys(IEnumerable<NodePlugin> nodePlugins)
    {
        if (hotKeyManager == null)
        {
            hotKeyManager = ServiceLocator.GetRequiredService<HotKeyManager>();
        }

        foreach (var nodePlugin in nodePlugins)
        {
            if (string.IsNullOrWhiteSpace(nodePlugin.HotKey))
            {
                continue;
            }

            var hotKey = new HotKeyConfig(nodePlugin.HotKey);
            if (hotKey.Key == Key.None || hotKey.Modifiers == ModifierKeys.None)
            {
                continue;
            }

            try
            {
                hotKeyManager.RegisterHotKey(hotKey.Key, hotKey.Modifiers, () => OpenNodePluginDetail(nodePlugin));
            }
            catch (InvalidOperationException ex)
            {
                var logger = ServiceLocator.GetRequiredService<ILogger<AppBootstrapper>>();
                logger.LogError(ex, "Cannot register hotkey {HotKey} for node plugin {PluginName}.", nodePlugin.HotKey, nodePlugin.Name);
            }
        }
    }

    private static void OpenNodePluginDetail(NodePlugin nodePlugin)
    {
        if (WindowHelper.TryFocusNodePluginDetail(nodePlugin))
        {
            return;
        }

        var context = nodePlugin.CreateHotKeyDetailContext();
        if (context == null)
        {
            WindowHelper.ShowSearchWindow(nodePlugin);
            return;
        }

        WindowHelper.ShowSearchWindow(text: context.SearchText);
        ServiceLocator.GetRequiredService<NodePluginDetailNavigator>().OpenDetail(context);
        WindowHelper.FocusCurrentNodePluginDetailInput();
    }
    
    private void CopyExampleWhenConfigNotExists()
    {
        var configPath = ConfigPath.Base;
        var examplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples");
        var pluginTargetPath = Path.Combine(configPath, "plugins");
        
        if (Directory.Exists(examplePath))
        {
            foreach (var file in Directory.GetFiles(examplePath))
            {
                var fileName = Path.GetFileName(file);
                var configFile = Path.Combine(configPath, fileName);
                if (!File.Exists(configFile) && File.Exists(file))
                {
                    File.Copy(file, configFile);
                }
            }

            Directory.CreateDirectory(pluginTargetPath);
            foreach (var sourcePluginDirectory in Directory.GetDirectories(examplePath))
            {
                var targetPluginDirectory = Path.Combine(pluginTargetPath, Path.GetFileName(sourcePluginDirectory));
                SyncDirectory(GetExamplePluginSourceDirectory(sourcePluginDirectory), targetPluginDirectory);
            }
        }
    }

    private static string GetExamplePluginSourceDirectory(string sourcePluginDirectory)
    {
        var distDirectory = Path.Combine(sourcePluginDirectory, "dist");
        return File.Exists(Path.Combine(distDirectory, "plugin.json")) ? distDirectory : sourcePluginDirectory;
    }

    private static void SyncDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            if (!File.Exists(targetFile) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(targetFile))
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
            var registry = ServiceLocator.GetRequiredService<IConfigurationRegistry>();
            
            // 添加常规设置分类
            var generalCategory = registry.AddCategory("General", "General Settings");
            registry.AddSetting(generalCategory, "Language", "界面语言", "选择应用程序的显示语言",  "en-US");
            registry.AddSetting(generalCategory, "AutoStart", "开机自启", "是否在系统启动时自动运行", false);
            registry.AddSetting(generalCategory, "MaxHistory", "最大历史记录", "保存的最大历史记录数量", 100);
            registry.AddSetting(generalCategory, "SearchDelay", "搜索延迟", "搜索防抖延迟时间（毫秒）", 100.0);
            registry.AddSetting(generalCategory, "UpdateUrl", "更新地址", "Velopack 发布目录的 HTTPS 地址或本地目录；留空则禁用更新检查", string.Empty);
            registry.AddSetting(generalCategory, "UpdateChannel", "更新通道", "Velopack 更新通道，例如 win、stable 或 beta", "win");
            
            // Add Plugin Settings
            registry.AddCategory("Plugins", "Plugin Settings", IsSelectable: false);
            var plugins = ServiceLocator.GetServices<IPlugin>();
            foreach (var plugin in plugins)
            {
                plugin.RegisterSettings(registry);
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
        hotKeyManager?.UnregisterAllHotKeys();
        if (serviceProvider != null)
        {
            serviceProvider.Dispose();
        }
        else
        {
            gestureRegistry?.Dispose();
        }
    }
}