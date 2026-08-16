// Plugin Node-SDK tests: verify the fluent API (initialize/search/action/handle/publish/
// hostCall) maps to v3 routes. Uses a stubbed runPlugin to avoid a real pipe.
// Run with: node --test MyTools.Plugins/Examples/sdk-v3/test/node.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";

const capturedRoutes = {};
let lastRuntime = null;

globalThis.__stubRunPlugin = async (routes) => {
  Object.assign(capturedRoutes, routes);
  lastRuntime = {
    transport: {
      send(env) { globalThis.__lastSent = env; },
    },
    router: {
      callHost(route, payload, timeoutMs) {
        globalThis.__lastHostCall = { route, payload, timeoutMs };
        return Promise.resolve({ ok: true });
      },
    },
    close() { return Promise.resolve(); },
  };
  return lastRuntime;
};

const mod = await import("../src/node.ts");

test("Plugin exposes fluent chainable API", () => {
  const plugin = mod.createPlugin();
  assert.equal(typeof plugin.initialize, "function");
  assert.equal(typeof plugin.search, "function");
  assert.equal(typeof plugin.action, "function");
  assert.equal(typeof plugin.handle, "function");
  assert.equal(typeof plugin.publish, "function");
  assert.equal(typeof plugin.hostCall, "function");
  assert.equal(typeof plugin.start, "function");

  const r = plugin.initialize(() => ({})).search(() => ({})).action(() => ({}));
  assert.equal(r, plugin);
});

test("Plugin.handle registers named handlers without error", () => {
  const plugin = mod.createPlugin();
  plugin.handle("refresh", (payload, ctx) => ({ payload, ctx }));
});

test("Plugin.handle rejects empty action name", () => {
  const plugin = mod.createPlugin();
  assert.throws(() => plugin.handle("", () => ({})), /action name/);
});

test("Plugin.handle rejects non-function handler", () => {
  const plugin = mod.createPlugin();
  assert.throws(() => plugin.handle("x", null), /handler/);
});

test("Plugin.hostCall before start rejects", async () => {
  const plugin = mod.createPlugin();
  await assert.rejects(() => plugin.hostCall("foo"), /not started/);
});

test("Plugin.publish before start throws", () => {
  const plugin = mod.createPlugin();
  assert.throws(() => plugin.publish("subj"), /not started/);
});

test("buildRoutes maps search handler to plugin.call.search", async () => {
  const plugin = mod.createPlugin();
  plugin.search((p) => ({ items: [{ id: "1", title: p.query, mode: p.mode }] }));
  const routes = plugin.buildRoutes();
  assert.ok(routes["plugin.call.search"], "search route missing");
  const result = await routes["plugin.call.search"]({ query: "hi", mode: "plugin" });
  assert.deepEqual(result, { items: [{ id: "1", title: "hi", mode: "plugin" }] });
});

test("search params default query, mode, locale and theme when payload is sparse", async () => {
  const plugin = mod.createPlugin();
  plugin.search((p) => p);
  const routes = plugin.buildRoutes();
  const result = await routes["plugin.call.search"]({});
  assert.deepEqual(result, {
    locale: "en-US",
    fallbackLocale: "en-US",
    theme: "dark",
    query: "",
    mode: "global",
  });
});

test("buildRoutes maps action handler to plugin.call.invokeAction", async () => {
  const plugin = mod.createPlugin();
  plugin.action((p) => ({ itemId: p.itemId, actionId: p.actionId, query: p.query }));
  const routes = plugin.buildRoutes();
  assert.ok(routes["plugin.call.invokeAction"], "action route missing");
  const result = await routes["plugin.call.invokeAction"]({
    itemId: "hello:1",
    actionId: "open-detail",
    query: "hi",
    locale: "zh-CN",
    theme: "light",
  });
  assert.deepEqual(result, { itemId: "hello:1", actionId: "open-detail", query: "hi" });
});

test("action params default ids and query when payload is sparse", async () => {
  const plugin = mod.createPlugin();
  plugin.action((p) => p);
  const routes = plugin.buildRoutes();
  const result = await routes["plugin.call.invokeAction"]({});
  assert.deepEqual(result, {
    locale: "en-US",
    fallbackLocale: "en-US",
    theme: "dark",
    itemId: "",
    actionId: "",
    query: "",
  });
});

test("buildRoutes maps named handler to plugin.call.<name>", async () => {
  const plugin = mod.createPlugin();
  plugin.handle("refresh", (payload, ctx) => ({ echoed: payload, ctx }));
  const routes = plugin.buildRoutes();
  assert.ok(routes["plugin.call.refresh"], "named route missing");
  const result = await routes["plugin.call.refresh"]({ foo: 1 });
  assert.deepEqual(result, { echoed: { foo: 1 }, ctx: { action: "refresh", itemId: "", query: "", locale: "en-US", fallbackLocale: "en-US", theme: "dark" } });
});

test("buildRoutes named handler uses the registered action name in context", async () => {
  const plugin = mod.createPlugin();
  plugin.handle("save", (payload) => ({ saved: true, payload }));
  const routes = plugin.buildRoutes();
  const result = await routes["plugin.call.save"]({ x: 1, itemId: "i1" });
  assert.deepEqual(result, { saved: true, payload: { x: 1, itemId: "i1" } });
});

test("buildRoutes unknown named route is not registered", async () => {
  const plugin = mod.createPlugin();
  const routes = plugin.buildRoutes();
  assert.equal(routes["plugin.call.nope"], undefined);
});

test("buildRoutes includes initialize when registered", async () => {
  const plugin = mod.createPlugin();
  plugin.initialize((p) => ({ configured: p.locale, keys: Object.keys(p.messages) }));
  const routes = plugin.buildRoutes();
  assert.ok(routes["plugin.call.initialize"]);
  const result = await routes["plugin.call.initialize"]({
    locale: "zh-CN",
    fallbackLocale: "en-US",
    messages: { "Plugin.Hello.Name": "你好搜索" },
  });
  assert.deepEqual(result, { configured: "zh-CN", keys: ["Plugin.Hello.Name"] });
});

test("initialize params default locale and empty messages when payload is sparse", async () => {
  const plugin = mod.createPlugin();
  plugin.initialize((p) => p);
  const routes = plugin.buildRoutes();
  const result = await routes["plugin.call.initialize"]({});
  assert.deepEqual(result, { locale: "en-US", fallbackLocale: "en-US", messages: {}, theme: "dark" });
});
