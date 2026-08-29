/**
 * Node-side plugin SDK: fluent `createPlugin()` over the v3 named-pipe message bus.
 *
 * Method names map to v3 routes:
 *   initialize -> plugin.call.initialize
 *   search     -> plugin.call.search
 *   actions    -> plugin.call.invokeAction (dispatched by action id)
 *   handle(name) -> plugin.call.<name>
 *   publish    -> plugin.event.<subjectId>
 *   hostCall   -> host.call.<method>
 */

import { runPlugin, type PluginRuntime } from "./bootstrap.ts";
import { asHostEnv, type PluginHostEnv, type PluginTheme } from "./hostEnv.ts";
import { toActionManifest, type ActionDefinition, type ActionOutcome } from "./actions.ts";
import {
  EndpointIds,
  MessageKind,
  ProtocolVersion,
  Routes,
  hostCallRoute,
  pluginCallRoute,
  pluginEventRoute,
} from "./protocol.ts";

export type { PluginHostEnv, PluginTheme } from "./hostEnv.ts";
export {
  HostAction,
  Key,
  Modifiers,
  type ActionContext,
  type ActionDefinition,
  type ActionOutcome,
  type DetailRequest,
  type HostActionKind,
  type HostActionRequest,
  type Hotkey,
  type HotkeyKey,
  type HotkeyModifiers,
  type LocalizedText,
  type RunSpec,
} from "./actions.ts";

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

export type SearchIcon = {
  kind: string;
  value: string;
};

/**
 * A search result row. Only `id`/`title`/`subtitle`/`priority`/`icon`/`actions` reach the host;
 * any other field stays on the Node side and comes back as `context.item` when an action runs.
 */
export type SearchItem = {
  id: string;
  title: string;
  subtitle?: string;
  priority?: number;
  icon?: SearchIcon;
  /** Ids of registered actions, in display order. The first one is bound to Enter. */
  actions?: string[];
  [extra: string]: unknown;
};

export type SearchResult = {
  items: SearchItem[];
};

/** Keeps `context.item` available without letting a long-lived session grow without bound. */
const ItemCacheLimit = 1000;
const SessionCacheLimit = 8;

type PluginInitializeHandler = (params: PluginInitializeParams) => unknown | Promise<unknown>;
type PluginSearchHandler = (params: PluginSearchParams) => SearchResult | Promise<SearchResult>;
type PluginHandler = (payload: any, context: PluginContext) => unknown | Promise<unknown>;

export class Plugin {
  #handlers = new Map<string, PluginHandler>();
  #actions = new Map<string, ActionDefinition<any>>();
  #searchHandler: PluginSearchHandler | null = null;
  #initializeHandler: PluginInitializeHandler | null = null;
  #runtime: PluginRuntime | null = null;
  #itemsBySession = new Map<string, Map<string, unknown>>();

  initialize(handler: PluginInitializeHandler): this {
    this.#initializeHandler = handler;
    return this;
  }

  search(handler: PluginSearchHandler): this {
    this.#searchHandler = handler;
    return this;
  }

  /**
   * Registers every action this plugin offers. The list is sent to the host in the initialize
   * response, so the host knows the ids, labels and hotkeys before any search runs; search items
   * and the detail page then reference them by id only.
   */
  actions<TItem = any>(definitions: ActionDefinition<TItem>[]): this {
    if (!Array.isArray(definitions)) {
      throw new Error("plugin.actions requires an array of action definitions.");
    }
    for (const definition of definitions) {
      if (!definition?.id) {
        throw new Error("plugin.actions requires every action to have an id.");
      }
      if (this.#actions.has(definition.id)) {
        throw new Error(`plugin.actions has a duplicate action id: ${definition.id}`);
      }
      if (typeof definition.execute !== "function") {
        throw new Error(`plugin.actions requires an execute function for action: ${definition.id}`);
      }
      this.#actions.set(definition.id, definition);
    }
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
  buildRoutes(): Record<
    string,
    (payload: any, context?: { sessionId: string }) => unknown | Promise<unknown>
  > {
    const routes: Record<
      string,
      (payload: any, context?: { sessionId: string }) => unknown | Promise<unknown>
    > = {};

    // initialize always answers, even without a handler, because the host reads the action
    // registry out of this response.
    routes[Routes.PluginCall.Initialize] = async (p) => {
      const result = this.#initializeHandler
        ? await this.#initializeHandler(asInitializeParams(p))
        : {};
      const body = result && typeof result === "object" ? { ...(result as object) } : {};
      return { ...body, actions: [...this.#actions.values()].map(toActionManifest) };
    };

    if (this.#searchHandler) {
      routes[Routes.PluginCall.Search] = async (p, request) => {
        const result = await this.#searchHandler!(asSearchParams(p));
        return { items: this.#trackItems(request?.sessionId ?? "default", result?.items ?? []) };
      };
    }

    if (this.#actions.size > 0) {
      routes[Routes.PluginCall.InvokeAction] = (p, request) =>
        this.#invokeAction(request?.sessionId ?? "default", p);
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

  /** Remembers the full items and returns the trimmed rows the host actually renders. */
  #trackItems(sessionId: string, items: SearchItem[]): Record<string, unknown>[] {
    const sessionItems = this.#sessionItems(sessionId);
    const wire: Record<string, unknown>[] = [];
    for (const item of items) {
      if (!item || typeof item !== "object") continue;
      const id = typeof item.id === "string" ? item.id : "";
      if (id) {
        sessionItems.delete(id);
        sessionItems.set(id, item);
      }
      wire.push(toWireItem(item));
    }
    while (sessionItems.size > ItemCacheLimit) {
      const oldest = sessionItems.keys().next();
      if (oldest.done) break;
      sessionItems.delete(oldest.value);
    }
    return wire;
  }

  async #invokeAction(sessionId: string, payload: any): Promise<ActionOutcome> {
    const env = asHostEnv(payload);
    const actionId = typeof payload?.actionId === "string" ? payload.actionId : "";
    const itemId = typeof payload?.itemId === "string" ? payload.itemId : "";
    const query = typeof payload?.query === "string" ? payload.query : "";

    const definition = this.#actions.get(actionId);
    if (!definition) {
      throw new Error(`unknown action: ${actionId}`);
    }

    const outcome = (await definition.execute({
      ...env,
      actionId,
      itemId,
      query,
      item: this.#itemsBySession.get(sessionId)?.get(itemId),
    })) as ActionOutcome | undefined;
    return outcome ?? {};
  }

  #sessionItems(sessionId: string): Map<string, unknown> {
    const key = sessionId || "default";
    let items = this.#itemsBySession.get(key);
    if (!items) {
      items = new Map<string, unknown>();
      this.#itemsBySession.set(key, items);
      while (this.#itemsBySession.size > SessionCacheLimit) {
        const oldest = this.#itemsBySession.keys().next();
        if (oldest.done) break;
        this.#itemsBySession.delete(oldest.value);
      }
    }
    return items;
  }
}

function toWireItem(item: SearchItem): Record<string, unknown> {
  const wire: Record<string, unknown> = {
    id: item.id,
    title: item.title,
  };
  if (typeof item.subtitle === "string") wire.subtitle = item.subtitle;
  if (typeof item.priority === "number") wire.priority = item.priority;
  if (item.icon) wire.icon = item.icon;
  if (Array.isArray(item.actions)) wire.actions = item.actions.filter((id) => typeof id === "string");
  return wire;
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
