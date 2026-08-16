/**
 * Hand-written TypeScript protocol types for the plugin message bus v3. These MUST stay
 * byte-for-byte aligned with the C# types in MyTools.Protocol (see the canonical fixtures in
 * MyTools.Protocol.Test/Fixtures/*.json). The drift-prevention self-check
 * (fixtures-selfcheck.mjs) encodes/decodes those fixtures through these types.
 *
 * Field names are camelCase on the wire (System.Text.Json camelCase policy on the C# side).
 * Null fields are omitted on the wire (WhenWritingNull).
 *
 * Runtime constants mirror MyTools.Protocol (MessageKindWire, Routes, EndpointIds,
 * ProtocolVersion.CurrentWire). Do not re-hardcode those strings in SDK source.
 */

export const MessageKind = {
  Request: "request",
  Response: "response",
  Event: "event",
} as const;
export type MessageKind = (typeof MessageKind)[keyof typeof MessageKind];

export const ErrorCode = {
  ProtocolMismatch: "ProtocolMismatch",
  HandshakeFailed: "HandshakeFailed",
  CapabilityNotDeclared: "CapabilityNotDeclared",
  CapabilityDenied: "CapabilityDenied",
  InvalidPayload: "InvalidPayload",
  MessageTooLarge: "MessageTooLarge",
  RouteNotFound: "RouteNotFound",
  RequestTimeout: "RequestTimeout",
  TooManyRequests: "TooManyRequests",
  TransportDisconnected: "TransportDisconnected",
  PluginUnavailable: "PluginUnavailable",
  InternalError: "InternalError",
  Cancelled: "Cancelled",
  RateLimited: "RateLimited",
} as const;
export type ErrorCode = (typeof ErrorCode)[keyof typeof ErrorCode];

export const ProtocolVersion = "3.0";

export const EndpointIds = {
  NodeMain: "node-main",
  Host: "host",
} as const;

export const Routes = {
  Bus: {
    Handshake: "bus.handshake",
    Ping: "bus.ping",
    Cancel: "bus.cancel",
    Subscribe: "bus.subscribe",
    Unsubscribe: "bus.unsubscribe",
  },
  Prefix: {
    PluginCall: "plugin.call.",
    HostCall: "host.call.",
    PluginEvent: "plugin.event.",
    HostEvent: "host.event.",
    Diagnostics: "diagnostics.",
  },
  PluginCall: {
    Initialize: "plugin.call.initialize",
    Search: "plugin.call.search",
    InvokeAction: "plugin.call.invokeAction",
    DetailEvent: "plugin.call.detailEvent",
    DetailCall: "plugin.call.detailCall",
  },
} as const;

export function pluginCallRoute(method: string): string {
  return method.startsWith(Routes.Prefix.PluginCall)
    ? method
    : `${Routes.Prefix.PluginCall}${method}`;
}

export function hostCallRoute(method: string): string {
  return method.startsWith(Routes.Prefix.HostCall)
    ? method
    : `${Routes.Prefix.HostCall}${method}`;
}

export function pluginEventRoute(subjectId: string): string {
  return subjectId.startsWith(Routes.Prefix.PluginEvent)
    ? subjectId
    : `${Routes.Prefix.PluginEvent}${subjectId}`;
}

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
  version: string; // e.g. ProtocolVersion
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
