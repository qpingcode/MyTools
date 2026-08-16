// Builds @qping/plugin-bus: esbuild bundles the JS runtime, tsc generates real .d.mts type
// declarations (so consumers get full type inference — no implicit-any errors like the fake
// declare-module stub caused). The .d.mts files contain resolved types, not source re-exports,
// so consumer tsc does not penetrate into sdk-v3/src.
import { build } from "esbuild";
import fs from "node:fs";
import path from "node:path";

const root = import.meta.dirname;
fs.rmSync(path.join(root, "dist"), { recursive: true, force: true });
fs.mkdirSync(path.join(root, "dist"), { recursive: true });

const entryPoints = ["server", "protocol", "bootstrap", "i18n"];
const browserEntries = ["webClient"];

// 1. esbuild: bundle JS runtime (.mjs) for each entry point.
for (const entry of entryPoints) {
  await build({
    entryPoints: [path.join(root, `src/${entry}.ts`)],
    bundle: true,
    platform: "node",
    format: "esm",
    target: "es2024",
    outfile: path.join(root, `dist/${entry}.mjs`),
  });
  console.log(`esbuild: ${entry}.mjs`);
}

for (const entry of browserEntries) {
  await build({
    entryPoints: [path.join(root, `src/${entry}.ts`)],
    bundle: true,
    platform: "browser",
    format: "esm",
    target: "es2024",
    outfile: path.join(root, `dist/${entry}.mjs`),
  });
  console.log(`esbuild: ${entry}.mjs (browser)`);
}

// 2. tsc: generate type declarations only (--declaration --emitDeclarationOnly) into dist.
//    Uses a tsconfig that allows .ts imports and outputs .d.mts. The declarations contain fully
//    resolved types — consumers import these, NOT the source, so no .ts-extension penetration.
import { execSync } from "node:child_process";
execSync("tsc -p tsconfig.json", { cwd: root, stdio: "inherit" });

console.log("@qping/plugin-bus build complete");
