using System;

namespace MyTools.Host.Transports.WebView2;

/// <summary>
/// Platform-agnostic postMessage bridge used by <see cref="WebView2Transport"/>. Desktop wraps
/// <c>CoreWebView2</c>; unit tests supply an in-memory channel.
/// </summary>
public interface IWebViewMessageChannel
{
    /// <summary>Posts a JSON string to the page (WebView2 <c>PostWebMessageAsJson</c>).</summary>
    void PostWebMessageAsJson(string json);

    /// <summary>Raised when the page posts a message; argument is the raw JSON text.</summary>
    event Action<string>? WebMessageReceived;
}
