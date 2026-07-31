# MyTools 插件协议草案

## 目的

本文档用于落实 Roadmap 中的 B2，定义 MyTools 插件平台的第一版协议草案，为后续的：

1. `plugin.json` 设计
2. Node 运行时实现
3. .NET 外部运行时实现
4. TypeScript SDK 设计
5. .NET SDK 设计

提供统一的通信基础。

## 设计原则

协议设计遵守以下原则：

1. 跨语言。TypeScript 和 .NET 必须共享同一协议语义。
2. 跨进程。协议对象必须可序列化，不能依赖宿主内存对象。
3. 跨平台。协议不包含 WPF、Windows API 或平台 UI 类型。
4. 宿主主导。插件声明能力和需求，但最终调度和能力开放由宿主控制。
5. 向前演进。协议必须支持版本字段和兼容策略。

## 协议总览

协议可分为五类消息：

1. 生命周期消息
2. 搜索消息
3. 动作执行消息
4. 设置与描述消息
5. 能力调用消息

建议所有运行时统一采用请求响应模型，消息格式可落在：

1. JSON over stdio
2. 本地 socket
3. 未来可扩展为命名管道或其他 IPC

第一版建议统一使用 JSON 消息格式。

## 顶层消息模型

### ProtocolEnvelope

所有消息建议统一包裹在一个 envelope 中。

字段建议：

1. `protocolVersion: string`
2. `messageId: string`
3. `messageType: string`
4. `timestampUtc: string`
5. `payload: object`

说明：

1. `protocolVersion` 用于宿主和插件做握手与兼容校验。
2. `messageId` 用于请求响应配对。
3. `messageType` 用于区分消息类型。
4. `timestampUtc` 用于日志和调试。
5. `payload` 为具体业务对象。

### MessageType 建议集合

建议第一版支持以下消息类型：

1. `initialize.request`
2. `initialize.response`
3. `search.request`
4. `search.response`
5. `action.execute.request`
6. `action.execute.response`
7. `settings.schema.request`
8. `settings.schema.response`
9. `capability.invoke.request`
10. `capability.invoke.response`
11. `shutdown.request`
12. `shutdown.response`
13. `error.response`
14. `event.log`
15. `event.progress`

## 插件描述模型

### PluginManifest

`PluginManifest` 是插件包的静态声明对象，用于宿主在加载前理解插件。

建议字段：

1. `id: string`
2. `name: string`
3. `version: string`
4. `displayName?: string`
5. `description?: string`
6. `author?: string`
7. `runtime: "node" | "dotnet" | "builtin"`
8. `entry: string`
9. `protocolVersion: string`
10. `supportedPlatforms: string[]`
11. `keywords?: string[]`
12. `defaultCapabilities?: string[]`
13. `permissions?: string[]`
14. `homepage?: string`
15. `repository?: string`
16. `icon?: IconDescriptor`

说明：

1. `runtime` 决定由哪个 runtime 启动插件。
2. `supportedPlatforms` 由宿主决定是否允许加载。
3. `permissions` 只是申请，不代表自动授权。
4. `keywords` 是插件路由提示，不等于最终宿主索引结果。

### PluginDescriptor

`PluginDescriptor` 是插件运行后由宿主维护的动态描述对象，用于运行期索引和展示。

建议字段：

1. `id: string`
2. `manifestVersion: string`
3. `runtime: string`
4. `isEnabled: bool`
5. `isHealthy: bool`
6. `supportsGlobalSearch: bool`
7. `declaredKeywords: string[]`
8. `capabilities: string[]`
9. `grantedPermissions: string[]`

说明：

1. `PluginManifest` 偏静态。
2. `PluginDescriptor` 偏运行期状态。

## 生命周期协议

### InitializeRequest

宿主启动插件后发送初始化请求。

建议字段：

1. `hostName: string`
2. `hostVersion: string`
3. `protocolVersion: string`
4. `platform: string`
5. `culture?: string`
6. `grantedPermissions: string[]`
7. `availableCapabilities: string[]`
8. `pluginDataDirectory: string`
9. `cacheDirectory?: string`
10. `configuration?: PluginConfigurationSnapshot`

### InitializeResponse

插件返回初始化结果和运行期能力。

建议字段：

1. `success: bool`
2. `pluginId: string`
3. `pluginVersion: string`
4. `supportsGlobalSearch: bool`
5. `declaredKeywords: string[]`
6. `supportedActions: ActionDescriptor[]`
7. `previewModes: string[]`
8. `warnings?: string[]`
9. `error?: ProtocolError`

说明：

1. 宿主可将 manifest 中的静态信息与初始化返回的动态能力合并。
2. 插件若初始化失败，宿主应隔离并跳过该插件。

### ShutdownRequest

宿主关闭插件前发送。

建议字段：

1. `reason: "host_exit" | "plugin_reload" | "disable_plugin" | "protocol_error"`

### ShutdownResponse

建议字段：

1. `success: bool`
2. `error?: ProtocolError`

## 搜索协议

### SearchRequest

搜索是插件的核心能力。

建议字段：

1. `query: string`
2. `searchMode: "global" | "keyword" | "direct"`
3. `invokedKeyword?: string`
4. `requestId: string`
5. `cancellationTokenHint?: string`
6. `limit?: number`
7. `context?: SearchContext`

### SearchContext

建议字段：

1. `platform: string`
2. `culture?: string`
3. `timeZone?: string`
4. `previousQuery?: string`
5. `triggerSource?: string`
6. `uiSurface?: string`

### SearchResponse

建议字段：

1. `success: bool`
2. `items: ResultItemDto[]`
3. `warnings?: string[]`
4. `error?: ProtocolError`
5. `metrics?: SearchMetrics`

### SearchMetrics

建议字段：

1. `durationMs?: number`
2. `itemCount?: number`
3. `cacheHit?: bool`

## 搜索结果模型

### ResultItemDto

建议字段：

1. `id: string`
2. `title: string`
3. `subtitle?: string`
4. `summary?: string`
5. `icon?: IconDescriptor`
6. `score?: number`
7. `priority?: number`
8. `tags?: string[]`
9. `actions: ActionDescriptor[]`
10. `preview?: PreviewDescriptor`
11. `payload?: object`
12. `metadata?: Record<string, string>`

说明：

1. `payload` 是动作执行时插件回收使用的数据载体。
2. `payload` 必须是可序列化对象。
3. `id` 应在同一插件结果集合内稳定可识别。

### IconDescriptor

建议支持以下形式：

1. `kind: "emoji"`, `value: string`
2. `kind: "dataUrl"`, `value: string`
3. `kind: "assetPath"`, `value: string`
4. `kind: "uri"`, `value: string`

说明：

1. 不在协议中传递 WPF ImageSource。
2. 图标渲染由宿主决定。

### PreviewDescriptor

建议字段：

1. `type: "text" | "markdown" | "html" | "image" | "json"`
2. `content?: string`
3. `contentUri?: string`
4. `lazyLoad?: bool`

说明：

1. 若 `lazyLoad=true`，后续可扩展独立 preview 加载协议。
2. 第一版可以先只支持内联内容。

## 动作执行协议

### ActionDescriptor

动作描述由插件返回，宿主负责渲染和调用。

建议字段：

1. `id: string`
2. `title: string`
3. `description?: string`
4. `shortcutHint?: string`
5. `mode?: "primary" | "secondary" | "inline"`
6. `requiresConfirmation?: bool`

### ExecuteActionRequest

建议字段：

1. `pluginId: string`
2. `resultItemId: string`
3. `actionId: string`
4. `payload?: object`
5. `context?: ActionContext`

### ActionContext

建议字段：

1. `platform: string`
2. `triggerSource?: string`
3. `windowId?: string`
4. `uiSurface?: string`

### ExecuteActionResponse

建议字段：

1. `success: bool`
2. `message?: string`
3. `postAction?: PostActionInstruction`
4. `error?: ProtocolError`

### PostActionInstruction

建议字段：

1. `closeWindow?: bool`
2. `refreshSearch?: bool`
3. `newQuery?: string`
4. `openPreview?: bool`

说明：

1. 宿主根据 `postAction` 决定 UI 行为。
2. 插件不直接操作宿主窗口对象。

## 设置协议

### SettingsSchemaRequest

建议字段：

1. `pluginId: string`
2. `culture?: string`

### SettingsSchemaResponse

建议字段：

1. `success: bool`
2. `schema?: SettingsSchema`
3. `error?: ProtocolError`

### SettingsSchema

建议字段：

1. `sections: SettingsSection[]`
2. `version: string`

### SettingsSection

建议字段：

1. `id: string`
2. `title: string`
3. `description?: string`
4. `fields: SettingField[]`

### SettingField

建议字段：

1. `key: string`
2. `title: string`
3. `description?: string`
4. `type: "string" | "number" | "boolean" | "select" | "secret" | "json"`
5. `defaultValue?: object`
6. `required?: bool`
7. `options?: SettingOption[]`

### SettingOption

建议字段：

1. `value: string`
2. `label: string`

说明：

1. 插件只描述配置结构。
2. 设置的持久化由宿主控制。
3. 这样后续可以由不同宿主以不同 UI 呈现同一套插件设置。

## 能力调用协议

### CapabilityInvokeRequest

插件若需要宿主能力，应通过该协议请求。

建议字段：

1. `capability: string`
2. `operation: string`
3. `arguments?: object`
4. `requestContext?: CapabilityContext`

### CapabilityContext

建议字段：

1. `pluginId: string`
2. `permissionScope?: string`

### CapabilityInvokeResponse

建议字段：

1. `success: bool`
2. `result?: object`
3. `error?: ProtocolError`

建议第一版 capability 语义空间：

1. `clipboard.read`
2. `clipboard.write`
3. `shell.openPath`
4. `shell.revealFile`
5. `shell.openUrl`
6. `storage.read`
7. `storage.write`
8. `http.fetch`
9. `notification.show`
10. `log.write`

说明：

1. 插件不能直接假设自己有宿主权限。
2. capability 的可用性取决于平台、宿主配置和权限授权。

## 错误模型

### ProtocolError

建议字段：

1. `code: string`
2. `message: string`
3. `details?: string`
4. `retryable?: bool`

建议错误码分层：

1. `manifest.invalid`
2. `protocol.unsupported_version`
3. `plugin.initialize_failed`
4. `plugin.search_failed`
5. `plugin.action_failed`
6. `capability.denied`
7. `capability.unavailable`
8. `runtime.process_crashed`
9. `runtime.timeout`
10. `host.internal_error`

## 日志与进度事件

### LogEvent

建议字段：

1. `level: "trace" | "debug" | "info" | "warn" | "error"`
2. `message: string`
3. `category?: string`

### ProgressEvent

建议字段：

1. `stage?: string`
2. `message?: string`
3. `percent?: number`

说明：

1. 这些事件是可选增强能力。
2. 宿主可选择显示或仅记录日志。

## 序列化规则

第一版建议遵守以下规则：

1. 使用 UTF-8 JSON。
2. 字段命名统一使用 camelCase。
3. 所有时间统一使用 ISO 8601 UTC 字符串。
4. 枚举值统一使用小写字符串或 snake_case 字符串，不使用整数枚举。
5. 不传递二进制对象，二进制内容统一转 data URL 或外部资源路径。

## 生命周期顺序

建议宿主和插件遵守以下顺序：

1. 宿主读取 manifest。
2. 宿主校验运行时和协议版本。
3. 宿主启动插件进程。
4. 宿主发送 `initialize.request`。
5. 插件返回 `initialize.response`。
6. 宿主开始允许 `search.request` 和 `action.execute.request`。
7. 宿主关闭或重载插件时发送 `shutdown.request`。

如果初始化失败：

1. 宿主应标记插件为 unavailable。
2. 宿主应记录错误并继续启动其他插件。

## 版本兼容策略

### 版本字段

建议至少维护以下版本：

1. `manifestVersion`
2. `protocolVersion`
3. `sdkVersion`
4. `pluginVersion`

### 兼容原则

1. 宿主与插件必须在 `protocolVersion` 上兼容才可正常通信。
2. `pluginVersion` 由插件自身语义版本控制。
3. `manifestVersion` 用于宿主解析插件包结构。
4. SDK 版本仅作为开发支持信息，不直接决定运行兼容性。

### 推荐策略

1. 主版本不兼容：拒绝加载。
2. 次版本向后兼容：允许加载，宿主可降级能力。
3. 修订版本差异：默认允许。

### 兼容示例

1. 宿主支持 `1.x`，插件是 `1.2`，允许加载。
2. 宿主支持 `1.x`，插件是 `2.0`，拒绝加载并给出提示。
3. 宿主支持 `1.3`，插件仅用到 `1.1` 字段，允许加载。

## 第一版协议刻意不做的事情

以下内容不纳入第一版协议核心范围：

1. 插件间直接通信
2. 远程插件市场协议
3. 流式增量预览协议
4. 长连接订阅式后台任务协议

这些能力可以在后续版本扩展。

## 对后续任务的输入

本协议草案完成后，后续任务应直接基于本文推进：

1. B3 设计 `plugin.json` 时，以 `PluginManifest` 为蓝本。
2. B4 设计 TypeScript SDK 时，以 `initialize/search/executeAction` 为最小实现面。
3. B5 设计 .NET 外部运行时时，必须复用同一套消息与 DTO。
4. B6 设计宿主能力时，以 `CapabilityInvokeRequest` 为宿主能力入口。

## 本阶段完成标准

当以下条件成立时，可视为 B2 完成：

1. 已有一套统一的协议对象草案。
2. 已有生命周期、搜索、动作、设置、能力调用五类消息定义。
3. 已有错误模型、序列化规则和版本兼容策略。
4. TypeScript SDK 和 .NET SDK 都能基于本文档设计最小实现。
