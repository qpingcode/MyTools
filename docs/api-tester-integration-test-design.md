# API Tester 无 Host 集成测试概要设计

## 目标

在不启动 MyTools Desktop 的情况下，覆盖正式前端、后端入口及消息协议：

```text
Playwright → dist/web/index.html → TestHostBroker → dist/backend/index.mjs
             chrome.webview shim      named pipe
```

不模拟 WebView2/WPF 行为；CSP、虚拟路径和窗口生命周期由少量 Desktop 冒烟测试覆盖。

## 原则

- 测试同一次正式构建生成的 `dist`，不直接 import `src/backend/index.mts`。
- 后端作为独立 Node 子进程运行，沿用 stdin bootstrap、named pipe、握手和心跳。
- Broker 只处理协议和 Host capability，不实现 `load`、`save`、`start` 等业务路由。
- 生产代码不增加测试分支；每个 Playwright worker 使用独立进程、pipe、端口和数据目录。

## 组件

- `IntegrationRuntime`：创建并清理 worker 的全部资源。
- `BackendProcess`：按 `dist/plugin.json` 启动后端，设置 `MYTOOLS_PLUGIN_DATA_DIR`，收集 stdout/stderr。
- `TestHostBroker`：完成两侧握手、身份盖章、消息转发、correlation、心跳和 Host event。
- `WebViewBridgeShim`：在页面加载前注入 `window.chrome.webview`，只转发 envelope。
- `HostCapabilityRegistry`：按白名单模拟 `path.pick` 等 `host.call.*`。
- `StaticPluginServer`：按 manifest 提供 `dist/web` 静态资源；API fixture 使用独立 HTTP 服务。

## 生命周期

1. 测试套件执行一次 `npm run build`。
2. Broker 监听唯一 named pipe，启动 `dist/backend/index.mjs` 并写入 bootstrap 信息。
3. Node 完成握手；Playwright 注入 bridge 后打开 manifest 声明的 Web 入口。
4. Web 完成握手；Broker 初始化 Node，并向 Web 发送 `host.event.initialize`。
5. Web 的 `plugin.call.*` 原样转发至真实后端，响应按 correlation 返回。
6. 测试结束后关闭页面、后端、pipe、HTTP 服务和临时目录。

## 测试范围

- 启动、workspace 加载/保存及刷新后持久化。
- HTTP 请求、Cookie、环境变量、历史记录和 collection run。
- `path.pick` 的完整 Web → Node → Host capability 往返。
- 初始化语言、运行时语言切换及后端错误到 UI 的映射。
- 超时、capability 拒绝和后端断线等协议故障。

业务故障优先通过真实输入制造，例如损坏 workspace、无效文件或 HTTP 超时；确定性较差的内部故障留在单元/组件测试中，不由 Broker 伪造业务响应。

## 验收标准

- Fixture 不包含 API Tester 业务路由分发。
- 前后端来自同一份 `dist`，入口从 `dist/plugin.json` 解析。
- 修改真实 handler、存储或 SDK 通信会影响集成测试结果。
- Playwright 可并行运行且数据互不污染。
- 测试结束后无遗留进程、pipe 或监听端口。
- 失败报告包含浏览器 trace、截图及后端 stdout/stderr。
