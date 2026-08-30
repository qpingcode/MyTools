import assert from "node:assert/strict";
import test from "node:test";
import { hashText, transformText } from "./dist/codec.mjs";

test("Base64 round-trips UTF-8 text", () => {
  const encoded = transformText("你好, MyTools!", "encode", "base64");
  assert.equal(encoded.output, "5L2g5aW9LCBNeVRvb2xzIQ==");
  assert.deepEqual(transformText(encoded.output, "decode", "base64"), { output: "你好, MyTools!", binary: false });
});

test("Base64 decoder accepts whitespace and missing padding", () => {
  assert.equal(transformText("aG Vs\nbG8", "decode", "base64").output, "hello");
});

test("Base64URL omits padding and round-trips", () => {
  const encoded = transformText("subjects?_d", "encode", "base64url").output;
  assert.equal(encoded.includes("="), false);
  assert.equal(transformText(encoded, "decode", "base64url").output, "subjects?_d");
});

test("URL Component round-trips", () => {
  const encoded = transformText("你好 /?=", "encode", "url").output;
  assert.equal(encoded, "%E4%BD%A0%E5%A5%BD%20%2F%3F%3D");
  assert.equal(transformText(encoded, "decode", "url").output, "你好 /?=");
});

test("Hex decoder reports binary data as hex", () => {
  assert.deepEqual(transformText("ff00", "decode", "hex"), { output: "ff00", binary: true });
});

test("invalid Base64 and Hex inputs are rejected", () => {
  assert.throws(() => transformText("%%%", "decode", "base64"));
  assert.throws(() => transformText("abc", "decode", "hex"));
});

test("hash algorithms encode only", () => {
  assert.equal(transformText("hello", "encode", "md5").output, "5d41402abc4b2a76b9719d911017c592");
  assert.equal(hashText("hello", "sha256", "hex-upper"), "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824");
  assert.equal(hashText("hello", "sha1", "base64"), "qvTGHdzF6KLavt4PO0gs2a6pQ00=");
  assert.throws(() => transformText("hello", "decode", "sha256"));
});
