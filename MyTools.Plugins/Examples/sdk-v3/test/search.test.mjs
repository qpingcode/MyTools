import { test } from "node:test";
import assert from "node:assert/strict";

import { isSubsequence } from "../dist/search.mjs";

test("isSubsequence matches ordered non-adjacent characters case-insensitively", () => {
  assert.equal(isSubsequence("gthb", "GitHub"), true);
  assert.equal(isSubsequence("qck", "Quick phrase"), true);
});

test("isSubsequence rejects missing or out-of-order characters", () => {
  assert.equal(isSubsequence("hubg", "GitHub"), false);
  assert.equal(isSubsequence("xyz", "Quick phrase"), false);
});

test("isSubsequence handles empty values", () => {
  assert.equal(isSubsequence("", "anything"), true);
  assert.equal(isSubsequence("a", ""), false);
});
