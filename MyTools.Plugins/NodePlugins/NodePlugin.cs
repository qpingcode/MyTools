using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Plugins;
using MyTools.Common.Theming;
using MyTools.Protocol.Errors;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePlugin : IPlugin, IDisposable
{
    private readonly NodePluginManifest manifest;
    private readonly INodePluginHost processHost;
    private readonly ILogger<NodePlugin> logger;
    private readonly ILocalizationService localizationService;
    private readonly IThemeService themeService;
    private PluginLocalizationService? pluginLocalization;
    private IReadOnlyList<string>? effectiveKeywords;
    private IReadOnlyList<NodePluginActionDefinitionDto> actionDefinitions = [];
    private readonly SemaphoreSlim initializeLock = new(1, 1);
    private string? initializedSessionId;
    private string? initializedLocale;
    private string? initializedTheme;

    /// <summary>
    /// 插件级别的翻译服务，查询此插件自己的 locale 文件。
    /// 首次访问时加载并缓存，之后直接返回缓存实例。
    /// </summary>
    public PluginLocalizationService PluginLocalization =>
        pluginLocalization ??= new PluginLocalizationService(
            NodePluginLocalization.LoadMessages(manifest, localizationService.CurrentLocale),
            localizationService.CurrentLocale);

    internal NodePlugin(
        NodePluginManifest manifest,
        INodePluginHost processHost,
        ILogger<NodePlugin> logger,
        ILocalizationService localizationService,
        IThemeService themeService)
    {
        this.manifest = manifest;
        this.processHost = processHost;
        this.logger = logger;
        this.localizationService = localizationService;
        this.themeService = themeService;
        IsGlobalSearchPlugin = manifest.SearchGlobal;
    }

    public event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived
    {
        add => processHost.EventReceived += value;
        remove => processHost.EventReceived -= value;
    }

    public event EventHandler? ActionsChanged;

    /// <summary>
    /// 为此插件注册宿主能力回调（hostCall）。注册后，插件的 Node 后端可以通过
    /// <c>tool.hostCall(method, params)</c> 向宿主发起请求。
    /// 仅需要宿主能力的插件（如 settings）才注册。
    /// </summary>
    public void RegisterHostCallHandler(Func<HostCallRequest, CancellationToken, Task<JsonElement>> handler)
    {
        processHost.HostCallHandler = handler;
    }

    /// <summary>
    /// 未翻译的显示名称（defaultValue），fallback 到 entry id。
    /// </summary>
    public string Name => manifest.Name;

    /// <summary>
    /// 获取当前 locale 下翻译后的名称。
    /// 通过 NameMessage.Resolve 查找插件级翻译，找不到时回退到 defaultValue。
    /// </summary>
    public string GetDisplayName()
    {
        if (manifest.NameMessage == null)
        {
            return Name;
        }

        return manifest.NameMessage.Resolve(PluginLocalization);
    }

    /// <summary>
    /// Optional plugin-level description. Settings copy comes from configuration label/description.
    /// </summary>
    public string Description =>
        manifest.DescriptionMessage == null
            ? ""
            : manifest.DescriptionMessage.Resolve(PluginLocalization);

    public string PluginId => manifest.Id;

    /// <summary>Bus session id after the Node process has been started.</summary>
    public string? BusSessionId => processHost.SessionId;

    /// <summary>Ensures the Node session is Ready (starts pipe/handshake if needed).</summary>
    public Task EnsureV3SessionAsync(CancellationToken cancellationToken = default)
    {
        var busHost = (NodePluginBusHost)processHost;
        return busHost.StartAsync(busHost.NodeExePath, cancellationToken);
    }

    /// <summary>Entry id within the plugin package (e.g. <c>main</c>).</summary>
    public string EntryId => manifest.EntryId;

    /// <summary>
    /// 插件所属的根 ID（不含 entry 后缀）。例如 PluginId 为 "settings:main"，
    /// ParentId 为 "settings"。用于匹配同一插件的所有 entry。
    /// </summary>
    public string ParentId => manifest.ParentId;

    public List<IActionWithHotkey> Actions { get; } = [];

    public bool IsEnabled { get; set; } = true;

    public ViewModelType ViewModelType => ViewModelType.Basic;

    /// <summary>plugin.json <c>search.global</c> default, before user override.</summary>
    public bool DefaultIncludeInGlobalResults => manifest.SearchGlobal;

    public bool IsGlobalSearchPlugin { get; set; }

    public IReadOnlyList<string> Keywords => manifest.Keywords;
    public IReadOnlyList<string> Capabilities => manifest.Capabilities;

    public string PrimaryKeyword => (effectiveKeywords ?? manifest.Keywords).FirstOrDefault() ?? string.Empty;

    public void SetEffectiveKeywords(IEnumerable<string> keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        effectiveKeywords = keywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .ToArray();
    }

    public string Id => manifest.Id;

    public string? HotKey => manifest.HotKey;

    public async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        try
        {
            await InitializeAsync(cancellationToken);
            var mode = searchOptions?.SearchFrom == SearchFrom.Plugin ? "plugin" : "global";
            var response = await processHost.SearchAsync(
                query,
                mode,
                localizationService.CurrentLocale,
                manifest.DefaultLocale,
                CurrentThemeWire,
                cancellationToken);
            var items = response.Items.Select((item, index) => MapResultItem(item, query, index)).ToList();
            return Result.CreateSuccessResult(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BusCallException ex) when (ex.Code == ErrorCode.Cancelled)
        {
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Node plugin search failed for {PluginName}.", Name);
            return Result.CreateFailure(ex.Message, ex);
        }
    }

    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (initializedSessionId != null
                && string.Equals(initializedSessionId, processHost.SessionId, StringComparison.Ordinal)
                && string.Equals(initializedLocale, localizationService.CurrentLocale, StringComparison.Ordinal)
                && string.Equals(initializedTheme, CurrentThemeWire, StringComparison.Ordinal))
            {
                return;
            }

            var response = await processHost.InitializeAsync(
                localizationService.CurrentLocale,
                manifest.DefaultLocale,
                GetCurrentMessages(),
                CurrentThemeWire,
                cancellationToken);
            actionDefinitions = response.Actions
                .Where(action => !string.IsNullOrWhiteSpace(action.Id))
                .DistinctBy(action => action.Id, StringComparer.Ordinal)
                .ToArray();
            pluginLocalization = null;
            initializedSessionId = processHost.SessionId;
            initializedLocale = localizationService.CurrentLocale;
            initializedTheme = CurrentThemeWire;
            ActionsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            initializeLock.Release();
        }
    }

    public void RegisterSettings(IConfigurationRegistry configurationRegistry)
    {
        PluginConfigurationRegistrar.Register(
            configurationRegistry,
            ParentId,
            GetDisplayName(),
            "",
            manifest.Configuration,
            PluginLocalization,
            manifest.Icon);
    }

    public string GetQueryWithoutKeyword(string searchText)
    {
        return TryParseKeywordSearchText(searchText, out var query, out _)
            ? query
            : searchText;
    }

    public bool CanHandleSearchText(string searchText)
    {
        return TryParseKeywordSearchText(searchText, out _, out _);
    }

    public bool ShouldOpenDetailOnKeywordRoute(string searchText)
    {
        if (!manifest.HasWebDetail)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(PrimaryKeyword))
        {
            return false;
        }

        return TryParseKeywordSearchText(searchText, out _, out _);
    }

    public NodePluginDetailContext? CreateKeywordDetailContext(string searchText, string query)
    {
        if (!manifest.HasWebDetail)
        {
            return null;
        }

        return CreateDetailContext(
            itemId: $"{manifest.Id}:keyword-route",
            searchText: searchText,
            query: query,
            detail: new NodePluginDetailViewDto
            {
                Page = manifest.DetailEntry ?? string.Empty,
                Title = Name,
                InitialState = CloneJson(BuildKeywordRouteState(query))
            });
    }

    public NodePluginDetailContext? CreateHotKeyDetailContext()
    {
        if (!manifest.HasWebDetail)
        {
            return null;
        }

        return CreateDetailContext(
            itemId: $"{manifest.Id}:hotkey-route",
            searchText: string.IsNullOrWhiteSpace(PrimaryKeyword) ? string.Empty : $"{PrimaryKeyword} ",
            query: string.Empty,
            detail: new NodePluginDetailViewDto
            {
                Page = manifest.DetailEntry ?? string.Empty,
                Title = Name,
                InitialState = CloneJson(BuildKeywordRouteState(string.Empty))
            });
    }

    internal async Task<NodePluginActionResponse> InvokeActionAsync(string itemId, string actionId, string query)
    {
        return await processHost.InvokeActionAsync(
            itemId, actionId, query, localizationService.CurrentLocale, manifest.DefaultLocale,
            CurrentThemeWire);
    }

    internal void LogActionStarted(string actionId, string actionName)
    {
        logger.LogInformation(
            "Invoking node plugin action: plugin={PluginId} action={ActionId} name={ActionName}",
            PluginId,
            actionId,
            actionName);
    }

    internal void LogActionCompleted(string actionId, string actionName, NodePluginActionResponse response)
    {
        logger.LogInformation(
            "Completed node plugin action: plugin={PluginId} action={ActionId} name={ActionName} host={HasHost} web={HasWeb} detail={HasDetail} close={Close}",
            PluginId,
            actionId,
            actionName,
            response.Host != null,
            response.Web != null,
            response.Detail != null,
            response.Close);
    }

    internal void LogActionFailed(string actionId, string actionName, Exception exception)
    {
        logger.LogError(
            exception,
            "Node plugin action failed: plugin={PluginId} action={ActionId} name={ActionName}",
            PluginId,
            actionId,
            actionName);
    }

    internal void LogActionFailed(string actionId, string actionName, string message)
    {
        logger.LogWarning(
            "Node plugin action failed: plugin={PluginId} action={ActionId} name={ActionName} message={Message}",
            PluginId,
            actionId,
            actionName,
            message);
    }

    internal NodePluginDetailContext? CreateDetailContext(string itemId, string searchText, string query, NodePluginDetailViewDto? detail)
    {
        var detailPath = ResolveDetailEntryFullPath(detail?.Page);
        if (detailPath == null)
        {
            return null;
        }

        return new NodePluginDetailContext
        {
            Plugin = this,
            PluginId = manifest.Id,
            Version = manifest.Version,
            ProtocolVersion = manifest.ProtocolVersion,
            PluginDirectory = manifest.PluginDirectory,
            EntryFullPath = detailPath,
            ItemId = itemId,
            SearchText = searchText,
            Query = query,
            Keyword = PrimaryKeyword,
            InitialState = CloneJson(detail?.InitialState),
            Locale = localizationService.CurrentLocale,
            FallbackLocale = manifest.DefaultLocale,
            Messages = GetCurrentMessages()
        };
    }

    /// <summary>
    /// Action outcome 只有显式包含 detail 时才导航。不能把缺失的 detail
    /// 当成 manifest 默认详情页，否则会错误抑制同一 outcome 的 close。
    /// </summary>
    internal NodePluginDetailContext? CreateActionDetailContext(
        string itemId, string searchText, string query, NodePluginDetailViewDto? detail)
    {
        return detail == null ? null : CreateDetailContext(itemId, searchText, query, detail);
    }

    public List<IActionWithHotkey> BuildActions(
        IReadOnlyList<string>? actionIds = null,
        Action<JsonElement>? forwardToWeb = null)
    {
        var definitions = actionDefinitions;
        if (definitions.Count == 0)
        {
            return [];
        }

        IEnumerable<NodePluginActionDefinitionDto> selected = definitions;
        if (actionIds != null)
        {
            var byId = definitions.ToDictionary(action => action.Id, StringComparer.Ordinal);
            selected = actionIds.Select(id =>
            {
                if (byId.TryGetValue(id, out var definition)) return definition;
                logger.LogWarning("Node plugin {PluginId} referenced unknown action {ActionId}.", PluginId, id);
                return null;
            }).OfType<NodePluginActionDefinitionDto>();
        }

        var selectedList = selected.ToList();
        var hotkeys = ResolveHotkeys(selectedList);
        return selectedList.Select((definition, index) =>
        {
            var title = ResolveText(definition.Title, definition.Id);
            var description = ResolveText(definition.Description, string.Empty);
            return (IActionWithHotkey)new NodePluginInvokeAction(
                    this, definition.Id, title, description, forwardToWeb)
                .WithHotkey(hotkeys[index]);
        }).ToList();
    }

    public IReadOnlyList<NodePluginWebActionDefinition> GetWebActionDefinitions()
    {
        var hotkeys = ResolveHotkeys(actionDefinitions);
        return actionDefinitions.Select((definition, index) => new NodePluginWebActionDefinition
        {
            Id = definition.Id,
            Name = ResolveText(definition.Title, definition.Id),
            Hotkey = hotkeys[index].IsAssigned ? hotkeys[index].ToString() : null
        }).ToArray();
    }

    private IReadOnlyList<Hotkey> ResolveHotkeys(IReadOnlyList<NodePluginActionDefinitionDto> definitions)
    {
        var result = Enumerable.Repeat(Hotkey.None, definitions.Count).ToArray();
        var used = new HashSet<Hotkey>();
        if (definitions.Count > 0 && definitions[0].Hotkey == null)
        {
            result[0] = Hotkey.Enter;
            used.Add(Hotkey.Enter);
        }
        for (var index = 0; index < definitions.Count; index++)
        {
            var dto = definitions[index].Hotkey;
            if (dto == null) continue;
            if (!Hotkey.TryParse(dto.Key, dto.Modifiers, out var hotkey) || IsReserved(hotkey) || !used.Add(hotkey))
            {
                logger.LogWarning(
                    "Ignoring invalid, reserved, or conflicting hotkey on node plugin {PluginId} action {ActionId}.",
                    PluginId, definitions[index].Id);
                continue;
            }
            result[index] = hotkey;
        }

        return result;
    }

    private static bool IsReserved(Hotkey hotkey) =>
        hotkey.Modifiers == HotkeyModifiers.Control
        && (hotkey.Key == HotkeyKey.K
            || hotkey.Key is >= HotkeyKey.D0 and <= HotkeyKey.D9
            || hotkey.Key is HotkeyKey.A or HotkeyKey.C or HotkeyKey.V or HotkeyKey.X);

    private string ResolveText(NodePluginLocalizedTextDto? text, string fallback)
    {
        if (text == null) return fallback;
        return new LocalizedMessage(text.Key, text.DefaultValue).Resolve(PluginLocalization);
    }

    public IReadOnlyDictionary<string, string> GetCurrentMessages() =>
        NodePluginLocalization.LoadMessages(manifest, localizationService.CurrentLocale);

    private string CurrentThemeWire => themeService.CurrentTheme.ToWireString();

    private ResultItem MapResultItem(NodePluginSearchItem item, string query, int index)
    {
        var title = string.IsNullOrWhiteSpace(item.Title) ? Name : item.Title;
        var resultItem = new ResultItem(
            MapIcon(item.Icon),
            title,
            item.Subtitle ?? string.Empty,
            new NodePluginActionArgs(item.Id, query),
            item.Priority)
        {
            ResultKey = string.IsNullOrWhiteSpace(item.Id) ? $"{manifest.Id}-{index}" : item.Id,
        };

        resultItem.AllowedActions = BuildActions(item.Actions);
        return resultItem;
    }

    private string? ResolveDetailEntryFullPath(string? relativePath)
    {
        var effectiveRelativePath = string.IsNullOrWhiteSpace(relativePath) ? manifest.DetailEntry : relativePath;
        if (string.IsNullOrWhiteSpace(effectiveRelativePath))
        {
            return manifest.DetailEntryFullPath;
        }

        var fullPath = Path.GetFullPath(Path.Combine(manifest.PluginDirectory, effectiveRelativePath));
        if (!fullPath.StartsWith(manifest.PluginDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return null;
        }

        return fullPath;
    }

    private static Icon MapIcon(NodePluginIconDto? icon)
    {
        if (icon == null || string.IsNullOrWhiteSpace(icon.Value))
        {
            return StringIcon.Empty;
        }

        if (string.Equals(icon.Kind, "emoji", StringComparison.OrdinalIgnoreCase))
        {
            return new StringIcon(icon.Value);
        }

        if (string.Equals(icon.Kind, "mdi", StringComparison.OrdinalIgnoreCase)
            || icon.Value.StartsWith("mdi-", StringComparison.OrdinalIgnoreCase))
        {
            return new MdiIcon(icon.Value);
        }

        return StringIcon.Empty;
    }

    private static JsonElement CloneJson(JsonElement? element)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Undefined)
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }

        return element.Value.Clone();
    }

    private static JsonElement BuildKeywordRouteState(string query)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            query,
            route = "keyword",
            lastEvent = "keyword-route"
        }));
        return document.RootElement.Clone();
    }

    private bool TryParseKeywordSearchText(string searchText, out string query, out bool hasSeparator)
    {
        query = searchText;
        hasSeparator = false;

        if (string.IsNullOrWhiteSpace(PrimaryKeyword) || string.IsNullOrEmpty(searchText))
        {
            return false;
        }

        if (!searchText.StartsWith(PrimaryKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (searchText.Length == PrimaryKeyword.Length)
        {
            query = string.Empty;
            return true;
        }

        var separator = searchText[PrimaryKeyword.Length];
        if (!char.IsWhiteSpace(separator))
        {
            return false;
        }

        hasSeparator = true;
        query = searchText[PrimaryKeyword.Length..].TrimStart();
        return true;
    }

    public void Dispose()
    {
        processHost.Dispose();
    }

    internal Task DisposeAsync() => processHost.DisposeAsync();

    /// <summary>Test-only accessor for the backend host.</summary>
    internal INodePluginHost GetHostForTest() => processHost;
}

internal sealed class NodePluginInvokeAction : IAction
{
    private readonly NodePlugin plugin;
    private readonly string actionId;
    private readonly string title;
    private readonly string description;
    private readonly Action<JsonElement>? forwardToWeb;

    public NodePluginInvokeAction(
        NodePlugin plugin,
        string actionId,
        string title,
        string description,
        Action<JsonElement>? forwardToWeb = null)
    {
        this.plugin = plugin;
        this.actionId = actionId;
        this.title = string.IsNullOrWhiteSpace(title) ? actionId : title;
        this.description = description;
        this.forwardToWeb = forwardToWeb;
    }

    public string Name => title;

    public string Description => string.IsNullOrWhiteSpace(description) ? $"Invoke {title}" : description;

    public async Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not NodePluginActionArgs actionArgs)
        {
            return ActionResult.CreateFailure("Invalid node plugin action arguments.");
        }

        try
        {
            plugin.LogActionStarted(actionId, title);
            var response = await plugin.InvokeActionAsync(actionArgs.ItemId, actionId, actionArgs.Query);
            if (response.Host != null)
            {
                var hostResult = await NodePluginWellKnownActions.ExecuteAsync(response.Host);
                if (!hostResult.Success)
                {
                    plugin.LogActionFailed(actionId, title, hostResult.Message);
                    return hostResult;
                }
            }

            if (response.Web != null)
            {
                if (forwardToWeb == null)
                {
                    return ActionResult.CreateFailure("The action returned web output without an active detail page.");
                }
                forwardToWeb(response.Web.Payload);
            }

            var detailContext = plugin.CreateActionDetailContext(
                actionArgs.ItemId, BuildSearchText(actionArgs.Query), actionArgs.Query, response.Detail);
            if (detailContext != null)
            {
                var navigator = ServiceLocator.GetRequiredService<INodePluginDetailNavigator>();
                navigator.OpenDetail(detailContext);
            }

            var actionType = response.Close && detailContext == null
                ? ActionTypeEnum.Close
                : response.Refresh && detailContext == null
                    ? ActionTypeEnum.Refresh
                    : ActionTypeEnum.None;
            var message = response.Message == null
                ? $"Executed {title}"
                : new LocalizedMessage(response.Message.Key, response.Message.DefaultValue)
                    .Resolve(plugin.PluginLocalization);
            plugin.LogActionCompleted(actionId, title, response);
            return ActionResult.CreateSuccess(message, actionType);
        }
        catch (Exception ex)
        {
            plugin.LogActionFailed(actionId, title, ex);
            return ActionResult.CreateFailure(ex.Message);
        }
    }

    private string BuildSearchText(string query)
    {
        return string.IsNullOrWhiteSpace(plugin.PrimaryKeyword)
            ? query
            : string.IsNullOrWhiteSpace(query)
                ? plugin.PrimaryKeyword
                : $"{plugin.PrimaryKeyword} {query}";
    }

}
