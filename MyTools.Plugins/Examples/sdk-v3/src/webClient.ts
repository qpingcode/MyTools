/**
 * v3 Web SDK: speaks protocol envelopes over chrome.webview.postMessage.
 * Host stamps identity; the page does not supply plugin/entry/session ids.
 *
 * Connections start with bus.handshake. Subsequent plugin.call.* envelopes use the
 * negotiated version. call("refresh") is sent as plugin.call.refresh.
 */

import { mytoolsI18n } from "./i18n.ts";
import {
  type Envelope,
  type BusError,
  MessageKind,
  ProtocolVersion,
  Routes,
  pluginCallRoute,
} from "./protocol.ts";
import type { MyToolsThemePayload } from "./webTypes.ts";

export { mytoolsI18n } from "./i18n.ts";
export { HostEvents } from "./webTypes.ts";
export type {
  MyToolsHostInitializePayload,
  MyToolsHostKeyPayload,
  MyToolsHostSearchPayload,
  MyToolsInputActionCapturedPayload,
  MyToolsLanguageChangedPayload,
  MyToolsThemeChangedPayload,
  MyToolsThemePayload,
} from "./webTypes.ts";

type Pending = {
  resolve: (value: unknown) => void;
  reject: (err: Error) => void;
};

export interface WebBusClient {
  /** Sends plugin.call.<method>. Bare names are prefixed; full routes are left as-is. */
  call<T = unknown>(method: string, payload?: unknown, timeoutMs?: number): Promise<T>;
  on<T = unknown>(route: string, handler: (payload: T) => void): () => void;
  i18n: typeof mytoolsI18n;
  theme: typeof mytoolsTheme;
  close(): void;
}

const HandshakeTimeoutMs = 8_000;

const mytoolsTheme = {
  current: "dark",
  apply(payload: MyToolsThemePayload): void {
    if (typeof payload.theme === "string") {
      this.current = payload.theme;
    }
    const root = typeof document !== "undefined" ? document.documentElement : null;
    if (!root) {
      return;
    }
    if (typeof payload.theme === "string") {
      root.setAttribute("data-theme", payload.theme);
      root.style.colorScheme = payload.theme;
    }
    if (payload.themeTokens) {
      for (const [key, value] of Object.entries(payload.themeTokens)) {
        if (typeof value === "string") {
          root.style.setProperty(key, value);
        }
      }
    }
  },
};

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

function parseMessage(data: unknown): Envelope | null {
  if (typeof data === "string") {
    try {
      data = JSON.parse(data);
    } catch {
      return null;
    }
  }
  if (!data || typeof data !== "object") return null;
  const env = data as Envelope;
  if (typeof env.kind !== "string" || typeof env.route !== "string") return null;
  return env;
}

function applyHostSideEffects(env: Envelope): void {
  const payload = env.payload;
  if (!payload || typeof payload !== "object") return;
  if (env.route === Routes.HostEvent.Initialize) {
    mytoolsI18n.configure(payload);
    mytoolsI18n.apply();
    mytoolsTheme.apply(payload as MyToolsThemePayload);
  } else if (env.route === Routes.HostEvent.LanguageChanged) {
    mytoolsI18n.configure(payload);
    mytoolsI18n.apply();
  } else if (env.route === Routes.HostEvent.ThemeChanged) {
    mytoolsTheme.apply(payload as MyToolsThemePayload);
  }
}

function negotiatedVersionFrom(payload: unknown): string {
  if (payload && typeof payload === "object" && "negotiatedVersion" in payload) {
    const value = (payload as { negotiatedVersion?: unknown }).negotiatedVersion;
    if (typeof value === "string" && value.length > 0) return value;
  }
  return ProtocolVersion;
}

/**
 * Creates a Web bus client. Registers the message listener immediately so host
 * events that arrive before `on()` are buffered and replayed.
 *
 * Handshake is required before call(). Page scripts are bundled as IIFE, so this
 * function is synchronous; handshake runs in the background and gates call().
 */
export function createWebBusClient(options?: {
  timeoutMs?: number;
}): WebBusClient {
  const pending = new Map<string, Pending>();
  const eventHandlers = new Set<(env: Envelope) => void>();
  const lastByRoute = new Map<string, Envelope>();
  const defaultTimeout = options?.timeoutMs ?? 30_000;
  let wireVersion = ProtocolVersion;
  let handshakeError: Error | null = null;

  const onMessage = (event: MessageEvent) => {
    const env = parseMessage(event.data);
    if (!env) return;
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
      lastByRoute.set(env.route, env);
      applyHostSideEffects(env);
      for (const h of eventHandlers) h(env);
    }
  };

  let handshakeDone: Promise<void> = Promise.resolve();
  if (hasWebView()) {
    (window as any).chrome.webview.addEventListener("message", onMessage);
    handshakeDone = handshake(HandshakeTimeoutMs)
      .then((version) => {
        wireVersion = version;
      })
      .catch((err: unknown) => {
        handshakeError = err instanceof Error ? err : new Error(String(err));
        throw handshakeError;
      });
  }

  function ensureHandshaken(): Promise<void> {
    if (handshakeError) return Promise.reject(handshakeError);
    return handshakeDone.then(() => {
      if (handshakeError) throw handshakeError;
    });
  }

  function call<T = unknown>(method: string, payload?: unknown, timeoutMs = defaultTimeout): Promise<T> {
    const route = pluginCallRoute(method);
    return ensureHandshaken().then(
      () =>
        new Promise<T>((resolve, reject) => {
          const id = randomId();
          const timer = window.setTimeout(() => {
            pending.delete(id);
            reject(new Error(`request timed out: ${route}`));
          }, timeoutMs);
          pending.set(id, {
            resolve: (v) => {
              window.clearTimeout(timer);
              resolve(v as T);
            },
            reject: (e) => {
              window.clearTimeout(timer);
              reject(e);
            },
          });
          post({
            version: wireVersion,
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
        }),
    );
  }

  function subscribe(handler: (env: Envelope) => void): () => void {
    eventHandlers.add(handler);
    for (const env of lastByRoute.values()) handler(env);
    return () => {
      eventHandlers.delete(handler);
    };
  }

  return {
    call,
    on: (route, handler) =>
      subscribe((env) => {
        if (env.route === route) handler((env.payload ?? {}) as never);
      }),
    i18n: mytoolsI18n,
    theme: mytoolsTheme,
    close: () => {
      if (hasWebView()) {
        (window as any).chrome.webview.removeEventListener("message", onMessage);
      }
      pending.clear();
      eventHandlers.clear();
      lastByRoute.clear();
    },
  };
}

function handshake(timeoutMs: number): Promise<string> {
  const id = randomId();
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      cleanup();
      reject(new Error("bus.handshake timed out"));
    }, timeoutMs);

    const onMessage = (event: MessageEvent) => {
      const env = parseMessage(event.data);
      if (!env || env.kind !== MessageKind.Response || env.correlationId !== id) return;
      cleanup();
      if (env.error) {
        reject(new Error(`${env.error.code}: ${env.error.message}`));
      } else {
        resolve(negotiatedVersionFrom(env.payload));
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
