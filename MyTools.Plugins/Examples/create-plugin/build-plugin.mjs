import { build, context } from "esbuild";
import fs from "node:fs";
import path from "node:path";

const watching = process.argv.includes("--watch");
const copyStatic = () => {
  fs.mkdirSync("dist/web", { recursive: true });
  fs.copyFileSync("plugin.json", "dist/plugin.json");
  fs.copyFileSync("src/web/index.html", "dist/web/index.html");
  fs.copyFileSync("src/web/style.css", "dist/web/style.css");
  fs.cpSync("i18n", "dist/i18n", { recursive: true });
  fs.cpSync("src/templates", "dist/templates", { recursive: true });
};
const builds = [
  { entryPoints: ["src/backend/index.mts"], bundle: true, platform: "node", format: "esm", target: "es2024", outfile: "dist/backend/index.mjs" },
  { entryPoints: ["src/web/main.ts"], bundle: true, format: "iife", target: "es2024", outfile: "dist/web/main.js" }
];

if (!watching) {
  fs.rmSync(path.resolve("dist"), { recursive: true, force: true });
  await Promise.all(builds.map((options) => build(options)));
  copyStatic();
} else {
  copyStatic();
  const contexts = await Promise.all(builds.map((options) => context(options)));
  await Promise.all(contexts.map((item) => item.watch()));
  fs.watch("plugin.json", copyStatic);
  fs.watch("i18n", { recursive: true }, copyStatic);
  fs.watch("src/templates", { recursive: true }, copyStatic);
  fs.watch("src/web", { recursive: true }, (_event, file) => {
    if (file === "index.html" || file === "style.css") copyStatic();
  });
  console.log("Watching create-plugin sources...");
}
