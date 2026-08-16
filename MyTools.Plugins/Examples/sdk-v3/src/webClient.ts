/**
 * v3 Web SDK: speaks protocol envelopes over chrome.webview.postMessage.
 * Host stamps identity; the page does not supply plugin/entry/session ids.
 */

import { mytoolsI18n } from "./i18n.ts";
import {
  type Envelope,
  type BusError,
  MessageKind,
  ProtocolVersion,
  Routes,
} from "./protocol.ts";
import type { MyToolsThemePayload } from "./webTypes.ts";

export { mytoolsI18n } from "./i18n.ts";
export { HostEvents } from "./webTypes.ts";
export type {
  MyToolsHostInitializePayload,
  MyToolsHostKeyPayload,
  MyToolsHostSearchPayload,
  MyToolsLanguageChangedPayload,
  MyToolsThemeChangedPayload,
  MyToolsThemePayload,
} from "./webTypes.ts";

type Pending = {
  resolve: (value: unknown) => void;
  reject: (err: Error) => void;
};

export interface WebBusClient {
  call(route: string, payload?: unknown, timeoutMs?: number): Promise<unknown>;
  /** plugin.call.detailCall; unwraps the Node `{ result }` wrapper. */
  detailCall<T = unknown>(action: string, payload?: unknown, timeoutMs?: number): Promise<T>;
  onEvent(handler: (env: Envelope) => void): () => void;
  on<T = unknown>(route: string, handler: (payload: T) => void): () => void;
  i18n: typeof mytoolsI18n;
  theme: typeof mytoolsTheme;
  close(): void;
}

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

function unwrapDetailResult(payload: unknown): unknown {
  if (payload && typeof payload === "object" && "result" in payload) {
    return (payload as { result: unknown }).result;
  }
  return payload;
}

/**
 * Creates a Web bus client. Registers the message listener immediately so host
 * events that arrive before `on()` are buffered and replayed.
 *
 * Handshake runs in the background (host also marks the transport ready).
 * Page scripts are bundled as IIFE, so this function is synchronous.
 */
export function createWebBusClient(options?: {
  handshake?: boolean;
  timeoutMs?: number;
}): WebBusClient {
  const pending = new Map<string, Pending>();
  const eventHandlers = new Set<(env: Envelope) => void>();
  const lastByRoute = new Map<string, Envelope>();
  const defaultTimeout = options?.timeoutMs ?? 30_000;

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

  if (hasWebView()) {
    (window as any).chrome.webview.addEventListener("message", onMessage);
    if (options?.handshake !== false) {
      void handshake(defaultTimeout).catch(() => {
        // Host marks the transport handshaken; a missed handshake is not fatal.
      });
    }
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

  function onEvent(handler: (env: Envelope) => void): () => void {
    eventHandlers.add(handler);
    for (const env of lastByRoute.values()) handler(env);
    return () => {
      eventHandlers.delete(handler);
    };
  }

  return {
    call,
    detailCall: async (action, payload, timeoutMs) => {
      const response = await call(Routes.PluginCall.DetailCall, { action, payload }, timeoutMs);
      return unwrapDetailResult(response) as never;
    },
    onEvent,
    on: (route, handler) =>
      onEvent((env) => {
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

async function handshake(timeoutMs: number): Promise<void> {
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
