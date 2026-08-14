import { build } from "esbuild";
import { copy } from "esbuild-plugin-copy";
import fs from "node:fs";
import path from "node:path";

fs.rmSync(path.resolve("dist"), { recursive: true, force: true });

// v2 backend build.
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

// v3 backend build (if v3 source exists).
if (fs.existsSync("src/backend/index.v3.mts")) {
  await build({
    entryPoints: ["src/backend/index.v3.mts"],
    bundle: true,
    platform: "node",
    format: "esm",
    target: "es2024",
    outfile: "dist/backend/index.v3.mjs",
  });
}

// Copy v3 manifest if present.
if (fs.existsSync("plugin.v3.json")) {
  fs.copyFileSync("plugin.v3.json", "dist/plugin.v3.json");
}

// Web build (shared by v2 and v3 — the web↔WPF postMessage protocol is unchanged).
await build({
  entryPoints: ["src/web/main.ts"],
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
        { from: ["src/web/index.html"], to: ["dist/web/index.html"] },
        { from: ["src/web/style.css"], to: ["dist/web/style.css"] },
        { from: ["i18n/**/*"], to: ["dist/i18n"] },
      ],
    }),
  ],
});
