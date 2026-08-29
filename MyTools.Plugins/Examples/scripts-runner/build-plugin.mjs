import { build } from "esbuild";
import { copy } from "esbuild-plugin-copy";
import fs from "node:fs";
import path from "node:path";

fs.rmSync(path.resolve("dist"), { recursive: true, force: true });

await build({
  entryPoints: ["src/backend/index.mts"],
  bundle: true,
  platform: "node",
  format: "esm",
  target: "es2024",
  outbase: "src/backend",
  outdir: "dist/backend",
  outExtension: { ".js": ".mjs" },
});

await build({
  entryPoints: ["src/web/overrides.ts"],
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
        { from: ["src/web/overrides.html"], to: ["dist/web/overrides.html"] },
        { from: ["src/web/overrides.css"], to: ["dist/web/overrides.css"] },
        { from: ["i18n/**/*"], to: ["dist/i18n"] },
      ],
    }),
  ],
});
