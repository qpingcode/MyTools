/**
 * Thin v3 Web SDK: speaks protocol envelopes over chrome.webview.postMessage.
 * Mirrors Node HandlerRouter's callHost correlation model for page → Node plugin.call.*.
 */

import {
  type Envelope,
  type BusError,
  MessageKind,
  ProtocolVersion,
  Routes,
} from "./protocol.ts";

type Pending = {
  resolve: (value: unknown) => void;
  reject: (err: Error) => void;
};

export interface WebBusClient {
  call(route: string, payload?: unknown, timeoutMs?: number): Promise<unknown>;
  /** Convenience: plugin.call.detailCall with action field (legacy page API). */
  detailCall(action: string, payload?: unknown, timeoutMs?: number): Promise<unknown>;
  onEvent(handler: (env: Envelope) => void): () => void;
  close(): void;
}

function hasWebView(): boolean {
  return !!(typeof window !== "undefined" && (window as any).chrome?.webview);
}

function post(env: Envelope): void {
  if (!hasWebView()) throw new Error("chrome.webview is not available");
  (window as any).chrome.webview.postMessage(env);
}

function randomId(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
}

/**
 * Creates a Web bus client. Optionally completes bus.handshake first (version-only).
 * Host stamps identity; the page does not supply plugin/entry/session ids.
 */
export async function createWebBusClient(options?: {
  handshake?: boolean;
  timeoutMs?: number;
}): Promise<WebBusClient> {
  const pending = new Map<string, Pending>();
  const eventHandlers = new Set<(env: Envelope) => void>();
  const defaultTimeout = options?.timeoutMs ?? 30_000;

  const onMessage = (event: MessageEvent) => {
    const data = event.data;
    if (!data || typeof data !== "object") return;
    const env = data as Envelope;
    if (env.kind === MessageKind.Response && env.correlationId) {
      const p = pending.get(env.correlationId);
      if (!p) return;
      pending.delete(env.correlationId);
      if (env.error) {
        p.reject(new Error(`${(env.error as BusError).code}: ${(env.error as BusError).message}`));
      } else {
        p.resolve(env.payload);
      }
      return;
    }
    if (env.kind === MessageKind.Event) {
      for (const h of eventHandlers) h(env);
    }
  };

  if (hasWebView()) {
    (window as any).chrome.webview.addEventListener("message", onMessage);
  }

  if (options?.handshake !== false && hasWebView()) {
    await handshake(defaultTimeout);
  }

  function call(route: string, payload?: unknown, timeoutMs = defaultTimeout): Promise<unknown> {
    return new Promise((resolve, reject) => {
      const id = randomId();
      const timer = window.setTimeout(() => {
        pending.delete(id);
        reject(new Error(`request timed out: ${route}`));
      }, timeoutMs);
      pending.set(id, {
        resolve: (v) => {
          window.clearTimeout(timer);
          resolve(v);
        },
        reject: (e) => {
          window.clearTimeout(timer);
          reject(e);
        },
      });
      post({
        version: ProtocolVersion,
        id,
        traceId: id,
        sessionId: "",
        pluginId: "",
        entryId: "",
        endpointId: "",
        kind: MessageKind.Request,
        route,
        timeoutMs,
        payload: payload ?? {},
      });
    });
  }

  return {
    call,
    detailCall: (action, payload, timeoutMs) =>
      call(Routes.PluginCall.DetailCall, { action, payload }, timeoutMs),
    onEvent: (handler) => {
      eventHandlers.add(handler);
      return () => eventHandlers.delete(handler);
    },
    close: () => {
      if (hasWebView()) {
        (window as any).chrome.webview.removeEventListener("message", onMessage);
      }
      pending.clear();
      eventHandlers.clear();
    },
  };
}

async function handshake(timeoutMs: number): Promise<void> {
  const id = randomId();
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      cleanup();
      reject(new Error("bus.handshake timed out"));
    }, timeoutMs);

    const onMessage = (event: MessageEvent) => {
      const env = event.data as Envelope;
      if (!env || env.kind !== MessageKind.Response || env.correlationId !== id) return;
      cleanup();
      if (env.error) {
        reject(new Error(`${env.error.code}: ${env.error.message}`));
      } else {
        resolve();
      }
    };

    const cleanup = () => {
      window.clearTimeout(timer);
      (window as any).chrome.webview.removeEventListener("message", onMessage);
    };

    (window as any).chrome.webview.addEventListener("message", onMessage);
    post({
      version: ProtocolVersion,
      id,
      traceId: id,
      sessionId: "",
      pluginId: "",
      entryId: "",
      endpointId: "web",
      kind: MessageKind.Request,
      route: Routes.Bus.Handshake,
      timeoutMs,
      payload: { version: ProtocolVersion, supportedVersions: [ProtocolVersion] },
    });
  });
}
