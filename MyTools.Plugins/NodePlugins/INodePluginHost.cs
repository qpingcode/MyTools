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

    Task<NodePluginInitializeResponse> InitializeAsync(
        string locale,
        string fallbackLocale,
        IReadOnlyDictionary<string, string> messages,
        string theme,
        CancellationToken cancellationToken = default);

    Task<NodePluginSearchResponse> SearchAsync(
        string query, string mode, string locale, string fallbackLocale, string theme,
        CancellationToken cancellationToken);

    Task<NodePluginActionResponse> InvokeActionAsync(
        string itemId, string actionId, string query, string locale, string fallbackLocale, string theme,
        CancellationToken cancellationToken = default);

    /// <summary>Current bus session id.</summary>
    string? SessionId { get; }

    Task DisposeAsync();
    void Dispose();
}
