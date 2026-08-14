/**
 * Length-prefixed framing for the v3 named-pipe transport, mirroring the C# FrameCodec/FrameDecoder.
 * Wire format: [4-byte little-endian unsigned length][UTF-8 JSON payload].
 *
 * The incremental decoder handles fragmented, sticky and truncated streams, and rejects an oversize
 * length prefix as fatal *before* allocating the payload buffer (so a malicious/buggy peer cannot
 * force a huge allocation). After a fatal error the decoder stays dead.
 */

export const MAX_FRAME_BYTES = 4 * 1024 * 1024;
export const PREFIX_BYTES = 4;

/** Encodes a raw payload buffer into a length-prefixed frame. */
export function encodeFrame(payload: Buffer): Buffer {
  const length = payload.length;
  const frame = Buffer.alloc(PREFIX_BYTES + length);
  frame[0] = length & 0xff;
  frame[1] = (length >> 8) & 0xff;
  frame[2] = (length >> 16) & 0xff;
  frame[3] = (length >> 24) & 0xff;
  payload.copy(frame, PREFIX_BYTES);
  return frame;
}

/** Encodes a UTF-8 JSON string into a length-prefixed frame. */
export function encodeFrameString(json: string): Buffer {
  return encodeFrame(Buffer.from(json, "utf8"));
}

export interface FrameFeedResult {
  hasFrame: boolean;
  payload: Buffer;
  isFatal: boolean;
}

/**
 * Incremental length-prefixed frame decoder. Feed byte chunks (fragmented/sticky/partial) and get
 * back one complete payload at a time. Leftover bytes from a chunk that contained more than one
 * frame are buffered internally and surfaced by subsequent feeds (including an empty buffer).
 */
export class FrameDecoder {
  private prefixBuf = Buffer.alloc(PREFIX_BYTES);
  private prefixFilled = 0;
  private payload: Buffer | null = null;
  private payloadFilled = 0;
  private payloadLength = 0;
  private fatal = false;
  private pending: Buffer = Buffer.alloc(0);

  feed(chunk: Buffer): FrameFeedResult {
    const empty = { hasFrame: false, payload: Buffer.alloc(0), isFatal: false };
    if (this.fatal) {
      return { hasFrame: false, payload: Buffer.alloc(0), isFatal: true };
    }

    // Merge pending leftover with the new chunk into the working buffer.
    let current: Buffer;
    if (this.pending.length > 0) {
      current = Buffer.concat([this.pending, chunk]);
      this.pending = Buffer.alloc(0);
    } else {
      current = chunk;
    }

    let offset = 0;
    while (offset < current.length) {
      // Phase 1: accumulate the 4-byte length prefix.
      if (this.payload === null) {
        const need = PREFIX_BYTES - this.prefixFilled;
        const take = Math.min(need, current.length - offset);
        current.copy(this.prefixBuf, this.prefixFilled, offset, offset + take);
        this.prefixFilled += take;
        offset += take;

        if (this.prefixFilled < PREFIX_BYTES) {
          return empty; // still waiting for the full prefix
        }

        this.payloadLength =
          this.prefixBuf[0] |
          (this.prefixBuf[1] << 8) |
          (this.prefixBuf[2] << 16) |
          (this.prefixBuf[3] << 24);

        if (this.payloadLength < 0 || this.payloadLength > MAX_FRAME_BYTES) {
          this.fatal = true;
          return { hasFrame: false, payload: Buffer.alloc(0), isFatal: true };
        }
        if (this.payloadLength === 0) {
          this.reset();
          // Buffer any leftover and return the empty frame.
          this.bufferLeftover(current, offset);
          return { hasFrame: true, payload: Buffer.alloc(0), isFatal: false };
        }

        this.payload = Buffer.alloc(this.payloadLength);
        this.payloadFilled = 0;
      }

      // Phase 2: accumulate the payload.
      const payloadNeed = this.payloadLength - this.payloadFilled;
      const payloadTake = Math.min(payloadNeed, current.length - offset);
      current.copy(this.payload!, this.payloadFilled, offset, offset + payloadTake);
      this.payloadFilled += payloadTake;
      offset += payloadTake;

      if (this.payloadFilled >= this.payloadLength) {
        const out = this.payload!;
        this.reset();
        this.bufferLeftover(current, offset);
        return { hasFrame: true, payload: out, isFatal: false };
      }
    }

    return empty;
  }

  private bufferLeftover(src: Buffer, offset: number): void {
    if (offset < src.length) {
      this.pending = src.subarray(offset);
    }
  }

  private reset(): void {
    this.prefixFilled = 0;
    this.payload = null;
    this.payloadFilled = 0;
    this.payloadLength = 0;
  }
}
