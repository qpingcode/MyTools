using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Utils;
using MyTools.Desktop.Models;
using MyTools.Desktop.Utils;
using MyTools.Desktop.Views;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Manifest;

namespace MyTools.Desktop.Services;

/// <summary>
/// 为 settings 节点插件提供宿主能力：读取和保存应用配置。
/// 通过 hostCall 协议被 Node 后端调用。
/// </summary>
public sealed class SettingsPluginHostCallHandler : IPluginHostCapabilityHandler
{
    private readonly IConfigurationRegistry registry;
    private readonly ThemeService themeService;
    private readonly LanguageService languageService;
    private readonly LogLevelService logLevelService;
    private readonly AutoStartService autoStartService;
    private readonly PluginHotKeyService pluginHotKeyService;
    private readonly PluginKeymapService pluginKeymapService;
    private readonly PluginOverrideProvider pluginOverrideProvider;
    private readonly GestureConfigProvider gestureConfigProvider;
    private readonly GestureRegistry gestureRegistry;
    private readonly MouseHelper mouseHelper;
    private readonly PluginLoader pluginLoader;
    private readonly IKeywordRegistry keywordRegistry;
    private readonly IPluginLauncher pluginLauncher;
    private readonly HotKeyManager hotKeyManager;
    private readonly Searcher searcher;
    private readonly InputActionCaptureService inputActionCaptureService;
    private readonly NodePluginCatalog nodePluginCatalog;
    private readonly IKeyboardHelper keyboardHelper;
    private readonly ILogger<SettingsPluginHostCallHandler> logger;
    private const string IlSpyPathSettingFullPath = "DllInterfaceReader.ILSpyPathSetting";

    private static readonly JsonSerializerOptions JsonCamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        "configuration.read", "configuration.write", "configuration.readOwn", "configuration.writeOwn",
        "keymap.read", "keymap.write", "keymap.validate",
        "gestures.read", "gestures.write", "gestures.suspend", "gestures.resume",
        "hotkeys.read", "hotkeys.write", "hotkeys.suspend", "hotkeys.resume", "hotkeys.validate",
        "action.capture", "plugins.list"
    ];

    public SettingsPluginHostCallHandler(
        IConfigurationRegistry registry,
        ThemeService themeService,
        LanguageService languageService,
        LogLevelService logLevelService,
        AutoStartService autoStartService,
        PluginHotKeyService pluginHotKeyService,
        PluginKeymapService pluginKeymapService,
        PluginOverrideProvider pluginOverrideProvider,
        GestureConfigProvider gestureConfigProvider,
        GestureRegistry gestureRegistry,
        MouseHelper mouseHelper,
        PluginLoader pluginLoader,
        IKeywordRegistry keywordRegistry,
        IPluginLauncher pluginLauncher,
        HotKeyManager hotKeyManager,
        Searcher searcher,
        InputActionCaptureService inputActionCaptureService,
        ILogger<SettingsPluginHostCallHandler> logger,
        NodePluginCatalog nodePluginCatalog,
        IKeyboardHelper keyboardHelper)
    {
        this.registry = registry;
        this.themeService = themeService;
        this.languageService = languageService;
        this.logLevelService = logLevelService;
        this.autoStartService = autoStartService;
        this.pluginHotKeyService = pluginHotKeyService;
        this.pluginKeymapService = pluginKeymapService;
        this.pluginOverrideProvider = pluginOverrideProvider;
        this.gestureConfigProvider = gestureConfigProvider;
        this.gestureRegistry = gestureRegistry;
        this.mouseHelper = mouseHelper;
        this.pluginLoader = pluginLoader;
        this.keywordRegistry = keywordRegistry;
        this.pluginLauncher = pluginLauncher;
        this.hotKeyManager = hotKeyManager;
        this.searcher = searcher;
        this.inputActionCaptureService = inputActionCaptureService;
        this.logger = logger;
        this.nodePluginCatalog = nodePluginCatalog;
        this.keyboardHelper = keyboardHelper;
    }

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Method == "action.capture")
            {
                return await CaptureInputActionAsync(request);
            }

            return request.Method switch
            {
                "configuration.read" => GetConfiguration(),
                "configuration.readOwn" => GetOwnConfiguration(request.PluginId),
                "configuration.write" => SaveConfiguration(request.Params),
                "configuration.writeOwn" => SaveOwnConfiguration(request.PluginId, request.Params),
                "keymap.read" => GetKeymap(),
                "keymap.write" => SaveKeymap(request.Params),
                "keymap.validate" => ValidateKeymap(request.Params),
                "hotkeys.read" => GetHotKeys(),
                "hotkeys.write" => SaveHotKeys(request.Params),
                "gestures.read" => GetGestures(),
                "gestures.write" => SaveGestures(request.Params),
                "gestures.suspend" => SuspendGestures(),
                "gestures.resume" => ResumeGestures(),
                "hotkeys.suspend" => SuspendHotkeys(),
                "hotkeys.resume" => ResumeHotkeys(),
                "hotkeys.validate" => ValidateHotKeys(request.Params),
                "plugins.list" => GetPluginList(request.PluginId),
                _ => throw new NotSupportedException($"Unknown hostCall method: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SettingsPluginHostCallHandler failed for method {Method}.", request.Method);
            throw;
        }
    }

    private JsonElement GetConfiguration()
    {
        var rootCategories = registry.GetRootCategories();
        var categories = rootCategories.Select(MapCategory).ToList();

        var dto = new ConfigurationDto
        {
            Categories = categories,
            SupportedLocales = languageService.SupportedCultures
                .Select(c => new OptionDto { Value = c.Name, Label = LanguageService.GetNativeDisplayName(c) })
                .ToList(),
            SupportedThemes =
            [
                new() { Value = "light", Label = languageService.GetCaption("Theme.light", "Light") },
                new() { Value = "dark", Label = languageService.GetCaption("Theme.dark", "Dark") }
            ],
            SupportedUpdateChannels =
            [
                new() { Value = "stable", Label = "stable" },
                new() { Value = "beta", Label = "beta" }
            ],
            SupportedLogLevels = LogLevelService.SelectableLevels.Select(level =>
            {
                var name = level.ToString();
                return new OptionDto
                {
                    Value = name,
                    Label = languageService.GetCaption($"LogLevel.{name}", name)
                };
            }).ToList()
        };

        return JsonSerializer.SerializeToElement(dto, JsonCamelCaseOptions);
    }

    private static CategoryDto MapCategory(ConfigurationCategory category)
    {
        return new CategoryDto
        {
            Key = category.FullPath,
            Name = category.Name,
            Description = category.Description,
            Icon = category.Icon,
            IsSelectable = category.IsSelectable,
            Children = category.Children.Select(MapCategory).ToList(),
            Settings = category.Settings.Select(MapSetting).ToList()
        };
    }

    private static SettingDto MapSetting(ConfigurationSetting setting)
    {
        return new SettingDto
        {
            FullPath = setting.FullPath,
            Title = setting.Title,
            Description = setting.Description,
            ValueType = setting.ValueType switch
            {
                SettingValueTypes.H1 => PluginConfigurationTypes.H1,
                SettingValueTypes.H2 => PluginConfigurationTypes.H2,
                _ => setting.ValueType.ToString()
            },
            CurrentValue = ConfigurationSettingValues.ToDtoString(setting.CurrentValue),
            DefaultValue = ConfigurationSettingValues.ToDtoString(setting.DefaultValue),
            RequiresRestart = (setting.Options & SettingOptions.RequiresRestart) != 0,
            UiHint = setting.UiHint,
            Visibility = setting.Visibility,
            Schema = MapSchema(setting.Schema)
        };
    }

    private static SettingSchemaDto? MapSchema(SettingSchema? schema)
    {
        if (schema == null || schema.Properties.Count == 0)
        {
            return null;
        }

        return new SettingSchemaDto
        {
            Properties = schema.Properties.Select(property => new SettingSchemaPropertyDto
            {
                Key = property.Key,
                Type = property.Type,
                Title = property.Title,
                UiHint = property.UiHint,
                DefaultValue = property.DefaultValue,
                Hidden = property.Hidden,
                Table = property.Table,
                Visibility = property.Visibility
            }).ToList()
        };
    }

    private JsonElement GetOwnConfiguration(string pluginId)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in registry.GetRootCategories())
        {
            CollectOwnSettings(category, pluginId, values);
        }

        return JsonSerializer.SerializeToElement(new { values }, JsonCamelCaseOptions);
    }

    private JsonElement SaveOwnConfiguration(string pluginId, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new InvalidOperationException("configuration.writeOwn requires a plugin id.");
        }

        if (!payload.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("configuration.writeOwn requires a values object.");
        }

        ConfigurationSettingValues.ApplyOwnedValues(registry, pluginId, values);
        registry.SaveChanges();
        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private static void CollectOwnSettings(
        ConfigurationCategory category,
        string pluginId,
        Dictionary<string, JsonElement> values)
    {
        foreach (var setting in category.Settings)
        {
            if (!ConfigurationSettingValues.Owns(pluginId, setting.FullPath)
                || setting.IsDisplayOnly)
            {
                continue;
            }

            values[setting.Name] = ConfigurationSettingValues.ToJsonElement(setting.CurrentValue ?? setting.DefaultValue);
        }

        foreach (var child in category.Children)
        {
            CollectOwnSettings(child, pluginId, values);
        }
    }

    private JsonElement SaveConfiguration(JsonElement payload)
    {
        var request = payload.Deserialize<SaveConfigurationRequest>(JsonCamelCaseOptions) ?? new SaveConfigurationRequest();

        // 记录 Language 保存前的值，用于判断是否真的变化（而非仅被前端回写）。
        var languageSetting = registry.FindSetting("General.Language");
        var previousLanguage = languageSetting?.GetValue<string>();

        foreach (var change in request.Changes)
        {
            var setting = registry.FindSetting(change.FullPath);
            if (setting == null || setting.IsDisplayOnly)
            {
                logger.LogWarning("Setting not found: {FullPath}", change.FullPath);
                continue;
            }

            ValidatePathSettingIfNeeded(setting, change.Value);

            if (setting.FullPath is "ClipBoard.MaxHistoryDays" or "ClipBoard.MaxHistoryCount"
                && (!int.TryParse(change.Value, out var positiveValue) || positiveValue <= 0))
            {
                throw new InvalidOperationException($"{setting.Title} must be greater than zero.");
            }

            setting.CurrentValue = ConfigurationSettingValues.Convert(setting, change.Value);
        }

        registry.SaveChanges();
        pluginLoader.LoadedPlugins.OfType<ClipBoardPlugin>().FirstOrDefault()?.ApplyRetentionSettings();

        // 热应用 Theme / LogLevel / AutoStart：这些操作会触发 ThemeChanged 等事件，
        // 事件订阅者（如 App.OnThemeChanged → UpdateNotifyIconMenu）访问 WPF 控件，
        // 必须在 UI 线程执行。hostCall 回调在 Node stdout 读取线程上，需要切线程。
        var autoStartSetting = registry.FindSetting("General.AutoStart");
        var autoStartValue = autoStartSetting?.CurrentValue is bool b ? b : (bool?)null;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var themeBefore = themeService.CurrentTheme;
            themeService.ApplyFromSettings(registry);
            var themeAfter = themeService.CurrentTheme;
            logger.LogInformation("SaveConfiguration: theme {Before} -> {After}", themeBefore, themeAfter);
            logLevelService.ApplyFromSettings(registry);

            if (autoStartValue.HasValue)
            {
                autoStartService.AutoStart = autoStartValue.Value;
            }

            ApplySearchHotKeyFromSettings();
            ApplyClipboardHotKeyFromSettings();
        });

        // 语言需要重启：只有值真正变化（而非仅被前端回写相同的值）时才提示。
        var requiresRestart = false;
        var currentLanguage = languageSetting?.GetValue<string>();
        if (!string.Equals(previousLanguage, currentLanguage, StringComparison.OrdinalIgnoreCase)
            && currentLanguage != null)
        {
            requiresRestart = languageService.SetLanguageForNextStartup(currentLanguage);
        }

        // 其他标记了 RequiresRestart 的 setting 变化也需要提示重启
        if (!requiresRestart)
        {
            foreach (var change in request.Changes)
            {
                var setting = registry.FindSetting(change.FullPath);
                if (setting != null && (setting.Options & SettingOptions.RequiresRestart) != 0)
                {
                    requiresRestart = true;
                    break;
                }
            }
        }

        return JsonSerializer.SerializeToElement(
            new SaveConfigurationResult { RequiresRestart = requiresRestart }, JsonCamelCaseOptions);
    }

    private JsonElement GetGestures()
    {
        var gestures = gestureConfigProvider.GetAll();
        return JsonSerializer.SerializeToElement(new GesturesDto { Gestures = gestures }, JsonCamelCaseOptions);
    }

    private JsonElement SaveGestures(JsonElement payload)
    {
        var request = payload.Deserialize<GesturesSaveRequest>(JsonCamelCaseOptions);
        var gestures = request?.Gestures ?? new List<GestureConfig>();

        // 为缺少 Id 的手势生成一个
        foreach (var g in gestures)
        {
            if (string.IsNullOrEmpty(g.Id))
            {
                g.Id = Guid.NewGuid().ToString("N");
            }
        }

        gestureConfigProvider.Save(gestures);

        // 热应用：在手势检测线程上重新注册。GestureRegistry 的字典操作和
        // StartListening 是线程安全的（检测线程只读字典），可以在任意线程写入。
        gestureRegistry.ReloadFromConfigs(gestures, mouseHelper);

        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private JsonElement GetKeymap()
    {
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var overrides = pluginOverrideProvider.GetAll();

        var plugins = nodePlugins.Select(p =>
        {
            var pluginId = p.PluginId;
            var ov = overrides.TryGetValue(pluginId, out var o) ? o : null;
            var defaultKeywords = p.Keywords.ToList();
            return new KeymapPluginDto
            {
                PluginId = pluginId,
                Name = p.GetDisplayName(),
                DefaultKeywords = defaultKeywords,
                CurrentKeywords = ov?.Keywords ?? defaultKeywords,
                IsEnabled = p.IsEnabled,
                DefaultIncludeInGlobalResults = p.DefaultIncludeInGlobalResults,
                IncludeInGlobalResults = ov?.IncludeInGlobalResults ?? p.DefaultIncludeInGlobalResults,
                IsNodePlugin = true,
                IsDevelopment = nodePluginCatalog.IsDevelopmentPlugin(pluginId)
            };
        }).ToList();

        var dto = new KeymapDto { Plugins = plugins };
        return JsonSerializer.SerializeToElement(dto, JsonCamelCaseOptions);
    }

    private JsonElement SaveKeymap(JsonElement payload)
    {
        var request = payload.Deserialize<KeymapSaveRequest>(JsonCamelCaseOptions);
        var merged = pluginOverrideProvider.GetAll()
            .ToDictionary(kv => kv.Key, kv => CloneOverride(kv.Value));

        if (request?.Overrides != null)
        {
            foreach (var (pluginId, item) in request.Overrides)
            {
                var current = merged.GetValueOrDefault(pluginId) ?? new PluginOverride();
                current.Keywords = item.Keywords;
                current.IsEnabled = item.IsEnabled;
                current.IncludeInGlobalResults = item.IncludeInGlobalResults;
                merged[pluginId] = current;
            }
        }

        pluginOverrideProvider.Save(merged);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();

            pluginKeymapService.ApplyOverrides(nodePlugins);
            pluginHotKeyService.ReRegisterAll(nodePlugins, OpenPluginDetail);
            pluginKeymapService.ReRegisterKeywords(pluginLoader.LoadedPlugins);
            searcher.InvalidateHomePageCache();
        });

        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private JsonElement ValidateKeymap(JsonElement payload)
    {
        var request = payload.Deserialize<KeymapValidateRequest>(JsonCamelCaseOptions);
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var pluginNames = nodePlugins.ToDictionary(p => p.PluginId, p => p.Name);

        var currentKeywords = nodePlugins.ToDictionary(
            p => p.PluginId,
            p => (List<string>?)(pluginOverrideProvider.GetKeywords(p.PluginId) ?? p.Keywords.ToList()));

        var conflicts = new List<KeymapConflictDto>();

        if (request?.Keywords != null)
        {
            conflicts.AddRange(pluginKeymapService.ValidateKeywords(request.Keywords, pluginNames, currentKeywords)
                .Select(c => new KeymapConflictDto
                {
                    PluginId = c.PluginId,
                    Field = c.Field,
                    Value = c.Value,
                    ConflictsWith = c.ConflictsWithName
                }));
        }

        return JsonSerializer.SerializeToElement(new { conflicts }, JsonCamelCaseOptions);
    }

    private JsonElement GetHotKeys()
    {
        var plugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>()
            .Select(p =>
            {
                var defaultHotKey = p.HotKey ?? "";
                return new HotKeyPluginDto
                {
                    PluginId = p.PluginId,
                    DefaultHotKey = defaultHotKey,
                    CurrentHotKey = pluginOverrideProvider.GetHotKey(p.PluginId) ?? defaultHotKey
                };
            })
            .ToList();
        return JsonSerializer.SerializeToElement(new HotKeysDto { Plugins = plugins }, JsonCamelCaseOptions);
    }

    private JsonElement GetPluginList(string callerPluginId)
    {
        var aliasesByPlugin = new Dictionary<IPlugin, List<string>>();
        foreach (var (keyword, mapped) in keywordRegistry.Match(string.Empty))
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            if (!aliasesByPlugin.TryGetValue(mapped, out var aliases))
            {
                aliases = [];
                aliasesByPlugin[mapped] = aliases;
            }

            if (!aliases.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                aliases.Add(keyword);
            }
        }

        var items = new List<PluginListItemDto>();
        foreach (var plugin in pluginLoader.LoadedPlugins)
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            if (plugin is NodePlugin nodePlugin &&
                !string.IsNullOrWhiteSpace(callerPluginId) &&
                string.Equals(nodePlugin.ParentId, callerPluginId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new PluginListItemDto
            {
                PluginId = plugin.PluginId,
                Name = plugin is NodePlugin node ? node.GetDisplayName() : plugin.Name,
                Aliases = GetAliases(plugin, aliasesByPlugin),
                HotKey = GetHotKey(plugin)
            });
        }

        return JsonSerializer.SerializeToElement(new PluginListDto { Plugins = items }, JsonCamelCaseOptions);
    }

    private List<string> GetAliases(IPlugin plugin, Dictionary<IPlugin, List<string>> aliasesByPlugin)
    {
        if (plugin is NodePlugin nodePlugin)
        {
            return pluginOverrideProvider.GetKeywords(nodePlugin.PluginId) ?? nodePlugin.Keywords.ToList();
        }

        return aliasesByPlugin.GetValueOrDefault(plugin) ?? [];
    }

    private string GetHotKey(IPlugin plugin)
    {
        if (plugin is not NodePlugin nodePlugin)
        {
            return "";
        }

        return pluginOverrideProvider.GetHotKey(nodePlugin.PluginId) ?? nodePlugin.HotKey ?? "";
    }

    private JsonElement SaveHotKeys(JsonElement payload)
    {
        var request = payload.Deserialize<HotKeysSaveRequest>(JsonCamelCaseOptions);
        var merged = pluginOverrideProvider.GetAll()
            .ToDictionary(kv => kv.Key, kv => CloneOverride(kv.Value));

        if (request?.HotKeys != null)
        {
            foreach (var (pluginId, hotKey) in request.HotKeys)
            {
                var current = merged.GetValueOrDefault(pluginId) ?? new PluginOverride();
                current.HotKey = hotKey;
                merged[pluginId] = current;
            }
        }

        pluginOverrideProvider.Save(merged);
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
            pluginHotKeyService.ReRegisterAll(nodePlugins, OpenPluginDetail);
        });

        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private JsonElement ValidateHotKeys(JsonElement payload)
    {
        var request = payload.Deserialize<HotKeysValidateRequest>(JsonCamelCaseOptions);
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var pluginNames = nodePlugins.ToDictionary(p => p.PluginId, p => p.Name);
        var currentHotKeys = nodePlugins.ToDictionary(
            p => p.PluginId,
            p => (string?)(pluginOverrideProvider.GetHotKey(p.PluginId) ?? p.HotKey));
        currentHotKeys["__search__"] = registry.FindSetting(GeneralSettings.SearchHotKeyPath)?.CurrentValue as string
            ?? GeneralSettings.DefaultSearchHotKey;
        pluginNames["__search__"] = languageService.GetCaption(
            "Configuration.General.SearchHotKey.Title", "Search hotkey");

        var conflicts = request?.HotKeys == null
            ? []
            : pluginHotKeyService.Validate(request.HotKeys, pluginNames, currentHotKeys)
                .Select(c => new KeymapConflictDto
                {
                    PluginId = c.PluginId,
                    Field = c.Field,
                    Value = c.Value,
                    ConflictsWith = c.ConflictsWithName
                })
                .ToList();
        return JsonSerializer.SerializeToElement(new { conflicts }, JsonCamelCaseOptions);
    }

    private void OpenPluginDetail(NodePlugin plugin)
    {
        pluginLauncher.Open(plugin);
    }

    private JsonElement SuspendGestures()
    {
        gestureRegistry.SuspendDetection();
        return JsonSerializer.SerializeToElement(new { }, JsonCamelCaseOptions);
    }

    private JsonElement ResumeGestures()
    {
        gestureRegistry.ResumeDetection();
        return JsonSerializer.SerializeToElement(new { }, JsonCamelCaseOptions);
    }

    private JsonElement SuspendHotkeys()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => hotKeyManager.SuspendAllHotKeys());
        return JsonSerializer.SerializeToElement(new { }, JsonCamelCaseOptions);
    }

    private JsonElement ResumeHotkeys()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => hotKeyManager.ResumeAllHotKeys());
        return JsonSerializer.SerializeToElement(new { }, JsonCamelCaseOptions);
    }

    private async Task<JsonElement> CaptureInputActionAsync(HostCallRequest hostCall)
    {
        var request = hostCall.Params.Deserialize<CaptureInputActionRequest>(JsonCamelCaseOptions)
            ?? new CaptureInputActionRequest();
        var inspectRequest = new CheckHotKeyRequest
        {
            ExcludePluginId = request.ExcludePluginId,
            ExcludeSearchHotKey = request.ExcludeSearchHotKey,
            ExcludeReservedHotKey = request.ExcludeReservedHotKey,
            CurrentSearchHotKey = request.CurrentSearchHotKey
        };
        var options = new InputActionCaptureOptions
        {
            ShowKeyboard = request.ShowKeyboard,
            ShowMouse = request.ShowMouse,
            Kind = request.Kind ?? "hotkey",
            HotKey = request.HotKey,
            MouseButton = request.MouseButton,
            ShowReset = request.ShowReset,
            DefaultHotKey = request.DefaultHotKey,
            DefaultMouseButton = request.DefaultMouseButton,
            InspectHotKey = hotKey => InspectHotKey(hotKey, inspectRequest)
        };

        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF dispatcher is not available.");
        var result = dispatcher.CheckAccess()
            ? await inputActionCaptureService.CaptureAsync(options)
            : await dispatcher.Invoke(() => inputActionCaptureService.CaptureAsync(options));

        return JsonSerializer.SerializeToElement(
            new
            {
                cancelled = result == null,
                kind = result?.Kind,
                hotKey = result?.HotKey,
                mouseButton = result?.MouseButton
            },
            JsonCamelCaseOptions);
    }

    private HotKeyInspection InspectHotKey(string? hotKey, CheckHotKeyRequest request)
    {
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var pluginHotKeys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var pluginNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in nodePlugins)
        {
            // A malformed or transient development-plugin catalog must not crash the capture window.
            // Keep the first loaded plugin, matching the order used elsewhere by the plugin loader.
            if (!pluginHotKeys.TryAdd(
                    plugin.PluginId,
                    pluginOverrideProvider.GetHotKey(plugin.PluginId) ?? plugin.HotKey))
            {
                continue;
            }

            pluginNames.Add(plugin.PluginId, plugin.GetDisplayName());
        }

        AddClipboardHotKeyForInspection(pluginHotKeys, pluginNames,
            "ClipBoard.HotKey",
            "Plugin.ClipBoard.Settings.HotKey.Title",
            "Shortcut",
            ClipBoardPlugin.DefaultHotKey);
        AddClipboardHotKeyForInspection(pluginHotKeys, pluginNames,
            "ClipBoard.SequentialPasteHotKey",
            "Plugin.ClipBoard.Settings.SequentialPasteHotKey.Title",
            "Sequential paste shortcut",
            ClipBoardPlugin.DefaultSequentialPasteHotKey);

        var searchHotKey = request.CurrentSearchHotKey
            ?? registry.FindSetting(GeneralSettings.SearchHotKeyPath)?.CurrentValue as string
            ?? GeneralSettings.DefaultSearchHotKey;

        return HotKeyInspector.Inspect(hotKey, new HotKeyInspectionRequest
        {
            SearchHotKey = searchHotKey,
            SearchHotKeyDisplayName = languageService.GetCaption(
                "Configuration.General.SearchHotKey.Title", "Search hotkey"),
            ExcludeSearchHotKey = request.ExcludeSearchHotKey,
            ExcludeReservedHotKey = request.ExcludeReservedHotKey,
            ExcludePluginId = request.ExcludePluginId,
            PluginHotKeys = pluginHotKeys,
            PluginNames = pluginNames
        });
    }

    private void AddClipboardHotKeyForInspection(
        IDictionary<string, string?> hotKeys,
        IDictionary<string, string> names,
        string settingPath,
        string captionKey,
        string defaultCaption,
        string defaultHotKey)
    {
        hotKeys[settingPath] = registry.FindSetting(settingPath)?.GetValue<string>() ?? defaultHotKey;
        names[settingPath] = languageService.GetCaption(captionKey, defaultCaption);
    }

    private void ApplySearchHotKeyFromSettings()
    {
        var text = registry.FindSetting(GeneralSettings.SearchHotKeyPath)?.GetValue<string>()?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            hotKeyManager.RegisterySearchHotKey(null);
            return;
        }

        var parsed = new HotKeyConfig(text);
        if (parsed.Key == System.Windows.Input.Key.None || parsed.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            logger.LogWarning("Ignoring invalid search hotkey {HotKey}.", text);
            return;
        }

        hotKeyManager.RegisterySearchHotKey(parsed);
    }

    private void ApplyClipboardHotKeyFromSettings()
    {
        var text = registry.FindSetting("ClipBoard.HotKey")?.GetValue<string>()?.Trim()
                   ?? ClipBoardPlugin.DefaultHotKey;
        if (string.IsNullOrWhiteSpace(text))
        {
            hotKeyManager.RegisterClipboardHotKey(null, () => { });
        }
        else
        {
            var parsed = new HotKeyConfig(text);
            if (parsed.Key == System.Windows.Input.Key.None || parsed.Modifiers == System.Windows.Input.ModifierKeys.None)
            {
                logger.LogWarning("Ignoring invalid clipboard hotkey {HotKey}.", text);
                return;
            }

            hotKeyManager.RegisterClipboardHotKey(parsed, () => pluginLauncher.Open("ClipBoard"));
        }

        var sequentialText = registry.FindSetting("ClipBoard.SequentialPasteHotKey")?.GetValue<string>()?.Trim()
                             ?? ClipBoardPlugin.DefaultSequentialPasteHotKey;
        if (string.IsNullOrWhiteSpace(sequentialText))
        {
            hotKeyManager.RegisterClipboardSequentialPasteHotKey(null, () => { });
            return;
        }
        var sequentialParsed = new HotKeyConfig(sequentialText);
        if (sequentialParsed.Key == System.Windows.Input.Key.None
            || sequentialParsed.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            logger.LogWarning("Ignoring invalid clipboard sequential paste hotkey {HotKey}.", sequentialText);
            return;
        }

        var clipboardPlugin = pluginLoader.LoadedPlugins.OfType<ClipBoardPlugin>().FirstOrDefault();
        if (clipboardPlugin != null)
        {
            hotKeyManager.RegisterClipboardSequentialPasteHotKey(sequentialParsed,
                () => _ = clipboardPlugin.PasteLatestAndRemoveAsync(keyboardHelper));
        }
    }

    private static PluginOverride CloneOverride(PluginOverride source)
    {
        return new PluginOverride
        {
            HotKey = source.HotKey,
            Keywords = source.Keywords is null ? null : [.. source.Keywords],
            IsEnabled = source.IsEnabled,
            IncludeInGlobalResults = source.IncludeInGlobalResults
        };
    }

    private void ValidatePathSettingIfNeeded(ConfigurationSetting setting, string? value)
    {
        var kind = PathKindOf(setting);
        if (kind == null)
        {
            return;
        }

        var result = PathPluginHostCallHandler.ValidatePathByKind(value, kind);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Message ?? "Invalid path.");
        }
    }

    private static string? PathKindOf(ConfigurationSetting setting)
    {
        if (setting.ValueType == SettingValueTypes.Path)
        {
            return PluginConfigurationTypes.NormalizePathKind(setting.UiHint);
        }

        return string.Equals(setting.FullPath, IlSpyPathSettingFullPath, StringComparison.OrdinalIgnoreCase)
            ? PluginConfigurationTypes.PathFile
            : null;
    }

}

// ── Keymap DTO ──

public sealed class KeymapDto
{
    public List<KeymapPluginDto> Plugins { get; init; } = new();
}

public sealed class KeymapPluginDto
{
    public string PluginId { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> DefaultKeywords { get; init; } = new();
    public List<string> CurrentKeywords { get; init; } = new();
    public bool IsEnabled { get; init; }
    public bool DefaultIncludeInGlobalResults { get; init; }
    public bool IncludeInGlobalResults { get; init; }
    public bool IsNodePlugin { get; init; }
    public bool IsDevelopment { get; init; }
}

public sealed class KeymapSaveRequest
{
    public Dictionary<string, KeymapOverrideItem> Overrides { get; init; } = new();
}

public sealed class KeymapOverrideItem
{
    public List<string>? Keywords { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IncludeInGlobalResults { get; init; }
}

public sealed class KeymapValidateRequest
{
    public Dictionary<string, List<string>?>? Keywords { get; init; }
}

public sealed class HotKeysDto
{
    public List<HotKeyPluginDto> Plugins { get; init; } = new();
}

public sealed class HotKeyPluginDto
{
    public string PluginId { get; init; } = "";
    public string DefaultHotKey { get; init; } = "";
    public string CurrentHotKey { get; init; } = "";
}

public sealed class PluginListDto
{
    public List<PluginListItemDto> Plugins { get; init; } = [];
}

public sealed class PluginListItemDto
{
    public string PluginId { get; init; } = "";
    public string Name { get; init; } = "";
    public List<string> Aliases { get; init; } = [];
    public string HotKey { get; init; } = "";
}

public sealed class HotKeysSaveRequest
{
    public Dictionary<string, string?>? HotKeys { get; init; }
}

public sealed class HotKeysValidateRequest
{
    public Dictionary<string, string?>? HotKeys { get; init; }
}

public sealed class KeymapConflictDto
{
    public string PluginId { get; init; } = "";
    public string Field { get; init; } = "";
    public string Value { get; init; } = "";
    public string ConflictsWith { get; init; } = "";
}

public sealed class CheckHotKeyRequest
{
    public string? HotKey { get; init; }
    public string? ExcludePluginId { get; init; }
    public bool ExcludeSearchHotKey { get; init; }
    public bool ExcludeReservedHotKey { get; init; }
    public string? CurrentSearchHotKey { get; init; }
}

public sealed class CaptureInputActionRequest
{
    public bool ShowKeyboard { get; init; } = true;
    public bool ShowMouse { get; init; }
    public string? Kind { get; init; }
    public string? HotKey { get; init; }
    public string? MouseButton { get; init; }
    public bool ShowReset { get; init; }
    public string? DefaultHotKey { get; init; }
    public string? DefaultMouseButton { get; init; }
    public string? ExcludePluginId { get; init; }
    public bool ExcludeSearchHotKey { get; init; }
    public bool ExcludeReservedHotKey { get; init; }
    public string? CurrentSearchHotKey { get; init; }
}

// ── Gestures DTO ──

public sealed class GesturesDto
{
    public List<GestureConfig> Gestures { get; init; } = new();
}

public sealed class GesturesSaveRequest
{
    public List<GestureConfig> Gestures { get; init; } = new();
}
