using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using MyTools.Common.Config;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Theming;
using MyTools.Desktop.Services;
using MyTools.Desktop.Themes;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Transports.WebView2;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Identity;
using MyTools.Protocol.Messages;
using MyTools.Protocol.Routing;
using MyTools.Protocol.Versioning;

namespace MyTools.Desktop.Components;

public partial class NodePluginDetailView : UserControl
{
    private static readonly ILogger<NodePluginDetailView> StaticLogger =
        ServiceLocator.GetRequiredService<ILogger<NodePluginDetailView>>();
    private static readonly Lazy<Task<CoreWebView2Environment>> WebView2Environment =
        new(CreateWebView2EnvironmentAsync);

    private NodePluginDetailViewModel? viewModel;
    private string? loadedEntryPath;
    private bool browserReady;
    private bool focusPrimaryInputWhenReady;
    private readonly ILocalizationService localizationService;
    private readonly IThemeService themeService;
    private readonly MessageBus? _bus;
    private readonly PluginSessionManager? _sessionManager;
    private readonly IIdGenerator _ids = new GuidIdGenerator();
    private CoreWebView2MessageChannel? _webChannel;
    private WebView2Transport? _webTransport;
    private EndpointId? _webEndpoint;

    public NodePluginDetailView()
    {
        localizationService = ServiceLocator.GetRequiredService<ILocalizationService>();
        themeService = ServiceLocator.GetRequiredService<IThemeService>();
        _bus = ServiceLocator.GetRequiredService<MessageBus>();
        _sessionManager = ServiceLocator.GetRequiredService<PluginSessionManager>();

        InitializeComponent();

        PluginBrowser.DefaultBackgroundColor = System.Drawing.Color.Transparent;

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        localizationService.LocaleChanged -= OnLocaleChanged;
        localizationService.LocaleChanged += OnLocaleChanged;
        themeService.ThemeChanged -= OnThemeChanged;
        themeService.ThemeChanged += OnThemeChanged;
        if (_sessionManager is not null)
        {
            _sessionManager.SessionReplaced -= OnSessionReplaced;
            _sessionManager.SessionReplaced += OnSessionReplaced;
        }
        NavigateIfNeeded();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localizationService.LocaleChanged -= OnLocaleChanged;
        themeService.ThemeChanged -= OnThemeChanged;
        TearDownWebTransport();
        if (_sessionManager is not null)
        {
            _sessionManager.SessionReplaced -= OnSessionReplaced;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel != null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = DataContext as NodePluginDetailViewModel;
        if (viewModel != null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        NavigateIfNeeded();
        SendSearchMessage();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodePluginDetailViewModel.CurrentContext))
        {
            NavigateIfNeeded();
            SendInitializeMessage();
            SendSearchMessage();
        }

        if (e.PropertyName == nameof(NodePluginDetailViewModel.CurrentQuery))
        {
            SendSearchMessage();
        }

    }

    private void NavigateIfNeeded()
    {
        var entryPath = viewModel?.CurrentContext?.EntryFullPath;
        if (string.IsNullOrWhiteSpace(entryPath) || !File.Exists(entryPath))
        {
            return;
        }

        if (string.Equals(entryPath, loadedEntryPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedEntryPath = entryPath;
        _ = NavigateAsync(entryPath);
    }

    private async Task NavigateAsync(string entryPath)
    {
        browserReady = false;
        TearDownWebTransport();
        try
        {
            await PluginBrowser.EnsureCoreWebView2Async(await WebView2Environment.Value);

            PluginBrowser.NavigationCompleted -= PluginBrowserOnNavigationCompleted;
            PluginBrowser.NavigationCompleted += PluginBrowserOnNavigationCompleted;

            await AttachWebTransportAsync();

            var themedPath = ResolveThemedEntryPath(entryPath);
            PluginBrowser.Source = BuildPluginEntryUri(themedPath);
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.ReportStatic(ex, "Plugin WebView2 navigation");
        }
    }

    /// <summary>
    /// Returns the path to the theme-specific HTML variant for the given entry path,
    /// e.g. ".../index.html" + Dark → ".../index.dark.html". Falls back to the
    /// original path if the themed variant does not exist.
    /// </summary>
    private string ResolveThemedEntryPath(string entryPath)
    {
        var dir = Path.GetDirectoryName(entryPath);
        var themedName = WebThemeTokens.ThemeHtmlFileName(Path.GetFileName(entryPath), themeService.CurrentTheme);
        var themedPath = Path.Combine(dir ?? "", themedName);
        return File.Exists(themedPath) ? themedPath : entryPath;
    }

    /// <summary>
    /// Builds the script that sets <c>data-theme</c> and CSS variables on
    /// <c>:root</c>. Token values are inlined as literals so the script has no
    /// async dependency. Run via <see cref="CoreWebView2.ExecuteScriptAsync"/>
    /// (reliable, post-first-frame) — not via AddScriptToExecuteOnDocumentCreated,
    /// which was found not to fire reliably on first navigation.
    /// </summary>
    private static string BuildThemeBootstrapScript(ThemeKind theme)
    {
        var tokens = WebThemeTokens.For(theme);
        var tokenJs = string.Join(
            ",",
            tokens.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                  .Select(kv => $"\"{kv.Key}\":\"{kv.Value}\""));

        // Executed via ExecuteScriptAsync, which wraps the result in a JSON string.
        // We return a value to satisfy the wrapper; the DOM side effect is what matters.
        return $$"""
        (() => {
            const theme = "{{theme.ToWireString()}}";
            const tokens = { {{tokenJs}} };
            const apply = () => {
                const root = document.documentElement;
                root.setAttribute("data-theme", theme);
                root.style.colorScheme = theme;
                // Set an inline background on <html> directly from the surface-bg token,
                // so the page background is correct even before <body>'s CSS (which uses
                // var(--mt-surface-bg, <dark-fallback>)) is parsed. Without this, the body
                // fallback color would flash over the WebView2 native background.
                root.style.backgroundColor = tokens["--mt-surface-bg"] || "";
                for (const [k, v] of Object.entries(tokens)) {
                    root.style.setProperty(k, v);
                }
            };
            apply();
            // Re-apply once DOM is ready; documentElement exists at creation but
            // re-applying is harmless and guards against early reset.
            if (document.readyState === "loading") {
                document.addEventListener("DOMContentLoaded", apply, { once: true });
            }
            return theme;
        })();
        """;
    }

    private Uri BuildPluginEntryUri(string entryPath)
    {
        var pluginDirectory = viewModel?.CurrentContext?.PluginDirectory;
        if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
        {
            return new Uri(entryPath);
        }

        var fullPluginDirectory = Path.GetFullPath(pluginDirectory);
        var fullEntryPath = Path.GetFullPath(entryPath);
        if (!fullEntryPath.StartsWith(fullPluginDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(entryPath);
        }

        var hostName = BuildPluginHostName(fullPluginDirectory);
        PluginBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            hostName,
            fullPluginDirectory,
            CoreWebView2HostResourceAccessKind.Allow);

        var relativeEntryPath = Path.GetRelativePath(fullPluginDirectory, fullEntryPath).Replace('\\', '/');
        var escapedEntryPath = string.Join('/', relativeEntryPath.Split('/').Select(Uri.EscapeDataString));
        return new Uri($"https://{hostName}/{escapedEntryPath}");
    }

    private static string BuildPluginHostName(string pluginDirectory)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pluginDirectory))).ToLowerInvariant()[..16];
        return $"plugin-{hash}.mytools.localhost";
    }

    private static Task<CoreWebView2Environment> CreateWebView2EnvironmentAsync()
    {
        Directory.CreateDirectory(ConfigPath.WebView2UserDataPath);
        return CoreWebView2Environment.CreateAsync(userDataFolder: ConfigPath.WebView2UserDataPath);
    }

    private void PluginBrowserOnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        browserReady = e.IsSuccess;
        if (!browserReady)
        {
            return;
        }

        SendInitializeMessage();
        SendSearchMessage();
        if (focusPrimaryInputWhenReady)
        {
            _ = FocusPrimaryInputAsync();
        }
    }

    private async Task AttachWebTransportAsync()
    {
        if (_bus is null || PluginBrowser.CoreWebView2 is null)
        {
            return;
        }

        var plugin = viewModel?.CurrentContext?.Plugin;
        if (plugin is null)
        {
            return;
        }

        TearDownWebTransport();

        await plugin.EnsureV3SessionAsync();
        var sessionId = plugin.BusSessionId
            ?? throw new InvalidOperationException("v3 session did not start");

        var endpointLabel = $"web-{_ids.NewId()[..8]}";
        var binding = new EndpointBinding(
            plugin.ParentId, plugin.EntryId, sessionId, endpointLabel);

        _webChannel = new CoreWebView2MessageChannel(PluginBrowser.CoreWebView2);
        _webTransport = new WebView2Transport(
            binding,
            _webChannel,
            dispatchAsync: action =>
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        action();
                        tcs.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
                return tcs.Task;
            },
            enrichDetailCallPayload: EnrichDetailCallPayload);
        _webTransport.LegacyShimEnabled = false;
        // Page handshake is optional; mark ready so detailCall works if handshake is late/missed.
        _webTransport.MarkHandshaken();

        _webEndpoint = new EndpointId(
            binding.PluginId, binding.EntryId, binding.SessionId, binding.EndpointId, IsNode: false);
        _bus.RegisterEndpoint(_webEndpoint, _webTransport);
        StaticLogger.LogInformation(
            "Attached WebView2Transport endpoint={Endpoint} session={Session}",
            endpointLabel, sessionId);
    }

    private JsonObject EnrichDetailCallPayload(JsonObject payload)
    {
        var ctx = viewModel?.CurrentContext;
        if (ctx is null) return payload;

        payload["itemId"] = ctx.ItemId;
        payload["query"] = viewModel?.CurrentQuery ?? ctx.Query;
        payload["locale"] = localizationService.CurrentLocale;
        payload["fallbackLocale"] = ctx.FallbackLocale;
        return payload;
    }

    private void TearDownWebTransport()
    {
        if (_webEndpoint is not null && _bus is not null)
        {
            _bus.UnregisterEndpoint(_webEndpoint);
            _webEndpoint = null;
        }

        if (_webTransport is not null)
        {
            _webTransport.Invalidate();
            _ = _webTransport.DisposeAsync();
            _webTransport = null;
        }

        _webChannel?.Dispose();
        _webChannel = null;
    }

    private void OnSessionReplaced(object? sender, PluginSessionReplacedEventArgs e)
    {
        var plugin = viewModel?.CurrentContext?.Plugin;
        if (plugin is null) return;
        if (e.PluginId != plugin.ParentId || e.EntryId != plugin.EntryId) return;

        StaticLogger.LogWarning(
            "Node session replaced for detail view {Plugin}/{Entry}; reloading page",
            e.PluginId, e.EntryId);

        _ = Dispatcher.InvokeAsync(async () =>
        {
            TearDownWebTransport();
            loadedEntryPath = null;
            NavigateIfNeeded();
        });
    }

    private void SendInitializeMessage()
    {
        if (viewModel?.CurrentContext == null)
        {
            return;
        }

        var initialState = JsonSerializer.Deserialize<JsonElement>(viewModel.CurrentStateJson);
        var messages = viewModel.CurrentContext.Plugin.GetCurrentMessages();
        var theme = themeService.CurrentTheme;
        SendHostEvent(
            Routes.HostEvent.Initialize,
            new
            {
                protocolVersion = viewModel.CurrentContext.ProtocolVersion,
                pluginId = viewModel.CurrentContext.PluginId,
                version = viewModel.CurrentContext.Version,
                itemId = viewModel.CurrentContext.ItemId,
                query = viewModel.CurrentQuery,
                keyword = viewModel.CurrentContext.Keyword,
                initialState,
                locale = localizationService.CurrentLocale,
                fallbackLocale = viewModel.CurrentContext.FallbackLocale,
                translationRevision = BuildTranslationRevision(localizationService.CurrentLocale, messages),
                messages,
                theme = theme.ToWireString(),
                themeTokens = WebThemeTokens.For(theme)
            });
    }

    private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
    {
        if (viewModel?.CurrentContext == null)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            var context = viewModel.CurrentContext;
            var messages = context.Plugin.GetCurrentMessages();
            SendHostEvent(
                Routes.HostEvent.LanguageChanged,
                new
                {
                    locale = e.CurrentLocale,
                    fallbackLocale = context.FallbackLocale,
                    translationRevision = BuildTranslationRevision(e.CurrentLocale, messages),
                    messages
                });
            _ = context.Plugin.InitializeAsync();
        });
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        StaticLogger.LogInformation(
            "OnThemeChanged: previous={Previous}, current={Current}, hasContext={HasContext}, browserReady={BrowserReady}, coreWebView2={HasCoreWebView2}",
            e.PreviousTheme, e.CurrentTheme, viewModel?.CurrentContext != null, browserReady, PluginBrowser.CoreWebView2 != null);

        if (viewModel?.CurrentContext == null)
        {
            StaticLogger.LogWarning("OnThemeChanged: skipped, CurrentContext is null.");
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (browserReady && PluginBrowser.CoreWebView2 != null)
            {
                _ = PluginBrowser.CoreWebView2.ExecuteScriptAsync(BuildThemeBootstrapScript(e.CurrentTheme));
            }

            StaticLogger.LogInformation("OnThemeChanged: sending themeChanged event, browserReady={BrowserReady}.", browserReady);
            SendHostEvent(
                Routes.HostEvent.ThemeChanged,
                new
                {
                    theme = e.CurrentTheme.ToWireString(),
                    themeTokens = WebThemeTokens.For(e.CurrentTheme)
                });
        });
    }

    private static string BuildTranslationRevision(string locale, IReadOnlyDictionary<string, string> messages)
    {
        var content = locale + "\n" + string.Join("\n", messages.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private void SendSearchMessage()
    {
        if (viewModel?.CurrentContext == null)
        {
            return;
        }

        SendHostEvent(Routes.HostEvent.Search, new { query = viewModel.CurrentQuery });
    }

    private void SendHostEvent(string route, object payload)
    {
        if (!browserReady || _webTransport is null)
        {
            StaticLogger.LogWarning(
                "SendHostEvent: dropped (browserReady={BrowserReady}, transport={HasTransport}), route={Route}",
                browserReady, _webTransport is not null, route);
            return;
        }

        var binding = _webTransport.Binding;
        var id = _ids.NewId();
        var envelope = new Envelope
        {
            Version = ProtocolVersion.Current,
            Id = id,
            TraceId = id,
            SessionId = binding.SessionId,
            PluginId = binding.PluginId,
            EntryId = binding.EntryId,
            EndpointId = binding.EndpointId,
            Kind = MessageKind.Event,
            Route = route,
            Payload = JsonSerializer.SerializeToNode(payload, ProtocolJsonOptions.Default),
        };
        _ = _webTransport.SendAsync(envelope, CancellationToken.None);
    }

    public async Task FocusPrimaryInputAsync()
    {
        PluginBrowser.Focus();
        if (!browserReady || PluginBrowser.CoreWebView2 == null)
        {
            focusPrimaryInputWhenReady = true;
            return;
        }

        focusPrimaryInputWhenReady = false;
        await PluginBrowser.CoreWebView2.ExecuteScriptAsync("""
            (() => {
                const input = document.querySelector('textarea, input, [contenteditable="true"]');
                if (!input) {
                    return;
                }

                input.focus();
                if (typeof input.selectionStart === 'number' && typeof input.value === 'string') {
                    input.selectionStart = input.value.length;
                    input.selectionEnd = input.value.length;
                }
            })();
            """);
    }

    public void SendHostKey(string key)
    {
        SendHostEvent(Routes.HostEvent.Key, new { key });
    }
}