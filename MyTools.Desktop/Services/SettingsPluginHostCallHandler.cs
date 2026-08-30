using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Plugins;
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
    private const string IlSpyPathSettingKey = "dllinterfacereader.ILSpyPathSetting";
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
            Key = category.Key,
            Name = category.Name,
            Description = category.Description,
            Icon = category.Icon,
            IsSelectable = category.IsSelectable,
            Settings = category.Settings.Select(MapSetting).ToList()
        };
    }

    private static SettingDto MapSetting(ConfigurationSetting setting)
    {
        return new SettingDto
        {
            Key = setting.Key,
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
                Type = property.Type.ToWireString(),
                Title = property.Title,
                UiHint = property.UiHint,
                DefaultValue = property.DefaultValue,
                Hidden = property.Hidden,
                ShowInTable = property.ShowInTable,
                Visibility = property.Visibility
            }).ToList()
        };
    }

    private JsonElement GetOwnConfiguration(string pluginIdString)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var pluginId = new PluginId(pluginIdString);
        foreach (var category in registry.GetRootCategories())
        {
            CollectOwnSettings(category, pluginId, values);
        }

        return JsonSerializer.SerializeToElement(new { values }, JsonCamelCaseOptions);
    }

    private JsonElement SaveOwnConfiguration(string pluginIdString, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(pluginIdString))
        {
            throw new InvalidOperationException("configuration.writeOwn requires a plugin id.");
        }

        // The plugin configuration items come from the runtime schema, and neither the key nor value types are known to the host at compile time, so they cannot be converted into a fixed business class.
        var request = payload.Deserialize<OwnConfigurationSaveRequest>(JsonCamelCaseOptions);
        if (request?.Values == null)
        {
            throw new InvalidOperationException("configuration.writeOwn requires a values object.");
        }

        var pluginId = new PluginId(pluginIdString);
        ConfigurationSettingValues.ApplyOwnedValues(registry, pluginId, request.Values);
        registry.SaveChanges();
        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private static void CollectOwnSettings(
        ConfigurationCategory category,
        PluginId pluginId,
        Dictionary<string, JsonElement> values)
    {
        foreach (var setting in category.Settings)
        {
            if (!ConfigurationSettingValues.Owns(pluginId, setting)
                || setting.IsDisplayOnly)
            {
                continue;
            }

            values[setting.Name] = ConfigurationSettingValues.ToJsonElement(setting.CurrentValue ?? setting.DefaultValue);
        }
    }

    private JsonElement SaveConfiguration(JsonElement payload)
    {
        var request = payload.Deserialize<SaveConfigurationRequest>(JsonCamelCaseOptions) ?? new SaveConfigurationRequest();

        // Record the value of Language before saving, to determine if it actually changed (rather than merely being written back by the frontend with the same value).
        var languageSetting = registry.FindSetting(GeneralSettings.LanguagePath);
        var previousLanguage = languageSetting?.GetValue<string>();

        foreach (var change in request.Changes)
        {
            var setting = registry.FindSetting(change.Key);
            if (setting == null || setting.IsDisplayOnly)
            {
                logger.LogWarning("Setting not found: {Key}", change.Key);
                continue;
            }

            ValidatePathSettingIfNeeded(setting, change.Value);

            if (setting.Key is "clipboard.MaxHistoryDays" or "clipboard.MaxHistoryCount"
                && (!int.TryParse(change.Value, out var positiveValue) || positiveValue <= 0))
            {
                throw new InvalidOperationException($"{setting.Title} must be greater than zero.");
            }

            setting.CurrentValue = ConfigurationSettingValues.Convert(setting, change.Value);
        }

        registry.SaveChanges();
        pluginLoader.LoadedPlugins.OfType<ClipBoardPlugin>().FirstOrDefault()?.ApplyRetentionSettings();

        // Hot App Theme / LogLevel / AutoStart: These operations trigger events such as ThemeChanged.
        // Event subscribers (e.g., App.OnThemeChanged → UpdateNotifyIconMenu) access WPF controls and must execute on the UI thread.
        // The hostCall callback runs on the Node stdout reading thread, so a thread switch is required.
        var autoStartSetting = registry.FindSetting(GeneralSettings.AutoStart);
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

        // The language needs to be restarted: prompt only when the value actually changes (rather than merely being written back by the frontend with the same value).
        var requiresRestart = false;
        var currentLanguage = languageSetting?.GetValue<string>();
        if (!string.Equals(previousLanguage, currentLanguage, StringComparison.OrdinalIgnoreCase)
            && currentLanguage != null)
        {
            requiresRestart = languageService.SetLanguageForNextStartup(currentLanguage);
        }

        // Other settings marked with RequiresRestart also need to prompt for a restart
        if (!requiresRestart)
        {
            foreach (var change in request.Changes)
            {
                var setting = registry.FindSetting(change.Key);
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

        // Generate an Id for gestures that are missing one
        foreach (var g in gestures)
        {
            if (string.IsNullOrEmpty(g.Id))
            {
                g.Id = Guid.NewGuid().ToString("N");
            }
        }

        gestureConfigProvider.Save(gestures);

        // Hot apply: re-register on the gesture detection thread. Dictionary operations and
        // StartListening in GestureRegistry are thread-safe (the detection thread only reads the dictionary), so writes can be done on any thread.
        gestureRegistry.ReloadFromConfigs(gestures, mouseHelper);

        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private JsonElement GetKeymap()
    {
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var overrides = pluginOverrideProvider.GetAll();
        var duplicateIds = GetDuplicatePluginIds(nodePlugins);

        var plugins = nodePlugins.Select(p =>
        {
            var pluginId = p.PluginId;
            var overrideKey = p.OverrideKey;
            var ov = overrides.TryGetValue(overrideKey, out var installationOverride)
                ? installationOverride
                : !duplicateIds.Contains(pluginId) && overrides.TryGetValue(pluginId.Value, out var legacyOverride)
                    ? legacyOverride
                    : null;
            var defaultKeywords = p.Keywords.ToList();
            return new KeymapPluginDto
            {
                PluginId = pluginId.Value,
                OverrideKey = overrideKey,
                Location = p.PluginDirectory,
                Name = p.GetDisplayName(),
                DefaultKeywords = defaultKeywords,
                CurrentKeywords = ov?.Keywords ?? defaultKeywords,
                IsEnabled = p.IsEnabled,
                DefaultIncludeInGlobalResults = p.DefaultIncludeInGlobalResults,
                IncludeInGlobalResults = ov?.IncludeInGlobalResults ?? p.DefaultIncludeInGlobalResults,
                IsNodePlugin = true,
                IsDevelopment = nodePluginCatalog.IsDevelopmentPlugin(pluginId.Value)
            };
        }).ToList();

        var dto = new KeymapDto { Plugins = plugins };
        return JsonSerializer.SerializeToElement(dto, JsonCamelCaseOptions);
    }

    private JsonElement SaveKeymap(JsonElement payload)
    {
        var request = payload.Deserialize<KeymapSaveRequest>(JsonCamelCaseOptions);
        var requestedOverrideKeys = request?.Overrides.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var merged = pluginOverrideProvider.GetAll()
            .ToDictionary(kv => kv.Key, kv => CloneOverride(kv.Value), StringComparer.OrdinalIgnoreCase);

        if (request?.Overrides != null)
        {
            foreach (var (overrideKey, item) in request.Overrides)
            {
                var current = merged.GetValueOrDefault(overrideKey) ?? new PluginOverride();
                current.Keywords = item.Keywords;
                current.IsEnabled = item.IsEnabled;
                current.IncludeInGlobalResults = item.IncludeInGlobalResults;
                merged[overrideKey] = current;
            }

            EnforceSingleEnabledInstallation(request.Overrides, merged);
        }

        pluginOverrideProvider.Save(merged);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
            var previouslyEnabled = nodePlugins.ToDictionary(
                plugin => plugin.OverrideKey,
                plugin => plugin.IsEnabled,
                StringComparer.OrdinalIgnoreCase);

            pluginKeymapService.ApplyOverrides(nodePlugins);

            var enabledChanged = nodePlugins
                .Where(plugin => previouslyEnabled.GetValueOrDefault(plugin.OverrideKey) != plugin.IsEnabled)
                .ToList();
            if (enabledChanged.Count > 0)
            {
                pluginHotKeyService.ReRegisterPlugins(enabledChanged, OpenPluginDetail);
            }

            var keywordAffectedKeys = requestedOverrideKeys
                .Concat(enabledChanged.Select(plugin => plugin.OverrideKey))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            pluginKeymapService.ReRegisterKeywords(
                nodePlugins.Where(plugin => keywordAffectedKeys.Contains(plugin.OverrideKey)));
            searcher.InvalidateHomePageCache();
        });

        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private JsonElement ValidateKeymap(JsonElement payload)
    {
        var request = payload.Deserialize<KeymapValidateRequest>(JsonCamelCaseOptions);
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var pluginNames = nodePlugins.ToDictionary(
            p => p.OverrideKey,
            GetInstallationDisplayName,
            StringComparer.OrdinalIgnoreCase);

        var currentKeywords = nodePlugins.ToDictionary(
            p => p.OverrideKey,
            p => (List<string>?)(pluginOverrideProvider.GetKeywords(p.OverrideKey, p.PluginId) ?? p.Keywords.ToList()),
            StringComparer.OrdinalIgnoreCase);

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
                    PluginId = p.PluginId.Value,
                    OverrideKey = p.OverrideKey,
                    DefaultHotKey = defaultHotKey,
                    CurrentHotKey = pluginOverrideProvider.GetHotKey(p.OverrideKey, p.PluginId) ?? defaultHotKey
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
                string.Equals(nodePlugin.PluginId.Value, callerPluginId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new PluginListItemDto
            {
                PluginId = plugin.PluginId.Value,
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
            return pluginOverrideProvider.GetKeywords(nodePlugin.OverrideKey, nodePlugin.PluginId) ?? nodePlugin.Keywords.ToList();
        }

        return aliasesByPlugin.GetValueOrDefault(plugin) ?? [];
    }

    private string GetHotKey(IPlugin plugin)
    {
        if (plugin is not NodePlugin nodePlugin)
        {
            return "";
        }

        return pluginOverrideProvider.GetHotKey(nodePlugin.OverrideKey, nodePlugin.PluginId) ?? nodePlugin.HotKey ?? "";
    }

    private JsonElement SaveHotKeys(JsonElement payload)
    {
        var request = payload.Deserialize<HotKeysSaveRequest>(JsonCamelCaseOptions);
        var requestedHotKeyOverrideKeys = request?.HotKeys?.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var merged = pluginOverrideProvider.GetAll()
            .ToDictionary(kv => kv.Key, kv => CloneOverride(kv.Value), StringComparer.OrdinalIgnoreCase);

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
            pluginHotKeyService.ReRegisterPlugins(
                nodePlugins.Where(plugin => requestedHotKeyOverrideKeys.Contains(plugin.OverrideKey)),
                OpenPluginDetail);
        });

        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private JsonElement ValidateHotKeys(JsonElement payload)
    {
        var request = payload.Deserialize<HotKeysValidateRequest>(JsonCamelCaseOptions);
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var pluginNames = nodePlugins.ToDictionary(
            p => p.OverrideKey,
            GetInstallationDisplayName,
            StringComparer.OrdinalIgnoreCase);
        var currentHotKeys = nodePlugins.ToDictionary(
            p => p.OverrideKey,
            p => (string?)(pluginOverrideProvider.GetHotKey(p.OverrideKey, p.PluginId) ?? p.HotKey),
            StringComparer.OrdinalIgnoreCase);
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
                    plugin.OverrideKey,
                    pluginOverrideProvider.GetHotKey(plugin.OverrideKey, plugin.PluginId) ?? plugin.HotKey))
            {
                continue;
            }

            pluginNames.Add(plugin.OverrideKey, GetInstallationDisplayName(plugin));
        }

        AddClipboardHotKeyForInspection(pluginHotKeys, pluginNames,
            "clipboard.HotKey",
            "Plugin.ClipBoard.Settings.HotKey.Title",
            "Shortcut",
            ClipBoardPlugin.DefaultHotKey);
        AddClipboardHotKeyForInspection(pluginHotKeys, pluginNames,
            "clipboard.SequentialPasteHotKey",
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
        var text = registry.FindSetting("clipboard.HotKey")?.GetValue<string>()?.Trim()
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

            hotKeyManager.RegisterClipboardHotKey(parsed, () => pluginLauncher.Open("clipboard"));
        }

        var sequentialText = registry.FindSetting("clipboard.SequentialPasteHotKey")?.GetValue<string>()?.Trim()
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

    private void EnforceSingleEnabledInstallation(
        IReadOnlyDictionary<string, KeymapOverrideItem> requestedOverrides,
        Dictionary<string, PluginOverride> merged)
    {
        var duplicateGroups = pluginLoader.LoadedPlugins
            .OfType<NodePlugin>()
            .GroupBy(plugin => plugin.PluginId)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            var selected = group
                .Where(plugin => requestedOverrides.TryGetValue(plugin.OverrideKey, out var item)
                                 && item.IsEnabled == true)
                .OrderBy(plugin => plugin.OverrideKey, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (selected == null)
            {
                continue;
            }

            foreach (var plugin in group)
            {
                var current = merged.GetValueOrDefault(plugin.OverrideKey) ?? new PluginOverride();
                current.IsEnabled = ReferenceEquals(plugin, selected);
                merged[plugin.OverrideKey] = current;
            }
        }
    }

    private static HashSet<PluginId> GetDuplicatePluginIds(IEnumerable<NodePlugin> plugins) =>
        plugins.GroupBy(plugin => plugin.PluginId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

    private static string GetInstallationDisplayName(NodePlugin plugin) =>
        $"{plugin.GetDisplayName()} ({Path.GetFileName(plugin.PluginDirectory)})";

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

        return string.Equals(setting.Key, IlSpyPathSettingKey, StringComparison.OrdinalIgnoreCase)
            ? PluginConfigurationTypes.PathFile
            : null;
    }

}

// ── Keymap DTO ──

public sealed class OwnConfigurationSaveRequest
{
    public Dictionary<string, JsonElement>? Values { get; init; }
}

public sealed class KeymapDto
{
    public List<KeymapPluginDto> Plugins { get; init; } = new();
}

public sealed class KeymapPluginDto
{
    public string PluginId { get; init; } = "";
    public string OverrideKey { get; init; } = "";
    public string Location { get; init; } = "";
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
    public string OverrideKey { get; init; } = "";
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
