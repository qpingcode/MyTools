import { test } from "node:test";
import assert from "node:assert/strict";

import { isSubsequence, matchesSearchText, normalizeSearchText } from "../dist/search.mjs";

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

test("host-compatible title recall handles Unicode, multiword, pinyin and initials", () => {
  assert.equal(normalizeSearchText(" ＤＥＶ  \t Test "), "dev test");
  for (const [query, title] of [["dev", "Device Manager"], ["manager device", "Device Manager"],
    ["gthb", "GitHub"], ["qidong", "启动开发环境"], ["qdkf", "启动开发环境"], ["ＤＥＶ", "dev"]]) {
    assert.equal(matchesSearchText(query, title), true, `${query}: ${title}`);
  }
  assert.equal(matchesSearchText("xyz", "启动开发环境"), false);
});
