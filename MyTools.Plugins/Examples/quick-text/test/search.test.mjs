import { test } from "node:test";
import assert from "node:assert/strict";
import { build } from "esbuild";
const built = await build({ entryPoints: ["src/backend/search.mts"], bundle: true,
  platform: "node", format: "esm", write: false });
const search = await import(`data:text/javascript;base64,${Buffer.from(built.outputFiles[0].text).toString("base64")}`);

test("phrase titles support pinyin, initials, Unicode and multiword recall", () => {
  for (const [query, title] of [["qidong", "启动开发环境"], ["qdkf", "启动开发环境"],
    ["orfc", "Open Rider from Clipboard"], ["manager device", "Device Manager"], ["ＤＥＶ", "dev"]])
    assert.equal(search.matches({ trigger: title, content: "Body" }, query, false), true, query);
  assert.equal(search.matches({ content: "启动开发环境" }, "qidong", false, "启动开发环境"), true);
});
test("body substring recall and empty-query modes are retained", () => {
  const phrase = { trigger: "Unrelated", content: "GitHub documentation" };
  assert.equal(search.matches(phrase, "documentation", false), true);
  assert.equal(search.matches(phrase, "gthb", false), false);
  assert.equal(search.matches(phrase, "xyz", false), false);
  assert.equal(search.matches(phrase, "", false), false);
  assert.equal(search.matches(phrase, "", true), true);
});
test("new title matches rank ahead of body-only matches within the plugin", () => {
  const fuzzy = search.priority("启动开发环境", "Body", "qidong");
  const body = search.priority("Other", "qidong", "qidong");
  const prefix = search.priority("qidong script", "Body", "qidong");
  assert.ok(prefix > fuzzy && fuzzy > body);
});

test("long bodies reject scattered characters but retain substring matches beyond the preview", () => {
  const LongBodyPaddingLength = 4096;
  const padding = "-".repeat(LongBodyPaddingLength);
  const phrase = { trigger: "Other", content: `v${padding}s${padding}c` };
  assert.equal(search.matches(phrase, "vsc", false), false);
  assert.equal(search.priority(phrase.trigger, phrase.content, "vsc"), 0);
  phrase.content += `${padding}VSC`;
  assert.equal(search.matches(phrase, "vsc", false), true);
  assert.ok(search.priority(phrase.trigger, phrase.content, "vsc") > 0);
});

test("subsequence matching remains available on title and preview title", () => {
  const phrase = { trigger: "GitHub", content: "Other" };
  assert.equal(search.matches(phrase, "gthb", false), true);
  assert.equal(search.matches({ content: "GitHub documentation" }, "gthb", false, "GitHub documentation"), true);
});
