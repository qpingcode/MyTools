import assert from "node:assert/strict";
import test from "node:test";
import { detectLanguage, formatSource, isLanguageId } from "./dist/formatter.mjs";

const cases = [
  ["json", '{"name":"MyTools","enabled":true}'],
  ["javascript", "const answer = items.map(item => item.value);"],
  ["typescript", "interface Item { value: number }\nconst item: Item = { value: 1 };"],
  ["html", "<!doctype html><html><body><button>Save</button></body></html>"],
  ["css", ".toolbar { display: flex; gap: 8px; }"],
  ["yaml", "name: MyTools\nenabled: true\nitems:\n  - formatter"],
  ["xml", '<?xml version="1.0"?><root><item id="1">value</item></root>'],
];

for (const [language, source] of cases) {
  test(`detectLanguage recognizes ${language}`, () => {
    assert.equal(detectLanguage(source), language);
  });
}

test("detectLanguage does not pretend ambiguous plain text is code", () => {
  assert.equal(detectLanguage("hello world"), null);
  assert.equal(detectLanguage(""), null);
});

test("detectLanguage keeps JSX and object literals in JavaScript", () => {
  assert.equal(detectLanguage("const view = <div className=\"item\">Hello</div>;"), "javascript");
  assert.equal(detectLanguage('const theme = { color: "red" };'), "javascript");
});

test("detectLanguage recognizes a self-closing XML document", () => {
  assert.equal(detectLanguage('<root enabled="true"/>'), "xml");
});

test("isLanguageId accepts supported explicit selections only", () => {
  assert.equal(isLanguageId("typescript"), true);
  assert.equal(isLanguageId("auto"), false);
  assert.equal(isLanguageId("sql"), false);
});

for (const [language, source] of cases) {
  test(`formatSource formats ${language} through its real parser`, async () => {
    const formatted = await formatSource(source, language);
    assert.equal(typeof formatted, "string");
    assert.ok(formatted.length > 0);
    assert.notEqual(formatted, source);
  });
}

test("formatSource rejects invalid input without producing replacement content", async () => {
  await assert.rejects(() => formatSource("{invalid", "json"));
  await assert.rejects(() => formatSource("<root>", "xml"));
});
