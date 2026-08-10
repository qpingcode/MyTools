# Settings 节点插件设计

## 目标

将现有的 WPF `ConfigurationWindow` 改造为一个 **Node 插件**（HTML/WebView2），实现：

1. Settings 作为一个 Node 插件存在，通过热键/搜索打开，以独立窗口显示
2. 支持搜索（左侧分类 + 右侧内容均可搜索，匹配高亮）
3. 支持多语言（zh-CN / en-US 等，跟随宿主 locale）
4. 支持主题切换（dark / light，跟随宿主主题）

## 现状分析

### 现有 Settings 窗口

WPF `ConfigurationWindow`（`Views/ConfigurationWindow.xaml`）是三栏布局：

- 左栏：搜索框 + 分类 TreeView（`General` + `Plugins.*`）
- 右栏：当前分类的设置编辑器（Title + Description + 控件）
- 底部：Save 按钮

数据来自 `IConfigurationRegistry`，持久化到 `%AppData%\MyTools.Desktop\Settings.json`（JSON 数组，每项 `{name, value, lastModified}`）。

### 现有设置清单

| FullPath | 类型 | 默认值 | 说明 |
|----------|------|--------|------|
| `General.Language` | Language | 宿主 locale | 需重启 |
| `General.Theme` | Theme | light/dark | 热切换 |
| `General.AutoStart` | Bool | false | |
| `General.MaxHistory` | Integer | 100 | |
| `General.SearchDelay` | Double | 100.0 | |
| `General.UpdateUrl` | String | 默认 URL | |
| `General.UpdateChannel` | String | "win" | |
| `General.UpdateProxyUrl` | String | "" | |
| `General.LogLevel` | LogLevel | "Debug" | 热应用 |
| `Plugins.*.IsEnabled` | Bool | true | 每个插件一个 |

### 两个配置文件

| 文件 | 内容 | 管理者 |
|------|------|--------|
| `Settings.json` | 分类树注册的所有设置 | `IConfigurationRegistry` / `JsonConfigurationStorage` |
| `MyToolsConfig.json` | SearchHotKey, Language, Theme, EnableGesture, EnableClipboardHistory | `AppConfigService` |

Theme 和 Language 同时存在于两个文件，`MyToolsConfig.json` 为运行时权威来源。保存时 `ConfigurationViewModel.Save` 先写 `Settings.json`，再通过 `ThemeService.ApplyFromSettings` / `LanguageService.SetLanguageForNextStartup` 同步到 `MyToolsConfig.json`。

## 核心挑战：Node 插件无法读写宿主配置

当前 Node 插件协议（`NodePluginProcessHost` 的 JSON-RPC）**没有 config 读写方法**。Node 插件只能：
- 接收 `locale` / `fallbackLocale` 参数
- 接收自己的 i18n messages
- 通过 `detailCall` / `detailEvent` 处理自己的业务
- 通过 `publish` 推送事件

它**不能**读写 `Settings.json` 或 `MyToolsConfig.json`。

### 解决方案：扩展协议 — 新增 hostCall

当前协议只有宿主→Node 单向请求（`search`/`invokeAction`/`detailCall`/`detailEvent`），Node→宿主只有 `publish` 通知（单向，无返回值）。

新增 **`hostCall`** 方法类别，让 Node 后端可以**向宿主发起请求并获取响应**：

```text
Node 后端                       宿主 (NodePluginProcessHost)
   │                                │
   │── hostCall(id, method, params) ──▶│  HandleNotification 拦截
   │                                │  → HostCallHandler 处理
   │◀── hostCallResponse(id, result) ──│  通过 stdin 写回
   │                                │
```

## 设计方案

### 第一部分：协议扩展

#### 1.1 JSON-RPC 消息格式

**Node → 宿主（请求）**：

```json
{
  "jsonrpc": "2.0",
  "id": "<uuid>",
  "method": "hostCall",
  "params": {
    "method": "getConfiguration",
    "params": {}
  }
}
```

**宿主 → Node（响应，通过 stdin 写回）**：

```json
{
  "jsonrpc": "2.0",
  "id": "<uuid>",
  "result": { ... }
}
```

> 关键：当前 `HandleNotification`（`NodePluginProcessHost.cs:304`）收到非 `publish` 的通知会打日志丢弃。改造为识别 `hostCall` 并异步处理后写回响应。

#### 1.2 NodePluginProcessHost 改造

**文件：** `MyTools.Plugins/NodePlugins/NodePluginProcessHost.cs`

```csharp
// 新增：宿主能力回调注入点
public Func<HostCallRequest, CancellationToken, Task<JsonElement>>? HostCallHandler { get; set; }

private void HandleNotification(JsonElement root, string line)
{
    var method = root.GetProperty("method").GetString();

    if (method == "publish")
    {
        // ... 现有 publish 逻辑不变
        return;
    }

    if (method == "hostCall")
    {
        _ = HandleHostCallAsync(root);
        return;
    }

    logger.LogWarning("...unsupported notification...");
}

private async Task HandleHostCallAsync(JsonElement root)
{
    var id = root.GetProperty("id").GetString()!;
    var paramsElement = root.GetProperty("params");
    var callMethod = paramsElement.GetProperty("method").GetString()!;
    var callParams = paramsElement.TryGetProperty("params", out var p) ? p : default;

    try
    {
        if (HostCallHandler == null)
            throw new InvalidOperationException("No host call handler registered for this plugin.");

        var request = new HostCallRequest(callMethod, callParams);
        var result = await HostCallHandler(request, CancellationToken.None);

        // 通过 stdin 写回响应
        var responseJson = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
        await process!.StandardInput.WriteLineAsync(responseJson);
        await process.StandardInput.FlushAsync();
    }
    catch (Exception ex)
    {
        var errorJson = JsonSerializer.Serialize(new {
            jsonrpc = "2.0", id,
            error = new { code = -32000, message = ex.Message }
        });
        await process!.StandardInput.WriteLineAsync(errorJson);
        await process.StandardInput.FlushAsync();
    }
}
```

> **注意**：现有的 `ReadStdOutLoopAsync`（215 行）处理 Node→宿主的响应时，会按 `id` 匹配 `pendingRequests`。`hostCall` 的响应**也带 `id`**，会尝试匹配 pendingRequests。但 hostCall 的 id 不是宿主发起的，所以匹配会失败。
>
> **关键修改**：在 `ReadStdOutLoopAsync` 中，如果消息有 `result` 或 `error` 但 `id` 不在 `pendingRequests` 中，说明这是 hostCall 的响应，应交给 Node 侧的 hostCall 等待逻辑（见下文 node-tool）。
>
> **更简洁的方案**：让宿主直接把 hostCall 响应写回 stdin（上面的代码），Node 侧的 `node-tool.mts` 用独立的等待队列处理。宿主的 `ReadStdOutLoopAsync` 只处理自己发起的请求响应，**忽略**（跳过）没有匹配 pendingRequest 的带 id 消息即可。

#### 1.3 node-tool.mts 新增 hostCall

**文件：** `MyTools.Plugins/Examples/common/node-tool.mts`

```typescript
export class NodeTool {
  // 新增：等待 hostCall 响应的队列
  #hostCallPending = new Map<string, { resolve: Function; reject: Function }>();

  async hostCall(method: string, params: Record<string, unknown> = {}): Promise<unknown> {
    const id = crypto.randomUUID();
    const promise = new Promise((resolve, reject) => {
      this.#hostCallPending.set(id, { resolve, reject });
    });

    writeMessage({
      jsonrpc: "2.0",
      id,
      method: "hostCall",
      params: { method, params },
    });

    // 超时保护
    setTimeout(() => {
      if (this.#hostCallPending.has(id)) {
        this.#hostCallPending.delete(id);
        reject(new Error(`hostCall ${method} timed out`));
      }
    }, 30000);

    return promise;
  }

  // 在 #handleLine 中新增：处理宿主写回的 hostCall 响应
  // 宿主通过 stdin 写回 {jsonrpc, id, result/error}
  // 这类消息会被 stdin readline 捕获
  // 但它们不是请求（没有 method），是响应
  // 需要区分：有 method 的是宿主→Node请求，有 id 无 method 的是 hostCall 响应

  async #handleLine(line: string): Promise<void> {
    const message = JSON.parse(line);

    // 宿主写回的 hostCall 响应（有 id，无 method）
    if (message.id && !message.method) {
      const pending = this.#hostCallPending.get(message.id);
      if (pending) {
        this.#hostCallPending.delete(message.id);
        if (message.error) {
          pending.reject(new Error(message.error.message));
        } else {
          pending.resolve(message.result);
        }
      }
      return;
    }

    // ... 现有的请求处理逻辑
  }
}
```

#### 1.4 HostCallProtocol DTO

**文件：** `MyTools.Plugins/NodePlugins/HostCallProtocol.cs`

```csharp
public sealed record HostCallRequest(string Method, JsonElement Params);

// getConfiguration 响应
public sealed class ConfigurationDto
{
    public List<CategoryDto> Categories { get; init; } = new();
    public List<OptionDto> SupportedLocales { get; init; } = new();
    public List<OptionDto> SupportedThemes { get; init; } = new();
    public List<OptionDto> SupportedLogLevels { get; init; } = new();
}

public sealed class CategoryDto
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsSelectable { get; init; }
    public List<CategoryDto> Children { get; init; } = new();
    public List<SettingDto> Settings { get; init; } = new();
}

public sealed class SettingDto
{
    public string FullPath { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string ValueType { get; init; } = "";
    public string? CurrentValue { get; init; }
    public bool RequiresRestart { get; init; }
}

public sealed class OptionDto
{
    public string Value { get; init; } = "";
    public string Label { get; init; } = "";
}

// saveConfiguration 请求
public sealed class SaveConfigurationRequest
{
    public List<SettingChangeDto> Changes { get; init; } = new();
}

public sealed class SettingChangeDto
{
    public string FullPath { get; init; } = "";
    public string? Value { get; init; }
}

// saveConfiguration 响应
public sealed class SaveConfigurationResult
{
    public bool RequiresRestart { get; init; }
}
```

### 第二部分：宿主侧

#### 2.1 SettingsPluginHostCallHandler

**文件：** `MyTools.Desktop/Services/SettingsPluginHostCallHandler.cs`

```csharp
public sealed class SettingsPluginHostCallHandler
{
    private readonly IConfigurationRegistry registry;
    private readonly ThemeService themeService;
    private readonly LanguageService languageService;
    private readonly LogLevelService logLevelService;
    private readonly AutoStartService autoStartService;
    private readonly ILogger<SettingsPluginHostCallHandler> logger;

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken ct)
    {
        return request.Method switch
        {
            "getConfiguration" => GetConfiguration(),
            "saveConfiguration" => SaveConfiguration(request.Params),
            _ => throw new NotSupportedException($"Unknown hostCall method: {request.Method}")
        };
    }

    private JsonElement GetConfiguration()
    {
        // 1. 从 registry.GetRootCategories() 遍历分类树
        // 2. 序列化为 ConfigurationDto（CategoryDto + SettingDto）
        // 3. 附加选项列表（supportedLocales/themes/logLevels）
        // 4. 返回 JSON
    }

    private JsonElement SaveConfiguration(JsonElement payload)
    {
        // 1. 反序列化 SaveConfigurationRequest
        // 2. 逐项: registry.FindSetting(path)?.CurrentValue = value (注意类型转换)
        // 3. registry.SaveChanges()
        // 4. themeService.ApplyFromSettings(registry)
        // 5. logLevelService.ApplyFromSettings(registry)
        // 6. AutoStart: 如果 General.AutoStart 改了，autoStartService.AutoStart = value
        // 7. Language: 如果 General.Language 改了
        //    languageService.SetLanguageForNextStartup(locale)
        //    返回 RequiresRestart = true
        // 8. 返回 SaveConfigurationResult
    }
}
```

#### 2.2 AppBootstrapper 集成

**文件：** `MyTools.Desktop/AppBootstrapper.cs`

在 `LoadPlugins` 后，检测 settings 插件并注册 HostCallHandler：

```csharp
private void RegisterSettingsPluginHostCall(IEnumerable<NodePlugin> nodePlugins)
{
    var settingsPlugin = nodePlugins.FirstOrDefault(p => p.Manifest.Id == "settings");
    if (settingsPlugin?.ProcessHost == null) return;

    var handler = serviceProvider.GetRequiredService<SettingsPluginHostCallHandler>();
    settingsPlugin.ProcessHost.HostCallHandler = handler.HandleAsync;
}
```

#### 2.3 托盘菜单改造

**文件：** `MyTools.Desktop/App.xaml.cs`

`OpenSettings_Click`（224 行）从 `new ConfigurationWindow()` 改为通过插件窗口打开：

```csharp
private void OpenSettings_Click(object? sender, EventArgs e)
{
    var pluginWindowManager = ServiceLocator.GetRequiredService<PluginWindowManager>();
    var settingsPlugin = /* 找到 settings NodePlugin */;
    var context = settingsPlugin.CreateHotKeyDetailContext();
    pluginWindowManager.ShowOrFocus(settingsPlugin, context);
}
```

### 第三部分：Node 插件

#### 3.1 目录结构

```
MyTools.Plugins/Examples/settings/
  plugin.json
  package.json
  tsconfig.json
  build-plugin.mjs
  src/
    backend/index.mts
    web/
      index.html
      main.ts
      style.css
  i18n/
    catalog.en-US.json
    locales/
      en-US.json
      zh-CN.json
```

#### 3.2 plugin.json

```json
{
  "id": "settings",
  "name": "Settings",
  "version": "0.1.0",
  "runtime": "node",
  "protocolVersion": "2.0",
  "entries": [
    {
      "id": "main",
      "entry": "backend/index.mjs",
      "keywords": ["settings", "config", "配置", "设置"],
      "hotKey": "Ctrl+Comma",
      "detail": {
        "type": "web",
        "entry": "web/index.html"
      }
    }
  ],
  "i18n": {
    "defaultLocale": "en-US",
    "catalog": "i18n/catalog.en-US.json",
    "localesPath": "i18n/locales",
    "supportedLocales": ["en-US", "zh-CN"]
  }
}
```

#### 3.3 后端 (`src/backend/index.mts`)

后端作为**转发层**：

```typescript
import { createTool } from "@qping/plugin-common/node-tool";
import { mytoolsI18n } from "@qping/plugin-common/i18n";

const tool = createTool();

tool
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((params) => ({
    items: [{
      id: "settings:main",
      title: mytoolsI18n.t("Plugin.Settings.Name", { defaultValue: "Settings" }),
      subtitle: mytoolsI18n.t("Plugin.Settings.Subtitle", {
        defaultValue: "Application settings"
      }),
      priority: 100,
      icon: { kind: "emoji", value: "⚙️" },
      actions: [{
        id: "open-detail",
        title: "Open Settings",
        kind: "detail"
      }]
    }]
  }))
  .handle("getConfiguration", async () => {
    return await tool.hostCall("getConfiguration");
  })
  .handle("saveConfiguration", async (payload) => {
    return await tool.hostCall("saveConfiguration", payload);
  })
  .start();
```

#### 3.4 前端布局

```text
┌─────────────────────────────────────────────────────────┐
│  [🔍 Search settings...___________________]              │
├──────────────┬──────────────────────────────────────────┤
│  General     │  General                                 │
│   Language   │                                          │
│   Theme      │  Language     [English ▾]                │
│   Auto Start │  Select the application language         │
│   ...        │                                          │
│              │  Theme        [Dark ▾]                   │
│  Plugins     │  Choose the color theme                  │
│   Calculator │                                          │
│   ...        │  Auto Start   [☑]                        │
│              │  Run MyTools when system starts          │
│              │  ...                                     │
│              │                                          │
│              │              [Save]                      │
└──────────────┴──────────────────────────────────────────┘
```

#### 3.5 前端核心逻辑 (`src/web/main.ts`)

```typescript
import { tool } from "@qping/plugin-common/web-tool";

(function () {
    type Config = {
        categories: Category[];
        supportedLocales: Option[];
        supportedThemes: Option[];
        supportedLogLevels: Option[];
    };

    var config: Config | null = null;
    var dirtySettings = new Map<string, string>();  // fullPath → new value
    var currentCategory: Category | null = null;

    // 初始化
    tool.subscribe(tool.events.host.initialize, async (payload) => {
        config = await tool.call<Config>("getConfiguration");
        renderCategoryTree(config.categories);
        selectFirstCategory();
    });

    // 搜索
    searchInput.addEventListener("input", () => {
        filterTree(searchInput.value.trim().toLowerCase());
    });

    // 保存
    saveButton.addEventListener("click", async () => {
        if (dirtySettings.size === 0) return;

        var changes = [...dirtySettings.entries()].map(([path, value]) => ({
            fullPath: path, value
        }));

        var result = await tool.call<{ requiresRestart: boolean }>(
            "saveConfiguration", { changes }
        );

        dirtySettings.clear();

        if (result.requiresRestart) {
            showRestartPrompt();
        } else {
            showSavedToast();
        }
    });

    // 主题切换
    tool.subscribe(tool.events.host.themeChanged, () => {
        // CSS 变量自动更新，重渲染动态颜色
        if (currentCategory) renderSettings(currentCategory);
    });

    // 语言切换
    tool.subscribe(tool.events.host.languageChanged, async () => {
        tool.i18n.apply(document);  // 重新翻译 UI 文案
        // 重新加载配置（设置项标题需要重新翻译）
        config = await tool.call<Config>("getConfiguration");
        renderCategoryTree(config.categories);
        if (currentCategory) renderSettings(currentCategory);
    });

    // 搜索高亮
    function highlight(text: string, query: string): string {
        if (!query) return escapeHtml(text);
        var lower = text.toLowerCase();
        var idx = lower.indexOf(query);
        if (idx < 0) return escapeHtml(text);
        return escapeHtml(text.slice(0, idx))
            + "<mark>" + escapeHtml(text.slice(idx, idx + query.length)) + "</mark>"
            + escapeHtml(text.slice(idx + query.length));
    }
})();
```

#### 3.6 设置项编辑器

根据 `valueType` 渲染不同控件：

| valueType | 编辑器 | 选项来源 |
|-----------|--------|----------|
| `Bool` | checkbox | — |
| `Integer` / `Double` | `<input type="number">` | — |
| `String` | `<input type="text">` | — |
| `Language` | `<select>` | `config.supportedLocales` |
| `Theme` | `<select>` | `config.supportedThemes` |
| `LogLevel` | `<select>` | `config.supportedLogLevels` |

#### 3.7 i18n

设置项的标题/描述（如 "Language"、"Auto start"）由宿主 `getConfiguration` 返回时已是**当前 locale 的文本**（`IConfigurationRegistry` 注册时用了 `localization.GetCaption`）。前端只需翻译 UI 框架文案（搜索框、保存按钮等）。

`i18n/locales/en-US.json`（节选）：

```json
{
  "Plugin.Settings.Name": "Settings",
  "Plugin.Settings.Search.Placeholder": "Search settings...",
  "Plugin.Settings.Save": "Save",
  "Plugin.Settings.Saved": "Settings saved successfully.",
  "Plugin.Settings.RestartPrompt": "Language changed. Restart to apply?",
  "Plugin.Settings.Restart": "Restart",
  "Plugin.Settings.NoResults": "No matching settings found"
}
```

`i18n/locales/zh-CN.json`（节选）：

```json
{
  "Plugin.Settings.Name": "设置",
  "Plugin.Settings.Search.Placeholder": "搜索设置...",
  "Plugin.Settings.Save": "保存",
  "Plugin.Settings.Saved": "设置已保存。",
  "Plugin.Settings.RestartPrompt": "语言已更改，是否重启以应用？",
  "Plugin.Settings.Restart": "重启",
  "Plugin.Settings.NoResults": "未找到匹配的设置"
}
```

#### 3.8 主题适配

CSS 全部使用 `--mt-*` 主题变量（由宿主注入），自动适配 dark/light：

```css
.settings-app {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: var(--mt-surface-bg, #1e1e1e);
  color: var(--mt-text, #e0e0e0);
}

.category-item.active {
  background: var(--mt-surface-hover, #2a2a2a);
  border-left: 3px solid var(--mt-accent, #4a9eff);
}

mark {
  background: var(--mt-accent, #4a9eff);
  color: var(--mt-surface-bg, #1e1e1e);
  border-radius: 2px;
}

.setting-input, .setting-select {
  background: var(--mt-surface, #2a2a2a);
  color: var(--mt-text, #e0e0e0);
  border: 1px solid var(--mt-border, #3a3a3a);
}
```

## 数据流

```text
用户打开 settings 插件窗口
  → WebView2 加载 index.html
  → 前端 tool.call("getConfiguration")
    → Node 后端 tool.hostCall("getConfiguration")
      → NodePluginProcessHost 拦截 hostCall
        → SettingsPluginHostCallHandler.GetConfiguration()
          → IConfigurationRegistry.GetRootCategories()
          → 序列化为 ConfigurationDto
        ← 返回 JSON
      ← 写回 Node 进程 (stdin)
    ← Node 后端返回给前端
  → 前端渲染分类树 + 设置项

用户修改设置并点击保存
  → 前端 tool.call("saveConfiguration", { changes })
    → Node 后端 tool.hostCall("saveConfiguration", changes)
      → SettingsPluginHostCallHandler.SaveConfiguration(changes)
        → 逐项更新 registry.CurrentValue
        → registry.SaveChanges()
        → ThemeService.ApplyFromSettings()   (热切换)
        → LogLevelService.ApplyFromSettings() (热应用)
        → AutoStartService.AutoStart = ...
        → LanguageService.SetLanguageForNextStartup() (如需)
      ← 返回 { requiresRestart: true/false }
    ← 写回 Node 进程
  → 前端显示结果
    → requiresRestart=true → 弹出重启提示
    → requiresRestart=false → 显示保存成功
```

## 涉及的文件清单

### 新增

| 文件 | 说明 |
|------|------|
| `MyTools.Plugins/NodePlugins/HostCallProtocol.cs` | hostCall 请求/响应 DTO |
| `MyTools.Desktop/Services/SettingsPluginHostCallHandler.cs` | 宿主侧 config 读写 |
| `MyTools.Plugins/Examples/settings/plugin.json` | 插件 manifest |
| `MyTools.Plugins/Examples/settings/package.json` | npm 配置 |
| `MyTools.Plugins/Examples/settings/tsconfig.json` | TS 配置 |
| `MyTools.Plugins/Examples/settings/build-plugin.mjs` | 构建脚本 |
| `MyTools.Plugins/Examples/settings/src/backend/index.mts` | Node 后端 |
| `MyTools.Plugins/Examples/settings/src/web/index.html` | 主页面 |
| `MyTools.Plugins/Examples/settings/src/web/main.ts` | 前端逻辑 |
| `MyTools.Plugins/Examples/settings/src/web/style.css` | 样式 |
| `MyTools.Plugins/Examples/settings/i18n/locales/en-US.json` | 英文 |
| `MyTools.Plugins/Examples/settings/i18n/locales/zh-CN.json` | 中文 |
| `MyTools.Plugins/Examples/settings/i18n/catalog.en-US.json` | i18n 目录 |

### 修改

| 文件 | 说明 |
|------|------|
| `NodePluginProcessHost.cs` | `HandleNotification` 增加 `hostCall`；新增 `HostCallHandler` 属性；`ReadStdOutLoopAsync` 跳过无 pendingRequest 的带 id 消息 |
| `NodePlugin.cs` | 暴露 `ProcessHost` 以便宿主注册 `HostCallHandler` |
| `common/node-tool.mts` | 新增 `tool.hostCall(method, params)` 和响应等待逻辑 |
| `AppBootstrapper.cs` | `LoadPlugins` 后注册 `SettingsPluginHostCallHandler` |
| `App.xaml.cs` | `OpenSettings_Click` 改为通过插件窗口打开 |

### 不删除（本期保留）

| 文件 | 说明 |
|------|------|
| `ConfigurationWindow.xaml(.cs)` | 暂保留作为 fallback |
| `ConfigurationViewModel.cs` | 暂保留 |

## 实施步骤建议

1. **协议扩展**：`NodePluginProcessHost` + `node-tool.mts` + `HostCallProtocol.cs`
2. **宿主 handler**：`SettingsPluginHostCallHandler` + `AppBootstrapper` 集成
3. **插件骨架**：plugin.json + backend + 空 frontend，验证 `getConfiguration` 能返回数据
4. **前端 UI**：分类树 + 设置编辑器 + 搜索高亮 + 保存
5. **托盘菜单**：`App.xaml.cs` 改为打开插件窗口
6. **测试验证**

## 验收标准

1. `Ctrl+,`（或搜索 "settings"）打开 settings 插件窗口
2. 左侧分类树显示所有分类（General + Plugins.\*），可点击切换
3. 右侧显示对应分类的设置项，可编辑
4. 搜索框输入关键词，左侧分类和右侧设置项均可匹配，匹配文本高亮（`<mark>`）
5. 切换语言（宿主 locale 变化），settings 页面 UI 文案自动更新
6. 切换主题（dark/light），settings 页面样式自动适配
7. 保存设置后，Theme/LogLevel 立即生效，Language 提示重启
8. 多次打开只复用一个窗口（通过 PluginWindowManager）

## 风险与边界

1. **协议改动影响面**：`hostCall` 是新增方法，不影响现有 `publish` 通知。但 `ReadStdOutLoopAsync` 需要正确区分 hostCall 响应和普通请求响应，避免误匹配。
2. **AutoStart 断连**：现有 `General.AutoStart` registry 设置与 `AutoStartService`（Win32 注册表）未连接。`SettingsPluginHostCallHandler.SaveConfiguration` 需要补上这个桥接。
3. **语言切换需重启**：Language 改变后需要重启应用才能完全生效（现有行为一致），前端需提示用户。
4. **Node 插件内嵌套访问宿主配置的安全边界**：hostCall 目前只为 settings 插件注册 `HostCallHandler`，其他 Node 插件不会获得此能力。如需通用化，需要加权限控制。
