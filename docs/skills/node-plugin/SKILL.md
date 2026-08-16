---
name: node-plugin
description: Develop a MyTools Node plugin (backend + optional WebView2 detail page) on the v3 named-pipe message bus. Use whenever the user wants to create, scaffold, or edit a MyTools plugin. Covers plugin.json, @qping/plugin-bus, backend handlers, list or web UI, host.call capabilities, i18n, and theming.
---

# MyTools Node Plugin 开发

MyTools Node 插件 = 独立 Node 进程里的后端 + 可选的 WebView2 HTML 详情页。不写 `detail` 时宿主用 `search` 结果走原生列表。通信走 v3 消息总线（Named Pipe + WebView2 postMessage），协议版本 **3.0**。

参考实现：`MyTools.Plugins/Examples/` 下的 `hello-search`（最小）、`json-formatter` / `xml-formatter`（页面自包含）、`settings`（`hostCall`）、`deepseek-chat`、`deepseek-translator`（多 entry）。SDK 源码：`MyTools.Plugins/Examples/sdk-v3`（包名 `@qping/plugin-bus`）。

## 1. 目录结构

单 entry（照 `hello-search`）：

```text
my-plugin/
  plugin.json
  package.json
  tsconfig.json
  build-plugin.mjs
  src/
    backend/index.mts
    web/{index.html, main.ts, style.css}
  i18n/
    catalog.en-US.json
    locales/{en-US.json, zh-CN.json}
```

多 entry（照 `deepseek-translator`）：每个 entry 各自 `src/backend/<Id>/index.mts` 和 `src/web/<Id>/{index.html, main.ts, style.css}`。

构建输出到 `dist/`。`plugin.json` 里的路径相对 `dist` 根（如 `backend/index.mjs`、`web/index.html`）。

## 2. SDK：`@qping/plugin-bus`

仓库内示例通过 `MyTools.Plugins/Examples` workspace 引用本地 `sdk-v3`。`package.json`：

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
    "@qping/plugin-bus": "0.2.0"
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

`tsconfig.json`（`types` 只需 `node`）：

```json
{
  "compilerOptions": {
    "target": "ES2024",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "moduleDetection": "legacy",
    "lib": ["ES2024", "DOM"],
    "types": ["node"],
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

导出：


| import                   | 用途                                                                           |
| ------------------------ | ---------------------------------------------------------------------------- |
| `@qping/plugin-bus/node` | `createPlugin()`、`PluginInitializeParams` / `PluginSearchParams` / `PluginActionParams` |
| `@qping/plugin-bus/web`  | `createWebBusClient()`、`HostEvents`、payload 类型                               |
| `@qping/plugin-bus/i18n` | 后端 `mytoolsI18n`（页面用 `bus.i18n`）                                             |


SDK 把旧式方法名映射到 v3 路由：`initialize` → `plugin.call.initialize`，`search` → `plugin.call.search`，`action` → `plugin.call.invokeAction`，`handle("foo")` → `plugin.call.foo`，`publish` → `plugin.event.*`，`hostCall` → `host.call.*`。

## 3. plugin.json

```json
{
  "id": "my-plugin",
  "version": "0.1.0",
  "protocolVersion": "3.0",
  "i18n": {
    "defaultLocale": "en-US",
    "catalog": "i18n/catalog.en-US.json",
    "localesPath": "i18n/locales",
    "supportedLocales": ["en-US", "zh-CN"]
  },
  "entries": [
    {
      "id": "my-plugin",
      "name": {
        "key": "Plugin.MyPlugin.Name",
        "defaultValue": "My Plugin"
      },
      "entry": "backend/index.mjs",
      "capabilities": [],
      "keywords": ["kw1"],
      "search": { "global": false },
      "hotKey": "Alt+V",
      "detail": { "type": "web", "entry": "web/index.html" }
    }
  ]
}
```

要点：

- `protocolVersion` 必须是 `"3.0"`，否则宿主拒绝加载。
- `id` 稳定、kebab-case；配置路径 `Plugins.{id}.*`，i18n scope `plugin:{id}`。
- 每个 entry 的 `name` 是 `{ key, defaultValue }`，不是字符串。
- `capabilities` 必填（可 `[]`）。只有声明过的能力才能 `hostCall`；`settings` 声明 `"configuration.write"`。
- `detail` 可选。省略（或 `"detail": { "type": "list" }`）时宿主用 `search` 的结果走原生列表：关键词路由停留在列表，热键打开搜索主窗口并锁定该插件。
- 需要自定义页面时再写 `"detail": { "type": "web", "entry": "web/index.html" }`。`hotKey`、`keywords`、`search` 可选。
- `search.global`：出现在**无关键词**的全局搜索结果中。省略或 `false` 时不参与全局搜索（opt-in，避免设置类插件污染每次搜索）。用户可在设置 → 插件列表的 **全局结果** 中覆盖此项。
- 有非空 `keywords` 就会注册 `keyword + 查询串` 的插件级搜索；没有 keywords 则只能靠全局搜索或热键进入。只有全局、没有关键词时（如 `hello-search`）必须设 `"search": { "global": true }`。
- 没有顶层 `name` / `runtime`；旧的单 entry（无 `entries[]`）清单会被跳过。

## 4. 后端（Node）

```ts
import { createPlugin, type PluginInitializeParams, type PluginSearchParams, type PluginActionParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const plugin = createPlugin();

plugin
  .initialize((params: PluginInitializeParams) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((params: PluginSearchParams) => ({
    items: [buildSearchItem(params.query)],
  }))
  .action((params: PluginActionParams) => ({
    message: mytoolsI18n.t("Plugin.MyPlugin.Action.Open.Success", {
      defaultValue: "Opened",
    }),
    actionType: "none",
    detail: {
      type: "web-detail",
      htmlEntry: "web/index.html",
      title: mytoolsI18n.t("Plugin.MyPlugin.Name", { defaultValue: "My Plugin" }),
      initialState: { query: params.query },
    },
  }))
  .handle("refresh", (payload, context) => {
    return { query: context.query, payload };
  })
  .start();
```

搜索 item：

```ts
{
  id: "my-plugin:item-1",
  title: "...",
  subtitle: "...",
  priority: 100,
  icon: { kind: "emoji", value: "🌐" },
  actions: [
    { id: "open-detail", title: "Open", kind: "detail", description: "..." },
  ],
}
```

- `initialize` 的 params 是 `{ locale, fallbackLocale, messages, theme }`（`PluginInitializeParams`）。`theme` 为 `"light"` | `"dark"`，不含 CSS token。`mytoolsI18n.configure(params)` 装进 i18n。
- `search` 的 params 是 `{ query, mode, locale, fallbackLocale, theme }`（`PluginSearchParams`）。`mode` 为 `"global"` | `"plugin"`：全局搜索（无关键词）为 `"global"`，用户输入 `keyword + 查询串` 进入该插件时为 `"plugin"`。返回 `{ items }`。
- `action` 的 params 是 `{ itemId, actionId, query, locale, fallbackLocale, theme }`（`PluginActionParams`）。返回 `{ message, actionType, detail? }`。
- `handle(name, fn)` 给详情页 `bus.call(name)` 用。`context` 有 `action / itemId / query / locale / fallbackLocale / theme`（由宿主注入，页面不必带）。
- 纯前端工具（如 json-formatter）可以只有 `initialize/search/action`，不注册 `handle`。
- 环境变量从 `process.env` 读。`start()` 连宿主 Named Pipe，不要自己读 stdin。

需要宿主能力时（照 `settings`）：页面 `bus.call("getConfiguration")` → 后端 `handle` → `plugin.hostCall("getConfiguration")`。页面不能直接发 `host.call.*`。manifest 必须声明对应 capability（`getConfiguration` 等旧方法名折到 `configuration.write`）。

`plugin.hostCall(method, params?, timeoutMs?)`：未传 `timeoutMs` 时，若当前正在处理页面的 `bus.call`，使用该请求的**剩余超时**；否则默认 30 秒。显式传入时不会超过剩余时间。

`plugin.publish("subject", payload)` 发 `plugin.event.subject` 给同会话其他 WebView；当前示例未使用。

## 5. 详情页（Web）

模块加载时立刻 `createWebBusClient()`，握手在后台进行；`bus.call()` 会等握手完成。不要等 DOM/`initialize` 再创建 client。

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

```ts
import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";

(function () {
  const bus = createWebBusClient();
  let currentState: unknown = {};

  bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, (payload) => {
    currentState = payload.initialState || {};
    render(currentState);
  });
  bus.on<MyToolsHostSearchPayload>(HostEvents.Search, (payload) => {
    // 宿主搜索框变化
  });
  bus.on(HostEvents.LanguageChanged, () => {
    render(currentState);
  });

  document.getElementById("refresh")!.addEventListener("click", async () => {
    render(await bus.call("refresh", { some: "param" }));
  });
})();
```

`HostEvents`：`initialize` / `search` / `key` / `languageChanged` / `themeChanged`（完整路由 `host.event.*`）。设置页热键/鼠标捕获用长超时 `bus.call("captureInputAction")`，等宿主窗口确认或取消后在 Response 里返回结果。

`bus.on(route, handler)` 按路由订阅，晚订阅会重放该路由最后一次事件。不要暴露/使用 catch-all listener。

宿主在 `initialize` / `languageChanged` / `themeChanged` 到达时自动 `bus.i18n.configure` + `apply`（所有 `data-i18n`）以及主题 token。静态文案用 `data-i18n`；动态文案用 `bus.i18n.t`。

多文件页面（`settings`）可把 `export const bus = createWebBusClient()` 放到单独模块，保证只创建一次。

通信：


| 方向              | 路由                                                                   |
| --------------- | -------------------------------------------------------------------- |
| 页面 → 后端         | `bus.call("foo")` → `plugin.call.foo`                                |
| 宿主 → 页面         | `host.event.initialize/search/key/languageChanged/themeChanged` |
| 后端 → 宿主能力       | `plugin.hostCall("getConfiguration")` → `host.call.getConfiguration` |
| 后端 → 其他 WebView | `plugin.publish("x")` → `plugin.event.x`                             |


页面只能发 `plugin.call.*`。握手失败时宿主显示页内错误，不会下发 `initialize`。

## 6. i18n（必做）

用户可见文本：稳定 key + 英文 `defaultValue`。key 用 PascalCase，前缀 `Plugin.{PluginId}.*`。占位符 `{{name}}`。

后端：`mytoolsI18n.t("Plugin.MyPlugin.Result.Greeting", { defaultValue: "Hello {{name}}", name })`  
页面动态：`bus.i18n.t("Plugin.MyPlugin.Detail.Empty", { defaultValue: "No results" })`  
HTML：`data-i18n="[attr]key" data-i18n-default-value="english text"`。`[attr]` 可省略（默认 `[text]`），或 `[placeholder]` / `[title]` / `[aria-label]`。

禁止：动态拼接 key、省略 `defaultValue`、用英文当 key。

`i18n/locales/en-US.json` 扁平 `key → 文本`；`zh-CN.json` 可选人工翻译。`i18n/catalog.en-US.json` 是提取产物，每条至少有 `key/defaultValue/placeholders/references/sourceHash`（可从现有插件 catalog 复制后改）。`sourceHash` 是 `defaultValue` 的 sha256。

解析：人工翻译 > locales JSON > 英文 defaultValue > key。占位符翻译前后必须一致。缺失语言由宿主兜底。

## 7. 主题

CSS 一律用宿主变量，并写深色 fallback：

```css
body {
  background: var(--mt-surface-bg, #141414);
  color: var(--mt-text, #f4f4f4);
}
.card {
  background: var(--mt-surface, rgba(255,255,255,0.06));
  border: 1px solid var(--mt-border-subtle, rgba(255,255,255,0.08));
}
button {
  background: var(--mt-accent, #3F51B5);
  color: var(--mt-accent-foreground, #fff);
}
```

变量：`--mt-surface-bg` / `--mt-surface` / `--mt-surface-alt` / `--mt-surface-hover`、`--mt-text` / `--mt-text-muted` / `--mt-text-tertiary` / `--mt-text-disabled`、`--mt-border` / `--mt-border-subtle`、`--mt-accent` / `--mt-accent-hover` / `--mt-accent-pressed` / `--mt-accent-foreground`、`--mt-selection`、`--mt-shadow`。

- 禁止写死颜色；必须 `var(--mt-..., #fallback)`，fallback 取深色。
- 首帧由宿主注入变量，插件不用处理闪烁。
- 主题热切换由 `bus.theme` 自动处理。JS 若要按主题换图标，订阅 `HostEvents.ThemeChanged`，不要自己读 CSS 变量做关键逻辑。

## 8. 构建

`build-plugin.mjs`：backend（`platform: "node"`, `format: "esm"`, 输出 `.mjs`），web（`format: "iife"`），`esbuild-plugin-copy` 把 `plugin.json`、html、css、`i18n/**/*` 拷到 `dist/`。多 entry 时 `entryPoints` 传数组，`outbase: "src/backend"`（或 `src/web`）。完整脚本照 `hello-search/build-plugin.mjs` 或 `deepseek-translator/build-plugin.mjs`。

构建：`npm run build`（先 `tsc --noEmit`，再打包到 `dist/`）。安装指向 `dist/`。在 Examples workspace 里先 `npm run build -w @qping/plugin-bus`。