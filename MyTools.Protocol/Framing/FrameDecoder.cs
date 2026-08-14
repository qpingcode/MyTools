using System.Buffers;

namespace MyTools.Protocol.Framing;

/// <summary>
/// Result of feeding bytes into <see cref="FrameDecoder"/>.
/// </summary>
public readonly record struct FrameFeedResult(bool HasFrame, ReadOnlyMemory<byte>? Payload, bool IsFatal);

/// <summary>
/// Incremental length-prefixed frame decoder for the named-pipe transport. Accepts byte chunks
/// (fragmented, sticky, or partial) and yields one complete payload at a time. Oversized length
/// prefixes are rejected as fatal <em>before</em> allocating the payload buffer, so a malicious
/// or buggy peer cannot force a huge allocation. After a fatal error the decoder is permanently
/// dead (the connection must be closed per the design's 故障处理 section).
/// </summary>
public sealed class FrameDecoder
{
    private readonly byte[] _prefixBuf = new byte[FrameLimits.PrefixBytes];
    private int _prefixFilled;
    private byte[]? _payload;
    private int _payloadFilled;
    private int _payloadLength;
    private bool _fatal;
    private ReadOnlyMemory<byte> _pending;

    /// <summary>
    /// Feeds a chunk of bytes and returns the first complete frame decoded, if any. Leftover
    /// bytes (e.g. a second frame that arrived in the same chunk) are buffered internally and
    /// surfaced by subsequent <see cref="Feed"/> calls — including with an empty chunk.
    /// </summary>
    public FrameFeedResult Feed(ReadOnlySpan<byte> chunk)
    {
        if (_fatal)
        {
            return new FrameFeedResult(false, null, IsFatal: true);
        }

        // Work from a combined buffer of pending leftover + new chunk.
        byte[]? workBuf = null;
        ReadOnlySpan<byte> current;
        if (!_pending.IsEmpty)
        {
            workBuf = new byte[_pending.Length + chunk.Length];
            _pending.Span.CopyTo(workBuf.AsSpan(0, _pending.Length));
            chunk.CopyTo(workBuf.AsSpan(_pending.Length));
            current = workBuf;
            _pending = ReadOnlyMemory<byte>.Empty;
        }
        else
        {
            current = chunk;
        }

        while (!current.IsEmpty)
        {
            // Phase 1: accumulate the 4-byte length prefix.
            if (_payload is null)
            {
                var need = FrameLimits.PrefixBytes - _prefixFilled;
                var take = Math.Min(need, current.Length);
                current.Slice(0, take).CopyTo(_prefixBuf.AsSpan(_prefixFilled));
                _prefixFilled += take;
                current = current.Slice(take);

                if (_prefixFilled < FrameLimits.PrefixBytes)
                {
                    // Still waiting for the full prefix; buffer the rest (none here).
                    return new FrameFeedResult(false, null, IsFatal: false);
                }

                _payloadLength = _prefixBuf[0]
                                 | (_prefixBuf[1] << 8)
                                 | (_prefixBuf[2] << 16)
                                 | (_prefixBuf[3] << 24);

                // Reject oversize BEFORE allocating.
                if (_payloadLength < 0 || _payloadLength > FrameLimits.MaxFrameBytes)
                {
                    _fatal = true;
                    return new FrameFeedResult(false, null, IsFatal: true);
                }

                // Zero-length frame: return immediately without buffering.
                if (_payloadLength == 0)
                {
                    Reset();
                    BufferLeftover(current, workBuf);
                    return new FrameFeedResult(true, ReadOnlyMemory<byte>.Empty, IsFatal: false);
                }

                _payload = ArrayPool<byte>.Shared.Rent(_payloadLength);
                _payloadFilled = 0;
            }

            // Phase 2: accumulate the payload.
            var payloadNeed = _payloadLength - _payloadFilled;
            var payloadTake = Math.Min(payloadNeed, current.Length);
            current.Slice(0, payloadTake).CopyTo(_payload!.AsSpan(_payloadFilled));
            _payloadFilled += payloadTake;
            current = current.Slice(payloadTake);

            if (_payloadFilled >= _payloadLength)
            {
                // Complete frame: copy out (rented buffer may be larger than needed).
                var copy = _payload.AsSpan(0, _payloadLength).ToArray();
                ArrayPool<byte>.Shared.Return(_payload);
                Reset();
                BufferLeftover(current, workBuf);
                return new FrameFeedResult(true, copy, IsFatal: false);
            }
        }

        return new FrameFeedResult(false, null, IsFatal: false);
    }

    /// <summary>
    /// Saves any unconsumed bytes from the working buffer as the next <see cref="_pending"/>.
    /// </summary>
    private void BufferLeftover(ReadOnlySpan<byte> current, byte[]? workBuf)
    {
        if (current.IsEmpty) return;
        // If we have a working buffer, carve out the leftover slice; otherwise copy from the chunk span.
        if (workBuf is not null)
        {
            var offset = workBuf.Length - current.Length;
            _pending = new ReadOnlyMemory<byte>(workBuf, offset, current.Length);
        }
        else
        {
            _pending = current.ToArray();
        }
    }

    private void Reset()
    {
        _prefixFilled = 0;
        _payload = null;
        _payloadFilled = 0;
        _payloadLength = 0;
    }
}
