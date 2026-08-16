/**
 * Handler router for the Node SDK v3. Dispatches inbound plugin.call.* requests to registered
 * handlers, auto-replies to bus.ping, and provides callHost() to invoke host.call.* capabilities
 * and correlate their responses. Mirrors the C# MessageBus routing rules on the Node side.
 */

import { randomBytes } from "node:crypto";
import {
  type Envelope,
  type BusError,
  EndpointIds,
  ErrorCode,
  MessageKind,
  ProtocolVersion,
  Routes,
} from "./protocol.ts";

type Handler = (payload: unknown) => Promise<unknown> | unknown;
type Sender = (env: Envelope) => void;

interface PendingHostCall {
  resolve: (value: unknown) => void;
  reject: (err: Error) => void;
  route: string;
}

export class HandlerRouter {
  private handlers = new Map<string, Handler>();
  private pendingHostCalls = new Map<string, PendingHostCall>();
  private pluginId = "p";
  private entryId = "e";
  private sessionId = "s";
  private endpointId = EndpointIds.NodeMain;

  /** Injected transport send fn; tests can override `router.send` directly. */
  send: Sender;

  constructor(deps: { send: Sender }) {
    this.send = deps.send;
  }

  /** Sets the bound identity stamped on outbound messages (after handshake). */
  setIdentity(ids: { pluginId: string; entryId: string; sessionId: string; endpointId: string }): void {
    this.pluginId = ids.pluginId;
    this.entryId = ids.entryId;
    this.sessionId = ids.sessionId;
    this.endpointId = ids.endpointId;
  }

  handle(route: string, handler: Handler): void {
    this.handlers.set(route, handler);
  }

  /** Dispatches an inbound request/response. Returns once handled. */
  async dispatch(env: Envelope): Promise<void> {
    if (env.kind === MessageKind.Response) {
      this.handleHostResponse(env);
      return;
    }
    if (env.kind !== MessageKind.Request) return;

    // bus.ping is always auto-replied; it does not occupy a handler slot.
    if (env.route === Routes.Bus.Ping) {
      this.send(this.responseFor(env, { ok: true }));
      return;
    }

    const handler = this.handlers.get(env.route);
    if (!handler) {
      this.send(this.errorResponseFor(env, ErrorCode.RouteNotFound, `route '${env.route}' has no handler`));
      return;
    }

    try {
      const result = await handler(env.payload);
      this.send(this.responseFor(env, result ?? {}));
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      this.send(this.errorResponseFor(env, ErrorCode.InternalError, message));
    }
  }

  /** Calls a host.call.* capability and resolves with the response payload. */
  callHost(route: string, payload: unknown, timeoutMs = 30000): Promise<unknown> {
    return new Promise((resolve, reject) => {
      const id = randomBytesHex();
      const req: Envelope = {
        version: ProtocolVersion,
        id,
        traceId: id,
        sessionId: this.sessionId,
        pluginId: this.pluginId,
        entryId: this.entryId,
        endpointId: this.endpointId,
        kind: MessageKind.Request,
        route,
        timeoutMs,
        payload,
      };
      const pending: PendingHostCall = { resolve, reject, route };
      this.pendingHostCalls.set(id, pending);
      const timer = setTimeout(() => {
        if (this.pendingHostCalls.has(id)) {
          this.pendingHostCalls.delete(id);
          reject(new Error(`host call ${route} timed out after ${timeoutMs}ms`));
        }
      }, timeoutMs);
      // Clear the timer when settled.
      const origResolve = pending.resolve;
      const origReject = pending.reject;
      pending.resolve = (v) => { clearTimeout(timer); origResolve(v); };
      pending.reject = (e) => { clearTimeout(timer); origReject(e); };
      this.send(req);
    });
  }

  private handleHostResponse(env: Envelope): void {
    if (!env.correlationId) return;
    const pending = this.pendingHostCalls.get(env.correlationId);
    if (!pending) return;
    this.pendingHostCalls.delete(env.correlationId);
    if (env.error) {
      pending.reject(new Error(`${env.error.code}: ${env.error.message}`));
    } else {
      pending.resolve(env.payload);
    }
  }

  private responseFor(req: Envelope, payload: unknown): Envelope {
    return {
      version: ProtocolVersion,
      id: randomBytesHex(),
      correlationId: req.id,
      traceId: req.traceId,
      sessionId: req.sessionId,
      pluginId: req.pluginId,
      entryId: req.entryId,
      endpointId: this.endpointId,
      kind: MessageKind.Response,
      route: req.route,
      payload,
    };
  }

  private errorResponseFor(req: Envelope, code: BusError["code"], message: string): Envelope {
    return {
      ...this.responseFor(req, null),
      payload: undefined,
      error: { code, message, retryable: false } as BusError,
    };
  }
}

function randomBytesHex(): string {
  // 16 random bytes -> 32 hex chars, matching the C# GuidIdGenerator format.
  return randomBytes(16).toString("hex");
}
