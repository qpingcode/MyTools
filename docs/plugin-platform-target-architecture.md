# MyTools 插件平台目标架构

## 目的

本文档用于落实 Roadmap 中的 B1，明确 MyTools 后续插件平台的目标形态，使后续的协议设计、插件包格式、运行时实现和现有插件迁移都能在同一架构假设下推进。

## 架构目标

插件平台需要同时满足以下目标：

1. 插件可独立开发，不依赖主项目源码。
2. 插件可独立打包并投放到插件目录自动加载。
3. 插件优先支持 TypeScript，同时保留 .NET 插件能力。
4. 插件默认按进程外模式运行，避免与宿主强耦合。
5. 插件协议与宿主能力边界清晰，便于未来支持 macOS。

## 核心设计结论

### 结论 1：插件平台的中心不是 `IPlugin` 对象，而是协议

后续宿主不再以“拿到某个程序集中的 `IPlugin` 实例”为插件接入中心，而是以“加载插件包、启动对应运行时、通过协议通信”为中心。

### 结论 2：默认采用进程外运行时

默认形态如下：

1. TypeScript 插件运行在 Node.js 子进程中。
2. .NET 插件运行在独立 .NET 进程中。
3. 宿主通过统一协议与它们通信。

只有在兼容旧插件时，才允许保留少量过渡期进程内插件模式。

### 结论 3：平台能力属于宿主，不属于插件

以下能力统一视为 host capabilities，而不是普通插件直接访问的 API：

1. 剪贴板
2. 打开文件、目录、URL
3. 通知
4. 热键
5. 托盘
6. 系统消息
7. 平台 Shell 集成

插件只能通过受控 capability 接口申请使用这些能力。

## 目标模块

建议后续形成以下模块结构。

### 1. Plugin Protocol

建议命名：`MyTools.PluginProtocol`

职责：

1. 定义插件协议 DTO
2. 定义请求、响应、动作、结果、预览、设置 schema
3. 定义协议版本与错误模型

约束：

1. 不依赖 WPF
2. 不依赖 Windows API
3. 不依赖宿主内部服务
4. 不依赖具体运行时实现

### 2. Host Core

建议命名：`MyTools.Host.Core`

职责：

1. 扫描插件目录
2. 读取和校验 manifest
3. 选择运行时并启动插件
4. 管理插件生命周期
5. 聚合搜索结果
6. 路由动作执行
7. 调用宿主能力接口
8. 记录日志和诊断信息

约束：

1. 不直接依赖 WPF 视图或控件
2. 不直接依赖某个具体插件实现类
3. 不通过 `ServiceLocator` 暴露依赖

### 3. Host Platform Adapter

建议拆分：

1. `MyTools.Host.Windows`
2. `MyTools.Host.Mac`

职责：

1. 实现平台窗口生命周期
2. 实现热键、托盘、手势等平台能力
3. 实现剪贴板、Shell、通知、文件打开等 capability
4. 提供平台相关诊断信息

约束：

1. 平台 API 不直接暴露给插件
2. 所有平台能力都通过 Host Core 调度

### 4. Plugin Runtime Node

建议命名：`MyTools.PluginRuntime.Node`

职责：

1. 启动 Node.js 插件进程
2. 建立标准化通信通道
3. 转换协议消息
4. 管理 Node 插件生命周期

### 5. Plugin Runtime DotNet

建议命名：`MyTools.PluginRuntime.DotNet`

职责：

1. 启动外部 .NET 插件进程
2. 建立统一通信通道
3. 对接相同协议
4. 兼容旧插件迁移期的需求

### 6. Plugin SDK for TypeScript

建议命名：`mytools-plugin-sdk-ts`

职责：

1. 封装协议细节
2. 提供插件入口定义
3. 提供 manifest 校验工具
4. 提供开发模板和本地调试能力

### 7. Plugin SDK for .NET

建议命名：`MyTools.PluginSdk.DotNet`

职责：

1. 为 .NET 插件提供与 TypeScript 一致的抽象
2. 提供进程外插件入口约定
3. 提供兼容迁移支持

### 8. Builtin Plugins Compatibility Layer

建议命名：`MyTools.BuiltinPlugins` 或 `MyTools.PluginCompatibility`

职责：

1. 承接现有内置插件过渡期兼容
2. 避免旧模型和新模型在宿主主干中混杂
3. 为老插件迁移提供缓冲层

## 模块依赖规则

后续必须遵守以下依赖方向：

1. Platform Adapter 可以依赖 Host Core 和 Plugin Protocol。
2. Host Core 可以依赖 Plugin Protocol。
3. Plugin Runtime 可以依赖 Plugin Protocol。
4. SDK 可以依赖 Plugin Protocol。
5. 插件实现只能依赖 SDK 和协议，不应依赖 Host Core 或 Platform Adapter。
6. Host Core 不应依赖具体插件实现。

可简化为：

1. 协议在最底层。
2. 核心宿主和运行时围绕协议工作。
3. 平台层和插件层都不能跨越协议直接耦合。

## 运行时总览

### TypeScript 插件运行流程

1. 宿主扫描插件目录。
2. Host Core 读取 `plugin.json`。
3. 若 `runtime=node`，则交给 Node Runtime。
4. Node Runtime 启动插件进程。
5. 宿主通过协议发送 `initialize`、`search`、`executeAction` 等请求。
6. 插件结果返回给 Host Core。
7. Host Core 将结果交给宿主 UI 展示。

### .NET 插件运行流程

1. 宿主扫描插件目录。
2. Host Core 读取 `plugin.json`。
3. 若 `runtime=dotnet`，则交给 DotNet Runtime。
4. DotNet Runtime 启动插件进程。
5. 宿主通过统一协议进行通信。
6. 返回结果交由 Host Core 聚合。

### 旧插件兼容流程

1. 宿主可保留一层兼容装配入口。
2. 旧插件暂时继续按内置模式运行。
3. 兼容层与新平台边界隔离，避免继续污染主干架构。

## 术语表

### 宿主 Host

负责启动应用、管理插件、提供系统能力、渲染结果的主程序。

### 插件 Plugin

基于插件协议实现的独立扩展单元，可以是 TypeScript 插件，也可以是 .NET 插件。

### 插件包 Plugin Package

插件的可分发单元，通常包含：

1. `plugin.json`
2. 插件入口文件
3. 可选资源文件

### 运行时 Runtime

负责启动插件进程并桥接协议通信的中间层。

### 协议 Protocol

宿主与插件之间通信所遵守的统一消息模型和行为约定。

### 能力 Capability

由宿主提供、插件通过受控方式使用的平台能力，如剪贴板、文件打开、通知等。

### 插件清单 Manifest

用于描述插件元数据、运行时类型、入口、权限、平台支持范围的声明文件。

## 能力边界

插件能力应拆成三类。

### 第一类：纯业务能力

特点：

1. 无平台依赖
2. 易于迁移到 TypeScript
3. 可直接纳入新协议体系

候选示例：

1. Calculator
2. JsonFormatter
3. XmlFormatter
4. SearchEngine

### 第二类：需要宿主能力支持的半可移植能力

特点：

1. 插件自身逻辑可移植
2. 但需要宿主提供网络、存储、打开 URL 等能力

候选示例：

1. Translator
2. CommandRunner 的部分能力

### 第三类：平台特化能力

特点：

1. 强依赖 Windows API 或系统行为
2. 不适合作为第一批通用插件协议的设计中心

候选示例：

1. Clipboard 监听与历史集成
2. ProcessKiller
3. 默认浏览器探测
4. 打开 Explorer、读取系统图标等功能

## 架构图文本版

```text
MyTools.Host.Windows / MyTools.Host.Mac
    -> MyTools.Host.Core
        -> MyTools.PluginProtocol
        -> MyTools.PluginRuntime.Node
        -> MyTools.PluginRuntime.DotNet

TypeScript Plugins
    -> mytools-plugin-sdk-ts
        -> MyTools.PluginProtocol

DotNet Plugins
    -> MyTools.PluginSdk.DotNet
        -> MyTools.PluginProtocol

Builtin Plugin Compatibility Layer
    -> MyTools.Host.Core
    -> MyTools.PluginProtocol
```

## 非目标

本阶段明确以下内容暂不作为 B1 的实现目标：

1. 不在本阶段直接重写现有全部插件。
2. 不在本阶段定义完整协议字段明细。
3. 不在本阶段实现插件市场、远程安装或自动升级。
4. 不在本阶段彻底移除全部旧插件兼容路径。

## 对后续任务的输入

B1 完成后，后续任务应直接基于本文档展开：

1. B2 按本文定义的模块和边界设计协议。
2. B3 按本文的运行时和插件包假设设计 `plugin.json` 和目录结构。
3. B4 以 Node Runtime 为第一优先级落地 TypeScript 开发链路。
4. B5 以统一协议为前提设计 .NET 外部运行时。
5. B6 按本文定义的 capability 边界抽象宿主能力。
6. B7 以本文的三类插件能力模型安排迁移顺序。

## 本阶段完成标准

当以下条件成立时，可视为 B1 完成：

1. 团队对插件平台的目标形态形成统一认识。
2. 已经明确协议、宿主、运行时、SDK、平台适配层的职责边界。
3. 已经确认插件默认走进程外模式。
4. 已经确认平台能力通过 capability 由宿主提供。
5. 后续 B2、B3、B4、B5、B6 能直接以本文为设计前提。
