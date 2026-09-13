import { test } from "node:test";
import assert from "node:assert/strict";
import { build } from "esbuild";
const built = await build({ entryPoints: ["src/backend/search.mts"], bundle: true,
  platform: "node", format: "esm", write: false });
const search = await import(`data:text/javascript;base64,${Buffer.from(built.outputFiles[0].text).toString("base64")}`);

test("plugin display names use unified title recall", () => {
  for (const [query, name] of [["qidong", "启动开发环境"], ["qdkf", "启动开发环境"],
    ["manager device", "Device Manager"], ["ＤＥＶ", "dev"]])
    assert.equal(search.matches({ name, pluginId: "example" }, query), true, query);
});
test("aliases, IDs, hotkeys and empty-query suggestions remain searchable", () => {
  const plugin = { name: "Other", pluginId: "example-plugin", aliases: ["GitHub"], hotKey: "Ctrl+Alt+O" };
  for (const query of ["example", "gthb", "ctrl+alt", ""]) assert.equal(search.matches(plugin, query), true, query);
  assert.equal(search.matches(plugin, "xyz"), false);
});
test("new fuzzy matches retain a lower position than exact and prefix names", () => {
  const fuzzy = search.priority({ name: "启动开发环境" }, "qidong");
  const prefix = search.priority({ name: "qidong launcher" }, "qidong");
  const exact = search.priority({ name: "qidong" }, "qidong");
  assert.ok(exact > prefix && prefix > fuzzy);
});
