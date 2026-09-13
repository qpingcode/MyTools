import { test } from "node:test";
import assert from "node:assert/strict";
import { build } from "esbuild";
const built = await build({ entryPoints: ["src/backend/search.mts"], bundle: true,
  platform: "node", format: "esm", write: false });
const search = await import(`data:text/javascript;base64,${Buffer.from(built.outputFiles[0].text).toString("base64")}`);

test("process names support pinyin, initials and multiword recall", () => {
  for (const [query, name] of [["qidong", "启动开发环境"], ["qdkf", "启动开发环境"],
    ["manager device", "Device Manager"], ["orfc", "Open Rider from Clipboard"]])
    assert.equal(search.matches({ name, id: 123, port: 456 }, query), true, query);
});
test("PID and port recall and empty-query behavior are retained", () => {
  const process = { name: "Unrelated", id: 123, port: 456 };
  for (const query of ["123", "456", ""]) assert.equal(search.matches(process, query), true, query);
  for (const query of ["789", "123x", "xyz"]) assert.equal(search.matches(process, query), false, query);
});
