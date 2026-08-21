import { createPlugin } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";
import { createHash } from "node:crypto";

type CreateInput = { name: string; pluginId: string; author: string; pluginType: "standard" | "custom-ui" };

function pluginKey(input: CreateInput) {
  return input.pluginId.split(/[^a-z0-9]+/i).filter(Boolean)
    .map((part) => part[0].toUpperCase() + part.slice(1)).join("");
}

function sourceHash(defaultValue: string) {
  return `sha256:${createHash("sha256").update(defaultValue).digest("hex")}`;
}

function escapeHtmlAttribute(value: string) {
  return value.replaceAll("&", "&amp;").replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}

function packageJson(input: CreateInput) {
  return JSON.stringify({
    name: `mytools-plugin-${input.pluginId}`,
    version: "0.1.0",
    private: true,
    author: input.author,
    type: "module",
    scripts: { build: "node build-plugin.mjs", watch: "node build-plugin.mjs --watch", check: "tsc -p tsconfig.json --noEmit" },
    dependencies: { "@qping/plugin-bus": "0.2.0" },
    devDependencies: { "@types/node": "^26.1.1", esbuild: "^0.25.8", typescript: "^7.0.2" }
  }, null, 2) + "\n";
}

function manifest(input: CreateInput) {
  const keyRoot = `Plugin.${pluginKey(input)}`;
  const entry: Record<string, unknown> = {
    id: "main",
    name: { key: `${keyRoot}.Name`, defaultValue: input.name },
    entry: "backend/index.mjs",
    capabilities: [],
    alias: [input.pluginId],
    search: { global: false }
  };
  if (input.pluginType === "custom-ui") entry.detail = { type: "web", entry: "web/index.html" };
  return JSON.stringify({
    id: input.pluginId,
    version: "0.1.0",
    protocolVersion: "3.0",
    icon: "mdi-puzzle-outline",
    i18n: {
      defaultLocale: "en-US",
      catalog: "i18n/catalog.en-US.json",
      localesPath: "i18n/locales",
      supportedLocales: ["en-US", "zh-CN"]
    },
    entries: [entry]
  }, null, 2) + "\n";
}

const tsconfig = JSON.stringify({ compilerOptions: {
  target: "ES2024", module: "NodeNext", moduleResolution: "NodeNext", lib: ["ES2024", "DOM"],
  types: ["node"], strict: true, noEmit: true, skipLibCheck: true
}, include: ["src/**/*.ts", "src/**/*.mts"] }, null, 2) + "\n";

function buildScript(input: CreateInput) {
  const customUi = input.pluginType === "custom-ui";
  const webBuild = customUi
    ? ',\n  { entryPoints: ["src/web/main.ts"], bundle: true, format: "iife", target: "es2024", outfile: "dist/web/main.js", plugins: [refreshPlugin] }'
    : "";
  const webCopy = customUi
    ? '\n  fs.mkdirSync("dist/web", { recursive: true });\n  fs.copyFileSync("src/web/index.html", "dist/web/index.html");\n  fs.copyFileSync("src/web/style.css", "dist/web/style.css");'
    : "";
  const webWatch = customUi
    ? '\n  fs.watch("src/web", { recursive: true }, (_event, file) => { if (file === "index.html" || file === "style.css") { copyStatic(); requestMyToolsRefresh(); } });'
    : "";
  return `import { build, context } from "esbuild";
import fs from "node:fs";
import path from "node:path";
import net from "node:net";
const watching = process.argv.includes("--watch");
const refreshPipe = ${JSON.stringify("\\\\.\\pipe\\MyTools.DevelopmentPlugins.Refresh")};
const pluginId = ${JSON.stringify(input.pluginId)};
function requestMyToolsRefresh(attempt = 0) {
  const socket = net.createConnection(refreshPipe);
  socket.once("connect", () => socket.end(pluginId + "\\n"));
  socket.once("error", (error) => {
    socket.destroy();
    if (attempt === 0) {
      setTimeout(() => requestMyToolsRefresh(1), 250);
      return;
    }
    console.warn("[MyTools] Failed to request refresh for " + pluginId
      + " after one retry: " + error.message);
  });
}
const refreshPlugin = {
  name: "refresh-mytools",
  setup(build) {
    build.onEnd((result) => {
      if (watching && result.errors.length === 0) requestMyToolsRefresh();
    });
  }
};
const builds = [
  { entryPoints: ["src/backend/index.mts"], bundle: true, platform: "node", format: "esm", target: "es2024", outfile: "dist/backend/index.mjs", plugins: [refreshPlugin] }${webBuild}
];
function copyStatic() {
  fs.mkdirSync("dist", { recursive: true });
  fs.copyFileSync("plugin.json", "dist/plugin.json");
  fs.cpSync("i18n", "dist/i18n", { recursive: true });${webCopy}
}
if (!watching) {
  fs.rmSync(path.resolve("dist"), { recursive: true, force: true });
  await Promise.all(builds.map((options) => build(options)));
  copyStatic();
} else {
  copyStatic();
  const contexts = await Promise.all(builds.map((options) => context(options)));
  await Promise.all(contexts.map((item) => item.watch()));
  fs.watch("plugin.json", () => { copyStatic(); requestMyToolsRefresh(); });
  fs.watch("i18n", { recursive: true }, () => { copyStatic(); requestMyToolsRefresh(); });${webWatch}
  console.log("Watching plugin sources...");
}
`;
}

function backend(input: CreateInput) {
  const keyRoot = `Plugin.${pluginKey(input)}`;
  const detail = input.pluginType === "custom-ui"
    ? `,\n    detail: { type: "web-detail", htmlEntry: "web/index.html", title: mytoolsI18n.t("${keyRoot}.Name", { defaultValue: ${JSON.stringify(input.name)} }), initialState: { query: text } }`
    : "";
  return `import { createPlugin } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";
const plugin = createPlugin();
plugin
  .initialize((params) => { mytoolsI18n.configure(params); return {}; })
  .search((params) => {
    const text = String(params.query || "").trim();
    return { items: [{
      id: "${input.pluginId}:result",
      title: text || mytoolsI18n.t("${keyRoot}.Name", { defaultValue: ${JSON.stringify(input.name)} }),
      subtitle: mytoolsI18n.t("${keyRoot}.Result.Subtitle", { defaultValue: "Generated by MyTools Create Plugin" }),
      icon: { kind: "mdi", value: "mdi-puzzle-outline" },
      actions: []${detail}
    }] };
  })
  .start();
`;
}

function i18nFiles(input: CreateInput): Record<string, string> {
  const keyRoot = `Plugin.${pluginKey(input)}`;
  const entries = [
    { key: `${keyRoot}.Name`, defaultValue: input.name, zh: input.name, filePath: "plugin.json" },
    { key: `${keyRoot}.Result.Subtitle`, defaultValue: "Generated by MyTools Create Plugin", zh: "由 MyTools 创建插件生成", filePath: "src/backend/index.mts" },
    ...(input.pluginType === "custom-ui" ? [
      { key: `${keyRoot}.Detail.Title`, defaultValue: input.name, zh: input.name, filePath: "src/web/index.html" },
      { key: `${keyRoot}.Detail.Query`, defaultValue: "Current query", zh: "当前查询", filePath: "src/web/index.html" },
      { key: `${keyRoot}.Detail.Ready`, defaultValue: "Ready", zh: "就绪", filePath: "src/web/main.ts" }
    ] : [])
  ];
  const locale = (field: "defaultValue" | "zh") => JSON.stringify(
    Object.fromEntries(entries.map((entry) => [entry.key, entry[field]])), null, 2) + "\n";
  const catalog = {
    schemaVersion: 1,
    scope: `plugin:${input.pluginId}`,
    pluginId: input.pluginId,
    sourceLocale: "en-US",
    entries: entries.map((entry) => ({
      key: entry.key,
      defaultValue: entry.defaultValue,
      placeholders: [],
      references: [{ filePath: entry.filePath, line: 1, column: 1 }],
      existingTranslations: { "zh-CN": entry.zh },
      sourceHash: sourceHash(entry.defaultValue)
    }))
  };
  return {
    "i18n/locales/en-US.json": locale("defaultValue"),
    "i18n/locales/zh-CN.json": locale("zh"),
    "i18n/catalog.en-US.json": JSON.stringify(catalog, null, 2) + "\n"
  };
}

function filesFor(input: CreateInput): Record<string, string> {
  const files: Record<string, string> = {
    "package.json": packageJson(input),
    "plugin.json": manifest(input),
    "tsconfig.json": tsconfig,
    "build-plugin.mjs": buildScript(input),
    "src/backend/index.mts": backend(input),
    ...i18nFiles(input)
  };
  if (input.pluginType === "custom-ui") {
    const keyRoot = `Plugin.${pluginKey(input)}`;
    const nameAttribute = escapeHtmlAttribute(input.name);
    files["src/web/index.html"] = `<!doctype html>
<html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<link rel="stylesheet" href="./style.css"><title data-i18n="[text]${keyRoot}.Name" data-i18n-default-value="${nameAttribute}"></title></head>
<body><main><h1 data-i18n="[text]${keyRoot}.Detail.Title" data-i18n-default-value="${nameAttribute}"></h1>
<p class="label" data-i18n="[text]${keyRoot}.Detail.Query" data-i18n-default-value="Current query"></p><p id="query"></p></main><script src="./main.js"></script></body></html>\n`;
    files["src/web/style.css"] = `body{margin:0;font-family:"Segoe UI",sans-serif;background:var(--mt-surface-bg,#141414);color:var(--mt-text,#f4f4f4)}main{margin:24px;padding:28px;border:1px solid var(--mt-border-subtle,rgba(255,255,255,.08));border-radius:14px;background:var(--mt-surface,rgba(255,255,255,.06));box-shadow:0 12px 32px var(--mt-shadow,rgba(0,0,0,.28))}h1{margin:0 0 20px;font-size:28px}.label{margin:0 0 6px;color:var(--mt-text-muted,#c4c9d4);font-size:12px;text-transform:uppercase;letter-spacing:.08em}#query{margin:0;padding:12px;border-radius:8px;background:var(--mt-surface-alt,#202020);color:var(--mt-text,#f4f4f4)}\n`;
    files["src/web/main.ts"] = `import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
const bus=createWebBusClient();
let query="";
function render(){document.getElementById("query")!.textContent=query||bus.i18n.t("${keyRoot}.Detail.Ready",{defaultValue:"Ready"});}
bus.on(HostEvents.Initialize,(payload:any)=>{query=String(payload?.initialState?.query||"");render();});
bus.on(HostEvents.LanguageChanged,render);\n`;
  }
  return files;
}

const plugin = createPlugin();
plugin
  .initialize((params) => { mytoolsI18n.configure(params); return {}; })
  .handle("createPlugin", async (payload: CreateInput) => plugin.hostCall("development.create", { ...payload, files: filesFor(payload) }))
  .handle("listDevelopmentPlugins", async () => plugin.hostCall("development.list"))
  .handle("refreshDevelopmentPlugins", async () => plugin.hostCall("development.refresh"))
  .handle("openFolder", async (payload: { sourcePath: string }) => plugin.hostCall("development.openFolder", payload))
  .handle("openCode", async (payload: { sourcePath: string }) => plugin.hostCall("development.openCode", payload))
  .start();
