import { build } from "esbuild";
import { copy } from "esbuild-plugin-copy";
import fs from "node:fs";
import path from "node:path";

fs.rmSync(path.resolve("dist"), { recursive: true, force: true });

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
