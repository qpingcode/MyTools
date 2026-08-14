// Node SDK v3 handler-router tests: routes plugin.call.* requests to registered handlers, auto-
// replies to bus.ping, and supports host.call.* as an async client. Pure logic, no real pipe.
// Run with: node --test MyTools.Plugins/Examples/sdk-v3/test/router.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { HandlerRouter } from "../src/router.ts";

function req(id, route, payload = {}) {
  return {
    version: "3.0", id, traceId: id, sessionId: "s",
    pluginId: "p", entryId: "e", endpointId: "node-main",
    kind: "request", route, timeoutMs: 5000, payload,
  };
}

test("router dispatches plugin.call.* to a registered handler", async () => {
  const router = new HandlerRouter({ send: () => {} });
  let called;
  router.handle("plugin.call.save", async (payload) => {
    called = payload;
    return { saved: true };
  });

  const responses = captureSends(router);
  await router.dispatch(req("r1", "plugin.call.save", { key: "theme" }));

  assert.deepEqual(called, { key: "theme" });
  assert.equal(responses.length, 1);
  assert.equal(responses[0].correlationId, "r1");
  assert.equal(responses[0].kind, "response");
  assert.deepEqual(responses[0].payload, { saved: true });
});

test("router auto-replies to bus.ping with a matching response", async () => {
  const router = new HandlerRouter({ send: () => {} });
  const responses = captureSends(router);

  await router.dispatch(req("ping-1", "bus.ping"));

  assert.equal(responses.length, 1);
  assert.equal(responses[0].correlationId, "ping-1");
  assert.equal(responses[0].route, "bus.ping");
});

test("router returns InternalError response when handler throws", async () => {
  const router = new HandlerRouter({ send: () => {} });
  router.handle("plugin.call.fail", async () => {
    throw new Error("boom");
  });
  const responses = captureSends(router);

  await router.dispatch(req("r2", "plugin.call.fail"));

  assert.equal(responses.length, 1);
  assert.equal(responses[0].error.code, "InternalError");
  assert.match(responses[0].error.message, /boom/);
});

test("router returns RouteNotFound for an unregistered plugin.call route", async () => {
  const router = new HandlerRouter({ send: () => {} });
  const responses = captureSends(router);

  await router.dispatch(req("r3", "plugin.call.unknown"));

  assert.equal(responses[0].error.code, "RouteNotFound");
});

test("host.call client sends a request envelope and correlates the response", async () => {
  const sent = [];
  const router = new HandlerRouter({ send: (e) => sent.push(e) });

  // Issue a host.call; it should enqueue an outbound request.
  const pending = router.callHost("host.call.configuration.read", { ns: "theme" });
  assert.equal(sent.length, 1);
  assert.equal(sent[0].route, "host.call.configuration.read");
  assert.equal(sent[0].kind, "request");

  // Simulate the host replying with a correlated response.
  await router.dispatch({
    version: "3.0", id: "resp-x", correlationId: sent[0].id, traceId: sent[0].traceId,
    sessionId: "s", pluginId: "p", entryId: "e", endpointId: "host",
    kind: "response", route: "host.call.configuration.read",
    payload: { value: "dark" },
  });

  const result = await pending;
  assert.deepEqual(result, { value: "dark" });
});

function captureSends(router) {
  const out = [];
  router.send = (env) => out.push(env);
  return out;
}
