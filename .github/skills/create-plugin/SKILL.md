---
name: create-plugin
description: Develop a MyTools Node plugin (backend + optional WebView2 detail page) on the v3 named-pipe message bus. Use whenever the user wants to create, scaffold, or edit a MyTools plugin. Covers plugin.json, @qping/plugin-bus, backend handlers, list or web UI, host.call capabilities, i18n, and theming.
---

# MyTools Node Plugin 开发

MyTools Node 插件 = 独立 Node 进程里的后端 + 可选的 WebView2 HTML 详情页。不写 `detail` 时宿主用 `search` 结果走原生列表。通信走 v3 消息总线（Named Pipe + WebView2 postMessage），协议版本 **3.0**。

随 MyTools 安装的参考包位于 [`references/Examples/`](references/Examples/)；
它是当前版本的权威源码参考，不依赖用户安装目录之外的仓库。
常用入口：
- [`calculator`](references/Examples/calculator/)（简单标准插件）
- [`formatter`](references/Examples/formatter/)（页面自包含）
- [`settings`](references/Examples/settings/)（`hostCall`）
- [`quick-text`](references/Examples/quick-text/) / [`scripts-runner`](references/Examples/scripts-runner/)（`plugin.json` configuration + `configuration.readOwn`）
- [`chat`](references/Examples/chat/)
- [`translator`](references/Examples/translator/)。

创建或编辑前只读取与需求最相关的最小参考集合。
创建模式下选择唯一的插件 ID 和名称，从完整目录结构开始生成。编辑模式下，先读取 Host 提供的
`selectedPlugin`（ID、名称、类型和源码目录）并检查现有文件，只能修改该插件目录；保持插件 ID，
保留与用户需求无关的行为和配置。无论创建还是编辑，完成前都要重新检查 manifest、i18n、图标、
构建脚本以及 `build` / `watch` / `check`，最后调用 `complete_plugin`。

Create Plugin 的“打开目录”和“用 VS Code 打开”必须通过 Host capability（分别为
`development.openFolder` / `development.openCode`）完成；AI 和插件页面不能直接启动
`explorer.exe`、`code`、`Code.exe` 或其他本地进程。

## 1. 目录结构

（照 [`calculator`](references/Examples/calculator/)）：

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

构建输出到 `dist/`。`plugin.json` 里的路径相对 `dist` 根（如 `backend/index.mjs`、`web/index.html`）。

## 2. SDK：`@qping/plugin-bus`

所有示例和 AI 创建到用户 coding 目录的插件都必须从 npmjs 安装已发布的 `@qping/plugin-bus`，不得使用仓库中的 `sdk-v3` workspace、`file:` 路径或其他本地 SDK 引用。`package.json`：

```json
{
  "name": "mytools-plugin-my-plugin",
  "version": "0.1.0",
  "private": true,
  "type": "commonjs",
  "scripts": {
    "clean": "rimraf dist",
    "build": "npm run check && node build-plugin.mjs",
    "watch": "node build-plugin.mjs --watch",
    "check": "tsc -p tsconfig.json --noEmit"
  },
  "dependencies": {
    "@qping/plugin-bus": "0.8.0"
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
| `@qping/plugin-bus/node` | `createPlugin()`、action registry、`HostAction`、`Key`、`Modifiers` |
| `@qping/plugin-bus/web`  | `createWebBusClient()`、`HostEvents`、payload 类型                               |
| `@qping/plugin-bus/i18n` | 后端 `mytoolsI18n`（页面用 `bus.i18n`）                                             |
| `@qping/plugin-bus/dev`  | 构建脚本用 `requestDevelopmentPluginRefresh()` 通知 MyTools 刷新开发插件             |


SDK 把方法映射到 v3 路由：`initialize` → `plugin.call.initialize`，`search` → `plugin.call.search`，注册 action 的 `execute` → `plugin.call.invokeAction`，`handle("foo")` → `plugin.call.foo`，`publish` → `plugin.event.*`，`hostCall` → `host.call.*`。

## 3. plugin.json

```json
{
  "id": "my-plugin",
  "name": {
    "key": "Plugin.MyPlugin.Name",
    "defaultValue": "My Plugin"
  },
  "version": "0.1.0",
  "protocolVersion": "3.0",
  "icon": "mdi-star-outline",
  "i18n": {
    "defaultLocale": "en-US",
    "catalog": "i18n/catalog.en-US.json",
    "localesPath": "i18n/locales",
    "supportedLocales": ["en-US", "zh-CN"]
  },
  "entry": "backend/index.mjs",
  "capabilities": [],
  "alias": ["kw1"],
  "search": { "global": false },
  "hotKey": "Alt+V",
  "detail": { "type": "web", "entry": "web/index.html" }
}
```

要点：

- `version` 必须是符合 [semver](https://semver.org/) 的字符串，例如 `"0.1.0"`。 会展示到界面, 用户可以看到
- `protocolVersion` 必须是 `"3.0"`，否则宿主拒绝加载。
- `id` 稳定、kebab-case；配置路径 `Plugins.{id}.*`，i18n scope `plugin:{id}`。
- 根节点的 `name` 是 `{ key, defaultValue }`，不是字符串。
- `capabilities` 必填（可 `[]`）。只有声明过的能力才能 `hostCall`；调用的方法名必须与声明的 capability 完全一致，例如读取配置使用 `plugin.hostCall("configuration.read")` 并声明 `"configuration.read"`。
- 顶层 `icon` 是 Settings 侧栏图标（Material Design Icons 类名，如 `"mdi-message-text-outline"`）。省略时用默认齿轮变体图标。
- 插件设置写在顶层 `configuration`。宿主启动时按 schema 注册到 Settings 侧栏，分类名为插件显示名，设置完整路径为 `{pluginId}.{key}`（例如 `quick-text.Phrases`）。`key` 也可以写成 `quick-text.Phrases` 或 `Plugins.QuickText.Phrases`，宿主会去掉前缀。
- 详情页标题不要复用根节点 `name`。有 `configuration` 时第一项写成展示用 `h1`（`label` / `description` 可选），宿主不保存、不读取。`h2` 是小标题。`label` / `description` 在所有类型上都是可选的：table 有则显示为与其它项相同字号的二级标题，没有则不显示；其它类型有则显示在左侧，没有则左侧留白。
- `type`：`string` / `bool` / `int` / `double` / `array` / `path` / `h1` / `h2`。`uiHint` 可选：string 默认 `input`（也可 `textarea` / `email` / `telephone`）；bool 默认 `checkbox`（也可 `radio` / `select`）；int/double 默认 `input-number`；array 默认 `table`，必须带 `schema.properties`。`path` 默认 `fileOrDirectory`（也可 `file` / `directory`），Settings 显示文件/目录选择器。`h1` / `h2` 没有 `key` / `defaultValue` / `schema`。列 `type: "hidden"` 表格和编辑框都不显示（时间戳等）。列 `"table": false` 只在编辑对话框出现，不出现在表格。默认值支持宏 `${DateTime.Now}`（新增行时解析）。`visibility` 可选，条件宏，用当前插件其他 configuration 的 `key` 写成表达式，例如 `"visibility": "${ChromeEnabled == true}"`。支持 `==` / `!=` / `&&` / `||` / 括号；条件为真时才在 Settings 里显示该项。省略则始终显示。
- 需要读自己的设置时声明 `configuration.readOwn`。需要写入自己的设置时再声明 `configuration.writeOwn`。不要用 `configuration.read` / `configuration.write`（那是 settings 插件读写全部设置）。Open Path、Snippet、Command Runner、Search Engine 都走 `readOwn`。
- `detail` 可选。省略（或 `"detail": { "type": "list" }`）时宿主用 `search` 的结果走原生列表：关键词路由停留在列表，热键打开搜索主窗口并锁定该插件。
- 需要自定义页面时再写 `"detail": { "type": "web", "entry": "web/index.html" }`。`hotKey`、`alias`、`search` 可选。
- action 不写在 manifest。详情页默认使用本插件通过 `plugin.actions()` 注册的全部 action；列表 item 用 action id 子集引用。
- 宿主 action bar 默认只露出一项（Enter，或没有 Enter 时的第一项），其余收进 **Actions**。需要同时露出多项时，在对应 action 上写 `pinned: true`。C# 侧可用 `WithPinned()`。
- `search.global`：出现在**无关键词**的全局搜索结果中。省略或 `false` 时不参与全局搜索（opt-in，避免设置类插件污染每次搜索）。用户可在设置 → 插件列表的 **全局结果** 中覆盖此项。
- 有非空 `alias` 就会注册 `alias + 查询串` 的插件级搜索；没有 alias 则只能靠全局搜索或热键进入。只有全局、没有别名时必须设 `"search": { "global": true }`。
- 没有顶层 `runtime`；
- AI自动创建时 alias最多只设置一个

## 4. 后端（Node）

```ts
import { createPlugin, HostAction, Key, Modifiers, type PluginInitializeParams, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const plugin = createPlugin();

plugin
  .initialize((params: PluginInitializeParams) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<{ content: string }>([{
    id: "copy",
    title: { key: "Plugin.MyPlugin.Action.Copy", defaultValue: "Copy" },
    hotkey: { key: Key.C, modifiers: Modifiers.Control | Modifiers.Shift },
    execute: ({ item }) => ({
      target: {
        kind: "host",
        action: { kind: HostAction.Copy, text: item?.content ?? "" },
      },
      after: "close",
    }),
  }])
  .search((params: PluginSearchParams) => ({
    items: [{ ...buildSearchItem(params.query), content: "...", actions: ["copy"] }],
  }))
  .handle("refresh", (payload, context) => {
    return { query: context.query, payload };
  })
  .start();
```

搜索 item：

```json
{
  id: "my-plugin:item-1",
  title: "...",
  subtitle: "...",
  priority: 100,
  icon: { kind: "mdi", value: "mdi-hand-wave-outline" },
  content: "插件自己的业务字段，宿主不可见",
  actions: ["copy"]
}
```

- `icon.kind`：`emoji` 显示 emoji；`mdi`（或 `value` 以 `mdi-` 开头）在 **SearchWindow / 原生列表** 里用宿主内嵌的 Material Design Icons 画图标。Settings 侧栏仍用顶层 `plugin.json` 的 `icon`。
- `initialize` 的 params 是 `{ locale, fallbackLocale, messages, theme }`（`PluginInitializeParams`）。`theme` 为 `"light"` | `"dark"`，不含 CSS token。`mytoolsI18n.configure(params)` 装进 i18n。
- `search` 的 params 是 `{ query, mode, locale, fallbackLocale, theme }`（`PluginSearchParams`）。`mode` 为 `"global"` | `"plugin"`：全局搜索（无关键词）为 `"global"`，用户输入 `keyword + 查询串` 进入该插件时为 `"plugin"`。返回 `{ items }`。
- `plugin.actions()` 在 initialize 响应中自动发送完整注册表。`execute` 收到 `ActionContext`，其中 `item` 是 SDK 按 `sessionId + itemId` 缓存的原始业务对象。
- outcome 最多选择一个 `target`：`{ kind: "host", action }`、`{ kind: "web", payload }` 或 `{ kind: "detail", page?, title?, initialState? }`。三种 target 互斥；`host` target 可配 `after: "keep" | "close" | "refresh"`，`web` / `detail` target 只能保持当前界面。`message` 可独立附加。不要使用旧的顶层 `host` / `web` / `detail` / `close` / `refresh` 字段。
- `HostActionRequest` 是按 `action.kind` 判别的联合，参数跟着动作走；不要再在 item 上放万能 `path` / `args` / `copyText`。
- `hotkey` 是 `{ key: Key.*, modifiers?: Modifiers.* }`，不是字符串。修饰键用 `|` 组合；省略时当前 action 子集的第一项默认 Enter，其余只可点击。
- `pinned: true` 把该 action 固定显示在宿主 action bar 上，而不是收进 **Actions**。省略则走默认：只露出 Enter（或第一项）。

若目标路径要在按下 Enter 之后才算出来（剪贴板、找 `.sln` 等），**不要在 Node 里 `spawn`/`exec`**。插件 Job 开了 kill-on-close，MyTools 退出会把子进程一起杀掉。插件算完后返回 `host` target，由宿主执行：

```ts
return {
  message: { key: "Plugin.OpenPath.Opened", defaultValue: "Opened Rider" },
  after: "close",
  target: {
    kind: "host",
    action: {
      kind: HostAction.Execute,
      path: "C:\\...\\rider64.exe",
      args: "\"D:\\work\\app.sln\"",
    },
  },
};
```

`target.action.kind` 使用 `HostAction` 常量。参考 [`smart-open`](references/Examples/smart-open/)，动态计算后交给宿主执行。
- `handle(name, fn)` 给详情页 `bus.call(name)` 用。`context` 有 `action / itemId / query / locale / fallbackLocale / theme`（由宿主注入，页面不必带）。
- 详情页工具可以用 `handle` 把页面状态同步到 Node，再由注册 action 返回 `host` target；纯 UI 动作返回 `{ target: { kind: "web", payload } }`。
- 环境变量从 `process.env` 读。`start()` 连宿主 Named Pipe，不要自己读 stdin。
- 数据落盘优先用宿主注入目录：`MYTOOLS_PLUGIN_DATA_DIR`（单插件目录，例如 `%APPDATA%\MyTools.Desktop\pluginsData\translator`），其次可读 `MYTOOLS_PLUGINS_DATA_DIR`（所有插件数据根目录）。避免把数据写到 `process.cwd()`。

需要宿主能力时（照 [`settings`](references/Examples/settings/)）：页面 `bus.call("getConfiguration")` → 后端 `handle` → `plugin.hostCall("configuration.read")`。页面不能直接发 `host.call.*`。manifest 必须声明完全相同的 capability；

### 可用 capabilities

只有已注册 `IPluginHostCapabilityHandler` 的 capability 才能调用。Node 后端调用
`plugin.hostCall("<capability>", params?)`，且当前插件的 `plugin.json` 必须在
`capabilities` 中逐项声明完全相同的字符串。声明不匹配时宿主返回
`CapabilityNotDeclared`。

| Capability | 说明 | 参数/结果摘要 |
| --- | --- | --- |
| `configuration.read` | 读取宿主全部设置分类、设置项及支持的语言、主题、日志级别。 | 无参数；返回 settings 配置 DTO。 |
| `configuration.readOwn` | 读取**当前插件自己的**设置值。按 `pluginId` 过滤，不能读其他插件。 | 无参数；返回 `{ values: { [settingName]: value } }`，数组保持 JSON 数组。 |
| `configuration.writeOwn` | 写入**当前插件自己的**设置值。按 `pluginId` 过滤，不能改其他插件。 | `{ values: { [settingName]: value } }`；返回 `{ success }`。 |
| `configuration.write` | 保存设置值，并应用主题、语言、日志级别、开机启动和搜索热键等变更。 | `{ changes }`；返回 `{ requiresRestart }`。 |
| `keymap.read` | 读取插件 Alias、全局搜索和启用状态。 | 无参数；返回插件 keymap 列表，不包含热键。 |
| `keymap.write` | 保存插件 Alias、全局搜索和启用状态，并刷新关键词及搜索缓存。 | `{ overrides }`；返回 `{ success }`。 |
| `keymap.validate` | 检查插件 Alias 冲突。 | `{ keywords }`；返回 `{ conflicts }`。 |
| `gestures.read` | 读取鼠标手势配置。 | 无参数；返回 gestures 列表。 |
| `gestures.write` | 保存鼠标手势配置并刷新手势注册。 | `{ gestures }`；返回 `{ success }`。 |
| `gestures.suspend` | 临时暂停鼠标手势检测，通常在录制手势时使用。 | 无参数；返回空对象。 |
| `gestures.resume` | 恢复鼠标手势检测。 | 无参数；返回空对象。 |
| `hotkeys.read` | 读取插件默认热键和当前覆盖热键。 | 无参数；返回插件热键列表。 |
| `hotkeys.write` | 保存插件热键覆盖并重新注册插件热键。 | `{ hotKeys }`；返回 `{ success }`。 |
| `hotkeys.suspend` | 临时注销/暂停宿主全局热键。 | 无参数；返回空对象。 |
| `hotkeys.resume` | 恢复宿主全局热键注册。 | 无参数；返回空对象。 |
| `hotkeys.validate` | 检查待保存的插件热键是否与搜索热键或其他插件热键冲突。 | `{ hotKeys }`；返回 `{ conflicts }`。 |
| `action.capture` | 打开宿主原生输入录制窗口，捕获键盘热键或鼠标按钮。 | 录制选项；返回 `{ cancelled, kind, hotKey, mouseButton }`。 |
| `path.pick` | 打开 Windows 原生文件或目录选择窗口。`kind: directory` 选文件夹，否则选文件。 | `{ title?, filter?, initialPath?, kind? }`，`kind` 为 `file` / `directory` / `fileOrDirectory`；返回 `{ cancelled, path }`。 |
| `path.validate` | 校验路径是否为绝对路径、是否存在，以及是否满足文件/目录类型要求。空路径视为有效。 | `{ path, kind }`，`kind` 为 `file` / `directory` / `fileOrDirectory`；返回 `{ valid, message }`。 |
| `location.city` | 获取宿主按公网 IP 推断的城市级近似位置；不会返回 IP、经纬度或邮编，VPN/代理可能导致城市偏差。 | 无参数；返回 `{ available, city, region, country, countryCode, approximate, source, error? }`。 |
| `restart` | 重启 MyTools Desktop。 | 无参数；返回空对象后执行重启。 |
| `plugins.list` | 列出当前已启用的插件（名称、Alias、热键）。调用方自己的插件会被排除。 | 无参数；返回 `{ plugins: [{ pluginId, name, aliases, hotKey }] }`。 |

上述表格是当前安装版本的 capability 契约。Host 会按插件 manifest 做声明校验和调用审计；不要依赖未列出的内部类名或源码路径。

不要根据测试或设计文档推断 capability 可用性。例如 `clipboard.read` 当前没有注册
`IPluginHostCapabilityHandler`，因此尚不是可调用的宿主能力。

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

`HostEvents`：`initialize` / `search` / `key` / `detailAction` / `languageChanged` / `themeChanged`（完整路由 `host.event.*`）。其中 `detailAction` 只承载 action outcome 中 `{ target: { kind: "web", payload } }` 的 payload。设置页热键/鼠标捕获用长超时 `bus.call("captureInputAction")`，等宿主窗口确认或取消后在 Response 里返回结果。

`HostEvents.Key` 只用于没有被注册 action 消费、需要交给页面的按键（例如 Shift+Enter 聚焦页面输入框）；注册 action 的点击和快捷键统一走 `plugin.call.invokeAction`。

`bus.on(route, handler)` 按路由订阅，晚订阅会重放该路由最后一次事件。不要暴露/使用 catch-all listener。

宿主在 `initialize` / `languageChanged` / `themeChanged` 到达时自动 `bus.i18n.configure` + `apply`（所有 `data-i18n`）以及主题 token。静态文案用 `data-i18n`；动态文案用 `bus.i18n.t`。

多文件页面（`settings`）可把 `export const bus = createWebBusClient()` 放到单独模块，保证只创建一次。

通信：


| 方向              | 路由                                                                   |
| --------------- | -------------------------------------------------------------------- |
| 页面 → 后端         | `bus.call("foo")` → `plugin.call.foo`                                |
| 宿主 → 页面         | `host.event.initialize/search/key/detailAction/languageChanged/themeChanged` |
| 后端 → 宿主能力       | `plugin.hostCall("getConfiguration")` → `host.call.getConfiguration` |
| 后端 → 其他 WebView | `plugin.publish("x")` → `plugin.event.x`                             |


页面只能发 `plugin.call.*`。握手失败时宿主显示页内错误，不会下发 `initialize`。

## 6. i18n（必做）

用户可见文本：稳定 key + 英文 `defaultValue`。key 用 PascalCase，前缀 `Plugin.{PluginId}.*`。占位符 `{{name}}`。

后端：`mytoolsI18n.t("Plugin.MyPlugin.Result.Greeting", { defaultValue: "Hello {{name}}", name })`  
页面动态：`bus.i18n.t("Plugin.MyPlugin.Detail.Empty", { defaultValue: "No results" })`  
HTML：`data-i18n="[attr]key" data-i18n-default-value="english text"`。`[attr]` 可省略（默认 `[text]`），或 `[placeholder]` / `[title]` / `[aria-label]`。

禁止：动态拼接 key、省略 `defaultValue`、用英文当 key。

`i18n/locales/en-US.json` 扁平 `key → 文本`；`zh-CN.json` 可选人工翻译。`i18n/catalog.en-US.json` 是提取产物，每条至少有 `key/defaultValue/placeholders/references`（可从现有插件 catalog 复制后改）。

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

`build-plugin.mjs`：backend（`platform: "node"`, `format: "esm"`, 输出 `.mjs`），web（`format: "iife"`），并把 `plugin.json`、html、css、`i18n/**/*` 拷到 `dist/`。watch 构建成功后通过 `@qping/plugin-bus/dev` 的 `requestDevelopmentPluginRefresh()` 请求 MyTools 刷新，不要在构建脚本中写死刷新管道或消息格式。完整 watch 实现必须以随安装发布的 [`build-plugin.mjs.mustache`](references/Examples/create-plugin/src/templates/common/build-plugin.mjs.mustache) 为准。

esbuild 新版 watch 必须使用 `const ctx = await context(options); await ctx.watch();`，`watch()` **不接收任何参数**。需要监听构建结束时，在 options 中加入自定义插件并使用 `build.onEnd(result => ...)`。禁止生成 `ctx.watch({ onRebuild })`、`onRebuild` 或旧版 `build({ watch: ... })` 写法；这些 API 与当前 esbuild 不兼容。

`package.json` 必须同时提供 `build`、`watch`、`check` 脚本；`watch` 应调用支持持续构建的 `node build-plugin.mjs --watch`。Create Plugin Host 会在 AI 创建完成后执行 `npm install`，随后在可见终端中执行 `npm run watch`，因此缺少 `scripts.watch` 的插件视为未完成。

构建：`npm run build`（先 `tsc --noEmit`，再打包到 `dist/`）。安装指向 `dist/`。参考包仅用于读取，不在安装目录内执行 workspace 构建。
