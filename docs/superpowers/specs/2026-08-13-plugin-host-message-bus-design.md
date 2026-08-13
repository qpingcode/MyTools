# 插件宿主中央消息总线设计

## 背景

当前 Node 插件由 WPF 宿主启动。宿主与 Node 通过 stdin/stdout 上的逐行 JSON-RPC 通信，WebView2 前端再通过 `postMessage` 由具体 WPF 控件转发到 Node。这个模型足以支持现有插件，但协议路由、UI 控件、进程生命周期和宿主能力绑定较紧，难以同时支持不可信第三方插件、自动恢复、多进程插件和独立诊断工具。

本设计允许直接升级现有插件协议，不要求兼容当前 `plugin.json` 和 `@qping/plugin-common` API。协议保持跨平台，首个实现仅支持 Windows。

## 目标

1. 将插件通信和生命周期从具体 WPF 控件中移出。
2. 为 WebView、Node Worker、宿主能力和诊断工具提供统一消息模型。
3. 默认将第三方插件视为不可信，仅授予声明且获批的能力。
4. 支持断线检测、自动重启、重连和多个插件 Worker。
5. 使协议与平台无关，Windows 首先使用命名管道实现。
6. 保持组件边界清晰，允许独立测试协议、路由、权限和 transport。

## 非目标

1. 本阶段不实现 macOS 或 Linux 宿主。
2. 不允许 WebView 直接连接 Node 或直接访问系统能力。
3. 不提供任意插件间通信；跨插件通信必须通过显式宿主能力。
4. 不保证兼容旧版插件协议和 SDK。

## 方案选择

采用“宿主中央消息总线 + 插件会话 + capability 网关”。

未选择继续增强 stdio，是因为 stdio 与具体子进程生命周期绑定，不利于独立重连、多 Worker 和外部诊断。未选择 Node WebSocket 服务，是因为端口、Origin、认证和不可信页面隔离会增加安全面，并削弱宿主的统一控制。

## 模块边界

### MyTools.Protocol

包含可序列化的纯协议类型：

- 消息 envelope、请求、响应和事件
- 协议版本与握手模型
- 标准错误码
- capability 标识和授权结果
- 插件状态与诊断模型

该模块不依赖 WPF、Windows API、Node 或宿主容器。

### MyTools.Host.Core

包含与 UI 和平台无关的宿主核心：

- `MessageBus`：endpoint 注册、请求路由、响应关联和事件订阅
- `PluginSessionManager`：创建、查找、停止和恢复插件会话
- `PluginSession`：插件身份、连接、进程树、授权和健康状态
- `CapabilityGateway`：声明校验、授权决策、参数校验和能力调用
- 超时、消息大小、重启策略及诊断事件

### MyTools.Host.Transports

定义统一的 `IMessageTransport`，负责连接、收发帧和断线通知，不承担业务路由。

首批实现：

- `NamedPipeTransport`：WPF 宿主与 Node 进程
- `WebView2Transport`：宿主与插件 WebView

未来可增加 Unix Domain Socket，而无需修改消息总线和插件协议。

### MyTools.Host.Windows

实现 Windows capability，例如配置、剪贴板、热键、手势、窗口、Shell 和通知。实现类型不直接暴露给插件。

### Node SDK

负责命名管道连接、握手、协议编解码、请求处理、事件发布、心跳和自动重连。插件业务只注册 handler，不直接操作 transport。

### Web SDK

负责通过 WebView2 transport 调用消息总线、订阅事件和处理响应。Web SDK 不感知 Node 的进程、管道或重启细节。

## 插件会话模型

每个 manifest entry 对应一个 `PluginSession`。会话包含：

- 稳定的插件 ID 和本次运行的 session ID
- manifest 声明及已批准 capability
- 一个主 Node endpoint 和零个或多个 Worker endpoint
- 零个或多个 WebView endpoint
- 进程树、连接状态、健康信息和重启计数

窗口只是 WebView endpoint，不拥有 Node 生命周期。关闭窗口仅注销对应 endpoint；停止插件、重新加载插件或退出宿主时才停止会话。

状态机为：

```text
Created -> Starting -> Handshaking -> Ready
                                |       |
                                v       v
                              Stopped  Degraded -> Restarting -> Handshaking
```

超过重启次数上限后进入 `Stopped`，必须由用户或宿主策略重新启动。

## 统一消息协议

所有 transport 使用同一 envelope：

```json
{
  "version": "3.0",
  "messageId": "01J...",
  "pluginId": "settings:main",
  "endpointId": "node-main",
  "kind": "request",
  "route": "plugin.call.saveConfiguration",
  "payload": {},
  "error": null
}
```

字段规则：

- `version`：协议主次版本
- `messageId`：请求关联 ID；事件同样拥有唯一 ID
- `pluginId`：经过握手确认的插件身份
- `endpointId`：会话内连接身份
- `kind`：`request`、`response` 或 `event`
- `route`：受约束的路由名
- `payload`：路由对应的结构化数据
- `error`：响应失败时的标准错误

命名管道采用 4 字节无符号长度前缀加 UTF-8 JSON 帧。接收端必须在分配完整缓冲区前检查长度上限。WebView2 transport 使用相同 envelope，但由 WebView2 提供消息边界。

## 路由规则

- `plugin.call.*`：WebView 或宿主调用插件 Node handler
- `host.call.*`：Node 或 WebView调用受控宿主 capability
- `plugin.event.*`：插件发布的业务事件
- `host.event.*`：语言、主题、查询和生命周期等宿主事件
- `diagnostics.*`：只读诊断接口

消息总线按 `pluginId + route/topic` 隔离。默认禁止跨插件路由。响应只返回发起请求的 endpoint；事件只发给同一插件会话中已订阅的 endpoint。

## 核心数据流

### WebView 调用 Node

```text
Web SDK
  -> WebView2Transport
  -> MessageBus
  -> PluginSession
  -> NamedPipeTransport
  -> Node SDK handler
  -> 原路返回 response
```

具体 WPF 控件只负责托管 WebView2Transport，不解析插件业务 action。

### Node 调用宿主能力

```text
Node SDK
  -> NamedPipeTransport
  -> MessageBus
  -> CapabilityGateway
  -> Windows capability adapter
  -> 原路返回 response
```

### Node 发布事件

```text
Node SDK
  -> MessageBus
  -> 插件会话内订阅匹配
  -> 一个或多个 WebView endpoint
```

## 权限与安全

第三方插件默认不可信。

1. manifest 必须声明所需 capability，例如 `clipboard.read`、`configuration.write`。
2. 安装或首次调用敏感能力时由用户批准；授权结果按插件 ID 和版本策略持久化。
3. 每次调用都由 `CapabilityGateway` 校验插件身份、声明、授权和参数，不因同一进程已通过握手而跳过。
4. Node 启动时获得随机管道名和一次性启动令牌。握手同时验证插件 ID、协议版本、令牌和进程 ID。
5. 命名管道 ACL 仅允许当前 Windows 用户和必要的系统主体访问。
6. Worker 必须由当前会话注册，获得独立 endpoint ID 和最小 capability 集合。
7. 诊断 endpoint 默认只读取状态、时延和错误摘要，不读取业务 payload、凭据或敏感数据。
8. capability 参数使用路由级 DTO 校验，禁止把宿主内部对象或任意命令执行接口暴露给插件。

## 故障处理

- 启动、握手、请求和空闲心跳分别配置超时。
- 每个协议帧和各路由 payload 设置大小上限。
- 非法帧只关闭对应连接并记录诊断，不终止消息总线。
- transport 断线后，会话立即进入 `Degraded`，所有未完成请求返回 `TransportDisconnected`。
- 重启期间新请求直接返回 `PluginUnavailable`，不静默排队。
- 插件异常退出采用带抖动的指数退避，并设置时间窗口内的最大重启次数。
- 每次重启使用新的管道名、令牌、session ID 和 endpoint ID。
- Node stdout/stderr 不承载协议，完全作为日志流采集，并附加插件和进程身份。
- 宿主退出时先停止接受请求，再通知会话关闭，最后在超时后终止残留进程树。

标准错误至少包括：

- `ProtocolMismatch`
- `UnauthorizedCapability`
- `InvalidPayload`
- `MessageTooLarge`
- `RouteNotFound`
- `RequestTimeout`
- `TransportDisconnected`
- `PluginUnavailable`
- `PluginCrashed`

## 可观测性

Host Core 记录结构化诊断事件：

- 会话状态变化
- transport 连接和断开
- 请求路由、耗时和结果类型
- capability 授权和拒绝
- 插件重启次数及退出码
- 丢弃的超大或非法消息

默认日志不记录完整业务 payload。诊断工具通过受限 endpoint 读取会话快照和聚合指标。

## 测试策略

### 单元测试

- envelope 和长度前缀帧的编解码
- 协议版本与握手校验
- 请求响应关联和超时
- topic 订阅与插件隔离
- capability 声明、授权和参数校验
- 会话状态转换和重启上限

### 组件测试

使用内存 fake transport 验证消息总线、多个 endpoint、断线和乱序响应。使用 fake process controller 验证启动失败、崩溃和退避重启。

### 集成测试

使用真实测试 Node 插件验证命名管道握手、双向调用、事件、超大消息拒绝、进程崩溃和自动重连。

### 端到端测试

覆盖：

```text
WebView -> Node -> host capability -> Node -> WebView
```

并验证未授权 capability、Node 重启、窗口关闭后重新打开及多个 Worker 的行为。

## 实施边界

实施应分阶段完成，但以新协议整体替换旧协议：

1. 建立协议、transport 抽象和 fake transport 测试。
2. 实现消息总线、capability 网关和插件会话状态机。
3. 实现 Windows 命名管道、Node SDK 和进程控制。
4. 将 WebView2 接入统一 transport。
5. 迁移插件 manifest、Node SDK 和示例插件。
6. 删除旧 stdio JSON-RPC 和具体控件中的业务转发代码。
7. 完成安全、故障恢复和端到端验证。

旧协议不会长期并存，避免宿主维护两套权限和生命周期语义。
