using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Plugins;
using MyTools.Common.Theming;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePlugin : IPlugin, IDisposable
{
    private readonly NodePluginManifest manifest;
    private readonly INodePluginHost processHost;
    private readonly ILogger<NodePlugin> logger;
    private readonly ILocalizationService localizationService;
    private readonly IThemeService themeService;
    private PluginLocalizationService? pluginLocalization;

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
    }

    public event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived
    {
        add => processHost.EventReceived += value;
        remove => processHost.EventReceived -= value;
    }

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

    public string Description => $"Node plugin: {Name}";

    public List<IActionWithCommand> Actions { get; } = [];

    public bool IsEnabled { get; set; } = true;

    public ViewModelType ViewModelType => ViewModelType.Basic;

    public bool IsGlobalSearchPlugin => true;

    public IReadOnlyList<string> Keywords => manifest.Keywords;

    public string PrimaryKeyword => manifest.Keywords.FirstOrDefault() ?? string.Empty;

    public string Id => manifest.Id;

    public string? HotKey => manifest.HotKey;

    public async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        try
        {
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Node plugin search failed for {PluginName}.", Name);
            return Result.CreateFailure(ex.Message, ex);
        }
    }

    public async Task InitializeAsync()
    {
        await processHost.InitializeAsync(
            localizationService.CurrentLocale,
            manifest.DefaultLocale,
            GetCurrentMessages(),
            CurrentThemeWire);
    }

    public void RegisterSettings(IConfigurationRegistry configurationRegistry)
    {
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
        if (string.IsNullOrWhiteSpace(manifest.DetailEntryFullPath))
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
        return CreateDetailContext(
            itemId: $"{manifest.Id}:keyword-route",
            searchText: searchText,
            query: query,
            detail: new NodePluginDetailViewDto
            {
                HtmlEntry = manifest.DetailEntry ?? string.Empty,
                Title = Name,
                InitialState = CloneJson(BuildKeywordRouteState(query))
            });
    }

    public NodePluginDetailContext? CreateHotKeyDetailContext()
    {
        return CreateDetailContext(
            itemId: $"{manifest.Id}:hotkey-route",
            searchText: string.IsNullOrWhiteSpace(PrimaryKeyword) ? string.Empty : $"{PrimaryKeyword} ",
            query: string.Empty,
            detail: new NodePluginDetailViewDto
            {
                HtmlEntry = manifest.DetailEntry ?? string.Empty,
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

    internal NodePluginDetailContext? CreateDetailContext(string itemId, string searchText, string query, NodePluginDetailViewDto? detail)
    {
        var detailPath = ResolveDetailEntryFullPath(detail?.HtmlEntry);
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

        resultItem.AllowedActions = BuildActions(item, query).ToList();
        return resultItem;
    }

    private IEnumerable<IActionWithCommand> BuildActions(NodePluginSearchItem item, string query)
    {
        if (item.Actions.Count == 0)
        {
            yield break;
        }

        for (var index = 0; index < item.Actions.Count; index++)
        {
            var action = item.Actions[index];
            var command = index == 0 ? Commands.DefaultCommand : $"NodeAction:{index}";
            yield return new NodePluginInvokeAction(this, action.Id, action.Title, action.Description, action.Kind).WithCommand(command);
        }
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

        return string.Equals(icon.Kind, "emoji", StringComparison.OrdinalIgnoreCase)
            ? new StringIcon(icon.Value)
            : StringIcon.Empty;
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

    /// <summary>Test-only accessor for the backend host.</summary>
    internal INodePluginHost GetHostForTest() => processHost;
}

internal sealed class NodePluginInvokeAction : IAction
{
    private readonly NodePlugin plugin;
    private readonly string actionId;
    private readonly string title;
    private readonly string description;
    private readonly string kind;

    public NodePluginInvokeAction(NodePlugin plugin, string actionId, string title, string description, string kind)
    {
        this.plugin = plugin;
        this.actionId = actionId;
        this.title = string.IsNullOrWhiteSpace(title) ? actionId : title;
        this.description = description;
        this.kind = kind;
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
            var response = await plugin.InvokeActionAsync(actionArgs.ItemId, actionId, actionArgs.Query);
            var detailContext = plugin.CreateDetailContext(actionArgs.ItemId, BuildSearchText(actionArgs.Query), actionArgs.Query, response.Detail);
            if (detailContext != null)
            {
                var navigator = ServiceLocator.GetRequiredService<INodePluginDetailNavigator>();
                navigator.OpenDetail(detailContext);
            }

            var actionType = ParseActionType(response.ActionType, detailContext != null || string.Equals(kind, "detail", StringComparison.OrdinalIgnoreCase));
            var message = string.IsNullOrWhiteSpace(response.Message) ? $"Executed {title}" : response.Message;
            return ActionResult.CreateSuccess(message, actionType);
        }
        catch (Exception ex)
        {
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

    private static ActionTypeEnum ParseActionType(string? actionType, bool openedDetail)
    {
        if (openedDetail)
        {
            return ActionTypeEnum.None;
        }

        return string.Equals(actionType, "close", StringComparison.OrdinalIgnoreCase)
            ? ActionTypeEnum.Close
            : ActionTypeEnum.None;
    }
}