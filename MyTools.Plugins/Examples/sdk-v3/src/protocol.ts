/**
 * Hand-written TypeScript protocol types for the plugin message bus v3. These MUST stay
 * byte-for-byte aligned with the C# types in MyTools.Protocol (see the canonical fixtures in
 * MyTools.Protocol.Test/Fixtures/*.json). The drift-prevention self-check
 * (fixtures-selfcheck.mjs) encodes/decodes those fixtures through these types.
 *
 * Field names are camelCase on the wire (System.Text.Json camelCase policy on the C# side).
 * Null fields are omitted on the wire (WhenWritingNull).
 */

export type MessageKind = "request" | "response" | "event";

export type ErrorCode =
  | "ProtocolMismatch"
  | "HandshakeFailed"
  | "CapabilityNotDeclared"
  | "CapabilityDenied"
  | "InvalidPayload"
  | "MessageTooLarge"
  | "RouteNotFound"
  | "RequestTimeout"
  | "TooManyRequests"
  | "TransportDisconnected"
  | "PluginUnavailable"
  | "InternalError"
  | "Cancelled"
  | "RateLimited";

export interface BusError {
  code: ErrorCode;
  message: string;
  retryable: boolean;
  details?: unknown;
}

/**
 * The frozen Phase-1 envelope. All fields except correlationId/timeoutMs/error/payload are
 * required; the optional ones are omitted on the wire when null.
 */
export interface Envelope {
  version: string; // e.g. "3.0"
  id: string;
  correlationId?: string | null;
  traceId: string;
  sessionId: string;
  pluginId: string;
  entryId: string;
  endpointId: string;
  kind: MessageKind;
  route: string;
  timeoutMs?: number | null;
  payload?: unknown;
  error?: BusError | null;
}

/** Omit null/undefined-valued keys to match the C# WhenWritingNull behavior. */
export function canonicalStringify(value: unknown): string {
  return JSON.stringify(stripNulls(value));
}

function stripNulls(value: unknown): unknown {
  if (value === null || value === undefined) return undefined;
  if (Array.isArray(value)) return value.map(stripNulls);
  if (typeof value === "object") {
    const out: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(value as Record<string, unknown>)) {
      const stripped = stripNulls(v);
      if (stripped !== undefined) out[k] = stripped;
    }
    return out;
  }
  return value;
}

/** Parse + re-canonicalize, returning the canonical JSON string (stable key order via JSON.stringify). */
export function canonicalize(json: string): string {
  return canonicalStringify(JSON.parse(json));
}
