using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Framing;
using MyTools.Protocol.Messages;

namespace MyTools.Host.Transports.NamedPipe;

/// <summary>
/// <see cref="IMessageTransport"/> over a Windows named pipe using 4-byte little-endian
/// length-prefixed UTF-8 JSON frames (see <see cref="FrameCodec"/>). The host side creates the
/// pipe server (<c>isServer: true</c>); the Node side connects as a client. Frames are decoded
/// incrementally via <see cref="FrameDecoder"/> (handles fragmentation/sticky bytes). Oversized
/// length prefixes are rejected before allocation. A broken pipe fires <see cref="Disconnected"/>.
/// </summary>
public sealed class NamedPipeTransport : IMessageTransport
{
    private readonly string _pipeName;
    private readonly bool _isServer;
    private readonly SemaphoreSlim _writeGate = new(1, 1); // single writer for frame ordering
    private PipeStream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readLoop;
    private bool _connected;

    public NamedPipeTransport(string pipeName, bool isServer)
    {
        _pipeName = pipeName;
        _isServer = isServer;
    }

    public bool IsConnected => _connected;

    public event Action<Envelope>? MessageReceived;
    public event Action? Disconnected;

    /// <summary>Creates/connects the underlying pipe stream. Await this before sending/receiving.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_isServer)
        {
            var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(cancellationToken);
            _stream = server;
        }
        else
        {
            var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(cancellationToken);
            _stream = client;
        }

        _connected = true;
        _readCts = new CancellationTokenSource();
        _readLoop = ReadLoopAsync(_readCts.Token);
    }

    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken)
    {
        if (!_connected || _stream is null)
        {
            throw new InvalidOperationException("transport is not connected");
        }

        var json = JsonSerializer.Serialize(envelope, ProtocolJsonOptions.Default);
        var frame = FrameCodec.EncodeString(json);

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _stream.WriteAsync(frame, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var decoder = new FrameDecoder();
        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                int read;
                try
                {
                    read = await _stream.ReadAsync(buffer, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break; // pipe broken
                }

                if (read == 0)
                {
                    break; // peer closed
                }

                var result = decoder.Feed(buffer.AsSpan(0, read));
                if (result.IsFatal)
                {
                    break; // malformed/oversized frame -> close
                }

                while (result.HasFrame && result.Payload is { } payload)
                {
                    Envelope? env;
                    try
                    {
                        env = JsonSerializer.Deserialize<Envelope>(payload.ToArray(), ProtocolJsonOptions.Default);
                    }
                    catch (JsonException)
                    {
                        // Illegal JSON closes the connection per design.
                        goto close;
                    }

                    if (env is not null)
                    {
                        MessageReceived?.Invoke(env);
                    }

                    // Try to surface any additional frames buffered in the decoder.
                    result = decoder.Feed(ReadOnlySpan<byte>.Empty);
                    if (result.IsFatal)
                    {
                        goto close;
                    }
                }
            }

            close:;
        }
        catch (Exception)
        {
            // Any unexpected error in the read loop is treated as a disconnect; the connection is
            // closed. Swallowing here prevents an unobserved task exception from crashing the host.
        }
        finally
        {
            OnDisconnected();
        }
    }

    private void OnDisconnected()
    {
        if (!_connected) return;
        _connected = false;
        Disconnected?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        _readCts?.Cancel();
        try { _stream?.Dispose(); }
        catch { /* disposing a half-closed pipe can throw; ignore during teardown */ }
        OnDisconnected();
        _writeGate.Dispose();
        // Note: the read loop is fire-and-forget; cancellation/peer-close ends it promptly.
        return ValueTask.CompletedTask;
    }
}
