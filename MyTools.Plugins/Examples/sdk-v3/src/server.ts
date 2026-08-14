/**
 * v3 server-side tool SDK: a fluent `createTool()` API that mirrors the v2 @qping/plugin-common/server
 * surface (initialize/search/action/handle/publish/hostCall/start) but runs over the v3 named-pipe
 * message bus via bootstrap.ts. This lets existing plugin backends switch to v3 transport by
 * changing only the import — no handler-logic rewrite.
 *
 * Legacy method names map to v3 routes:
 *   initialize -> plugin.call.initialize
 *   search     -> plugin.call.search
 *   invokeAction -> plugin.call.invokeAction
 *   detailEvent  -> plugin.call.detailEvent
 *   detailCall   -> plugin.call.detailCall (+ named handlers via plugin.call.<action>)
 *   publish    -> plugin.event.<subjectId>
 *   hostCall   -> host.call.<method>
 */

import { runPlugin, type PluginRuntime } from "./bootstrap.ts";

type NodeToolContext = {
  action: string;
  itemId: string;
  query: string;
  locale: string;
  fallbackLocale: string;
};

type NodeToolHostHandler = (params: any) => unknown | Promise<unknown>;
type NodeToolHandler = (payload: any, context: NodeToolContext) => unknown | Promise<unknown>;

export class NodeTool {
  #handlers = new Map<string, NodeToolHandler>();
  #searchHandler: NodeToolHostHandler | null = null;
  #actionHandler: NodeToolHostHandler | null = null;
  #initializeHandler: NodeToolHostHandler | null = null;
  #runtime: PluginRuntime | null = null;

  initialize(handler: NodeToolHostHandler): this {
    this.#initializeHandler = handler;
    return this;
  }

  search(handler: NodeToolHostHandler): this {
    this.#searchHandler = handler;
    return this;
  }

  action(handler: NodeToolHostHandler): this {
    this.#actionHandler = handler;
    return this;
  }

  handle(action: string, handler: NodeToolHandler): this {
    if (!action || typeof action !== "string") {
      throw new Error("tool.handle requires an action name.");
    }
    if (typeof handler !== "function") {
      throw new Error("tool.handle requires a handler.");
    }
    this.#handlers.set(action, handler);
    return this;
  }

  /** Publishes a plugin.event.<subjectId> event to all webviews in the session. */
  publish(subjectId: string, payload: unknown = {}): void {
    if (!this.#runtime) throw new Error("tool not started");
    // Strip the legacy prefix to form a clean route; the host EventReceived surfaces the route.
    const route = subjectId.startsWith("plugin.event.") ? subjectId : `plugin.event.${subjectId}`;
    this.#runtime.transport.send({
      version: "3.0",
      id: crypto.randomUUID().replace(/-/g, "").slice(0, 32),
      traceId: crypto.randomUUID().replace(/-/g, "").slice(0, 32),
      sessionId: "",
      pluginId: "",
      entryId: "",
      endpointId: "node-main",
      kind: "event",
      route,
      payload,
    });
  }

  /** Calls a host.call.<method> capability and awaits the response. */
  hostCall(method: string, params: Record<string, unknown> = {}): Promise<unknown> {
    if (!this.#runtime) return Promise.reject(new Error("tool not started"));
    return this.#runtime.router.callHost(`host.call.${method}`, params);
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
      routes["plugin.call.initialize"] = (p) => this.#initializeHandler!(p);
    }
    if (this.#searchHandler) {
      routes["plugin.call.search"] = (p) => this.#searchHandler!(p);
    }
    if (this.#actionHandler) {
      routes["plugin.call.invokeAction"] = (p) => {
        return this.#actionHandler!({ ...p, itemId: p.itemId, query: p.query });
      };
    }
    // Legacy detail calls: plugin.call.detailCall carries an `action` field selecting the handler.
    routes["plugin.call.detailCall"] = async (p) => {
      const action = p?.action ?? "";
      const handler = this.#handlers.get(action);
      if (!handler) {
        throw new Error(`no handler registered for action '${action}'`);
      }
      const ctx = extractContext(p);
      const result = await handler(p?.payload ?? {}, ctx);
      return { result: result ?? {} };
    };
    routes["plugin.call.detailEvent"] = async (p) => {
      return { state: p?.payload ?? {} };
    };
    for (const [action, handler] of this.#handlers) {
      const route = `plugin.call.${action}`;
      if (!routes[route]) {
        routes[route] = async (p) => {
          const ctx = extractContext(p);
          return handler(p, ctx);
        };
      }
    }
    return routes;
  }

  async stop(): Promise<void> {
    if (this.#runtime) await this.#runtime.close();
  }
}

function extractContext(p: any): NodeToolContext {
  return {
    action: p?.action ?? "",
    itemId: p?.itemId ?? "",
    query: p?.query ?? "",
    locale: p?.locale ?? "en-US",
    fallbackLocale: p?.fallbackLocale ?? "en-US",
  };
}

/** Creates a v3 NodeTool. Drop-in replacement for @qping/plugin-common/server's createTool(). */
export function createTool(): NodeTool {
  return new NodeTool();
}
