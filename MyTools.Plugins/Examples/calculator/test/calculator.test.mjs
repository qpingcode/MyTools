import assert from "node:assert/strict";
import test from "node:test";
import { addHistoryEntry, evaluate, normalizeHistory } from "../dist/backend/index.mjs";

test("evaluate handles supported arithmetic", () => {
  assert.equal(evaluate("(2 + 3) * 4"), 20);
  assert.throws(() => evaluate("1 / 0"));
});

test("addHistoryEntry de-duplicates expressions and keeps the newest first", () => {
  const initial = [
    { expression: "1 + 1", result: "2", timestamp: 1 },
    { expression: "2 + 2", result: "4", timestamp: 2 },
  ];
  assert.deepEqual(addHistoryEntry(initial, "1 + 1", "2", 3), [
    { expression: "1 + 1", result: "2", timestamp: 3 },
    { expression: "2 + 2", result: "4", timestamp: 2 },
  ]);
});

test("normalizeHistory rejects malformed data and caps stored entries", () => {
  assert.deepEqual(normalizeHistory(null), []);
  const entries = Array.from({ length: 60 }, (_, index) => ({
    expression: `${index} + 1`,
    result: String(index + 1),
    timestamp: index,
  }));
  assert.equal(normalizeHistory({ entries }).length, 50);
  assert.deepEqual(normalizeHistory({ entries: [{ expression: "1 + 1" }] }), []);
});
