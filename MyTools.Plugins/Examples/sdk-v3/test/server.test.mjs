// NodeTool v3 server-SDK tests: verify the fluent API (initialize/search/action/handle/publish/
// hostCall) maps to v3 routes. Uses a stubbed runPlugin to avoid a real pipe.
// Run with: node --test MyTools.Plugins/Examples/sdk-v3/test/server.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";

// Stub bootstrap's runPlugin before importing server.ts so NodeTool.start() captures the routes.
const capturedRoutes = {};
let lastRuntime = null;

globalThis.__stubRunPlugin = async (routes) => {
  Object.assign(capturedRoutes, routes);
  lastRuntime = {
    transport: {
      send(env) { globalThis.__lastSent = env; },
    },
    router: {
      callHost(route, payload) {
        globalThis.__lastHostCall = { route, payload };
        return Promise.resolve({ ok: true });
      },
    },
    close() { return Promise.resolve(); },
  };
  return lastRuntime;
};

// Inline-replace the import by monkeypatching: we import server.ts which imports bootstrap.ts.
// To keep this test dependency-free, we test the mapping logic via a thin re-implementation check
// using the real NodeTool class but an injected start.

// Import after stub is in place. Node ESM caches modules, so we use dynamic import with a query to
// bypass cache.
const mod = await import("../src/server.ts");

test("NodeTool exposes fluent chainable API", () => {
  const tool = mod.createTool();
  assert.equal(typeof tool.initialize, "function");
  assert.equal(typeof tool.search, "function");
  assert.equal(typeof tool.action, "function");
  assert.equal(typeof tool.handle, "function");
  assert.equal(typeof tool.publish, "function");
  assert.equal(typeof tool.hostCall, "function");
  assert.equal(typeof tool.start, "function");

  // Chainable.
  const r = tool.initialize(() => ({})).search(() => ({})).action(() => ({}));
  assert.equal(r, tool);
});

test("NodeTool.handle registers named handlers without error", () => {
  const tool = mod.createTool();
  tool.handle("refresh", (payload, ctx) => ({ payload, ctx }));
  // No throw = pass; handler stored internally.
});

test("NodeTool.handle rejects empty action name", () => {
  const tool = mod.createTool();
  assert.throws(() => tool.handle("", () => ({})), /action name/);
});

test("NodeTool.handle rejects non-function handler", () => {
  const tool = mod.createTool();
  assert.throws(() => tool.handle("x", null), /handler/);
});

test("NodeTool.hostCall before start rejects", async () => {
  const tool = mod.createTool();
  await assert.rejects(() => tool.hostCall("foo"), /not started/);
});

test("NodeTool.publish before start throws", () => {
  const tool = mod.createTool();
  assert.throws(() => tool.publish("subj"), /not started/);
});

test("buildRoutes maps search handler to plugin.call.search", async () => {
  const tool = mod.createTool();
  tool.search((p) => ({ items: [{ id: "1", title: p.query }] }));
  const routes = tool.buildRoutes();
  assert.ok(routes["plugin.call.search"], "search route missing");
  const result = await routes["plugin.call.search"]({ query: "hi" });
  assert.deepEqual(result, { items: [{ id: "1", title: "hi" }] });
});

test("buildRoutes maps named handler to plugin.call.<name>", async () => {
  const tool = mod.createTool();
  tool.handle("refresh", (payload, ctx) => ({ echoed: payload, ctx }));
  const routes = tool.buildRoutes();
  assert.ok(routes["plugin.call.refresh"], "named route missing");
  const result = await routes["plugin.call.refresh"]({ foo: 1 });
  assert.deepEqual(result, { echoed: { foo: 1 }, ctx: { action: "", itemId: "", query: "", locale: "en-US", fallbackLocale: "en-US" } });
});

test("buildRoutes detailCall dispatches by action field to named handler", async () => {
  const tool = mod.createTool();
  tool.handle("save", (payload) => ({ saved: true }));
  const routes = tool.buildRoutes();
  const result = await routes["plugin.call.detailCall"]({ action: "save", payload: { x: 1 } });
  assert.deepEqual(result, { result: { saved: true } });
});

test("buildRoutes detailCall with unknown action throws", async () => {
  const tool = mod.createTool();
  const routes = tool.buildRoutes();
  await assert.rejects(() => routes["plugin.call.detailCall"]({ action: "nope" }), /no handler registered/);
});

test("buildRoutes includes initialize when registered", async () => {
  const tool = mod.createTool();
  tool.initialize((p) => ({ configured: p.locale }));
  const routes = tool.buildRoutes();
  assert.ok(routes["plugin.call.initialize"]);
  const result = await routes["plugin.call.initialize"]({ locale: "zh-CN" });
  assert.deepEqual(result, { configured: "zh-CN" });
});
