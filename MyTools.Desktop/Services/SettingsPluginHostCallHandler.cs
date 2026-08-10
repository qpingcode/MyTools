using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Serilog.Events;

namespace MyTools.Desktop.Services;

/// <summary>
/// 为 settings 节点插件提供宿主能力：读取和保存应用配置。
/// 通过 hostCall 协议被 Node 后端调用。
/// </summary>
public sealed class SettingsPluginHostCallHandler
{
    private readonly IConfigurationRegistry registry;
    private readonly ThemeService themeService;
    private readonly LanguageService languageService;
    private readonly LogLevelService logLevelService;
    private readonly AutoStartService autoStartService;
    private readonly KeymapService keymapService;
    private readonly KeymapOverrideProvider keymapOverrideProvider;
    private readonly PluginLoader pluginLoader;
    private readonly HotKeyManager hotKeyManager;
    private readonly ILogger<SettingsPluginHostCallHandler> logger;

    private static readonly JsonSerializerOptions JsonCamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SettingsPluginHostCallHandler(
        IConfigurationRegistry registry,
        ThemeService themeService,
        LanguageService languageService,
        LogLevelService logLevelService,
        AutoStartService autoStartService,
        KeymapService keymapService,
        KeymapOverrideProvider keymapOverrideProvider,
        PluginLoader pluginLoader,
        HotKeyManager hotKeyManager,
        ILogger<SettingsPluginHostCallHandler> logger)
    {
        this.registry = registry;
        this.themeService = themeService;
        this.languageService = languageService;
        this.logLevelService = logLevelService;
        this.autoStartService = autoStartService;
        this.keymapService = keymapService;
        this.keymapOverrideProvider = keymapOverrideProvider;
        this.pluginLoader = pluginLoader;
        this.hotKeyManager = hotKeyManager;
        this.logger = logger;
    }

    public Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        JsonElement result;
        try
        {
            result = request.Method switch
            {
                "getConfiguration" => GetConfiguration(),
                "saveConfiguration" => SaveConfiguration(request.Params),
                "getKeymap" => GetKeymap(),
                "saveKeymap" => SaveKeymap(request.Params),
                "validateKeymap" => ValidateKeymap(request.Params),
                "suspendHotkeys" => SuspendHotkeys(),
                "resumeHotkeys" => ResumeHotkeys(),
                "restart" => Restart(),
                _ => throw new NotSupportedException($"Unknown hostCall method: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SettingsPluginHostCallHandler failed for method {Method}.", request.Method);
            throw;
        }

        return Task.FromResult(result);
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
        });

        // 语言需要重启：只有值真正变化（而非仅被前端回写相同的值）时才提示。
        var requiresRestart = false;
        var currentLanguage = languageSetting?.GetValue<string>();
        if (!string.Equals(previousLanguage, currentLanguage, StringComparison.OrdinalIgnoreCase)
            && currentLanguage != null)
        {
            requiresRestart = languageService.SetLanguageForNextStartup(currentLanguage);
        }

        return JsonSerializer.SerializeToElement(
            new SaveConfigurationResult { RequiresRestart = requiresRestart }, JsonCamelCaseOptions);
    }

    private JsonElement GetKeymap()
    {
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var overrides = keymapOverrideProvider.GetAll();

        var plugins = nodePlugins.Select(p =>
        {
            var pluginId = p.PluginId;
            var ov = overrides.TryGetValue(pluginId, out var o) ? o : null;
            var defaultHotKey = p.HotKey ?? "";
            var defaultKeywords = p.Keywords.ToList();
            return new KeymapPluginDto
            {
                PluginId = pluginId,
                Name = p.Name,
                DefaultHotKey = defaultHotKey,
                CurrentHotKey = ov?.HotKey ?? defaultHotKey,
                DefaultKeywords = defaultKeywords,
                CurrentKeywords = ov?.Keywords ?? defaultKeywords,
                IsEnabled = p.IsEnabled,
                IsNodePlugin = true
            };
        }).ToList();

        var dto = new KeymapDto { Plugins = plugins };
        return JsonSerializer.SerializeToElement(dto, JsonCamelCaseOptions);
    }

    private JsonElement SaveKeymap(JsonElement payload)
    {
        var request = payload.Deserialize<KeymapSaveRequest>(JsonCamelCaseOptions);
        var newOverrides = new Dictionary<string, KeymapOverride>();

        if (request?.Overrides != null)
        {
            foreach (var (pluginId, item) in request.Overrides)
            {
                newOverrides[pluginId] = new KeymapOverride
                {
                    HotKey = item.HotKey,
                    Keywords = item.Keywords,
                    IsEnabled = item.IsEnabled
                };
            }
        }

        keymapOverrideProvider.Save(newOverrides);

        // 热应用：必须在 UI 线程执行（热键注册涉及 Win32 消息）
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();

            // 应用启用状态覆盖
            keymapService.ApplyEnabledOverrides(nodePlugins);

            // 重注册热键和关键词
            keymapService.ReRegisterAllHotKeys(nodePlugins, OpenPluginDetail);
            keymapService.ReRegisterKeywords(pluginLoader.LoadedPlugins);
        });

        return JsonSerializer.SerializeToElement(new { success = true }, JsonCamelCaseOptions);
    }

    private JsonElement ValidateKeymap(JsonElement payload)
    {
        var request = payload.Deserialize<KeymapValidateRequest>(JsonCamelCaseOptions);
        var nodePlugins = pluginLoader.LoadedPlugins.OfType<NodePlugin>().ToList();
        var pluginNames = nodePlugins.ToDictionary(p => p.PluginId, p => p.Name);

        // 构建当前值基准
        var currentHotKeys = nodePlugins.ToDictionary(
            p => p.PluginId,
            p => (string?)(keymapOverrideProvider.GetHotKey(p.PluginId) ?? p.HotKey));
        var currentKeywords = nodePlugins.ToDictionary(
            p => p.PluginId,
            p => (List<string>?)(keymapOverrideProvider.GetKeywords(p.PluginId) ?? p.Keywords.ToList()));

        var conflicts = new List<KeymapConflictDto>();

        if (request?.HotKeys != null)
        {
            conflicts.AddRange(keymapService.ValidateHotKeys(request.HotKeys, pluginNames, currentHotKeys)
                .Select(c => new KeymapConflictDto
                {
                    PluginId = c.PluginId,
                    Field = c.Field,
                    Value = c.Value,
                    ConflictsWith = c.ConflictsWithName
                }));
        }

        if (request?.Keywords != null)
        {
            conflicts.AddRange(keymapService.ValidateKeywords(request.Keywords, pluginNames, currentKeywords)
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

    private JsonElement Restart()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (System.Windows.Application.Current is App app)
            {
                app.Restart();
            }
        });

        return JsonSerializer.SerializeToElement(new { }, JsonCamelCaseOptions);
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
    public string DefaultHotKey { get; init; } = "";
    public string CurrentHotKey { get; init; } = "";
    public List<string> DefaultKeywords { get; init; } = new();
    public List<string> CurrentKeywords { get; init; } = new();
    public bool IsEnabled { get; init; }
    public bool IsNodePlugin { get; init; }
}

public sealed class KeymapSaveRequest
{
    public Dictionary<string, KeymapOverrideItem> Overrides { get; init; } = new();
}

public sealed class KeymapOverrideItem
{
    public string? HotKey { get; init; }
    public List<string>? Keywords { get; init; }
    public bool? IsEnabled { get; init; }
}

public sealed class KeymapValidateRequest
{
    public Dictionary<string, string?>? HotKeys { get; init; }
    public Dictionary<string, List<string>?>? Keywords { get; init; }
}

public sealed class KeymapConflictDto
{
    public string PluginId { get; init; } = "";
    public string Field { get; init; } = "";
    public string Value { get; init; } = "";
    public string ConflictsWith { get; init; } = "";
}
