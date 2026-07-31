# MyTools 插件 MVP 设计

## 目标

用最短路径做出一个可运行的插件闭环，同时不堵死后续扩展。

当前 MVP 只解决一件事：

1. 宿主提供统一顶部搜索框
2. 宿主从插件目录加载插件
3. 宿主打开插件自己的 HTML 页面
4. 宿主把搜索框内容发送给插件页面
5. 插件自行处理并在自己的页面中展示内容

## MVP 范围

### 本期必须做

1. 一个插件目录，例如 `plugins/`
2. 一个最小 `plugin.json`
3. 一个插件 HTML 页面
4. 一个最小消息桥接协议
5. 一个示例前端插件
6. 宿主中的最小插件加载器
7. 一个宿主内嵌 WebView 容器

### 本期不做

1. Node.js 后端运行时
2. .NET 外部插件运行时
3. 完整 capability 权限系统
4. 完整 settings schema
5. 插件热更新
6. 插件市场
7. macOS 宿主实现
8. 旧插件自动迁移

## 最小模块

MVP 只保留 4 个模块概念：

1. `Host`
2. `Plugin Manifest`
3. `Plugin WebView`
4. `HTML Plugin Page`

## 最小架构

```text
MyTools Desktop Host
    -> scan plugins/
    -> read plugin.json
    -> open plugin webview
    -> send search text to plugin page

Plugin HTML Page
    -> receive search text
    -> render plugin UI itself
```

## MVP 目录结构

建议最小目录结构如下：

```text
plugins/
  hello-search/
    plugin.json
    web/
      index.html
      main.js
      style.css
```

## MVP 的 `plugin.json`

第一版只保留最小字段：

```json
{
  "id": "hello-search",
  "name": "Hello Search",
  "version": "0.1.0",
  "runtime": "web",
  "entry": "web/index.html",
  "protocolVersion": "1.0",
  "keywords": ["hello"]
}
```

### 为什么只保留这些字段

1. `id` 用于唯一标识插件
2. `name` 用于展示
3. `version` 用于后续升级
4. `runtime` 用于决定如何启动
5. `entry` 用于找到 HTML 入口文件
6. `protocolVersion` 用于后续兼容
7. `keywords` 用于最小路由

## MVP 消息桥接

MVP 只保留两个宿主到插件页面的消息：

1. `initialize`
2. `search`

### initialize

```json
{
  "type": "initialize",
  "payload": {
    "protocolVersion": "1.0",
    "hostVersion": "1.0.0",
    "pluginId": "hello-search"
  }
}
```

### search

```json
{
  "type": "search",
  "payload": {
    "query": "hello world"
  }
}
```

## MVP 页面桥接接口

宿主至少需要提供一个消息发送入口给插件页面，例如：

1. WebView `postMessage`
2. WebView 注入桥接对象

插件页面至少需要能接收：

1. `initialize`
2. `search`

插件页面可以选择性向宿主发送：

1. `ready`
2. `log`
3. `requestHostAction`

最小 `ready` 示例：

```json
{
  "type": "ready",
  "payload": {
    "pluginId": "hello-search"
  }
}
```

## MVP 宿主展示模型

宿主在 MVP 阶段只需要负责：

1. 显示顶部统一搜索框
2. 选择目标插件
3. 打开对应插件 HTML 页面
4. 将搜索文本转发给插件页面

宿主不负责：

1. 渲染插件内部列表
2. 渲染插件内部详情
3. 维护插件内部状态
4. 解释插件自己的页面布局

## MVP 宿主实现建议

宿主侧只做最小改造：

1. 启动时扫描 `plugins/` 目录
2. 读到 `runtime=web` 的插件后，用 WebView 加载 `entry`
3. 在顶部搜索框内容变化时，把搜索文本通过消息桥接发给插件页面
4. 宿主只负责页面生命周期，不负责页面内部结果渲染

### 宿主不需要立刻做的事情

1. 不需要完整插件生命周期管理器
2. 不需要完整插件健康检查系统
3. 不需要并发调度框架
4. 不需要 capability 权限中心
5. 不需要统一结果 DTO 渲染器

## MVP 插件实现建议

第一个插件只需要实现：

1. 提供一个本地 HTML 页面
2. 监听宿主发送的 `initialize`
3. 监听宿主发送的 `search`
4. 根据搜索文本自行渲染页面内容

这样可以最快验证：

1. 插件包结构是否可行
2. 宿主和插件页面通信是否可行
3. 宿主是否能最小成本承载插件自定义 UI

## 必须保留的扩展点

虽然 MVP 很小，但以下扩展点必须现在就预留：

1. `protocolVersion` 字段必须保留
2. `runtime` 字段必须保留，后续支持 `node`、`dotnet`
3. 消息结构必须保留 `type` 和 `payload` 两层
4. 插件页面与宿主之间的桥接必须允许后续加入双向消息
5. 插件目录结构必须允许后续加入 `assets/`、`settings/`、`backend/` 等目录
6. 当前 `web` 插件模型后续必须允许扩展成“HTML 页面 + Node/.NET 后端”的双层插件模型

## 推荐实施顺序

1. 先实现 `plugins/` 目录扫描
2. 再实现 `plugin.json` 解析
3. 再实现 WebView 加载本地插件页面
4. 再实现 `initialize` 消息桥接
5. 再实现 `search` 消息桥接
6. 最后让插件页面根据搜索文本自行展示内容

## MVP 完成标准

满足以下条件即可认为 MVP 完成：

1. 新建一个插件目录后，无需修改主项目代码即可被识别
2. 宿主能成功打开该插件 HTML 页面
3. 宿主能把搜索词发送给插件
4. 插件能在自己的页面中显示对应内容

## MVP 之后的直接下一步

MVP 完成后再继续做：

1. 增加插件页面到宿主的双向调用
2. 增加 capability 调用
3. 增加 Node 后端运行时
4. 增加 .NET 外部运行时
5. 增加插件设置和权限控制

Node 运行时、主列表宿主渲染、自定义详情页桥接的下一阶段设计，见 `docs/node-plugin-runtime-design.md`。
