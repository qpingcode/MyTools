import { readFile, readdir } from 'node:fs/promises';
import assert from 'node:assert/strict';
import path from 'node:path';
const manifest = JSON.parse(await readFile('plugin.json', 'utf8'));
const catalog = JSON.parse(await readFile(manifest.i18n.catalog, 'utf8'));
const source = await readFile('src/web/i18n.ts', 'utf8');
const defaults = new Map([...source.matchAll(/bus\.i18n\.t\('([^']+)',\s*\{\s*defaultValue:\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')/g)].map(match => [match[1], match[2].startsWith('"') ? JSON.parse(match[2]) : match[2].slice(1, -1)]));
const placeholders = text => [...text.matchAll(/\{\{(\w+)\}\}/g)].map(match => match[1]).sort();
for (const locale of manifest.i18n.supportedLocales) {
  const messages = JSON.parse(await readFile(path.join(manifest.i18n.localesPath, locale + '.json'), 'utf8'));
  for (const entry of catalog.entries) {
    assert.equal(typeof messages[entry.key], 'string', `${locale}: ${entry.key}`);
    assert.deepEqual(placeholders(messages[entry.key]), entry.placeholders.slice().sort(), `${locale}: ${entry.key} placeholders`);
    assert.equal(defaults.get(entry.key), entry.defaultValue, `${entry.key} fallback`);
    if (locale === manifest.i18n.defaultLocale) assert.equal(messages[entry.key], entry.defaultValue, `${entry.key} English catalog`);
  }
}
async function files(directory) { const entries = await readdir(directory, {withFileTypes: true}); return (await Promise.all(entries.map(entry => entry.isDirectory() ? files(path.join(directory, entry.name)) : [path.join(directory, entry.name)]))).flat(); }
const keys = new Set(catalog.entries.map(entry => entry.key));
for (const file of await files('src')) {
  const text = await readFile(file, 'utf8');
  for (const match of text.matchAll(/['"](Plugin\.ApiTester\.[A-Za-z]+)['"]/g)) assert.ok(keys.has(match[1]), `${file}: missing catalog key ${match[1]}`);
}
console.log(`i18n checked: ${catalog.entries.length} keys, ${manifest.i18n.supportedLocales.length} locales.`);
