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

export type PluginContext = {
  action: string;
  itemId: string;
  query: string;
  locale: string;
  fallbackLocale: string;
};

type PluginLifecycleHandler = (params: any) => unknown | Promise<unknown>;
type PluginHandler = (payload: any, context: PluginContext) => unknown | Promise<unknown>;

export class Plugin {
  #handlers = new Map<string, PluginHandler>();
  #searchHandler: PluginLifecycleHandler | null = null;
  #actionHandler: PluginLifecycleHandler | null = null;
  #initializeHandler: PluginLifecycleHandler | null = null;
  #runtime: PluginRuntime | null = null;

  initialize(handler: PluginLifecycleHandler): this {
    this.#initializeHandler = handler;
    return this;
  }

  search(handler: PluginLifecycleHandler): this {
    this.#searchHandler = handler;
    return this;
  }

  action(handler: PluginLifecycleHandler): this {
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

  /** Calls a host.call.<method> capability and awaits the response. */
  hostCall(method: string, params: Record<string, unknown> = {}): Promise<unknown> {
    if (!this.#runtime) return Promise.reject(new Error("plugin not started"));
    return this.#runtime.router.callHost(hostCallRoute(method), params);
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
      routes[Routes.PluginCall.Initialize] = (p) => this.#initializeHandler!(p);
    }
    if (this.#searchHandler) {
      routes[Routes.PluginCall.Search] = (p) => this.#searchHandler!(p);
    }
    if (this.#actionHandler) {
      routes[Routes.PluginCall.InvokeAction] = (p) => {
        return this.#actionHandler!({ ...p, itemId: p.itemId, query: p.query });
      };
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

function extractContext(p: any, action: string): PluginContext {
  return {
    action,
    itemId: p?.itemId ?? "",
    query: p?.query ?? "",
    locale: p?.locale ?? "en-US",
    fallbackLocale: p?.fallbackLocale ?? "en-US",
  };
}

export function createPlugin(): Plugin {
  return new Plugin();
}
