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
  plugins: [
    copy({
      resolveFrom: "cwd",
      assets: [
        { from: ["plugin.json"], to: ["dist/plugin.json"] },
        { from: ["i18n/**/*"], to: ["dist/i18n"] },
      ],
    }),
  ],
});
