using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using MyTools.Common.Config;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Theming;
using MyTools.Desktop.Themes;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Components;

public partial class NodePluginDetailView : UserControl
{
    private static readonly Lazy<Task<CoreWebView2Environment>> WebView2Environment =
        new(CreateWebView2EnvironmentAsync);
    private const string HostInitializeSubject = "mytools.host.initialize";
    private const string HostSearchSubject = "mytools.host.search";
    private const string HostKeySubject = "mytools.host.key";

    private NodePluginDetailViewModel? viewModel;
    private string? loadedEntryPath;
    private bool browserReady;
    private bool focusPrimaryInputWhenReady;
    private readonly HashSet<string> subjectSubscriptions = new(StringComparer.Ordinal);
    private NodePlugin? subscribedPlugin;
    private readonly ILocalizationService localizationService;
    private readonly IThemeService themeService;

    public NodePluginDetailView()
    {
        localizationService = ServiceLocator.GetRequiredService<ILocalizationService>();
        themeService = ServiceLocator.GetRequiredService<IThemeService>();
        InitializeComponent();

        // Preset the native background to the dark fallback color used by every
        // plugin's CSS (var(--mt-surface-bg, #1e1e1e)). Keeping the control surface,
        // the CSS fallback and the first frame all dark-aligned means the dark theme
        // never flashes, and the light theme only has a brief dark first frame that is
        // corrected as soon as NavigationCompleted applies the real tokens. This is
        // fixed (not theme-aware) on purpose: the CSS fallback is static, so the native
        // background must match it to avoid a mismatched flash.
        PluginBrowser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(30, 30, 30);

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
        NavigateIfNeeded();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        localizationService.LocaleChanged -= OnLocaleChanged;
        themeService.ThemeChanged -= OnThemeChanged;
        ClearPluginEventSubscription();
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

        UpdatePluginEventSubscription();
        NavigateIfNeeded();
        SendSearchMessage();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodePluginDetailViewModel.CurrentContext))
        {
            subjectSubscriptions.Clear();
            UpdatePluginEventSubscription();
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
        subjectSubscriptions.Clear();
        await PluginBrowser.EnsureCoreWebView2Async(await WebView2Environment.Value);

        PluginBrowser.NavigationCompleted -= PluginBrowserOnNavigationCompleted;
        PluginBrowser.NavigationCompleted += PluginBrowserOnNavigationCompleted;
        PluginBrowser.CoreWebView2.WebMessageReceived -= PluginBrowserOnWebMessageReceived;
        PluginBrowser.CoreWebView2.WebMessageReceived += PluginBrowserOnWebMessageReceived;
        PluginBrowser.Source = BuildPluginEntryUri(entryPath);
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

    private async void PluginBrowserOnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        browserReady = e.IsSuccess;
        if (!browserReady)
        {
            return;
        }

        // Inject theme tokens as early as reliably possible. ExecuteScriptAsync is
        // the first dependable injection point (AddScriptToExecuteOnDocumentCreated
        // was found not to fire reliably on first navigation).
        if (PluginBrowser.CoreWebView2 != null)
        {
            await PluginBrowser.CoreWebView2.ExecuteScriptAsync(
                BuildThemeBootstrapScript(themeService.CurrentTheme));
        }

        SendInitializeMessage();
        SendSearchMessage();
        if (focusPrimaryInputWhenReady)
        {
            _ = FocusPrimaryInputAsync();
        }
    }

    private async void PluginBrowserOnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (viewModel?.CurrentContext == null)
        {
            return;
        }

        using var document = JsonDocument.Parse(e.WebMessageAsJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var type = typeElement.GetString();
        if (string.Equals(type, "tool-call", StringComparison.OrdinalIgnoreCase))
        {
            await HandleToolCallAsync(root);
            return;
        }

        if (string.Equals(type, "tool-subscribe", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSubscription(root, subscribe: true);
            return;
        }

        if (string.Equals(type, "tool-unsubscribe", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSubscription(root, subscribe: false);
        }
    }

    private async Task HandleToolCallAsync(JsonElement root)
    {
        var requestId = root.TryGetProperty("requestId", out var requestIdElement)
            ? requestIdElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var action = root.TryGetProperty("action", out var actionElement)
            ? actionElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(action))
        {
            SendToolResponse(requestId, null, "Missing tool action.");
            return;
        }

        try
        {
            var payloadJson = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.GetRawText()
                : string.Empty;
            var result = await viewModel!.HandleToolCallAsync(action, payloadJson);
            SendToolResponse(requestId, result, null);
        }
        catch (Exception ex)
        {
            SendToolResponse(requestId, null, ex.Message);
        }
    }

    private void UpdateSubscription(JsonElement root, bool subscribe)
    {
        var subjectId = root.TryGetProperty("subjectId", out var subjectIdElement)
            ? subjectIdElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return;
        }

        if (subscribe)
        {
            subjectSubscriptions.Add(subjectId);
        }
        else
        {
            subjectSubscriptions.Remove(subjectId);
        }
    }

    private void UpdatePluginEventSubscription()
    {
        ClearPluginEventSubscription();

        subscribedPlugin = viewModel?.CurrentContext?.Plugin;
        if (subscribedPlugin != null)
        {
            subscribedPlugin.EventReceived += OnPluginEventReceived;
        }
    }

    private void ClearPluginEventSubscription()
    {
        if (subscribedPlugin == null)
        {
            return;
        }

        subscribedPlugin.EventReceived -= OnPluginEventReceived;
        subscribedPlugin = null;
    }

    private void OnPluginEventReceived(object? sender, NodePluginEventReceivedEventArgs e)
    {
        if (!subjectSubscriptions.Contains(e.SubjectId))
        {
            return;
        }

        Dispatcher.Invoke(() => SendMessage(JsonSerializer.Serialize(new
        {
            type = "tool-event",
            subjectId = e.SubjectId,
            payload = e.Payload
        })));
    }

    private void SendToolResponse(string requestId, JsonElement? payload, string? errorMessage)
    {
        SendMessage(JsonSerializer.Serialize(new
        {
            type = "tool-response",
            requestId,
            ok = string.IsNullOrWhiteSpace(errorMessage),
            payload = payload ?? JsonSerializer.SerializeToElement(new { }),
            error = string.IsNullOrWhiteSpace(errorMessage)
                ? null
                : new
                {
                    message = errorMessage
                }
        }));
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
        var messageJson = BuildEventMessage(
            HostInitializeSubject,
            new
            {
                protocolVersion = viewModel.CurrentContext.ProtocolVersion,
                pluginId = viewModel.CurrentContext.PluginId,
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
        SendMessage(messageJson);
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
            SendMessage(JsonSerializer.Serialize(new
            {
                type = "language-changed",
                payload = new
                {
                    locale = e.CurrentLocale,
                    fallbackLocale = context.FallbackLocale,
                    translationRevision = BuildTranslationRevision(e.CurrentLocale, messages),
                    messages
                }
            }));
            _ = context.Plugin.InitializeAsync();
        });
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        if (viewModel?.CurrentContext == null)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            // Re-apply CSS variables on the already-loaded page (DOM exists, so
            // ExecuteScriptAsync is reliable). DefaultBackgroundColor stays fixed
            // dark on purpose (matches every plugin's CSS fallback); the token
            // swap below recolors the actual content.
            if (browserReady && PluginBrowser.CoreWebView2 != null)
            {
                _ = PluginBrowser.CoreWebView2.ExecuteScriptAsync(BuildThemeBootstrapScript(e.CurrentTheme));
            }

            // Notify plugin business logic (e.g. charts, icons that depend on theme).
            SendMessage(JsonSerializer.Serialize(new
            {
                type = "theme-changed",
                payload = new
                {
                    theme = e.CurrentTheme.ToWireString(),
                    themeTokens = WebThemeTokens.For(e.CurrentTheme)
                }
            }));
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

        var messageJson = BuildEventMessage(HostSearchSubject, new { query = viewModel.CurrentQuery });
        SendMessage(messageJson);
    }

    private void SendMessage(string messageJson)
    {
        if (!browserReady || PluginBrowser.CoreWebView2 == null)
        {
            return;
        }

        try
        {
            PluginBrowser.CoreWebView2.PostWebMessageAsJson(messageJson);
        }
        catch
        {
        }
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
        SendMessage(BuildEventMessage(HostKeySubject, new { key }));
    }

    private static string BuildEventMessage(string subjectId, object payload)
    {
        return JsonSerializer.Serialize(new
        {
            type = "tool-event",
            subjectId,
            payload
        });
    }
}