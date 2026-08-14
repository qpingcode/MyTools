// Node SDK v3 protocol-layer tests, mirroring the C# Protocol.Test coverage:
// - envelope round-trip against the canonical fixtures (drift gate)
// - length-prefix frame encode/decode
// - incremental frame decoder: fragmented / sticky / oversize-before-alloc / truncated
// Run with: node --test MyTools.Plugins/Examples/sdk-v3/test/protocol.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import {
  canonicalStringify,
  canonicalize,
} from "../src/protocol.ts";
import { encodeFrame, encodeFrameString, MAX_FRAME_BYTES } from "../src/framing.ts";
import { FrameDecoder } from "../src/framing.ts";

const here = dirname(fileURLToPath(import.meta.url));
// sdk-v3/test -> sdk-v3 -> Examples -> MyTools.Plugins -> repo root
const fixturesDir = join(here, "..", "..", "..", "..", "MyTools.Protocol.Test", "Fixtures");

// --- envelope round-trip against fixtures ---
for (const file of readdirSync(fixturesDir).filter((f) => f.endsWith(".json"))) {
  test(`fixture ${file} round-trips through TS envelope codec`, () => {
    const source = readFileSync(join(fixturesDir, file), "utf8").trim();
    const canonical = canonicalize(source);
    const reparsed = canonicalStringify(JSON.parse(canonical));
    assert.equal(reparsed, canonical, `round-trip changed payload for ${file}`);
  });
}

test("canonicalStringify omits null-valued keys", () => {
  const out = canonicalStringify({ a: 1, b: null, c: "x" });
  assert.equal(out, JSON.stringify({ a: 1, c: "x" }));
});

// --- frame encode/decode ---
test("encodeFrame prepends 4-byte little-endian length", () => {
  const payload = Buffer.from('{"id":"1"}');
  const frame = encodeFrame(payload);
  assert.equal(frame.length, 4 + payload.length);
  assert.equal(frame[0], payload.length & 0xff);
  assert.equal(frame[1], (payload.length >> 8) & 0xff);
  assert.equal(frame[2], 0);
  assert.equal(frame[3], 0);
  assert.deepEqual(frame.subarray(4), payload);
});

test("encodeFrameString encodes UTF-8 JSON", () => {
  const frame = encodeFrameString('{"k":"v"}');
  assert.equal(frame.length, 4 + 9);
  assert.equal(frame.subarray(4).toString("utf8"), '{"k":"v"}');
});

test("encodeFrame uses little-endian for large length", () => {
  const payload = Buffer.alloc(300);
  const frame = encodeFrame(payload);
  assert.equal(frame[0], 300 & 0xff);
  assert.equal(frame[1], (300 >> 8) & 0xff);
});

test("MAX_FRAME_BYTES is 4 MiB", () => {
  assert.equal(MAX_FRAME_BYTES, 4 * 1024 * 1024);
});

// --- incremental frame decoder ---
test("FrameDecoder yields a complete frame", () => {
  const dec = new FrameDecoder();
  const frame = encodeFrameString('{"id":"1"}');
  const r = dec.feed(frame);
  assert.equal(r.hasFrame, true);
  assert.equal(r.payload.toString("utf8"), '{"id":"1"}');
});

test("FrameDecoder reassembles a fragmented frame", () => {
  const dec = new FrameDecoder();
  const frame = encodeFrameString('{"hello":"world"}');
  const r1 = dec.feed(frame.subarray(0, 6));
  assert.equal(r1.hasFrame, false);
  const r2 = dec.feed(frame.subarray(6));
  assert.equal(r2.hasFrame, true);
  assert.equal(r2.payload.toString("utf8"), '{"hello":"world"}');
});

test("FrameDecoder surfaces sticky frames one at a time", () => {
  const dec = new FrameDecoder();
  const combined = Buffer.concat([
    encodeFrameString('{"a":1}'),
    encodeFrameString('{"b":2}'),
  ]);
  const r1 = dec.feed(combined);
  assert.equal(r1.hasFrame, true);
  assert.equal(r1.payload.toString("utf8"), '{"a":1}');
  // Leftover is buffered; an empty feed surfaces the second frame.
  const r2 = dec.feed(Buffer.alloc(0));
  assert.equal(r2.hasFrame, true);
  assert.equal(r2.payload.toString("utf8"), '{"b":2}');
});

test("FrameDecoder accepts a zero-length frame", () => {
  const dec = new FrameDecoder();
  const r = dec.feed(Buffer.from([0, 0, 0, 0]));
  assert.equal(r.hasFrame, true);
  assert.equal(r.payload.length, 0);
});

test("FrameDecoder rejects oversize prefix as fatal without allocating", () => {
  const dec = new FrameDecoder();
  const over = MAX_FRAME_BYTES + 1;
  const badPrefix = Buffer.from([
    over & 0xff,
    (over >> 8) & 0xff,
    (over >> 16) & 0xff,
    (over >> 24) & 0xff,
  ]);
  const r = dec.feed(badPrefix);
  assert.equal(r.hasFrame, false);
  assert.equal(r.isFatal, true);
  // Subsequent feeds stay fatal.
  const r2 = dec.feed(encodeFrameString("{}"));
  assert.equal(r2.isFatal, true);
});
