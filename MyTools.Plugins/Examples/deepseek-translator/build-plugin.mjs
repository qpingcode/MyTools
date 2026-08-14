import { build } from "esbuild";
import { copy } from "esbuild-plugin-copy";
import fs from "node:fs";
import path from "node:path";

fs.rmSync(path.resolve("dist"), { recursive: true, force: true });

// v2 backend build (two entries).
await build({
  entryPoints: ["src/backend/Translator/index.mts", "src/backend/AnkiCard/index.mts"],
  bundle: true,
  platform: "node",
  format: "esm",
  target: "es2024",
  outbase: "src/backend",
  outdir: "dist/backend",
  outExtension: { ".js": ".mjs" },
});

// v3 backend builds (if v3 sources exist).
if (fs.existsSync("src/backend/Translator/index.v3.mts")) {
  await build({
    entryPoints: ["src/backend/Translator/index.v3.mts"],
    bundle: true,
    platform: "node",
    format: "esm",
    target: "es2024",
    outfile: "dist/backend/Translator/index.v3.mjs",
  });
}
if (fs.existsSync("src/backend/AnkiCard/index.v3.mts")) {
  await build({
    entryPoints: ["src/backend/AnkiCard/index.v3.mts"],
    bundle: true,
    platform: "node",
    format: "esm",
    target: "es2024",
    outfile: "dist/backend/AnkiCard/index.v3.mjs",
  });
}

// Copy v3 manifest if present.
if (fs.existsSync("plugin.v3.json")) {
  fs.copyFileSync("plugin.v3.json", "dist/plugin.v3.json");
}

// Web build (shared by v2 and v3).
await build({
  entryPoints: ["src/web/Translator/main.ts", "src/web/AnkiCard/main.ts"],
  bundle: true,
  format: "iife",
  target: "es2024",
  outbase: "src/web",
  outdir: "dist/web",
  plugins: [
    copy({
      resolveFrom: "cwd",
      assets: [
        { from: ["plugin.json"], to: ["dist/plugin.json"] },
        { from: ["src/web/Translator/index.html"], to: ["dist/web/Translator/index.html"] },
        { from: ["src/web/Translator/style.css"], to: ["dist/web/Translator/style.css"] },
        { from: ["src/web/AnkiCard/index.html"], to: ["dist/web/AnkiCard/index.html"] },
        { from: ["src/web/AnkiCard/style.css"], to: ["dist/web/AnkiCard/style.css"] },
        { from: ["src/web/common/speech.js"], to: ["dist/web/common/speech.js"] },
        { from: ["i18n/**/*"], to: ["dist/i18n"] },
      ],
    }),
  ],
});
