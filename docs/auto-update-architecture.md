# MyTools 自动升级框架选型与集成设计

> 调查日期：2026-07-31  
> 状态：阶段一及本地发布闭环已完成；阶段二中的 HTTPS 更新源、代码签名、CI 发布和正式环境验收待完成

## 实施记录（2026-07-31）

已完成：

- 引入 `Velopack 1.2.0`，并用仓库级 Tool Manifest 固定 `vpk 1.2.0`；
- 在显式 `Program.Main` 中运行最早期 Velopack 启动钩子；
- 实现 `UpdateService`、托盘手动检查、下载、正常退出后安装及重启；
- 在 General 设置中加入 `UpdateUrl` 和 `UpdateChannel`；
- 修复版本目录变化后的自动启动路径；
- 在退出时释放 DI 容器和动态 Node 插件；
- 将 Velopack 完整/增量包生成接入带显式开关的 `dotnet publish`；
- 将 `publish.ps1` 改为无参数交互发布，并用 `version.txt` 记录最近成功发布版本；
- 新增 Desktop 更新服务测试，覆盖未配置更新源、非 Velopack 安装环境和无待下载更新；
- 使用连续三个本地预发布版本验证完整包和增量包生成。

正式发布前仍需完成：

- 部署静态 HTTPS 更新目录并填写正式 `UpdateUrl`；
- 配置 Authenticode 证书和 CI Secret；
- 将发布、签名、上传及最后替换索引的顺序固化到 CI；
- 在隔离环境安装 Setup，执行真实的客户端跨版本更新验收。

## 1. 背景与目标

集成 Velopack 之前，MyTools 通过脚本发布普通目录并手工覆盖文件。这种方式适合本机开发，但不具备可靠的安装和自动升级能力，主要缺少：

- 自动检查、下载和安装更新；
- 增量更新；
- 文件占用和进程退出协调；
- 安装失败处理；
- 安装入口、快捷方式和版本目录管理；
- 发布包校验、代码签名及更新通道。

目前上述本地最小闭环已经由 Velopack 实现。本设计继续作为当前实现说明和正式发布路线图，目标是在保持业务模块、插件系统和用户数据结构不变的前提下，补齐 HTTPS 分发、签名、CI 和生产验收。

## 2. 当前项目事实

### 2.1 技术栈

- `MyTools.Desktop` 是 WPF 桌面程序；
- 目标框架为 `net8.0-windows`；
- 最近成功发布版本记录在仓库根目录的 `version.txt`，Desktop 项目默认从该文件读取版本；
- 当前发布 RID 为 `win-x64`；
- Velopack 正式包默认采用 self-contained、非单文件发布；
- NuGet 版本由根目录 `Directory.Packages.props` 集中管理。

### 2.2 发布命令与版本推进

日常正式发布直接运行无参数脚本：

```powershell
.\publish.ps1
```

脚本读取 `version.txt` 中的最近成功版本，并默认将 patch 加一，例如 `1.0.1 → 1.0.2`。
脚本会先询问是否修改默认设置；直接按 Enter 将使用默认版本、`win` 通道、`win-x64` 和
self-contained 发布。选择修改后，可以交互输入 SemVer 版本、更新通道以及是否改为
framework-dependent。只有 `dotnet publish` 和后续 Velopack 打包全部成功，脚本才会原子更新
`version.txt`；构建或打包失败不会推进版本。

普通的 .NET 发布不会调用 Velopack：

```powershell
dotnet publish .\MyTools.Desktop\MyTools.Desktop.csproj --configuration Release
```

绕过交互脚本的 CI 可以显式开启 MSBuild Target：

```powershell
dotnet publish .\MyTools.Desktop\MyTools.Desktop.csproj `
    --configuration Release `
    --property:CreateVelopackRelease=true `
    --property:Version=1.0.2
```

该 Target 默认使用 `win-x64`、self-contained、非单文件、`win` 通道和 `BestSpeed` delta 模式。
它保留 `Releases` 中的旧完整包用于生成增量包。

### 2.3 启动与退出

- `MyTools.Desktop/App.xaml.cs` 使用名为 `MyTools.Desktop` 的 Mutex 保证单实例；
- 应用退出时会释放托盘图标和 `AppBootstrapper`；
- `AppBootstrapper.Dispose()` 会释放热键和手势监听；
- Node 插件宿主释放时会终止其子进程树；
- 当前更新流程会先注册退出后安装，再调用 WPF 正常退出并清理资源，不再依赖 `Stop-Process -Force`。

### 2.4 用户数据

用户数据不在程序安装目录中：

- 配置和数据库：`%AppData%\MyTools.Desktop`；
- WebView2 数据：`%LocalAppData%\MyTools.Desktop\WebView2`。

升级器只替换安装目录时不会直接覆盖这些数据。数据库或配置格式发生变化时，仍需单独设计可回退的数据迁移。

### 2.5 自动启动

`MyTools.Desktop/Services/AutoStartService.cs` 当前把正在运行的 `MyTools.Desktop.exe` 绝对路径写入：

```text
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run
```

Velopack 使用版本化安装目录，因此旧绝对路径会在升级后失效。当前实现采用“启动时修复”策略：`AutoStartService` 初始化时，如果用户已启用自动启动，会将注册表命令与 `Environment.ProcessPath` 比较并在不一致时重写。该修复已经落地，但仍需在真实跨版本升级中验收。

### 2.6 Launcher

`MyTools.Launcher` 用于通过 `runas` 启动需要 UAC 的外部程序，从而打破提权进程与 MyTools Job Object 的父子关系。目前 Desktop 没有对 Launcher 的项目引用或主启动链调用，因此 Desktop 的 Velopack 发布产物不包含 Launcher。

Velopack 应直接管理 `MyTools.Desktop` 的安装和启动。`MyTools.Launcher` 不应包装 Desktop 的常规启动；如果 Launcher 是运行期必需文件，应明确将其加入 Desktop 发布产物。

## 3. 候选框架

| 方案 | 适配度 | 优点 | 主要限制 |
|---|---:|---|---|
| Velopack | 高 | 安装器、更新检查、下载、增量包、文件替换、重启和 Channel 一体化 | 需要调整应用启动生命周期和发布脚本 |
| NetSparkleUpdater | 中 | WPF 更新 UI 和 AppCast 支持成熟、界面可定制 | 安装器、稳定入口和增量发布仍需较多自建工作 |
| ClickOnce | 较低 | .NET/Visual Studio 原生支持、配置简单 | 安装路径、发布方式和定制能力受限 |
| MSIX | 较低 | Windows 标准安装模型、安全和签名机制完整 | 对私有分发、插件目录和现有运行方式约束较多 |
| 自研 ZIP 更新器 | 不建议 | 完全可控 | 文件占用、失败恢复、安全校验和回滚成本高 |

截至调查日期，经 NuGet 官方源和本机 `net8.0` 临时项目验证：

- `Velopack` 稳定版为 `1.2.0`，包含原生 `net8.0` 资产；
- `vpk` .NET 工具版本为 `1.2.0`；
- NetSparkle 的相关包为 `NetSparkleUpdater.SparkleUpdater 3.1.0` 和 `NetSparkleUpdater.UI.WPF 3.1.0`。

## 4. 选型结论

推荐使用 **Velopack**。

主要原因：

1. 与当前 `net8.0-windows` WPF 应用直接兼容；
2. 能取代目前手工 ZIP 覆盖流程；
3. 内置安装器、更新源、增量包、校验、更新应用和重启能力；
4. 支持静态 Web 目录，适合现有自建 `git.qping.me` 环境；
5. 用户数据已与安装目录分离，迁移成本较低；
6. 能通过“等待应用正常退出再更新”与插件清理流程配合。

不建议把 GitHub Releases 作为默认方案，因为当前仓库远端是自建 Git 服务。推荐在服务器上提供独立的静态 HTTPS 下载目录；开发阶段也可以先使用本地或 OneDrive 同步目录作为文件更新源。

## 5. 目标架构

```text
MyTools.Desktop
    ├── Velopack 最早期启动钩子
    ├── General.UpdateUrl / UpdateChannel
    ├── UpdateService
    ├── 托盘“检查更新”入口
    ├── AutoStartService 启动时路径修复
    └── App.OnExit 释放托盘、DI、热键和插件进程

publish.ps1（已实现）
    ├── 从 version.txt 计算下一 patch 版本
    ├── dotnet publish + CreateVelopackRelease=true
    ├── MSBuild 恢复仓库级 vpk 并执行 vpk pack
    ├── 生成完整包、增量包、Portable ZIP、Setup 和索引
    └── 成功后原子更新 version.txt

CI / 正式分发（待实现）
    ├── 构建与测试
    ├── Authenticode 签名
    ├── 上传包文件
    └── 最后原子替换索引

静态 HTTPS 更新源（待部署）
    ├── releases.win.json
    ├── 完整发布包
    ├── 增量发布包
    └── Setup 安装程序
```

正式环境建议按通道分目录：

```text
https://downloads.qping.me/mytools/stable/win-x64/
https://downloads.qping.me/mytools/beta/win-x64/
```

具体域名和路径可以根据现有服务器调整。

## 6. 应用集成设计

### 6.1 添加依赖

已在 `Directory.Packages.props` 中集中声明 `Velopack 1.2.0`，在 `MyTools.Desktop/MyTools.Desktop.csproj` 中添加 `Velopack` 引用，并在 `.config/dotnet-tools.json` 中固定 `vpk 1.2.0`。

升级能力只属于 Desktop 宿主，不应让 `MyTools.Common` 或 `MyTools.Plugins` 依赖 Velopack。

### 6.2 启动钩子顺序

`MyTools.Desktop/Program.cs` 已在创建 WPF `App` 前运行 `VelopackApp.Build().SetAutoApplyOnStartup(false).Run()`。Velopack 启动处理早于：

- 单实例 Mutex；
- 依赖注入初始化；
- 热键和手势注册；
- 插件加载；
- 托盘图标创建。

当前顺序：

```text
Velopack 启动钩子
    → 单实例检查
    → AppBootstrapper.Init()
    → 加载插件
    → 创建托盘图标
```

原因是安装、卸载和更新回调可能由升级器使用特殊参数启动应用，这些进程不应进入完整的 WPF 初始化流程，也不应被普通单实例检查阻断。

### 6.3 UpdateService

当前实现位于：

```text
MyTools.Desktop/Services/UpdateService.cs
```

已实现职责：

- 判断程序是否为 Velopack 安装版；
- 从 `General.UpdateUrl` 和 `General.UpdateChannel` 读取更新源及通道；
- 检查远程更新；
- 下载更新并报告进度；
- 使用 `SemaphoreSlim` 防止检查和下载并发执行；
- 通过取消令牌取消检查或下载，并由托盘入口捕获、记录和提示异常；
- 调用 `WaitExitThenApplyUpdates` 等待应用退出后安装并重启；
- 使用结构化日志记录检查、下载和错误。

`UpdateService` 只负责准备更新和注册退出后安装，不直接关闭 WPF 应用。托盘交互层在下载成功后调用 `Application.Current.Shutdown()`，从而保证正常执行 `App.OnExit`。

当前流程：

```text
CheckForUpdatesAsync
    → 返回 NotConfigured / NotInstalled / NoUpdate / Busy / UpdateAvailable
    → 托盘 UI 提示用户
    → DownloadUpdatesAsync
    → WaitExitThenApplyUpdates
    → Application.Current.Shutdown()
    → OnExit 清理托盘、热键和 Node 子进程
    → 更新器替换文件并重启
```

应优先使用等待正常退出的更新 API。立即退出型 API 会跳过部分清理逻辑，只有在明确保存状态和释放资源后才可使用。

开发目录或 Portable ZIP 直接运行时，更新服务通过 `VelopackLocator.IsCurrentSet` 和 `UpdateManager.IsInstalled` 识别“未安装状态”，托盘 UI 会提示只有 Velopack 安装版才能更新。更新地址留空时返回“未配置”，不会发起网络请求。

### 6.4 更新检查体验

当前已实现：

- 托盘菜单增加“检查更新”和“当前版本”；
- 检查和下载通过异步 API 执行，不阻塞应用启动；
- 下载前提示用户；
- 安装前提示应用将退出并重启；
- 检查或安装失败会记录日志并显示错误，不会导致未处理异常终止主程序；
- 同一时间只允许一个检查或下载任务。

当前只支持用户手动检查，不会在启动后自动检查。下载进度显示在托盘菜单项中，下载完成后立即走正常退出并安装。后台定时检查、静默下载、延后到用户主动退出时安装、发布说明 UI 和可取消进度 UI 仍属于后续增强。

### 6.5 自动启动修复

当前采用启动时校验修复策略：`AutoStartService` 先判断注册表值是否存在；若自动启动已启用，则将当前值与基于 `Environment.ProcessPath` 生成的命令比较，不一致时重写注册表。这样可在应用升级到新的版本目录并首次启动后修复入口。

该实现不依赖固定版本目录，也不需要在 Velopack 更新钩子内直接操作注册表。仍需通过真实安装版跨版本更新验证：更新后的首次启动能够完成修复，并且下一次 Windows 登录能够启动新版本。

### 6.6 版本管理

仓库根目录的 `version.txt` 保存最近一次成功发布版本，`MyTools.Desktop.csproj` 默认读取该文件作为程序集版本。无参数 `publish.ps1` 默认将 patch 加一，也允许交互输入其他 SemVer；脚本通过 `-p:Version` 将候选版本传给 MSBuild，并且只有发布和 VPK 打包全部成功后才原子更新 `version.txt`。手动调用 `dotnet publish` 不会自动推进该文件。

建议版本规则：

```text
1.0.0          stable
1.1.0-beta.1   beta
1.1.0          stable
```

每次发布版本必须严格递增，禁止用相同版本覆盖已有发布文件。

## 7. 发布流程

### 7.1 发布模式

正式安装版已默认采用 self-contained、非单文件的 `win-x64` 发布。日常发布入口为：

```powershell
.\publish.ps1
```

脚本提供默认值和交互修改入口，然后调用带 `CreateVelopackRelease=true` 的 `dotnet publish`。CI 或手动发布也可以直接调用：

```powershell
dotnet publish .\MyTools.Desktop\MyTools.Desktop.csproj `
    --configuration Release `
    --property:CreateVelopackRelease=true `
    --property:Version=1.0.2
```

`CreateVelopackRelease` 默认关闭，因此不带该开关的 `dotnet publish` 只生成普通应用目录，不调用 VPK。

理由：

- 新机器不需要预装 .NET 8 Desktop Runtime；
- 保持多文件更有利于增量更新；
- WebView2、本地 DLL 和插件资源更容易验证和排查。

交互脚本允许选择 framework-dependent，但这种产物要求目标机已安装 .NET 8 Desktop Runtime，正式采用前必须单独验证运行时检测和安装体验。

### 7.2 固定 vpk 工具版本

仓库已通过 `.config/dotnet-tools.json` 固定 `vpk 1.2.0`。`MyTools.Desktop.csproj` 中的 `PrepareVelopackRelease` 和 `CreateVelopackPackage` Target 会清理专用 publish 目录、恢复本地工具并执行打包，同时保留 `Releases` 中的历史完整包用于生成 delta。

Target 等效执行的核心打包参数为：

```powershell
dotnet tool run vpk -- pack `
    --packId MyTools.Desktop `
    --packVersion $version `
    --packDir $publishDir `
    --mainExe MyTools.Desktop.exe `
    --packTitle MyTools `
    --packAuthors qping `
    --runtime win-x64 `
    --channel win `
    --delta BestSpeed `
    --icon .\MyTools.Desktop\Assets\Maintenance.ico `
    --outputDir .\Releases
```

当前本地流程已验证可生成完整包、增量包、Portable ZIP、Setup、`RELEASES`、`assets.win.json` 和 `releases.win.json`。代码签名参数尚未接入；正式 CI 必须复用同一 MSBuild Target，而不是复制另一套 VPK 参数。

### 7.3 上传顺序

发布服务器更新应避免客户端读到不完整版本：

1. 构建和测试应用；
2. 生成完整包、增量包和安装器；
3. 签名并验证签名；
4. 上传包文件；
5. 校验服务器文件完整性；
6. 最后原子替换发布索引文件。

## 8. 安全要求

自动升级文件最终会在用户机器上执行，必须满足：

1. 更新源只使用 HTTPS；
2. Setup、主 EXE 和必要的可执行文件使用 Authenticode 签名；
3. 保留 Velopack 的包校验；
4. 发布 Token 仅放入 CI Secret，不写入仓库或客户端；
5. 客户端不包含服务器写权限凭据；
6. 上传完成后才发布新的索引文件；
7. 日志不得记录 Token、签名私钥或其他敏感信息。

包哈希只能证明下载内容与发布索引一致。如果发布服务器和索引一起被篡改，仅依赖哈希并不足够，因此正式分发仍需要代码签名。

## 9. 数据、插件和回退策略

### 9.1 用户数据迁移

- 配置变更应提供默认值并兼容旧字段；
- SQLite 迁移必须事务化；
- 迁移前可以创建轻量备份；
- 已完成的破坏性迁移可能导致旧程序无法读取数据，因此“程序文件降级”不等于“数据自动回滚”；
- 重要迁移应记录 schema/version，并为失败提供恢复路径。

### 9.2 插件兼容性

更新发布前应验证：

- 内置插件可正常加载；
- Node 插件协议与旧配置兼容；
- 更新时 Node 子进程被正常关闭；
- 插件目录位于 `%AppData%` 的内容不会被安装器覆盖；
- 示例插件同步逻辑不会在升级后覆盖用户已修改文件。

建议后续为插件元数据增加最低/最高宿主版本或协议版本约束。

### 9.3 发布回退

- 保留前一稳定版本的完整安装包和发布产物；
- 新版本先进入 beta 通道验证，再提升到 stable；
- 出现问题优先发布更高版本号的修复版本；
- 如需降级，必须先确认配置和数据库迁移是否向后兼容；
- 不依赖“覆盖同版本文件”实现回退。

## 10. 实施阶段

### 阶段一：本地最小闭环（已实现）

- [x] 引入 Velopack 并固定运行库及 CLI 版本；
- [x] 添加最早期启动钩子；
- [x] 添加 `UpdateService` 和 DI 注册；
- [x] 托盘菜单增加当前版本、手动检查、下载进度和安装入口；
- [x] 支持本地目录作为更新源，并生成连续版本的完整包和增量包；
- [x] 更新前走正常退出流程，释放托盘、热键、手势和插件宿主；
- [x] 用户配置、SQLite、WebView2 和插件数据保持在安装目录外。

阶段一的代码与本地打包闭环已经完成。安装器首次安装、真实安装版跨版本更新、数据保持和失败回退仍需按第 11 节在隔离环境做发布验收；“实现完成”不等同于这些场景已经全部验收通过。

### 阶段二：正式发布（部分完成）

- [x] 通过 MSBuild Target 和 `vpk pack` 生成 Velopack 发布物；
- [x] 提供无参数交互式 `publish.ps1` 和成功后版本推进；
- [x] 修复升级后自动启动入口；
- [x] 发布与客户端配置均支持自定义通道；
- [ ] 建立 stable/beta 独立目录并完成互不串包验收；
- [ ] 建立静态 HTTPS 更新目录；
- [ ] 加入 Authenticode 签名并验证签名链；
- [ ] 由 CI 构建、测试、签名、打包、上传并最后发布索引。

### 阶段三：体验增强（部分完成）

- [ ] 启动后后台检查；
- [ ] 后台静默下载；
- [x] 用户确认后下载，并通过正常退出安装和重启；
- [ ] 延后到用户主动退出时安装；
- [ ] 发布说明 UI；
- [x] 基础下载进度显示；
- [ ] 用户可操作的取消和重试；
- [ ] 数据迁移备份；
- [ ] 插件协议版本检查。

## 11. 验收清单

已由自动化测试或代码级检查覆盖：

- [x] 更新地址未配置时返回 `NotConfigured`；
- [x] 未安装版直接运行时返回 `NotInstalled`，不会错误应用更新；
- [x] 未先检查到更新时禁止直接下载；
- [x] 同一时间只允许一个检查或下载操作；

正式启用自动更新前仍需在隔离安装环境手工或端到端自动化验证：

- [ ] 首次安装成功；
- [ ] 完整包升级成功；
- [ ] 增量包升级成功；
- [ ] 增量包失败时能回退到完整包；
- [ ] 无更新时行为正确；
- [ ] 网络断开、超时、服务器 404/500 时主程序仍可使用；
- [ ] 下载中取消后可再次检查；
- [ ] 应用运行、设置窗口打开和托盘驻留时均可正常更新；
- [ ] 更新退出时热键、手势和 Node 插件进程被释放；
- [ ] 更新重启后 Mutex、托盘和热键正常；
- [ ] 自动启动在升级后仍有效；
- [ ] `%AppData%` 配置和 SQLite 数据保持完整；
- [ ] `%LocalAppData%` WebView2 数据保持完整；
- [ ] 旧版本配置和数据库能迁移到新版本；
- [ ] 安装失败或更新源不可用时，现有版本仍可启动；
- [ ] 安装器和程序签名验证通过；
- [ ] stable 与 beta 通道互不串包。

## 12. 最终建议

MyTools 应采用 **Velopack + 静态 HTTPS 更新源 + self-contained win-x64 发布** 作为正式升级方案。

当前已完成启动钩子、手动检查、下载进度、正常退出后安装重启、自动启动修复、版本管理，以及完整包和增量包的本地生成。下一步不应继续扩展后台体验，而应优先完成隔离环境端到端升级验收、静态 HTTPS 更新源、stable/beta 通道隔离、Authenticode 签名和 CI 原子发布。完成这些正式分发基础设施后，再实现后台检查、静默下载、发布说明和数据迁移增强。

