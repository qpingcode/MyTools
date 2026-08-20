using System;
using Microsoft.Web.WebView2.Core;
using MyTools.Host.Transports.WebView2;

namespace MyTools.Desktop.Components;

/// <summary>Adapts <see cref="CoreWebView2"/> to <see cref="IWebViewMessageChannel"/>.</summary>
internal sealed class CoreWebView2MessageChannel : IWebViewMessageChannel, IDisposable
{
    private readonly CoreWebView2 _webview;

    public CoreWebView2MessageChannel(CoreWebView2 webview)
    {
        _webview = webview;
        _webview.WebMessageReceived += OnWebMessageReceived;
    }

    public event Action<string>? WebMessageReceived;

    public void PostWebMessageAsJson(string json) => _webview.PostWebMessageAsJson(json);

    public void Dispose()
    {
        _webview.WebMessageReceived -= OnWebMessageReceived;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            WebMessageReceived?.Invoke(e.WebMessageAsJson);
        }
        catch
        {
            // Ignore malformed deliveries; transport decides whether to disconnect.
        }
    }
}
