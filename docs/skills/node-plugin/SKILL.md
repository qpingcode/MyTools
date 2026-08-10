---
name: node-plugin
description: Develop a MyTools Node plugin (backend + web detail page). Use whenever the user wants to create, scaffold, or edit a MyTools plugin that runs in the Node runtime with a WebView2 detail page. Covers project structure, referencing the common SDK, backend handlers, web detail page, i18n, and theming.
---

# MyTools Node Plugin 开发

MyTools Node 插件 = 一个运行在独立 Node 进程里的后端（处理搜索/动作/详情数据） + 一个由宿主用 WebView2 加载的 HTML 详情页。两者通过宿主中转通信。

参考实现：`MyTools.Plugins/Examples/` 下的 `hello-search`、`deepseek-chat`、`deepseek-translator`。

## 1. 目录结构

单 entry 插件（最简，照 `hello-search`）：

```text
my-plugin/
  plugin.json            # 清单（必填）
  package.json
  tsconfig.json
  build-plugin.mjs       # esbuild 打包脚本
  src/
    backend/
      index.mts          # 后端入口（.mts）
    web/
      index.html         # 详情页
      main.ts            # 详情页脚本
      style.css
  i18n/
    catalog.en-US.json   # 提取产物，随包发布（必填）
    locales/
      en-US.json         # 可选；通常与 defaultValue 等价
      zh-CN.json         # 可选；作者提供的人工翻译
```

多 entry 插件（每个 entry 各自的 backend + web，照 `deepseek-translator`）：

```text
my-plugin/
  plugin.json
  src/
    backend/
      Foo/index.mts
      Bar/index.mts
    web/
      Foo/{index.html, main.ts, style.css}
      Bar/{index.html, main.ts, style.css}
```

构建产物输出到 `dist/`，`plugin.json` 里的路径相对 `dist` 根（如 `backend/index.mjs`、`web/index.html`）。

## 2. 引用 common（SDK）

公共运行时已发布到 npmjs，包名 `@qping/plugin-common`。

`package.json`：

```json
{
  "name": "mytools-plugin-my-plugin",
  "version": "0.1.0",
  "private": true,
  "type": "commonjs",
  "scripts": {
    "clean": "rimraf dist",
    "build": "npm run check && node build-plugin.mjs",
    "check": "tsc -p tsconfig.json --noEmit"
  },
  "dependencies": {
    "@qping/plugin-common": "0.1.1"
  },
  "devDependencies": {
    "@types/node": "^26.1.1",
    "esbuild": "^0.25.8",
    "esbuild-plugin-copy": "^2.1.1",
    "rimraf": "^6.0.1",
    "typescript": "^7.0.2"
  }
}
```

`tsconfig.json` 关键项（`types` 必须包含 `@qping/plugin-common` 以加载全局类型）：

```json
{
  "compilerOptions": {
    "target": "ES2024",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "moduleDetection": "legacy",
    "lib": ["ES2024", "DOM"],
    "types": ["node", "@qping/plugin-common"],
    "strict": false,
    "noImplicitAny": true,
    "noEmitOnError": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true,
    "rootDir": ".",
    "outDir": "dist"
  },
  "include": ["src/web/**/*.ts", "src/backend/**/*.mts"],
  "exclude": ["dist"]
}
```

SDK 导出（来自 `@qping/plugin-common`）：

- `node-tool` → `createTool()`、`NodeTool`：后端用，链式注册 `initialize/search/action/handle`，`start()` 后从 stdin 读 NDJSON JSON-RPC。
- `web-tool` → `tool`：详情页用，提供 `tool.call / tool.subscribe / tool.events / tool.ready / tool.i18n / tool.theme`。
- `i18n` → `mytoolsI18n`：后端用（详情页用 `tool.i18n`）。
- `events` → `MyToolsEventSubjects`：host 消息 subject 常量。

## 3. plugin.json 清单

```json
{
  "id": "my-plugin",
  "name": "My Plugin",
  "version": "0.1.0",
  "runtime": "node",
  "protocolVersion": "2.0",
  "i18n": {
    "defaultLocale": "en-US",
    "catalog": "i18n/catalog.en-US.json",
    "localesPath": "i18n/locales",
    "supportedLocales": ["en-US", "zh-CN"]
  },
  "entries": [
    {
      "id": "my-plugin",
      "name": "My Plugin",
      "entry": "backend/index.mjs",
      "keywords": ["kw1", "kw2"],
      "hotKey": "Alt+V",
      "detail": { "type": "web", "entry": "web/index.html" }
    }
  ]
}
```

要点：
- `id` 稳定、不翻译、kebab-case，用作配置路径 `Plugins.{id}.*` 和 i18n scope `plugin:{id}`。
- 多 entry 时每个 entry 独立 `entry` + `detail.entry`。
- `hotKey`、`keywords` 可选。

## 4. 后端（Node）

```ts
import { createTool } from "@qping/plugin-common/node-tool";
import { mytoolsI18n } from "@qping/plugin-common/i18n";

const tool = createTool();

tool
  .initialize((params) => {
    mytoolsI18n.configure(params);   // 必做：接收宿主下发的 locale/messages
    return {};
  })
  .search((params) => ({
    items: [buildSearchItem(params.query || "")],
  }))
  .action((params) => ({
    message: "Opened",                // 动作执行后的提示
    actionType: "none",
    detail: {                         // detail 类型动作：打开详情页
      type: "web-detail",
      htmlEntry: "web/index.html",
      title: mytoolsI18n.t("Plugin.MyPlugin.Name", { defaultValue: "My Plugin" }),
      initialState: { query: params.query || "" },
    },
  }))
  .handle("refresh", (payload, context) => {   // 详情页通过 tool.call("refresh", ...) 触发
    return { /* 新状态 */ };
  })
  .start();
```

搜索结果 item 结构：

```ts
{
  id: "my-plugin:item-1",
  title: "...",
  subtitle: "...",
  priority: 100,
  icon: { kind: "emoji", value: "🌐" },
  actions: [
    { id: "open-detail", title: "Open", kind: "detail", description: "..." },
    { id: "copy", title: "Copy", kind: "copy" /* 宿主内置动作 */ },
  ],
}
```

`search` 返回 `{ items: [...] }`；`action` 返回 `{ message, actionType, detail? }`；`handle(name, fn)` 注册详情页可调用的方法，返回值作为详情页状态。`context` 提供 `action / itemId / query / locale / fallbackLocale`。

环境变量从 `process.env` 读（如 `process.env.MY_API_KEY`），不要假设配置文件。

## 5. 详情页（Web）

HTML：

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title data-i18n="[text]Plugin.MyPlugin.Name" data-i18n-default-value="My Plugin"></title>
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <h1 data-i18n="[text]Plugin.MyPlugin.Detail.Title" data-i18n-default-value="Detail"></h1>
  <button id="refresh" data-i18n="[text]Plugin.MyPlugin.Detail.Refresh" data-i18n-default-value="Refresh"></button>
  <pre id="state"></pre>
  <script src="main.js"></script>
</body>
</html>
```

main.ts：

```ts
import { tool } from "@qping/plugin-common/web-tool";

(function () {
  tool.subscribe(tool.events.host.initialize, (payload) => {
    render(payload.initialState);     // 宿主下发详情页初始状态
  });
  tool.subscribe(tool.events.host.search, (payload) => {
    // 搜索框变化转发到详情页
  });
  tool.subscribe(tool.events.host.languageChanged, () => {
    render(currentState);             // 语言切换后重新渲染
  });
  document.getElementById("refresh")!.addEventListener("click", async () => {
    render(await tool.call("refresh", { some: "param" }));  // 调后端 handler
  });
  tool.ready("my-plugin");
})();
```

宿主在 `initialize` 消息到达时会自动 `tool.i18n.configure()` + `tool.i18n.apply()`（应用所有 `data-i18n`）+ 应用主题 token，所以静态 DOM 文案用 `data-i18n` 即可，无需手写 `tool.i18n.t`。

通信方向：`详情页 tool.call/handle` → 宿主中转 → 后端 `tool.handle`；`后端 detail.state` → 宿主 → 详情页 `initialize`/订阅。

## 6. i18n（必做）

所有用户可见文本必须：稳定 key + 英文 `defaultValue`。key 用 PascalCase 层级，前缀 `Plugin.{PluginId}.*`。命名占位符用 `{{name}}`。

后端：

```ts
mytoolsI18n.t("Plugin.MyPlugin.Result.Greeting", { defaultValue: "Hello {{name}}", name });
```

详情页（动态文案）：

```ts
tool.i18n.t("Plugin.MyPlugin.Detail.Empty", { defaultValue: "No results" });
```

HTML 静态文案：`data-i18n="[attr]key" data-i18n-default-value="english text"`，`[attr]` 可省略（默认 `[text]`），或写 `[placeholder]`、`[title]`、`[aria-label]`。

禁止：动态拼接 key、省略 `defaultValue`、用英文文本本身当 key。

i18n 文件：

`i18n/locales/en-US.json`（扁平 key→文本）：

```json
{
  "Plugin.MyPlugin.Name": "My Plugin",
  "Plugin.MyPlugin.Result.Greeting": "Hello {{name}}"
}
```

`i18n/locales/zh-CN.json`（可选人工翻译）：

```json
{
  "Plugin.MyPlugin.Name": "我的插件",
  "Plugin.MyPlugin.Result.Greeting": "你好 {{name}}"
}
```

`i18n/catalog.en-US.json`（提取产物，每条至少有 `key/defaultValue/placeholders/references/sourceHash`；可从现有插件 catalog 复制结构后改）。`sourceHash` 是 `defaultValue` 的 sha256，改了 defaultValue 就重算。

解析优先级：人工翻译 > 插件 locales JSON > 英文 defaultValue > key。占位符翻译前后必须完全一致。作者不必提供所有语言，缺失语言由宿主兜底。

## 7. 主题（theme）

详情页 CSS 一律用宿主下发的 CSS 变量，**并写深色 fallback**：

```css
body {
  background: var(--mt-surface-bg, #141414);
  color: var(--mt-text, #f4f4f4);
}
.card {
  background: var(--mt-surface, rgba(255,255,255,0.06));
  border: 1px solid var(--mt-border-subtle, rgba(255,255,255,0.08));
}
.refresh-button {
  background: var(--mt-accent, #2f7cf6);
  color: var(--mt-text, #fff);
}
```

可用变量（均可省略用 fallback）：`--mt-surface-bg`、`--mt-surface`、`--mt-surface-alt`、`--mt-text`、`--mt-text-muted`、`--mt-text-disabled`、`--mt-border`/`--mt-border-subtle`、`--mt-accent`、`--mt-selection`。

规则：
- 禁止写死颜色字面量，必须走 `var(--mt-..., #fallback)`。
- fallback 取深色，保证脱离宿主单独调试时也可读。
- 首帧由宿主在 HTML 解析前注入引导脚本设置变量，无需插件处理闪烁。
- 主题热切换由 `tool.theme` 自动处理；若 JS 需要按主题变图标/配色，订阅 `tool.events.host.themeChanged`，不要自己解析 CSS 变量做关键逻辑。
- 后端 RPC 请求带 `theme` 字段，但多数插件可忽略；仅当「按主题返回不同数据」时才读。

## 8. build-plugin.mjs

打包两段：backend（`platform: "node"`, `format: "esm"`, 输出 `.mjs`），web（`format: "iife"`），并用 `esbuild-plugin-copy` 把 `plugin.json`、html、css、`i18n/**/*` 复制到 `dist/`。多 entry 时 `entryPoints` 传数组、`outbase: "src/backend"`（或 `src/web`）保持输出子目录结构。完整脚本直接照 `hello-search/build-plugin.mjs` 或 `deepseek-translator/build-plugin.mjs`。

构建：`npm run build`（先 `tsc --noEmit` 检查，再 esbuild 打包到 `dist/`）。交付/安装时指向 `dist/`。
