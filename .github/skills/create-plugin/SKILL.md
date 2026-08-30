---
name: create-plugin
description: Develop a MyTools Node plugin (backend + optional WebView2 detail page) on the v3 named-pipe message bus. Use whenever the user wants to create, scaffold, or edit a MyTools plugin. Covers plugin.json, @qping/plugin-bus, backend handlers, list or web UI, host.call capabilities, i18n, and theming.
---

# MyTools Node Plugin 开发

独立 Node 进程后端 + 可选 WebView2 详情页。不写 `detail` 时宿主用 `search` 结果走原生列表。协议 **3.0**。

**不要抄本文件里的想象代码。** 先 `get_mytools_context`，再用 `list_files` / `read_file` 读取随安装打包的 [`references/Examples/`](references/Examples/)。只读与需求相关的最小集合。

可引用的**内置插件**只有这四个：[`chat`](references/Examples/chat/)、[`plugin-search`](references/Examples/plugin-search/)、[`settings`](references/Examples/settings/)、[`store`](references/Examples/store/)。不要读取或模仿其它插件目录，那些不会留在仓库里。

脚手架只来自 Create Plugin 自己的模板（不是业务示例）：

| 需求 | 读这些 |
| --- | --- |
| 脚手架（`package.json`、`tsconfig`、`plugin.json`、构建、i18n、README） | [`create-plugin/src/templates/common/`](references/Examples/create-plugin/src/templates/common/) |
| Web 详情页骨架 / 主题 CSS | [`custom-ui/src/web/`](references/Examples/create-plugin/src/templates/custom-ui/src/web/) |
| 标准列表 + `host` target | [`plugin-search/src/backend/index.mts`](references/Examples/plugin-search/src/backend/index.mts)、[`plugin-search/plugin.json`](references/Examples/plugin-search/plugin.json) |
| `hostCall`、多文件 `bus` | [`settings/`](references/Examples/settings/)（宿主设置）；[`store/`](references/Examples/store/)（市场） |
| 确认卡片 | [`chat/`](references/Examples/chat/) |
| watch / 刷新 | [`build-plugin.mjs.mustache`](references/Examples/create-plugin/src/templates/common/build-plugin.mjs.mustache) |

不要把内置插件的 `package.json` 当脚手架（例如 `plugin-search` 没有 `watch`）。`complete_plugin` 要求 `build` / `watch` / `check` 都在，以模板为准。settings / store / chat 的页面比普通插件复杂，普通 Web 详情页只抄 custom-ui 模板。

## 流程

需求不足、且歧义会改变实现时：用下方 `mytools-interaction` 结束本轮，在用户回答前不要调用 `write_plugin_file`、`complete_plugin` 或其它会改文件的工具。有合理默认值的细节不要问。

需求明确后：

1. 创建：选唯一 kebab-case `id` 和名称，按模板生成完整目录。
2. 编辑：只改 `selectedPlugin` 目录，保持插件 ID，保留无关行为。
3. 完成前核对 manifest、i18n、图标、`build` / `watch` / `check`，然后 `complete_plugin` 一次。

打开目录 / VS Code 只能走 Host：`development.openFolder` / `development.openCode`。不要启动 `explorer.exe` 或 `code`。

## 需要用户确认时

说明之后只用一个 fenced `mytools-interaction` JSON 块结束回复。`id` 用简短稳定的 ASCII。不要把该格式用于修辞或已有默认值的细节。格式见 [`chat`](references/Examples/chat/)。

````markdown
```mytools-interaction
{
  "version": 1,
  "id": "stable_interaction_id",
  "title": "Optional short heading",
  "questions": [
    {
      "id": "stable_question_id",
      "prompt": "Question shown to the user",
      "options": ["First choice", "Second choice"],
      "multiple": false,
      "allowText": true,
      "textPlaceholder": "Enter another answer"
    }
  ]
}
```
````

## 脚手架

从 [`templates/common`](references/Examples/create-plugin/src/templates/common/) 复制并替换 mustache 变量。Web 详情再叠加 [`custom-ui`](references/Examples/create-plugin/src/templates/custom-ui/src/web/)。

```text
my-plugin/
  README.md
  README.zh-CN.md
  plugin.json
  package.json
  tsconfig.json
  build-plugin.mjs
  scripts/publish-hub.mjs
  src/backend/index.mts
  src/web/{index.html, main.ts, style.css}   # 仅 custom-ui
  i18n/catalog.en-US.json
  i18n/locales/{en-US.json, zh-CN.json}
```

构建输出到 `dist/`。`plugin.json` 路径相对 `dist`（`backend/index.mjs`、`web/index.html`）。

必须写 `README.md`（问题、功能、用法、开发、发布）以及已支持语言的本地化 README（至少 `README.zh-CN.md`），顶部互相链接。功能变了就同步改。

## 硬约束

- 只从 npm 安装已发布的 `@qping/plugin-bus`，版本与 [`package.json.mustache`](references/Examples/create-plugin/src/templates/common/package.json.mustache) 一致。禁止 `file:`、`sdk-v3`、其它本地 SDK。
- `protocolVersion` 必须是 `"3.0"`。
- `package.json` 必须有 `scripts.build`、`scripts.watch`、`scripts.check`；`watch` 为 `node build-plugin.mjs --watch`。
- 不要自己 `spawn` / `exec`。算完路径后返回 `host` target，由宿主执行（见 [`plugin-search`](references/Examples/plugin-search/src/backend/index.mts) 的 `HostAction.OpenPlugin`）。
- 数据写 `MYTOOLS_PLUGIN_DATA_DIR`，不要写 `process.cwd()`。
- 不要跑 shell。Host 会在注册后执行 `npm install` 和 `npm run watch`。
- AI 创建时 `alias` 最多一个。
- 页面只能 `bus.call` → 后端 `handle` → 需要时再 `plugin.hostCall`。页面不能发 `host.call.*`。
- `hostCall` 的方法名必须与 `plugin.json` `capabilities` 里的字符串完全一致，且必须在下方白名单中。

## plugin.json

对照 [`plugin.json.mustache`](references/Examples/create-plugin/src/templates/common/plugin.json.mustache)，列表插件再看 [`plugin-search/plugin.json`](references/Examples/plugin-search/plugin.json)，Web 插件再看 [`store/plugin.json`](references/Examples/store/plugin.json) 或 [`chat/plugin.json`](references/Examples/chat/plugin.json)。不要凭记忆编一份。

- `id`：稳定 kebab-case。配置路径 `Plugins.{id}.*`，i18n scope `plugin:{id}`。`plugin.json` 的 `id` 就是插件 ID。
- `name`：`{ key, defaultValue }`，不是字符串。
- `version`：semver，会显示给用户。
- `capabilities`：必填，可 `[]`。未声明的 `hostCall` 会 `CapabilityNotDeclared`。
- `icon`：Settings 侧栏 MDI 类名。省略则用默认齿轮。
- `detail` 省略或 `{ "type": "list" }`：原生列表；热键打开搜索窗并锁定该插件。自定义页才写 `{ "type": "web", "entry": "web/index.html" }`。
- action 不写在 manifest。详情页默认用 `plugin.actions()` 注册的全部 action；列表 item 用 action id 子集。需要同时露出多项时在对应 action 上写 `pinned: true`。
- `search.global`：无关键词时是否出现在全局结果。省略或 `false` 为不参与。只有全局、没有 alias 时必须 `"search": { "global": true }`。
- 没有顶层 `runtime`。

## 后端

SDK：`@qping/plugin-bus/node`（`createPlugin`、`HostAction`、`Key`、`Modifiers`）、`@qping/plugin-bus/i18n`（`mytoolsI18n`）。实现对照模板 [`index.mts.mustache`](references/Examples/create-plugin/src/templates/common/src/backend/index.mts.mustache)，列表行为对照 [`plugin-search`](references/Examples/plugin-search/src/backend/index.mts)。

- `initialize` params：`{ locale, fallbackLocale, messages, theme }`。先 `mytoolsI18n.configure(params)`。
- `search` params：`{ query, mode, locale, fallbackLocale, theme }`。`mode` 为 `"global"` | `"plugin"`。返回 `{ items }`。
- item 可带业务字段（宿主不读）和 `actions: ["copy"]`。`icon.kind`：`emoji` 或 `mdi`。
- `plugin.actions()` 在 initialize 响应里自动注册。`execute` 的 `item` 是 SDK 按 session 缓存的原始对象。
- outcome 最多一个 `target`：`{ kind: "host", action }`、`{ kind: "web", payload }` 或 `{ kind: "detail", page?, title?, initialState? }`。`host` 可配 `after: "keep" | "close" | "refresh"`。不要用旧的顶层 `host` / `web` / `close` 字段。
- `hotkey` 是 `{ key: Key.*, modifiers?: Modifiers.* }`，不是字符串。
- `handle(name, fn)` 给页面 `bus.call(name)`。`context` 有 `action / itemId / query / locale / fallbackLocale / theme`。
- `start()` 连 Named Pipe。环境变量从 `process.env` 读。
- `plugin.publish("subject", payload)` 发到同会话其它 WebView；现有例子基本不用。

`plugin.hostCall(method, params?, timeoutMs?)`：未传超时时，若正在处理页面的 `bus.call`，用该请求剩余时间，否则 30 秒。

## 详情页

模块加载时立刻 `createWebBusClient()`（`@qping/plugin-bus/web`）。普通详情页对照 [`custom-ui main.ts.mustache`](references/Examples/create-plugin/src/templates/custom-ui/src/web/main.ts.mustache)。

- `HostEvents`：`initialize` / `search` / `key` / `detailAction` / `languageChanged` / `themeChanged`。`detailAction` 只承载 `{ target: { kind: "web", payload } }`。
- `HostEvents.Key` 只给没被注册 action 消费的按键。注册 action 走 `plugin.call.invokeAction`。
- `bus.on(route, handler)` 晚订阅会重放该路由最后一次事件。不要 catch-all。
- 宿主在 initialize / language / theme 时自动 `bus.i18n.configure` + `apply` 以及主题 token。静态文案用 `data-i18n`，动态用 `bus.i18n.t`。
- 多文件页面把 `createWebBusClient()` 放进单独模块，只创建一次（见 [`settings`](references/Examples/settings/)）。

| 方向 | 路由 |
| --- | --- |
| 页面 → 后端 | `bus.call("foo")` → `plugin.call.foo` |
| 宿主 → 页面 | `host.event.*` |
| 后端 → 宿主能力 | `plugin.hostCall("<capability>")` → `host.call.<capability>` |
| 后端 → 其它 WebView | `plugin.publish("x")` → `plugin.event.x` |

## i18n

用户可见文本：稳定 key + 英文 `defaultValue`。key 用 PascalCase，前缀 `Plugin.{PluginId}.*`。占位符 `{{name}}`。

- 后端：`mytoolsI18n.t(key, { defaultValue, ... })`
- 页面动态：`bus.i18n.t(...)`
- HTML：`data-i18n="[attr]key"` + `data-i18n-default-value`。`[attr]` 可省略（默认 `[text]`）。

禁止动态拼接 key、省略 `defaultValue`、用英文当 key。catalog / locales 从模板或现有插件复制后改。解析：人工翻译 > locales JSON > defaultValue > key。

## 主题

对照 [`custom-ui style.css.mustache`](references/Examples/create-plugin/src/templates/custom-ui/src/web/style.css.mustache)。只用 `var(--mt-..., #darkFallback)`，禁止写死颜色。首帧由宿主注入。主题切换用 `bus.theme`；按主题换图标时订阅 `HostEvents.ThemeChanged`。

常用变量：`--mt-surface-bg` / `--mt-surface` / `--mt-surface-alt` / `--mt-surface-hover`、`--mt-text` / `--mt-text-muted` / `--mt-text-tertiary`、`--mt-border` / `--mt-border-subtle`、`--mt-accent` / `--mt-accent-foreground`。

## 构建

以 [`build-plugin.mjs.mustache`](references/Examples/create-plugin/src/templates/common/build-plugin.mjs.mustache) 为准：backend `platform: "node"` + ESM `.mjs`，web IIFE，拷贝 `plugin.json` / html / css / `i18n/**`。watch 成功后调用 `@qping/plugin-bus/dev` 的 `requestDevelopmentPluginRefresh()`，不要写死管道格式。

esbuild：`const ctx = await context(options); await ctx.watch();`。`watch()` 不接收参数。结束回调用 `build.onEnd`。禁止 `onRebuild` 和 `build({ watch })`。

发布 Hub：`npm run publish:hub`（模板含 `scripts/publish-hub.mjs`）。版本必须高于已发布 SemVer。

## configuration

设置写在顶层 `configuration`。内置插件里没有普通的 `readOwn` 示例，按下列规则写，不要去翻其它插件目录。分类名是插件显示名，完整路径 `{pluginId}.{key}`。

- `type`：`string` / `bool` / `int` / `double` / `array` / `path` / `h1` / `h2`。
- 详情页标题不要复用根 `name`。有 configuration 时第一项用展示用 `h1`。`h1` / `h2` 没有 `key` / `defaultValue`。
- `label` / `description` 都可选。
- 读自己的设置声明 `configuration.readOwn`，写自己的再加 `configuration.writeOwn`。不要用 `configuration.read` / `configuration.write`（那是 settings 插件改全部设置）。
- `array` 默认 `table`，必须带 `schema.properties`。列 `type: "hidden"` 不显示；`"table": false` 只出现在编辑框。
- `visibility` 可选，例如 `"${ChromeEnabled == true}"`。
- 默认值支持 `${DateTime.Now}`。

## 可用 capabilities

只有下表中的 capability 能 `hostCall`，且必须写进 `capabilities`。不要根据测试或设计文档推断；例如 `clipboard.read` 当前不可用。

| Capability | 说明 | 参数/结果 |
| --- | --- | --- |
| `configuration.read` | 读取宿主全部设置。 | 无参数 |
| `configuration.readOwn` | 读取当前插件自己的设置。 | 无参数；`{ values }` |
| `configuration.writeOwn` | 写入当前插件自己的设置。 | `{ values }` |
| `configuration.write` | 保存宿主设置并应用主题/语言等。 | `{ changes }` → `{ requiresRestart }` |
| `keymap.read` / `keymap.write` / `keymap.validate` | 插件 Alias、全局搜索、启用状态。 | write：`{ overrides }`；validate：`{ keywords }` |
| `gestures.read` / `gestures.write` / `gestures.suspend` / `gestures.resume` | 鼠标手势。 | write：`{ gestures }` |
| `hotkeys.read` / `hotkeys.write` / `hotkeys.suspend` / `hotkeys.resume` / `hotkeys.validate` | 插件热键。 | write/validate：`{ hotKeys }` |
| `action.capture` | 录制键盘或鼠标。 | 返回 `{ cancelled, kind, hotKey, mouseButton }` |
| `path.pick` | 原生文件/目录选择。 | `{ title?, filter?, initialPath?, kind? }` |
| `path.validate` | 校验绝对路径。空路径视为有效。 | `{ path, kind }` |
| `location.city` | 城市级近似位置，不含 IP/坐标。 | 无参数 |
| `restart` | 重启 Desktop。 | 无参数 |
| `plugins.list` | 已启用插件列表（排除调用方自己）。 | `{ plugins }` |
| `account.status` / `account.login` / `account.register` / `account.logout` / `account.externalLogin` | Hub 账号。 | login/register：`{ username, password }`；external：`{ provider }` |
| `marketplace.search` / `marketplace.get` / `marketplace.install` | 市场搜索、详情、安装。未登录可用。 | get/install：`{ pluginId, version? }` |
| `marketplace.publish.validate` / `marketplace.publish` | 校验或发布开发插件。必须登录。 | `{ pluginId, version? }` |
| `sync.pull` / `sync.push` | 云端配置。必须登录。 | 无参数 |
