using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// The backend-runtime surface that <see cref="NodePlugin"/> delegates to. Implemented by
/// <see cref="NodePluginBusHost"/> over the named-pipe message bus.
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

    /// <summary>Current bus session id.</summary>
    string? SessionId { get; }

    void Dispose();
}
