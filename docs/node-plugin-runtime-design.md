# MyTools Node 插件运行时设计

## 目标

这一阶段不再让插件直接负责主列表渲染。

新的目标是：

1. 主列表继续由宿主统一渲染
2. 插件逻辑改为运行在独立 Node.js 进程中
3. 宿主与插件进程通过 `stdio` 通信
4. 消息格式采用 `NDJSON` 分帧的 `JSON-RPC` 风格
5. 插件详情页允许返回自定义 `html/css/js`
6. 主进程、插件进程、详情页三者之间支持双向通信

这一方向参考 Raycast 的产品模式，但不假设照搬其私有实现。

可借鉴的是：

1. 宿主负责统一搜索入口
2. 宿主负责统一主列表渲染
3. 宿主负责统一 action 触发
4. 插件负责业务逻辑和数据返回
5. 详情页在必要时允许更自由的自定义展示

## 当前代码锚点

当前主列表链路已经存在，下一阶段应当复用，而不是推翻。

### 主列表聚合入口

`MyTools.Plugins/Searcher.cs` 中的 `GlobalSearchAsync` 已经承担全局搜索聚合职责：

1. 遍历所有全局搜索插件
2. 并发调用 `plugin.SearchAsync(...)`
3. 聚合 `ResultItem`
4. 返回统一 `Result`

这意味着下一阶段 Node 插件如果要进入主列表，最终仍然应该通过 `IPlugin.SearchAsync(...)` 返回宿主可识别的 `ResultItem` 集合。

### 主列表展示入口

`MyTools.Desktop/ViewModels/SearchViewModel.cs` 当前默认创建 `BasicListViewModel`。

`MyTools.Desktop/Views/Components/Search/BasicListViewModel.cs` 中：

1. `PerformSearch(...)` 会调用 `ISearcher.SearchAsync(...)`
2. 再把返回结果写入 `SearchResults`
3. 最终由 `BasicListView.xaml` 统一渲染

因此，下一阶段主列表目标应当明确为：

`Node Plugin Runtime -> Host Adapter -> Searcher.GlobalSearchAsync -> BasicListViewModel -> BasicListView`

## 核心决策

### 1. 主列表继续交给宿主

主列表不再由插件输出任意 HTML。

主列表由宿主统一渲染的原因：

1. 键盘导航需要统一
2. 动作栏需要统一
3. 排序和选中状态需要统一
4. 不同插件之间的视觉一致性需要统一
5. 历史权重和全局混排已经在宿主侧存在

因此，插件在主列表阶段只返回结构化数据，不返回 DOM。

### 2. 插件逻辑运行在独立 Node.js 进程中

这里选择“独立进程”而不是“Node worker thread 直连宿主”。

原因：

1. `worker_threads` 是 Node 进程内部并发模型，不是 `.NET <-> Node` 的天然边界
2. 独立进程更适合故障隔离
3. 独立进程更适合超时控制和重启
4. 独立进程更适合未来多平台宿主
5. `.NET` 启动 `node` 子进程的工程复杂度最低

如果插件内部以后需要 CPU 密集任务，Node 进程内部仍然可以自行使用 `worker_threads`，但那是插件运行时内部实现细节，不应成为宿主协议的一部分。

### 3. 传输层采用 `stdio`

宿主启动插件进程后，双方通过：

1. `stdin` 向插件发送请求
2. `stdout` 接收插件返回消息
3. `stderr` 只用于诊断日志

选择 `stdio` 的原因：

1. 跨平台最简单
2. 不需要额外端口管理
3. 不需要额外命名管道握手
4. 便于调试和录制协议日志
5. 适合作为第一版稳定协议

### 4. 协议层采用 `NDJSON` 分帧的 `JSON-RPC` 风格

这里的意思是：

1. 每条消息占一行
2. 每行是一个完整 JSON 对象
3. 消息字段采用 `JSON-RPC` 风格，例如 `id`、`method`、`params`、`result`、`error`

示例：

```json
{"jsonrpc":"2.0","id":"1","method":"search","params":{"query":"hello 123"}}
{"jsonrpc":"2.0","id":"1","result":{"items":[{"id":"r1","title":"Hello 123"}]}}
```

这样做的原因：

1. `JSON-RPC` 适合 request/response 和 notification
2. `NDJSON` 适合 `stdio` 流式读取
3. 宿主实现时不需要 `Content-Length` 解析器
4. 诊断日志更直观

约束：

1. `stdout` 不能混入普通文本日志
2. 普通日志一律走 `stderr`
3. 每个 JSON 消息必须单行输出

## 主列表协议

### 目标

插件返回结构化列表数据，宿主将其转换为 `ResultItem`，最终进入 `GlobalSearchAsync` 的聚合结果中。

### 推荐消息

#### 宿主发起搜索

```json
{"jsonrpc":"2.0","id":"search-1","method":"search","params":{"query":"hello 123","mode":"global"}}
```

#### 插件返回主列表

```json
{"jsonrpc":"2.0","id":"search-1","result":{"items":[{"id":"item-1","title":"Hello 123","subtitle":"From node plugin","icon":{"kind":"emoji","value":"👋"},"actions":[{"id":"open-detail","title":"Open Detail"},{"id":"copy-text","title":"Copy Text"}]}]}}
```

### 主列表数据模型

建议插件内部可以使用类似 `<list>/<item>/<icon>/<action>` 的概念，但跨进程传输层不要真的发送标签字符串，而是发送 JSON AST。

建议结构：

```json
{
  "items": [
    {
      "id": "item-1",
      "title": "Hello 123",
      "subtitle": "From node plugin",
      "icon": {
        "kind": "emoji",
        "value": "👋"
      },
      "actions": [
        {
          "id": "open-detail",
          "title": "Open Detail"
        }
      ]
    }
  ]
}
```

映射关系建议如下：

1. `<list>` -> `items[]`
2. `<item>` -> 单个结果对象
3. `<icon>` -> `icon`
4. `<action>` -> `actions[]`

这样既保留你想要的固定格式语义，也避免 XML/HTML 解析和转义成本。

### 宿主侧落点

宿主在适配 Node 插件结果时，应当把结构化 `items` 转换为现有 `ResultItem`：

1. `title` -> `ResultItem.Title`
2. `subtitle` -> `ResultItem.SubTitle`
3. `icon` -> `ResultItem.Icon`
4. `actions` -> `ResultItem.AllowedActions`
5. `id` -> `ResultItem.ResultKey`

这样主列表不需要新增第二套渲染器。

## 详情页协议

### 目标

插件详情页允许返回自定义 `html/css/js`，但生命周期仍由宿主管理。

### 推荐触发方式

详情页可以由以下两类场景进入：

1. 用户在主列表中选中某个 item 后执行 `open-detail`
2. 用户通过短语命中某个插件后直接进入插件详情模式

### 详情页返回模型

插件可以返回：

1. `htmlEntry`
2. `cssEntry`
3. `jsEntry`
4. `initialState`
5. `capabilities`

示例：

```json
{"jsonrpc":"2.0","id":"detail-1","result":{"view":{"type":"web-detail","htmlEntry":"web/detail.html","cssEntry":"web/detail.css","jsEntry":"web/detail.js","initialState":{"query":"hello 123","itemId":"item-1"}}}}
```

## 主进程、插件进程、详情页的通信关系

下一阶段不应只有“宿主 -> WebView”的单向消息，而应升级为三段桥接。

```text
Search Box / Host
    -> stdio NDJSON JSON-RPC
Node Plugin Process
    -> result items / detail view description
Host
    -> WebView2 postMessage
Detail HTML Page

Detail HTML Page
    -> WebView2 postMessage
Host
    -> stdio NDJSON JSON-RPC
Node Plugin Process
```

### 三段职责

#### 1. Host <-> Node Process

负责：

1. 搜索请求
2. 列表结果返回
3. 动作调用
4. 详情页初始化数据
5. 插件状态和错误上报

#### 2. Host <-> Web Detail Page

负责：

1. 页面初始化
2. 页面事件转发
3. 页面调用宿主动作
4. 页面实时状态更新

#### 3. Web Detail Page <-> Node Process

这两者不要直接建立本地进程连接。

建议所有消息都经由宿主中转：

`Detail Page -> Host -> Node Process`

`Node Process -> Host -> Detail Page`

这样做的原因：

1. 宿主可以统一做权限控制
2. 宿主可以记录协议日志
3. 宿主可以做生命周期清理
4. Web 页面不需要知道本地 IPC 细节

## 推荐消息集合

### 宿主到插件进程

1. `initialize`
2. `search`
3. `invokeAction`
4. `openDetail`
5. `detailEvent`
6. `dispose`

### 插件进程到宿主

1. `searchResult`
2. `showDetail`
3. `updateDetailState`
4. `updateStatus`
5. `log`
6. `error`

### 宿主到 Web 详情页

1. `initialize-detail`
2. `detail-state`
3. `detail-event-result`

### Web 详情页到宿主

1. `ready`
2. `detail-event`
3. `request-host-action`
4. `log`

## 插件目录建议

```text
plugins/
  hello-search/
    plugin.json
    backend/
      index.mjs
    web/
      detail.html
      detail.css
      detail.js
```

## 下一版 manifest 建议

```json
{
  "id": "hello-search",
  "name": "Hello Search",
  "version": "0.2.0",
  "runtime": "node",
  "entry": "backend/index.mjs",
  "protocolVersion": "2.0",
  "keywords": ["hello"],
  "detail": {
    "type": "web",
    "entry": "web/detail.html"
  }
}
```

## Hello Search 重构方向

`Hello Search` 下一步不应继续停留在“一个 HTML 页面直接吃搜索文本”。

建议改成：

1. `backend/index.mjs` 接收 `search` 请求
2. 返回结构化列表数据
3. 宿主把列表结果放进 `GlobalSearchAsync` 的最终结果
4. 用户进入详情页时，插件返回 `web/detail.html`
5. 宿主使用 `WebView2` 打开详情页
6. 详情页事件再通过宿主转发给 Node 进程

## 第一阶段实施顺序

1. 为 Node 插件定义 `stdio + NDJSON + JSON-RPC` 协议
2. 在宿主中增加 `NodePluginProcessHost`
3. 为 Node 插件实现 `IPlugin` 适配层
4. 让 `Searcher.GlobalSearchAsync` 能聚合 Node 插件返回的 `ResultItem`
5. 先保证主列表完整进入 `BasicListView`
6. 再增加 `showDetail` 和 `detailEvent` 通道
7. 最后把 `Hello Search` 改造成 `backend + web detail` 双层插件

## 明确不做的事情

这一阶段先不做：

1. 插件市场
2. 多进程池调度
3. 命名管道替换 `stdio`
4. `.NET` 外部运行时
5. 浏览器端直接连本地 IPC

## 后续演进方向

当 `stdio` 方案稳定后，可以再考虑把传输层升级为：

1. Windows 上用 `Named Pipe`
2. macOS/Linux 上用 `Unix Domain Socket`

但那应当是传输层替换，不应影响上层 `JSON-RPC` 消息模型。
