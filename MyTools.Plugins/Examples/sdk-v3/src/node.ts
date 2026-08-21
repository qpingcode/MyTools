/**
 * Node-side plugin SDK: fluent `createPlugin()` over the v3 named-pipe message bus.
 *
 * Method names map to v3 routes:
 *   initialize -> plugin.call.initialize
 *   search     -> plugin.call.search
 *   action     -> plugin.call.invokeAction
 *   handle(name) -> plugin.call.<name>
 *   publish    -> plugin.event.<subjectId>
 *   hostCall   -> host.call.<method>
 */

import { runPlugin, type PluginRuntime } from "./bootstrap.ts";
import {
  EndpointIds,
  MessageKind,
  ProtocolVersion,
  Routes,
  hostCallRoute,
  pluginCallRoute,
  pluginEventRoute,
} from "./protocol.ts";

export type PluginTheme = "light" | "dark";

export type PluginHostEnv = {
  locale: string;
  fallbackLocale: string;
  theme: PluginTheme;
};

export type PluginContext = PluginHostEnv & {
  action: string;
  itemId: string;
  query: string;
};

/** Payload of plugin.call.initialize. Host sends locale, theme, and the resolved message bag. */
export type PluginInitializeParams = PluginHostEnv & {
  messages: Record<string, string>;
};

/** Payload of plugin.call.search. */
export type PluginSearchParams = PluginHostEnv & {
  query: string;
  mode: "global" | "plugin";
};

export type PluginHostAction = {
  kind: "execute" | "openInExplorer" | "openInBrowser" | "copy" | "copyAndPaste";
  path: string;
  args?: string;
};

export type PluginActionResult = {
  message?: string;
  actionType?: "none" | "close";
  detail?: unknown;
  hostAction?: PluginHostAction;
};

/** Payload of plugin.call.invokeAction. */
export type PluginActionParams = PluginHostEnv & {
  itemId: string;
  actionId: string;
  query: string;
};

type PluginInitializeHandler = (params: PluginInitializeParams) => unknown | Promise<unknown>;
type PluginSearchHandler = (params: PluginSearchParams) => unknown | Promise<unknown>;
type PluginActionHandler = (params: PluginActionParams) => unknown | Promise<unknown>;
type PluginHandler = (payload: any, context: PluginContext) => unknown | Promise<unknown>;

export class Plugin {
  #handlers = new Map<string, PluginHandler>();
  #searchHandler: PluginSearchHandler | null = null;
  #actionHandler: PluginActionHandler | null = null;
  #initializeHandler: PluginInitializeHandler | null = null;
  #runtime: PluginRuntime | null = null;

  initialize(handler: PluginInitializeHandler): this {
    this.#initializeHandler = handler;
    return this;
  }

  search(handler: PluginSearchHandler): this {
    this.#searchHandler = handler;
    return this;
  }

  action(handler: PluginActionHandler): this {
    this.#actionHandler = handler;
    return this;
  }

  handle(action: string, handler: PluginHandler): this {
    if (!action || typeof action !== "string") {
      throw new Error("plugin.handle requires an action name.");
    }
    if (typeof handler !== "function") {
      throw new Error("plugin.handle requires a handler.");
    }
    this.#handlers.set(action, handler);
    return this;
  }

  /** Publishes a plugin.event.<subjectId> event to all webviews in the session. */
  publish(subjectId: string, payload: unknown = {}): void {
    if (!this.#runtime) throw new Error("plugin not started");
    const route = pluginEventRoute(subjectId);
    this.#runtime.transport.send({
      version: ProtocolVersion,
      id: crypto.randomUUID().replace(/-/g, "").slice(0, 32),
      traceId: crypto.randomUUID().replace(/-/g, "").slice(0, 32),
      sessionId: "",
      pluginId: "",
      entryId: "",
      endpointId: EndpointIds.NodeMain,
      kind: MessageKind.Event,
      route,
      payload,
    });
  }

  /** Calls a host.call.<method> capability and awaits the response.
   *  `timeoutMs` defaults to the remaining timeout of the inbound plugin.call
   *  (from a page `bus.call`) when inside a handler; otherwise 30s.
   */
  hostCall(method: string, params: Record<string, unknown> = {}, timeoutMs?: number): Promise<unknown> {
    if (!this.#runtime) return Promise.reject(new Error("plugin not started"));
    return this.#runtime.router.callHost(hostCallRoute(method), params, timeoutMs);
  }

  /** Connects to the host pipe and begins dispatching. Must be called last. */
  async start(): Promise<void> {
    const routes = this.buildRoutes();
    this.#runtime = await runPlugin(routes);
  }

  /**
   * Builds the v3 route map from the fluent registrations. Exposed for unit testing the mapping
   * without connecting a pipe.
   */
  buildRoutes(): Record<string, (payload: any) => unknown | Promise<unknown>> {
    const routes: Record<string, (payload: any) => unknown | Promise<unknown>> = {};

    if (this.#initializeHandler) {
      routes[Routes.PluginCall.Initialize] = (p) => this.#initializeHandler!(asInitializeParams(p));
    }
    if (this.#searchHandler) {
      routes[Routes.PluginCall.Search] = (p) => this.#searchHandler!(asSearchParams(p));
    }
    if (this.#actionHandler) {
      routes[Routes.PluginCall.InvokeAction] = (p) => this.#actionHandler!(asActionParams(p));
    }
    for (const [action, handler] of this.#handlers) {
      const route = pluginCallRoute(action);
      if (!routes[route]) {
        routes[route] = async (p) => {
          const ctx = extractContext(p, action);
          return handler(p ?? {}, ctx);
        };
      }
    }
    return routes;
  }

  async stop(): Promise<void> {
    if (this.#runtime) await this.#runtime.close();
  }
}

function asHostEnv(p: any): PluginHostEnv {
  return {
    locale: typeof p?.locale === "string" ? p.locale : "en-US",
    fallbackLocale: typeof p?.fallbackLocale === "string" ? p.fallbackLocale : "en-US",
    theme: asTheme(p?.theme),
  };
}

function asInitializeParams(p: any): PluginInitializeParams {
  const messages = p?.messages;
  return {
    ...asHostEnv(p),
    messages: isStringRecord(messages) ? messages : {},
  };
}

function asSearchParams(p: any): PluginSearchParams {
  return {
    ...asHostEnv(p),
    query: typeof p?.query === "string" ? p.query : "",
    mode: p?.mode === "plugin" ? "plugin" : "global",
  };
}

function asActionParams(p: any): PluginActionParams {
  return {
    ...asHostEnv(p),
    itemId: typeof p?.itemId === "string" ? p.itemId : "",
    actionId: typeof p?.actionId === "string" ? p.actionId : "",
    query: typeof p?.query === "string" ? p.query : "",
  };
}

function asTheme(value: unknown): PluginTheme {
  return value === "light" ? "light" : "dark";
}

function isStringRecord(value: unknown): value is Record<string, string> {
  return !!value && typeof value === "object" && !Array.isArray(value)
    && Object.values(value).every((v) => typeof v === "string");
}

function extractContext(p: any, action: string): PluginContext {
  return {
    ...asHostEnv(p),
    action,
    itemId: typeof p?.itemId === "string" ? p.itemId : "",
    query: typeof p?.query === "string" ? p.query : "",
  };
}

export function createPlugin(): Plugin {
  return new Plugin();
}
