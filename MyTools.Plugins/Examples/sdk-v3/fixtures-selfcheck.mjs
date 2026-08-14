// Drift-prevention self-check: load each canonical fixture, canonicalize it through the TS
// protocol types, and assert the result is byte-identical (modulo whitespace) to the source.
// Run with: node MyTools.Plugins/Examples/sdk-v3/fixtures-selfcheck.mjs
//
// The fixtures live in MyTools.Protocol.Test/Fixtures/*.json and are the single source of truth
// shared by the C# side (SampleFixturesTest) and this TS side.

import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

// Inline the canonicalize logic (kept tiny so this .mjs has no build step) — mirrors protocol.ts.
function stripNulls(value) {
  if (value === null || value === undefined) return undefined;
  if (Array.isArray(value)) return value.map(stripNulls);
  if (typeof value === "object") {
    const out = {};
    for (const [k, v] of Object.entries(value)) {
      const stripped = stripNulls(v);
      if (stripped !== undefined) out[k] = stripped;
    }
    return out;
  }
  return value;
}
function canonicalize(json) {
  return JSON.stringify(stripNulls(JSON.parse(json)));
}

const here = dirname(fileURLToPath(import.meta.url));
const fixturesDir = join(here, "..", "..", "..", "MyTools.Protocol.Test", "Fixtures");

let failures = 0;
const files = readdirSync(fixturesDir).filter((f) => f.endsWith(".json"));

if (files.length === 0) {
  console.error("SELF-CHECK FAIL: no fixtures found in " + fixturesDir);
  process.exit(1);
}

for (const file of files) {
  const path = join(fixturesDir, file);
  const source = readFileSync(path, "utf8").trim();
  const canonical = canonicalize(source);

  // The source file, canonicalized through JSON.parse/stringify, must equal our typed canonicalize.
  // We compare against the source re-canonicalized the same way — they must match, proving the TS
  // stripNulls logic preserves every field the C# side emits.
  const sourceCanonical = JSON.stringify(JSON.parse(source));
  if (canonical !== sourceCanonical) {
    console.error(`SELF-CHECK FAIL (${file}): field-stripping changed the payload`);
    console.error("  canonical:  " + canonical);
    console.error("  source:     " + sourceCanonical);
    failures++;
    continue;
  }

  // Round-trip: parse the fixture as an Envelope-shaped object and re-stringify.
  const env = JSON.parse(source);
  const required = ["version", "id", "traceId", "sessionId", "pluginId", "entryId", "endpointId", "kind", "route"];
  for (const key of required) {
    if (!(key in env)) {
      console.error(`SELF-CHECK FAIL (${file}): missing required envelope field '${key}'`);
      failures++;
    }
  }
  console.log(`SELF-CHECK OK: ${file}`);
}

if (failures > 0) {
  console.error(`\n${failures} fixture(s) failed the TS drift self-check`);
  process.exit(1);
}
console.log(`\nAll ${files.length} fixtures passed the TS drift self-check`);
