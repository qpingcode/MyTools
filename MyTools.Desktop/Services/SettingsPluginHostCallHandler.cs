using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Models;
using MyTools.Desktop.Utils;
using MyTools.Desktop.Views;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Serilog.Events;

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
    private readonly HotKeyManager hotKeyManager;
    private readonly Searcher searcher;
    private readonly AppConfigService appConfigService;
    private readonly InputActionCaptureService inputActionCaptureService;
    private readonly ILogger<SettingsPluginHostCallHandler> logger;
    private static readonly HashSet<string> FileOrDirectoryPathSettingPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "openpath.RiderInstallPath",
        "openpath.VsCodeInstallPath",
        "openpath.VisualStudioInstallPath",
        "openpath.IntelliJInstallPath"
    };
    private const string IlSpyPathSettingFullPath = "DllInterfaceReader.ILSpyPathSetting";

    private static readonly JsonSerializerOptions JsonCamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        "configuration.read", "configuration.write",
        "keymap.read", "keymap.write", "keymap.validate",
        "gestures.read", "gestures.write", "gestures.suspend", "gestures.resume",
        "hotkeys.read", "hotkeys.write", "hotkeys.suspend", "hotkeys.resume", "hotkeys.validate",
        "action.capture",
        "commandRunner.read", "commandRunner.write"
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
        HotKeyManager hotKeyManager,
        Searcher searcher,
        AppConfigService appConfigService,
        InputActionCaptureService inputActionCaptureService,
        ILogger<SettingsPluginHostCallHandler> logger)
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
        this.hotKeyManager = hotKeyManager;
        this.searcher = searcher;
        this.appConfigService = appConfigService;
        this.inputActionCaptureService = inputActionCaptureService;
        this.logger = logger;
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
                "configuration.write" => SaveConfiguration(request.Params),
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
                "commandRunner.read" => GetCommandRunner(),
                "commandRunner.write" => SaveCommandRunner(request.Params),
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
                .Select(c => new OptionDto { Value = c.Name, Label = c.NativeName })
                .ToList(),
            SupportedThemes =
            [
                new() { Value = "light", Label = languageService.GetCaption("Theme.light", "Light") },
                new() { Value = "dark", Label = languageService.GetCaption("Theme.dark", "Dark") }
            ],
            SupportedLogLevels = Enum.GetNames<LogEventLevel>().Select(name => new OptionDto
            {
                Value = name,
                Label = languageService.GetCaption($"LogLevel.{name}", name)
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
            IsSelectable = category.IsSelectable,
            Children = category.Children.Select(MapCategory).ToList(),
            Settings = category.Settings.Select(MapSetting).ToList()
        };
    }

    private static SettingDto MapSetting(ConfigurationSetting setting)
    {
        var value = setting.CurrentValue;
        var valueString = value switch
        {
            null => null,
            bool b => b ? "True" : "False",
            _ => value.ToString()
        };

        return new SettingDto
        {
            FullPath = setting.FullPath,
            Title = setting.Title,
            Description = setting.Description,
            ValueType = setting.ValueType.ToString(),
            CurrentValue = valueString,
            DefaultValue = setting.DefaultValue?.ToString(),
            RequiresRestart = (setting.Options & SettingOptions.RequiresRestart) != 0
        };
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
            if (setting == null)
            {
                logger.LogWarning("Setting not found: {FullPath}", change.FullPath);
                continue;
            }

            ValidatePathSettingIfNeeded(change.FullPath, change.Value);

            setting.CurrentValue = ConvertValue(setting, change.Value);
        }

        registry.SaveChanges();

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
                IsNodePlugin = true
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
        currentHotKeys["__search__"] = registry.FindSetting(AppConfigService.SearchHotKeySettingPath)?.CurrentValue as string
            ?? appConfigService.AppConfig.SearchHotKeyText;
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
        var context = plugin.CreateHotKeyDetailContext();
        if (context == null)
        {
            Utils.WindowHelper.ShowSearchWindow(plugin);
            return;
        }

        var pwm = System.Windows.Application.Current.Dispatcher.Invoke(() =>
            MyTools.Common.DependencyInjection.ServiceLocator.GetRequiredService<PluginWindowManager>());
        pwm.ShowOrFocus(plugin, context);
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
        var pluginHotKeys = nodePlugins.ToDictionary(
            p => p.PluginId,
            p => (string?)(pluginOverrideProvider.GetHotKey(p.PluginId) ?? p.HotKey));
        var pluginNames = nodePlugins.ToDictionary(p => p.PluginId, p => p.GetDisplayName());

        var searchHotKey = request.CurrentSearchHotKey
            ?? registry.FindSetting(AppConfigService.SearchHotKeySettingPath)?.CurrentValue as string
            ?? appConfigService.AppConfig.SearchHotKeyText;

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

    private static readonly JsonSerializerOptions CommandRunnerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static string CommandRunnerConfigPath => Path.Combine(ConfigPath.Base, "CommandRunner.json");

    private JsonElement GetCommandRunner()
    {
        var commands = ReadCommandRunnerConfigs();
        return JsonSerializer.SerializeToElement(new CommandRunnerDto { Commands = commands }, JsonCamelCaseOptions);
    }

    private JsonElement SaveCommandRunner(JsonElement payload)
    {
        var request = payload.Deserialize<CommandRunnerSaveRequest>(JsonCamelCaseOptions);
        var commands = request?.Commands ?? [];
        Directory.CreateDirectory(ConfigPath.Base);
        File.WriteAllText(CommandRunnerConfigPath, JsonSerializer.Serialize(commands, CommandRunnerJsonOptions));
        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private List<CommandConfig> ReadCommandRunnerConfigs()
    {
        var path = CommandRunnerConfigPath;
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CommandConfig>>(json, CommandRunnerJsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read CommandRunner.json");
            return [];
        }
    }

    private void ApplySearchHotKeyFromSettings()
    {
        var text = registry.FindSetting(AppConfigService.SearchHotKeySettingPath)?.GetValue<string>()?.Trim() ?? string.Empty;
        var current = appConfigService.AppConfig.SearchHotKeyText?.Trim() ?? string.Empty;
        if (string.Equals(text, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            appConfigService.SetSearchHotKey(string.Empty);
            hotKeyManager.RegisterySearchHotKey(null);
            return;
        }

        var parsed = new HotKeyConfig(text);
        if (parsed.Key == System.Windows.Input.Key.None || parsed.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            logger.LogWarning("Ignoring invalid search hotkey {HotKey}.", text);
            return;
        }

        appConfigService.SetSearchHotKey(text);
        hotKeyManager.RegisterySearchHotKey(parsed);
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

    private static object? ConvertValue(ConfigurationSetting setting, string? stringValue)
    {
        if (stringValue == null)
        {
            return null;
        }

        return setting.ValueType switch
        {
            SettingValueTypes.Bool => string.Equals(stringValue, "True", StringComparison.OrdinalIgnoreCase),
            SettingValueTypes.Integer => int.TryParse(stringValue, out var i) ? i : stringValue,
            SettingValueTypes.Double => double.TryParse(stringValue, out var d) ? d : stringValue,
            _ => stringValue
        };
    }

    private void ValidatePathSettingIfNeeded(string fullPath, string? value)
    {
        if (!FileOrDirectoryPathSettingPaths.Contains(fullPath)
            && !string.Equals(fullPath, IlSpyPathSettingFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var kind = string.Equals(fullPath, IlSpyPathSettingFullPath, StringComparison.OrdinalIgnoreCase)
            ? "file"
            : "fileOrDirectory";
        var result = PathPluginHostCallHandler.ValidatePathByKind(value, kind);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Message ?? "Invalid path.");
        }
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

public sealed class CommandRunnerDto
{
    public List<CommandConfig> Commands { get; init; } = new();
}

public sealed class CommandRunnerSaveRequest
{
    public List<CommandConfig> Commands { get; init; } = new();
}
