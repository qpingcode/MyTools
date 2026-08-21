// End-to-end integration test of the v3 server SDK: spawns the hello-search v3 backend as a child
// process, writes the bootstrap line to its stdin, completes bus.handshake, then sends a
// plugin.call.search request and verifies the response.
// Run with: node --test MyTools.Plugins/Examples/sdk-v3/test/hello-search-v3.integration.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { randomBytes } from "node:crypto";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { encodeFrameString, FrameDecoder } from "../src/framing.ts";
import { canonicalStringify } from "../src/protocol.ts";

const here = dirname(fileURLToPath(import.meta.url));
const entryPath = join(here, "..", "..", "hello-search", "src", "backend", "index.mts");

function waitFor(predicate, timeoutMs = 5000) {
  return new Promise((resolve, reject) => {
    const start = Date.now();
    const tick = () => {
      if (predicate()) return resolve();
      if (Date.now() - start > timeoutMs) return reject(new Error("waitFor timed out"));
      setTimeout(tick, 20);
    };
    tick();
  });
}

test("hello-search v3 backend answers plugin.call.search over the pipe", async () => {
  const pipeName = `mytools-v3-it-${randomBytes(8).toString("hex")}`;
  const pipePath = `\\\\.\\pipe\\${pipeName}`;
  const token = "test-token";

  let socket;
  const server = createServer((s) => { socket = s; });
  await new Promise((r) => server.listen(pipePath, r));

  const child = spawn(process.execPath, [entryPath], { stdio: ["pipe", "pipe", "pipe"] });
  child.stdin.write(`${pipePath}\t${token}\n`);
  child.stderr.on("data", (d) => { process.stderr.write("[child] " + d); });
  child.on("exit", (code, sig) => console.log("[child exit]", code, sig));

  await waitFor(() => socket !== undefined, 5000);

  const decoder = new FrameDecoder();
  let handshakeReq;
  let searchResponse;
  socket.on("data", (chunk) => {
    let r = decoder.feed(chunk);
    while (r.hasFrame) {
      const env = JSON.parse(r.payload.toString("utf8"));
      if (env.kind === "request" && env.route === "bus.handshake") handshakeReq = env;
      if (env.kind === "response" && env.correlationId === "search-1") searchResponse = env;
      r = decoder.feed(Buffer.alloc(0));
    }
  });

  await waitFor(() => handshakeReq !== undefined, 5000);
  assert.equal(handshakeReq.payload.token, token);

  // Host replies with bound identity (simulating PipeHandshake.CompleteAsHostAsync).
  const hsReply = canonicalStringify({
    version: "3.0",
    id: "hs-resp-1",
    correlationId: handshakeReq.id,
    traceId: handshakeReq.traceId,
    sessionId: "s",
    pluginId: "hello-search",
    entryId: "hello",
    endpointId: "host",
    kind: "response",
    route: "bus.handshake",
    payload: {
      negotiatedVersion: "3.0",
      pluginId: "hello-search",
      entryId: "hello",
      sessionId: "s",
      endpointId: "node-main",
    },
  });
  socket.write(encodeFrameString(hsReply));

  // Give the SDK a moment to finish setIdentity before the search call.
  await new Promise((r) => setTimeout(r, 50));

  const req = canonicalStringify({
    version: "3.0", id: "search-1", traceId: "search-1", sessionId: "s",
    pluginId: "hello-search", entryId: "hello", endpointId: "node-main",
    kind: "request", route: "plugin.call.search", timeoutMs: 5000,
    payload: { query: "world", mode: "global", locale: "en-US", fallbackLocale: "en-US" },
  });
  socket.write(encodeFrameString(req));

  await waitFor(() => searchResponse !== undefined, 5000);

  child.kill();
  server.close();

  assert.ok(searchResponse, "no search response received");
  assert.equal(searchResponse.correlationId, "search-1");
  assert.ok(Array.isArray(searchResponse.payload.items), "response items not an array");
  assert.equal(searchResponse.payload.items[0].id, "hello:world");
  assert.equal(searchResponse.payload.items[0].icon.kind, "mdi");
  assert.equal(searchResponse.payload.items[0].icon.value, "mdi-hand-wave-outline");
});
