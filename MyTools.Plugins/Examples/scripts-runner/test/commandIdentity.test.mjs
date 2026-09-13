import { test } from "node:test";
import assert from "node:assert/strict";
import { build } from "esbuild";
const result = await build({ entryPoints: ["src/backend/commandIdentity.mts"], bundle: true,
  platform: "node", format: "esm", write: false });
const { ensureCommandIds } = await import(`data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString("base64")}`);

test("persisted command identities survive filtering, renaming and configuration reordering", () => {
  const commands = [{ name: "First" }, { name: "Second" }];
  assert.equal(ensureCommandIds(commands), true);
  const ids = commands.map(c => c.id);
  assert.notEqual(ids[0], ids[1]);
  const saved = JSON.parse(JSON.stringify(commands)).reverse();
  saved[0].name = "Renamed";
  assert.equal(ensureCommandIds(saved), false);
  assert.equal(saved.filter(c => c.name === "Renamed")[0].id, ids[1]);
});

test("copied or blank identities get fresh UUIDs without changing valid identities", () => {
  const commands = [{ id: "stable" }, { id: "stable" }, { id: " " }];
  assert.equal(ensureCommandIds(commands), true);
  assert.equal(commands[0].id, "stable");
  assert.equal(new Set(commands.map(c => c.id)).size, 3);
});

test("script recall agrees with host query samples", async () => {
  const { matchesSearchText } = await import("@qping/plugin-bus/search");
  for (const [query, title] of [["dev", "Device Manager"], ["manager device", "Device Manager"],
    ["gthb", "GitHub"], ["qidong", "启动开发环境"], ["qdkf", "启动开发环境"], ["ＤＥＶ", "dev"]])
    assert.equal(matchesSearchText(query, title), true, `${query}: ${title}`);
});
