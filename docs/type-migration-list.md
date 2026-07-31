# MyTools 类型迁移清单

## 目的

本文档用于落实 A1 的第二项产出：整理当前核心类型的建议归属、迁移优先级和迁移理由。

## 使用方式

1. 本清单用于指导后续重构，不要求一次性完成。
2. 优先处理高优先级项。
3. 若某个类型暂时不能移动，也应先按目标边界重命名或包裹。

## 优先级说明

1. `P0`：必须优先处理，否则后续插件平台设计会被旧边界拖住。
2. `P1`：建议在宿主重构前后尽快处理。
3. `P2`：可在核心链路稳定后逐步处理。

## 迁移清单

| 当前类型或目录 | 当前所在项目 | 目标归属 | 优先级 | 处理建议 | 原因 |
| --- | --- | --- | --- | --- | --- |
| `Plugins/IPlugin.cs` | `MyTools.Common` | 核心契约层 | P0 | 保留为协议核心接口，但改命名空间，不再使用 `MyTools.Plugins` 语义 | 当前文件位置和命名空间冲突，是边界混乱的核心信号 |
| `Plugins/PluginBase.cs` | `MyTools.Common` | 先留插件 SDK，后续从核心层分离 | P1 | 不放在纯核心层；未来应进入 `.NET Plugin SDK` 或内置插件兼容层 | `PluginBase` 是开发便利层，不是跨语言核心契约 |
| `Plugins/SearchOptions.cs` | `MyTools.Common` | 核心契约层 | P0 | 保留为可序列化查询上下文对象 | 未来 TypeScript 和 .NET 插件都需要共享 |
| `Queries/Result.cs` | `MyTools.Common` | 核心契约层 | P0 | 改为纯 DTO 风格或 transport-safe 结果模型 | 属于跨运行时共享对象 |
| `Queries/ResultItem.cs` | `MyTools.Common` | 核心契约层 | P0 | 清理对宿主行为的隐式依赖，保留为协议模型 | 搜索结果是最核心共享模型 |
| `Queries/IPreviewContentProvider.cs` | `MyTools.Common` | 核心契约层或插件 SDK | P1 | 视协议形态调整为 preview DTO 或 provider contract | 需要去掉宿主 UI 假设 |
| `IKeywordRegistry.cs` | `MyTools.Common` | 宿主核心层接口 | P0 | 改为面向插件描述或插件标识，不直接暴露具体插件对象 | 当前 Common 直接依赖插件对象，方向不对 |
| `IGlobalSearchRegistry.cs` | `MyTools.Common` | 宿主核心层接口 | P0 | 改为管理插件描述或客户端句柄 | 属于宿主聚合逻辑，不是通用抽象 |
| `IHotKeyRegistry.cs` | `MyTools.Common` | 宿主平台层接口 | P1 | 改为平台能力接口，不直接接受插件对象 | 热键本质是平台能力 |
| `IActionRegistry.cs` | `MyTools.Common` | 宿主核心层接口 | P1 | 保留抽象，但应面向动作描述而非宿主静态对象 | 归属于宿主动作路由 |
| `DependencyInjection/ServiceLocator.cs` | `MyTools.Common` | 删除或仅暂存兼容层 | P0 | 不迁移到新核心层；逐步替换为显式依赖注入 | 这是边界不透明的主要来源 |
| `Config/Interfaces/*` | `MyTools.Common` | 核心契约层 | P0 | 继续保留，但剥离宿主特定实现假设 | 配置抽象应跨宿主可用 |
| `Config/Models/*` | `MyTools.Common` | 核心契约层 | P0 | 保持为通用数据模型，避免 UI 绑定字段继续扩散 | 配置模型应服务插件协议 |
| `MyTools.Desktop/Services/ConfigurationRegistry.cs` | `MyTools.Desktop` | 宿主核心层或基础设施层 | P0 | 从 Desktop 中移出，去除对 `Desktop.Serializers` 的依赖 | 当前出现核心抽象被 Desktop 实现反向控制 |
| `MyTools.Desktop/Serializers/*` | `MyTools.Desktop` | 核心基础设施层 | P1 | 从桌面 UI 项目中抽离 | 配置序列化不应依赖 WPF 宿主项目 |
| `MyTools.Plugins/PluginRegistry.cs` | `MyTools.Plugins` | 宿主核心层 | P0 | 从插件项目迁出，改为宿主管理插件描述和索引 | 这是宿主内部注册表，不应属于插件实现层 |
| `MyTools.Plugins/PluginLoader.cs` | `MyTools.Plugins` | 宿主核心层 | P0 | 从插件项目迁出，后续演进为 manifest 驱动的加载器 | 插件加载是宿主职责 |
| `MyTools.Plugins/Searcher.cs` | `MyTools.Plugins` | 宿主核心层 | P0 | 从插件项目迁出，作为宿主搜索聚合器存在 | 搜索聚合不是某个插件的职责 |
| `MyTools.Plugins/PluginServiceCollectionExtensions.cs` | `MyTools.Plugins` | 过渡期宿主装配层 | P1 | 短期保留，长期删除或仅保留内置插件兼容注册 | 编译期显式注册不符合目标架构 |
| `MyTools.Plugins/Actions/*` | `MyTools.Plugins` | 拆分为核心动作描述 + 宿主能力实现 | P1 | 其中平台相关动作应回到宿主 capability 层 | 当前动作层混有 Windows/WPF 依赖 |
| `MyTools.Plugins/Icon/*` | `MyTools.Plugins` | 协议模型或宿主显示适配层 | P1 | 从 WPF 图像对象转成可序列化图标描述 | 未来需要支持跨进程和跨平台 |
| `MyTools.Plugins/Helpers/DefaultBrowserHelper.cs` | `MyTools.Plugins` | Windows 宿主平台层 | P0 | 从插件层迁出，归为 host capability | 依赖注册表，明确平台特化 |
| `MyTools.Plugins/Helpers/FileIconHelper.cs` | `MyTools.Plugins` | Windows 宿主平台层 | P1 | 从插件层迁出，避免插件直接依赖系统图标 API | 属于宿主显示能力 |
| `MyTools.Plugins/Plugins/ClipBoard/*` | `MyTools.Plugins` | Windows 宿主平台层或 capability 适配 | P0 | 不作为第一批跨平台插件迁移对象 | 强依赖宿主消息和系统剪贴板 |
| `MyTools.Plugins/Plugins/ProcessKiller/*` | `MyTools.Plugins` | Windows 宿主平台能力或平台特化插件 | P1 | 先标记为平台特化，不纳入首批可移植插件模型 | 明显平台相关 |
| `MyTools.Plugins/Plugins/CommandRunner/*` | `MyTools.Plugins` | 宿主 capability + 插件实现 | P1 | 将命令执行能力改成宿主受控能力接口 | 直接拼接 Windows 路径不适合作为通用插件逻辑 |
| `MyTools.Plugins/Plugins/Calculator/*` | `MyTools.Plugins` | 插件实现层 | P2 | 作为优先迁移到新插件协议的候选 | 业务逻辑较纯，迁移成本低 |
| `MyTools.Plugins/Plugins/JsonFormatter/*` | `MyTools.Plugins` | 插件实现层 | P2 | 作为优先迁移候选 | 平台依赖相对弱 |
| `MyTools.Plugins/Plugins/XmlFormatter/*` | `MyTools.Plugins` | 插件实现层 | P2 | 作为优先迁移候选 | 平台依赖相对弱 |
| `MyTools.Plugins/Plugins/SearchEngine/*` | `MyTools.Plugins` | 插件实现层 | P2 | 作为优先迁移候选 | 业务边界相对清楚 |
| `MyTools.Plugins/Plugins/Translator/*` | `MyTools.Plugins` | 插件实现层 | P2 | 作为优先迁移候选，但需先改设置声明方式 | 适合做 TypeScript 或外部运行时验证 |
| `MyTools.Desktop/AppBootstrapper.cs` | `MyTools.Desktop` | 宿主核心编排层 | P1 | 保留为编排入口，但逐步瘦身 | 当前职责过重，需要拆分 |
| `MyTools.Desktop/App.xaml.cs` | `MyTools.Desktop` | Windows 宿主平台入口 | P1 | 保留为平台入口，不再承载过多业务逻辑 | 应聚焦生命周期与平台 UI |
| `MyTools.Desktop/Utils/WindowHelper.cs` | `MyTools.Desktop` | Windows 宿主平台服务 | P1 | 从静态 helper 迁移为接口服务 | 当前隐藏依赖较多 |
| `MyTools.Desktop/Utils/ResultItemExtensions.cs` | `MyTools.Desktop` | 宿主核心动作执行服务 | P1 | 拆出副作用逻辑，不再挂在结果模型扩展上 | 结果模型不应隐含宿主副作用 |
| `MyTools.Desktop/Views/SearchViewModel.cs` | `MyTools.Desktop` | Windows 宿主 UI 层 | P2 | 保留在 UI 层，但拆出搜索协调和动作路由服务 | 需降低复杂度，不涉及跨层迁移 |
| `MyTools.Desktop/Views/ConfigurationViewModel.cs` | `MyTools.Desktop` | Windows 宿主 UI 层 | P2 | 保留在 UI 层，但拆出树过滤和查询逻辑 | 需降低复杂度，不涉及跨层迁移 |

## 第一批必须处理的类型

建议优先处理以下类型或模块：

1. `IPlugin`
2. `IKeywordRegistry`
3. `IGlobalSearchRegistry`
4. `ConfigurationRegistry`
5. `PluginLoader`
6. `PluginRegistry`
7. `Searcher`
8. `ServiceLocator`

原因：

1. 这些类型共同决定当前系统的模块边界。
2. 不先处理它们，后续插件协议和目录加载方案会被旧模型反复干扰。

## 迁移策略建议

### 策略 1：先改归属，再改实现

对于高风险类型，先明确它应属于哪一层，再决定是否立刻移动文件、重命名命名空间或建立兼容包装层。

### 策略 2：允许短期兼容层存在

对于 `PluginBase`、`PluginServiceCollectionExtensions` 这类过渡期组件，可以先保留兼容层，避免一次性切断现有功能。

### 策略 3：平台特化能力不要硬迁移为通用插件

像剪贴板、注册表、资源管理器、系统图标这类能力，应该先回收为 Windows 宿主 capability，而不是强行塞进跨平台插件协议。

## 本阶段完成标准

当以下条件成立时，可认为 A1 已完成：

1. 已有清晰的模块职责说明文档。
2. 已有核心类型迁移清单和优先级排序。
3. 团队对哪些内容属于核心层、宿主层、插件层有统一认识。
4. 后续 A2、A4、B1、B2 可以直接基于本清单开展工作。
