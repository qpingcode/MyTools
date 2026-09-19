namespace MyTools.Protocol.Handshake;

/// <summary>
/// Named-pipe bootstrap payload. The manifest protocol version is validated before the Node
/// process starts, so the runtime handshake only proves possession of the one-shot token.
/// WebView readiness handshakes do not carry a payload.
/// </summary>
public sealed class HandshakePayload
{
    /// <summary>One-shot bootstrap token presented by the Node side.</summary>
    public string? Token { get; init; }

    public static HandshakePayload BuildNamedPipeRequest(string token)
        => new() { Token = token };
}
