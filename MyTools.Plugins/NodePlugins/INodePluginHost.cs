using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// The backend-runtime surface that <see cref="NodePlugin"/> delegates to. The legacy
/// <see cref="NodePluginProcessHost"/> (stdio JSON-RPC) implements it; a v3 message-bus runtime
/// can implement the same surface so that <see cref="NodePlugin"/> and all host-side consumers
/// (windows, keymaps, detail views) work unchanged.
/// </summary>
internal interface INodePluginHost
{
    event EventHandler<NodePluginEventReceivedEventArgs>? EventReceived;

    Func<HostCallRequest, CancellationToken, Task<JsonElement>>? HostCallHandler { get; set; }

    Task<JsonElement> InitializeAsync(
        string locale,
        string fallbackLocale,
        IReadOnlyDictionary<string, string> messages,
        CancellationToken cancellationToken = default);

    Task<NodePluginSearchResponse> SearchAsync(
        string query, string mode, string locale, string fallbackLocale, CancellationToken cancellationToken);

    Task<NodePluginActionResponse> InvokeActionAsync(
        string itemId, string actionId, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default);

    Task<NodePluginDetailEventResponse> SendDetailEventAsync(
        string itemId, string eventName, JsonElement? payload, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default);

    Task<NodePluginDetailCallResponse> SendDetailCallAsync(
        string itemId, string action, JsonElement? payload, string query, string locale, string fallbackLocale,
        CancellationToken cancellationToken = default);

    /// <summary>Current v3 session id when running on the message bus; null for legacy stdio hosts.</summary>
    string? SessionId { get; }

    void Dispose();
}
