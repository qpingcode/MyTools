// Node SDK v3 transport tests: a named-pipe round trip between a server socket and the Node
// transport (which connects as a client). Mirrors C# NamedPipeTransportLoopbackTest.
// Run with: node --test MyTools.Plugins/Examples/sdk-v3/test/transport.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";
import { createServer } from "node:net";
import { NodeTransport } from "../src/transport.ts";
import { encodeFrameString } from "../src/framing.ts";
import { canonicalStringify } from "../src/protocol.ts";

const PROTOCOL_VERSION = "3.0";

function sampleEnvelope(id, kind = "request") {
  return {
    version: PROTOCOL_VERSION,
    id,
    traceId: id,
    sessionId: "s",
    pluginId: "p",
    endpointId: "end",
    kind,
    route: "plugin.call.x",
    timeoutMs: kind === "request" ? 1000 : undefined,
    payload: { ok: true },
  };
}

async function withPipeServer(handler) {
  const pipeName = `\\\\.\\pipe\\mytools-node-test-${randomBytes(8).toString("hex")}`;
  const server = createServer(handler);
  await new Promise((resolve) => server.listen(pipeName, resolve));
  return { pipeName, server };
}

test("NodeTransport connects and receives a frame from the server", async () => {
  const env = sampleEnvelope("in-1");
  let received;
  const { pipeName, server } = await withPipeServer((socket) => {
    socket.write(encodeFrameString(canonicalStringify(env)));
  });

  try {
    const transport = new NodeTransport();
    transport.onMessage((e) => {
      received = e;
    });
    await transport.connect(pipeName);
    await waitFor(() => received !== undefined, 1000);
    assert.equal(received.id, "in-1");
    assert.equal(received.kind, "request");
    await transport.close();
  } finally {
    server.close();
  }
});

test("NodeTransport sends a length-prefixed frame the server can decode", async () => {
  const env = sampleEnvelope("out-1");
  let serverGot;
  const { pipeName, server } = await withPipeServer((socket) => {
    readOneFrame(socket, (payload) => {
      serverGot = JSON.parse(payload.toString("utf8"));
      socket.end();
    });
  });

  try {
    const transport = new NodeTransport();
    await transport.connect(pipeName);
    transport.send(env);
    await waitFor(() => serverGot !== undefined, 1000);
    assert.equal(serverGot.id, "out-1");
    await transport.close();
  } finally {
    server.close();
  }
});

// --- helpers ---

function readOneFrame(socket, cb) {
  // Minimal single-frame reader for the server side: read 4-byte LE prefix then payload.
  let buf = Buffer.alloc(0);
  socket.on("data", (chunk) => {
    buf = Buffer.concat([buf, chunk]);
    if (buf.length < 4) return;
    const len = buf[0] | (buf[1] << 8) | (buf[2] << 16) | (buf[3] << 24);
    if (buf.length >= 4 + len) {
      cb(buf.subarray(4, 4 + len));
    }
  });
}

async function waitFor(predicate, timeoutMs) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    if (predicate()) return;
    await new Promise((r) => setTimeout(r, 10));
  }
  throw new Error("waitFor timed out");
}
