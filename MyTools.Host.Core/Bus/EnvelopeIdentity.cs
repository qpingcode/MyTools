using System.Text.Json.Nodes;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Core.Bus;

/// <summary>
/// Stamps transport-bound identity onto an inbound envelope. The bus never trusts peer-declared
/// pluginId/entryId/sessionId/endpointId (design §统一消息协议).
/// </summary>
public static class EnvelopeIdentity
{
    public static Envelope Stamp(EndpointId source, Envelope env)
        => env with
        {
            PluginId = source.PluginId,
            EntryId = source.EntryId,
            SessionId = source.SessionId,
            EndpointId = source.EndpointLabel,
        };
}
