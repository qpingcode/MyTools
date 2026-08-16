import { build } from "esbuild";
import { build as viteBuild } from "vite";
import fs from "node:fs";
import path from "node:path";

const dist = path.resolve("dist");
fs.rmSync(dist, { recursive: true, force: true });

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

await viteBuild();

fs.copyFileSync("plugin.json", path.join(dist, "plugin.json"));
fs.cpSync("i18n", path.join(dist, "i18n"), { recursive: true });
