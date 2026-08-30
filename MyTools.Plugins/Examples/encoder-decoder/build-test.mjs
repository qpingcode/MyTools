import { build } from "esbuild";
import fs from "node:fs";
import path from "node:path";

fs.rmSync(path.resolve("test/dist"), { recursive: true, force: true });

await build({
  entryPoints: ["src/shared/index.ts"],
  bundle: true,
  platform: "node",
  format: "esm",
  target: "es2024",
  outfile: "test/dist/codec.mjs",
});
