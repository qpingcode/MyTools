import { test } from "node:test";
import assert from "node:assert/strict";
import { build } from "esbuild";

const built = await build({ entryPoints: ["src/backend/search.mts"], bundle: true,
  platform: "node", format: "esm", write: false });
const { itemMatches } = await import(`data:text/javascript;base64,${Buffer.from(built.outputFiles[0].text).toString("base64")}`);
const rider = { title: "Open Rider from Clipboard", subtitle: "Find nearest .sln and open in Rider" };

test("Smart Open recalls IDE actions by title initials and fuzzy input", () => {
  for (const query of ["orfc", "ORFC", "ＯＲＦＣ", "rider", "rdr", "clipboard", ""]) {
    assert.equal(itemMatches(rider, query), true, query);
  }
  assert.equal(itemMatches(rider, "xyz"), false);
});

test("existing subtitle-only recall is retained", () => {
  assert.equal(itemMatches(rider, ".SLN"), true);
  assert.equal(itemMatches(rider, "nearest"), true);
});

test("all IDE action titles support initials", () => {
  for (const [query, title] of [["ovfc", "Open VSCode from Clipboard"],
    ["ovsfc", "Open Visual Studio from Clipboard"], ["oifc", "Open Intellij from Clipboard"]]) {
    assert.equal(itemMatches({ title, subtitle: "" }, query), true, title);
  }
});
