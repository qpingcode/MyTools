# Plugin Message Bus Windows Named Pipe and Node SDK Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成插件消息总线第 3/5 份实施计划：提供防抢占、可认证、受 Job Object 管理的 Windows Named Pipe Node endpoint，以及在认证身份下加载插件 route manifest、执行双向 Schema 校验、取消和健康检查的 Node SDK。

**Architecture:** `MyTools.Host.Transports.Windows` 先创建带 `FirstPipeInstance` 和受保护 ACL 的单实例管道，再以挂起状态创建 Node、加入 Job Object、恢复线程，并通过 stdin 只发送一次 bootstrap；握手把管道客户端 PID 与宿主持有的进程句柄、创建时间、一次性令牌和 Host 分配的会话身份绑定。`@mytools/plugin-sdk-node` 从 stdin 读取 bootstrap 后只在命名管道上传输 v3 长度前缀帧；认证成功后、返回 client 前，它可加载插件的 `dist\route-manifest.json`，以认证得到的 `pluginId`/`entryId` 调用 `@mytools/protocol` 的 `registerRouteManifest`，并把动态路由与无需重复声明的 canonical routes 一起交给请求/响应 Schema 校验器。每个进程只容纳一个认证 endpoint，SDK 以 `(pluginId, entryId)` 隔离其 manifest 生命周期，并在关闭或断线时先封闭该身份的 client 状态；断线或 Host watchdog 超时时取消全部 handler 后退出。Host Core 继续拥有 session actor、路由、授权与重启决策；本计划通过其进程/endpoint 端口报告连接、心跳、断线和 Worker 创建结果。

**Tech Stack:** .NET 8 (`System.IO.Pipes`, Windows ACL, P/Invoke, `SafeHandle`, NUnit), Windows Job Objects and process APIs, TypeScript 5/Node.js 22 (`node:net`, `node:test`, `AbortController`), Protocol 生成的 C#/TypeScript DTO 与 AJV standalone 校验器。

---

## 计划位置、依赖与边界

- **这是第 3/5 份实施计划。** 五份实施计划映射固定为：1/5 Protocol Foundation（`MyTools.Protocol` 与 `@mytools/protocol`），2/5 Host Core，3/5 本计划（Windows Named Pipe 与 Node SDK），4/5 WebView2/Web SDK，5/5 manifest、插件迁移与 E2E。执行本计划前，1/5 必须已交付 envelope、握手、错误、长度帧 codec、版本化 JSON Schema、C# DTO/校验器及 TypeScript 生成包；2/5 必须已交付 `IMessageTransport`、session actor、`PluginSessionManager`、`CapabilityGateway`、endpoint 身份绑定、重启策略和进程控制端口。
- **本计划输出给第 4/5、5/5 份计划。** 4/5 可把 WebView2 endpoint 接到同一 Host Core；5/5 可为现有插件生成/部署本计划会加载的 `dist\route-manifest.json`，并把插件迁到本计划提供的 `WindowsNodeEndpointFactory` 和 `@mytools/plugin-sdk-node`。
- **明确不做：** 不实现或修改 WebView2 transport，不修改任何示例插件，不迁移现有插件 manifest，不把 `MyTools.Plugins\NodePlugins\NodePluginProcessHost.cs` 接到新协议，也不删除旧 stdin/stdout JSON-RPC。本计划只实现 SDK 对 Protocol Foundation 已定义 route manifest artifact/API 的消费，并在测试 fixture 中覆盖该消费；生成现有插件 artifact 和切换入口属于第 5/5 份计划。旧 `NodePluginProcessHost` 仅作为日志、UTF-8 和退出行为的阅读基线。
- **安全表述：** Job Object 只提供进程树回收及 CPU、内存、活动进程数限制，不是恶意插件的操作系统沙箱；Node 仍以当前用户权限运行。

### 必须先满足的依赖契约

执行 Task 1 前，确认第 1/5、2/5 份计划暴露以下已定名 API；若依赖分支尚未合入，先合入依赖，不在本计划复制协议或 Host Core 类型：

```csharp
// MyTools.Protocol.V3 generated types
namespace MyTools.Protocol.V3;

public sealed record EndpointIdentity(
    string PluginId, string EntryId, string SessionId, string EndpointId);
public sealed record HandshakeRequest(
    IReadOnlyList<string> SupportedVersions,
    string LaunchToken,
    string PluginId,
    string EntryId,
    int ProcessId,
    DateTimeOffset ProcessStartedAtUtc);
public sealed record HandshakeResponse(
    string SelectedVersion, string SessionId, string EndpointId);
public sealed record BusError(
    string Code, string Message, bool Retryable, JsonElement? Details = null);
public static class ProtocolErrorCodes
{
    public const string HandshakeFailed = "HandshakeFailed";
    public const string InvalidPayload = "InvalidPayload";
    public const string MessageTooLarge = "MessageTooLarge";
    public const string CapabilityDenied = "CapabilityDenied";
    public const string CapabilityNotDeclared = "CapabilityNotDeclared";
    public const string TransportDisconnected = "TransportDisconnected";
}
public static class ProtocolJson
{
    public static JsonSerializerOptions SerializerOptions { get; }
}
```

```csharp
// MyTools.Protocol.Framing
namespace MyTools.Protocol.Framing;

public static class LengthPrefixedJsonFrameCodec
{
    public const int DefaultMaxFrameBytes = 4 * 1024 * 1024;
    public static ValueTask<byte[]> ReadAsync(
        Stream stream, int maxFrameBytes, CancellationToken cancellationToken);
    public static ValueTask WriteAsync(
        Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}
```

```csharp
// MyTools.Protocol.Validation
namespace MyTools.Protocol.Validation;

public static class ProtocolValidator
{
    public static ValidationResult ValidateEnvelope(JsonElement envelope);
}
```

```csharp
// MyTools.Host.Core.Transports — consume verbatim; do not redeclare or wrap.
using MyTools.Protocol.V3;

namespace MyTools.Host.Core.Transports;

public enum TransportPriority { ControlOrResponse, Request, Event }
public sealed record TransportDisconnect(string Code, string Reason, Exception? Exception = null);
public interface IMessageTransport : IAsyncDisposable
{
    EndpointIdentity Identity { get; }
    ValueTask SendAsync(
        MessageEnvelope envelope,
        TransportPriority priority,
        CancellationToken cancellationToken);
    IAsyncEnumerable<MessageEnvelope> ReadAllAsync(CancellationToken cancellationToken);
    Task Completion { get; }
}
// No additional members may be added to IMessageTransport.

public sealed record NodeLaunchRequest(
    EndpointIdentity Identity, string EntryPath, string WorkingDirectory,
    IReadOnlySet<string> GrantedCapabilities, bool IsWorker);
public sealed record NodeProcessLimits(
    long ProcessMemoryBytes, long JobMemoryBytes, uint ActiveProcessLimit, uint CpuRatePercent);
public interface INodeEndpoint : IAsyncDisposable
{
    EndpointIdentity Identity { get; }
    IMessageTransport Transport { get; }
    Task StartAsync(CancellationToken cancellationToken);
}
public interface INodeEndpointEvents
{
    ValueTask ConnectedAsync(
        EndpointIdentity identity, IMessageTransport transport, CancellationToken cancellationToken);
    ValueTask DisconnectedAsync(
        EndpointIdentity identity, TransportDisconnect disconnect, CancellationToken cancellationToken);
    ValueTask HeartbeatAsync(
        EndpointIdentity identity, TimeSpan roundTripTime, CancellationToken cancellationToken);
}
public enum EndpointKind { Host, MainNode, Worker, WebView, Diagnostics }
public sealed record AuthenticatedEndpointContext(
    EndpointIdentity Identity,
    EndpointKind Kind,
    IReadOnlySet<string> DeclaredCapabilities,
    IReadOnlySet<string> GrantedCapabilities);
public sealed record WorkerSpawnRequest(
    string EntryPath, IReadOnlyList<string> Capabilities);
public sealed record WorkerSpawnResult(
    EndpointIdentity Identity, IReadOnlySet<string> Capabilities);
public sealed class TransportDisconnectedException : Exception
{
    public TransportDisconnectedException(TransportDisconnect disconnect)
        : base(disconnect.Reason, disconnect.Exception) => Disconnect = disconnect;
    public TransportDisconnect Disconnect { get; }
}
public sealed class BusException(BusError error) : Exception(error.Message)
{
    public BusError Error { get; } = error;
}
public interface IWorkerRegistration
{
    ValueTask RegisterWorkerAsync(
        EndpointIdentity mainIdentity, EndpointIdentity workerIdentity,
        IReadOnlySet<string> capabilities, IMessageTransport transport,
        CancellationToken cancellationToken);
}
```

TypeScript 依赖固定从 1/5 计划创建的 `@mytools/protocol` 导出；SDK 不复制 wire 类型或 validator：

```ts
import type {
  MessageEnvelope, EndpointIdentity, HandshakeRequest, HandshakeResponse, BusError,
  RouteManifest
} from "@mytools/protocol";
import {
  registerRouteManifest, validateEnvelope, validateRoutePayload,
  validateRouteResponsePayload
} from "@mytools/protocol";

export type {
  MessageEnvelope, EndpointIdentity, HandshakeRequest, HandshakeResponse, BusError,
  RouteManifest
};
export {
  registerRouteManifest, validateEnvelope, validateRoutePayload,
  validateRouteResponsePayload
};
```

## 文件映射

| 文件 | 操作 | 单一职责 |
| --- | --- | --- |
| `MyTools.Host.Transports.Windows\MyTools.Host.Transports.Windows.csproj` | 创建 | Windows transport/process 项目及 Protocol、Host Core 引用 |
| `MyTools.Host.Transports.Windows\NamedPipes\SecureNamedPipeServer.cs` | 创建 | `FirstPipeInstance`、ACL、单客户端 accept |
| `MyTools.Host.Transports.Windows\NamedPipes\NamedPipeMessageTransport.cs` | 创建 | v3 帧读写、单写者、断线完成语义 |
| `MyTools.Host.Transports.Windows\Processes\NativeMethods.cs` | 创建 | 进程、管道客户端 PID、Job Object 的最小 Win32 声明 |
| `MyTools.Host.Transports.Windows\Processes\SafeJobHandle.cs` | 创建 | Job handle 所有权 |
| `MyTools.Host.Transports.Windows\Processes\NodeJob.cs` | 创建 | kill-on-close、内存/CPU/活动进程限制 |
| `MyTools.Host.Transports.Windows\Processes\SuspendedNodeProcess.cs` | 创建 | 重定向日志、挂起创建、加入 Job、恢复、句柄身份 |
| `MyTools.Host.Transports.Windows\Security\BootstrapToken.cs` | 创建 | 256-bit 一次性短期令牌与原子消费 |
| `MyTools.Host.Transports.Windows\Security\PipePeerAuthenticator.cs` | 创建 | token、身份、版本、PID、创建时间和 pipe client PID 校验 |
| `MyTools.Host.Transports.Windows\WindowsNodeEndpoint.cs` | 创建 | 先建 pipe 后启动、stdin bootstrap、握手、日志、心跳、断线停树 |
| `MyTools.Host.Transports.Windows\WindowsNodeEndpointFactory.cs` | 创建 | 主 Node/Worker endpoint 创建及默认资源限制 |
| `MyTools.Host.Transports.Windows\WorkerSpawnService.cs` | 创建 | `host.call.worker.spawn` 主 endpoint 信任链与 capability 子集 |
| `MyTools.Host.Transports.Windows.Test\*.cs` | 创建 | Windows 单元、组件、真实 Node、安全及资源测试 |
| `MyTools.Host.Transports.Windows.Test\Fixtures\node-endpoint.mjs` | 创建 | 真实 Node 集成测试入口 |
| `MyTools.PluginSdk.Node\package.json` | 创建 | `@mytools/plugin-sdk-node` 包、构建和测试命令 |
| `MyTools.PluginSdk.Node\src\bootstrap.ts` | 创建 | stdin 首行 bootstrap 读取和清除 |
| `MyTools.PluginSdk.Node\src\framing.ts` | 创建 | 4-byte LE framing 和最大帧限制 |
| `MyTools.PluginSdk.Node\src\route-manifest.ts` | 创建 | 在认证身份下可选加载、注册并隔离 `dist\route-manifest.json` |
| `MyTools.PluginSdk.Node\src\client.ts` | 创建 | 握手、关联、路由、事件、取消、Schema 校验 |
| `MyTools.PluginSdk.Node\src\lifecycle.ts` | 创建 | Host ping/Node pong、Node watchdog、断线退出 |
| `MyTools.PluginSdk.Node\src\index.ts` | 创建 | 插件业务唯一公共 API |
| `MyTools.PluginSdk.Node\test\*.test.ts` | 创建 | Node SDK 协议、AbortSignal、Schema、心跳和断线测试 |
| `MyTools.sln` | 修改 | 加入两个 Windows 项目 |
| `Directory.Packages.props` | 修改 | 固定 `System.IO.Pipes.AccessControl` 版本 |

`MyTools.Plugins\Examples\common\**`、所有示例插件、WebView2 文件和 `NodePluginProcessHost.cs` 均保持不变。

### Task 1: 建立 Windows transport 与 Node SDK 骨架

**Files:**
- Create: `MyTools.Host.Transports.Windows\MyTools.Host.Transports.Windows.csproj`
- Create: `MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj`
- Create: `MyTools.PluginSdk.Node\package.json`
- Create: `MyTools.PluginSdk.Node\tsconfig.json`
- Modify: `Directory.Packages.props`
- Modify: `MyTools.sln`

- [ ] **Step 1: 写依赖引用失败检查**

在 PowerShell 运行：

```powershell
dotnet sln MyTools.sln list
Test-Path MyTools.Protocol\MyTools.Protocol.csproj
Test-Path MyTools.Host.Core\MyTools.Host.Core.csproj
Test-Path MyTools.Protocol.TypeScript\package.json
```

Expected: solution 列表包含 `MyTools.Protocol` 和 `MyTools.Host.Core`；三个 `Test-Path` 均输出 `True`。否则第 1/5、2/5 份计划尚未完成，不创建兼容副本。

- [ ] **Step 2: 创建项目文件**

```xml
<!-- MyTools.Host.Transports.Windows\MyTools.Host.Transports.Windows.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.IO.Pipes.AccessControl" />
    <ProjectReference Include="..\MyTools.Protocol\MyTools.Protocol.csproj" />
    <ProjectReference Include="..\MyTools.Host.Core\MyTools.Host.Core.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit3TestAdapter" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyTools.Host.Transports.Windows\MyTools.Host.Transports.Windows.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

把以下版本加入 `Directory.Packages.props` 的 `ItemGroup`：

```xml
<PackageVersion Include="System.IO.Pipes.AccessControl" Version="8.0.0" />
```

- [ ] **Step 3: 创建 SDK 构建定义**

```json
{
  "name": "@mytools/plugin-sdk-node",
  "version": "3.0.0",
  "private": true,
  "type": "module",
  "files": ["dist"],
  "exports": {
    ".": {
      "types": "./dist/src/index.d.ts",
      "import": "./dist/src/index.js"
    }
  },
  "scripts": {
    "clean": "node -e \"fs.rmSync('dist',{recursive:true,force:true})\"",
    "build": "npm run clean && tsc -p tsconfig.json",
    "check": "tsc -p tsconfig.json --noEmit",
    "test": "npm run build && node --test dist/test/*.test.js"
  },
  "dependencies": {
    "@mytools/protocol": "file:../MyTools.Protocol.TypeScript"
  },
  "devDependencies": {
    "@types/node": "^22.15.0",
    "typescript": "^5.8.3"
  }
}
```

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "strict": true,
    "declaration": true,
    "noEmitOnError": true,
    "rootDir": ".",
    "outDir": "dist",
    "types": ["node"]
  },
  "include": ["src/**/*.ts", "test/**/*.ts"]
}
```

- [ ] **Step 4: 加入 solution、安装锁文件并验证骨架**

Run:

```powershell
dotnet sln MyTools.sln add MyTools.Host.Transports.Windows\MyTools.Host.Transports.Windows.csproj MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj
Push-Location MyTools.PluginSdk.Node; npm install; npm run check; Pop-Location
dotnet build MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj
```

Expected: `package-lock.json` 创建；TypeScript 显示 `Found 0 errors`；MSBuild 输出 `Build succeeded`。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.sln Directory.Packages.props MyTools.Host.Transports.Windows MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj MyTools.PluginSdk.Node
git commit -m "build: add Windows plugin transport and Node SDK projects" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 2: 创建不可抢占的 Named Pipe server

**Files:**
- Create: `MyTools.Host.Transports.Windows\NamedPipes\SecureNamedPipeServer.cs`
- Create: `MyTools.Host.Transports.Windows.Test\NamedPipes\SecureNamedPipeServerTests.cs`

- [ ] **Step 1: 写 ACL 与抢占失败测试**

```csharp
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using MyTools.Host.Transports.Windows.NamedPipes;

namespace MyTools.Host.Transports.Windows.Test.NamedPipes;

[TestFixture]
public sealed class SecureNamedPipeServerTests
{
    [Test]
    public void Create_UsesFirstInstanceAndProtectedCurrentUserAcl()
    {
        using var server = SecureNamedPipeServer.Create($"mytools-test-{Guid.NewGuid():N}");
        var security = server.GetAccessControl();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(security.AreAccessRulesProtected, Is.True);
            Assert.That(rules.Any(x => x.IdentityReference.Equals(currentUser)
                && x.AccessControlType == AccessControlType.Allow
                && x.PipeAccessRights.HasFlag(PipeAccessRights.FullControl)), Is.True);
            Assert.That(rules.Any(x => x.IdentityReference.Equals(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null))), Is.True);
        });
    }

    [Test]
    public void Create_WhenNameAlreadyExists_RejectsSecondServer()
    {
        var name = $"mytools-test-{Guid.NewGuid():N}";
        using var first = SecureNamedPipeServer.Create(name);
        Assert.That(() => SecureNamedPipeServer.Create(name), Throws.TypeOf<IOException>());
    }
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter SecureNamedPipeServerTests
```

Expected: FAIL，编译器报告 `SecureNamedPipeServer` 不存在。

- [ ] **Step 3: 实现受保护 ACL 和 `FirstPipeInstance`**

```csharp
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MyTools.Host.Transports.Windows.NamedPipes;

public static class SecureNamedPipeServer
{
    public static NamedPipeServerStream Create(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows identity has no SID.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUser);
        security.AddAccessRule(new PipeAccessRule(
            currentUser, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough | PipeOptions.FirstPipeInstance,
            64 * 1024,
            64 * 1024,
            security,
            HandleInheritability.None,
            PipeAccessRights.ChangePermissions);
    }
}
```

- [ ] **Step 4: 运行测试确认绿灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter SecureNamedPipeServerTests
```

Expected: `Passed: 2, Failed: 0`。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows\NamedPipes MyTools.Host.Transports.Windows.Test\NamedPipes
git commit -m "feat: create non-squattable plugin pipes" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 3: 实现一次性 bootstrap token 和进程身份校验

**Files:**
- Create: `MyTools.Host.Transports.Windows\Security\BootstrapToken.cs`
- Create: `MyTools.Host.Transports.Windows\Security\PipePeerAuthenticator.cs`
- Create: `MyTools.Host.Transports.Windows\Processes\NativeMethods.cs`
- Create: `MyTools.Host.Transports.Windows.Test\Security\PipePeerAuthenticatorTests.cs`

- [ ] **Step 1: 写过期、重放、伪造身份和错误 PID 测试**

```csharp
using MyTools.Host.Transports.Windows.Security;
using MyTools.Protocol.V3;

namespace MyTools.Host.Transports.Windows.Test.Security;

[TestFixture]
public sealed class PipePeerAuthenticatorTests
{
    private static readonly EndpointIdentity Identity = new("sample", "main", "session-1", "node-main");

    [Test]
    public void Consume_AllowsExactlyOneMatchingHandshake()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var token = BootstrapToken.Create(now, TimeSpan.FromSeconds(10));
        Assert.That(token.TryConsume(token.Value, now.AddSeconds(1)), Is.True);
        Assert.That(token.TryConsume(token.Value, now.AddSeconds(2)), Is.False);
    }

    [Test]
    public void Consume_RejectsExpiredToken()
    {
        var now = DateTimeOffset.UtcNow;
        var token = BootstrapToken.Create(now, TimeSpan.FromSeconds(1));
        Assert.That(token.TryConsume(token.Value, now.AddSeconds(2)), Is.False);
    }

    [TestCase("other", "main")]
    [TestCase("sample", "other")]
    public void ValidateLaunchIdentity_RejectsForgedFields(string pluginId, string entryId)
    {
        Assert.That(
            () => PipePeerAuthenticator.ValidateLaunchIdentity(Identity, pluginId, entryId),
            Throws.TypeOf<AuthenticationException>());
    }

    [Test]
    public void ValidateProcess_RejectsPipeClientFromDifferentPid()
    {
        Assert.That(
            () => PipePeerAuthenticator.ValidateProcess(
                120, 121,
                DateTimeOffset.Parse("2026-08-13T12:00:00Z"),
                DateTimeOffset.Parse("2026-08-13T12:00:00Z")),
            Throws.TypeOf<AuthenticationException>());
    }
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter PipePeerAuthenticatorTests
```

Expected: FAIL，缺少 `BootstrapToken` 与 `PipePeerAuthenticator`。

- [ ] **Step 3: 实现 constant-time 一次性令牌**

```csharp
using System.Security.Cryptography;

namespace MyTools.Host.Transports.Windows.Security;

public sealed class BootstrapToken
{
    private readonly byte[] tokenBytes;
    private int consumed;

    private BootstrapToken(byte[] tokenBytes, DateTimeOffset expiresAtUtc)
    {
        this.tokenBytes = tokenBytes;
        Value = Convert.ToBase64String(tokenBytes);
        ExpiresAtUtc = expiresAtUtc;
    }

    public string Value { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public static BootstrapToken Create(DateTimeOffset now, TimeSpan lifetime) =>
        lifetime > TimeSpan.Zero
            ? new BootstrapToken(RandomNumberGenerator.GetBytes(32), now.Add(lifetime))
            : throw new ArgumentOutOfRangeException(nameof(lifetime));

    public bool TryConsume(string candidate, DateTimeOffset now)
    {
        byte[] candidateBytes;
        try { candidateBytes = Convert.FromBase64String(candidate); }
        catch (FormatException) { return false; }

        if (now > ExpiresAtUtc
            || !CryptographicOperations.FixedTimeEquals(tokenBytes, candidateBytes))
        {
            return false;
        }

        return Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
    }
}
```

- [ ] **Step 4: 实现 pipe client PID、句柄创建时间和身份比较**

```csharp
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using Microsoft.Win32.SafeHandles;
using MyTools.Protocol.V3;

namespace MyTools.Host.Transports.Windows.Security;

public static class PipePeerAuthenticator
{
    public static void ValidateLaunchIdentity(
        EndpointIdentity expected, string pluginId, string entryId)
    {
        if (!StringComparer.Ordinal.Equals(expected.PluginId, pluginId)
            || !StringComparer.Ordinal.Equals(expected.EntryId, entryId))
            throw new AuthenticationException("Handshake plugin or entry does not match the launch identity.");
    }

    public static void ValidateProcess(
        int expectedPid,
        int pipeClientPid,
        DateTimeOffset expectedStartedAtUtc,
        DateTimeOffset claimedStartedAtUtc)
    {
        if (expectedPid != pipeClientPid || expectedStartedAtUtc != claimedStartedAtUtc)
            throw new AuthenticationException("Handshake process identity does not match the launched process.");
    }

    public static int GetPipeClientProcessId(NamedPipeServerStream pipe)
    {
        if (!NativeMethods.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return checked((int)pid);
    }

    public static DateTimeOffset GetProcessStartedAtUtc(SafeProcessHandle processHandle)
    {
        if (!NativeMethods.GetProcessTimes(processHandle, out var created, out _, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var fileTime = ((long)created.dwHighDateTime << 32) | created.dwLowDateTime;
        return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
    }
}
```

在 `NativeMethods.cs` 定义且只定义所需签名：

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MyTools.Host.Transports.Windows;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime { internal uint dwLowDateTime; internal uint dwHighDateTime; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe, out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(
        SafeProcessHandle process, out FileTime creation, out FileTime exit,
        out FileTime kernel, out FileTime user);
}
```

- [ ] **Step 5: 运行测试确认绿灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter PipePeerAuthenticatorTests
```

Expected: `Passed: 5, Failed: 0`。

- [ ] **Step 6: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows\Security MyTools.Host.Transports.Windows\Processes\NativeMethods.cs MyTools.Host.Transports.Windows.Test\Security
git commit -m "feat: authenticate plugin pipe peers" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 4: 用 Job Object 管理挂起启动的 Node 进程树

**Files:**
- Create: `MyTools.Host.Transports.Windows\Processes\SafeJobHandle.cs`
- Create: `MyTools.Host.Transports.Windows\Processes\NodeJob.cs`
- Create: `MyTools.Host.Transports.Windows\Processes\SuspendedNodeProcess.cs`
- Create: `MyTools.Host.Transports.Windows.Test\Processes\NodeJobTests.cs`

- [ ] **Step 1: 写进程树回收和限制配置测试**

```csharp
using MyTools.Host.Core;
using MyTools.Host.Transports.Windows.Processes;

namespace MyTools.Host.Transports.Windows.Test.Processes;

[TestFixture]
public sealed class NodeJobTests
{
    [Test]
    public void Create_AppliesConfiguredLimits()
    {
        var limits = new NodeProcessLimits(128L << 20, 256L << 20, 3, 20);
        using var job = NodeJob.Create(limits);
        var actual = job.QueryLimits();
        Assert.Multiple(() =>
        {
            Assert.That(actual.KillOnJobClose, Is.True);
            Assert.That(actual.ProcessMemoryBytes, Is.EqualTo(128L << 20));
            Assert.That(actual.JobMemoryBytes, Is.EqualTo(256L << 20));
            Assert.That(actual.ActiveProcessLimit, Is.EqualTo(3));
            Assert.That(actual.CpuRatePercent, Is.EqualTo(20));
        });
    }

    [Test]
    public async Task Dispose_TerminatesChildProcessTree()
    {
        using var process = SuspendedNodeProcess.Start(
            "node", Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "spawn-child.mjs"),
            TestContext.CurrentContext.TestDirectory,
            new NodeProcessLimits(128L << 20, 256L << 20, 2, 50));
        process.Resume();
        var childPid = int.Parse((await process.StandardOutput.ReadLineAsync())!);
        process.Dispose();
        await Task.Delay(250);
        Assert.That(ProcessExists(childPid), Is.False);
    }

    private static bool ProcessExists(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
```

`Fixtures\spawn-child.mjs`：

```js
import { spawn } from "node:child_process";
const child = spawn(process.execPath, ["-e", "setInterval(() => {}, 1000)"], {
  stdio: "ignore",
  windowsHide: true
});
console.log(child.pid);
setInterval(() => {}, 1000);
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter NodeJobTests
```

Expected: FAIL，缺少 `NodeJob` 和 `SuspendedNodeProcess`。

- [ ] **Step 3: 实现 Job 限制与安全句柄**

在 `NativeMethods.cs` 增加 `CreateJobObjectW`、`SetInformationJobObject`、`QueryInformationJobObject`、`AssignProcessToJobObject`、`TerminateJobObject`，以及官方布局的 `JOBOBJECT_EXTENDED_LIMIT_INFORMATION` 和 `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION`。`NodeJob.Create` 必须组合以下标志并检查每个 Win32 返回值：

```csharp
const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
const uint JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200;
const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

extended.BasicLimitInformation.LimitFlags =
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
    JOB_OBJECT_LIMIT_PROCESS_MEMORY |
    JOB_OBJECT_LIMIT_JOB_MEMORY |
    JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
extended.ProcessMemoryLimit = (nuint)limits.ProcessMemoryBytes;
extended.JobMemoryLimit = (nuint)limits.JobMemoryBytes;
extended.BasicLimitInformation.ActiveProcessLimit = limits.ActiveProcessLimit;
cpu.ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP;
cpu.CpuRate = limits.CpuRatePercent * 100;
```

`SafeJobHandle` 的释放必须关闭 handle；`NodeJob.Dispose` 先调用 `TerminateJobObject(handle, 1)`，再释放 handle。`QueryLimits()` 从 Job 查询真实值而非返回构造参数。

- [ ] **Step 4: 实现无竞态的挂起创建顺序**

`SuspendedNodeProcess.Start` 使用 `CreatePipe` 创建 stdin/stdout/stderr，清除宿主端 `HANDLE_FLAG_INHERIT`，然后：

```csharp
var commandLine = $"\"{nodeExecutable}\" \"{entryPath}\"";
var startup = new STARTUPINFO
{
    cb = Marshal.SizeOf<STARTUPINFO>(),
    dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW,
    wShowWindow = SW_HIDE,
    hStdInput = childStdInRead,
    hStdOutput = childStdOutWrite,
    hStdError = childStdErrWrite
};
if (!NativeMethods.CreateProcessW(
        null, commandLine, 0, 0, true,
        CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
        0, workingDirectory, ref startup, out var info))
    throw new Win32Exception(Marshal.GetLastWin32Error());

var job = NodeJob.Create(limits);
job.Assign(info.hProcess);
return new SuspendedNodeProcess(
    job, info.hProcess, info.hThread,
    new StreamWriter(new FileStream(hostStdInWrite, FileAccess.Write), new UTF8Encoding(false))
        { AutoFlush = true },
    new StreamReader(new FileStream(hostStdOutRead, FileAccess.Read), new UTF8Encoding(false)),
    new StreamReader(new FileStream(hostStdErrRead, FileAccess.Read), new UTF8Encoding(false)));
```

失败路径必须按 thread handle → process handle → pipe handles → Job 的逆序释放。只有 `AssignProcessToJobObject` 成功后 `Resume()` 才调用 `ResumeThread`。公开 `ProcessId`、宿主持有的 `SafeProcessHandle`、`StandardInput/Output/Error`、`WaitForExitAsync`；不公开原始句柄。

- [ ] **Step 5: 运行测试确认绿灯及无残留**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter NodeJobTests
Get-Process node -ErrorAction SilentlyContinue | Where-Object Path -Like '*MyTools.Host.Transports.Windows.Test*'
```

Expected: `Passed: 2, Failed: 0`；第二条命令无输出。活动进程限制测试若创建第 3 个进程，应收到 Windows 拒绝且 Job 中活动进程不超过 2。

- [ ] **Step 6: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows\Processes MyTools.Host.Transports.Windows.Test\Processes MyTools.Host.Transports.Windows.Test\Fixtures\spawn-child.mjs
git commit -m "feat: constrain Node process trees with jobs" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 5: 实现帧 transport、先建管道再启动及认证握手

**Files:**
- Create: `MyTools.Host.Transports.Windows\NamedPipes\NamedPipeMessageTransport.cs`
- Create: `MyTools.Host.Transports.Windows\WindowsNodeEndpoint.cs`
- Create: `MyTools.Host.Transports.Windows\WindowsNodeEndpointFactory.cs`
- Create: `MyTools.Host.Transports.Windows.Test\WindowsNodeEndpointTests.cs`

- [ ] **Step 1: 写启动顺序、stdin 单次 bootstrap 和握手测试**

```csharp
[TestFixture]
public sealed class WindowsNodeEndpointTests
{
    [Test]
    public async Task StartAsync_CreatesPipeBeforeProcessAndAuthenticatesHeldProcess()
    {
        var events = new RecordingNodeEndpointEvents();
        await using var endpoint = WindowsNodeEndpointFactory.CreateForTests(
            Fixture("node-endpoint.mjs"), events, TimeProvider.System);

        await endpoint.StartAsync(CancellationToken.None);

        Assert.That(endpoint.Trace, Is.EqualTo(new[]
        {
            "pipe-created", "process-created-suspended", "job-assigned",
            "process-resumed", "bootstrap-written", "pipe-connected", "handshake-authenticated"
        }));
        Assert.That(events.Connected.Single().Identity.EndpointId, Is.EqualTo("node-main"));
        Assert.That(endpoint.BootstrapWriteCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StartAsync_WhenTokenIsReplayed_RejectsSecondHandshake()
    {
        await using var endpoint = WindowsNodeEndpointFactory.CreateForTests(
            Fixture("replay-token.mjs"), new RecordingNodeEndpointEvents(), TimeProvider.System);
        var exception = Assert.ThrowsAsync<AuthenticationException>(
            async () => await endpoint.StartAsync(CancellationToken.None));
        Assert.That(exception!.Message, Does.Contain("already consumed"));
    }
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter WindowsNodeEndpointTests
```

Expected: FAIL，缺少 endpoint、factory 和 transport。

- [ ] **Step 3: 实现有界帧 transport**

`NamedPipeMessageTransport` 以一个 `Channel<OutboundFrame>` 作为单写者输入，容量等于 Host Core endpoint 配置总和；优先级由 control/response、request、event 三个有界 channel 加权轮询。核心读写必须是：

```csharp
public async ValueTask SendAsync(
    MessageEnvelope envelope, TransportPriority priority, CancellationToken cancellationToken)
{
    var json = JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJson.SerializerOptions);
    using var document = JsonDocument.Parse(json);
    var validation = ProtocolValidator.ValidateEnvelope(document.RootElement);
    if (!validation.IsValid)
        throw new InvalidDataException(
            $"{ProtocolErrorCodes.InvalidPayload}: " +
            $"{validation.Issues[0].Path} {validation.Issues[0].Message}");
    if (json.Length > maxFrameBytes)
        throw new InvalidDataException(ProtocolErrorCodes.MessageTooLarge);
    await channels[(int)priority].Writer.WriteAsync(json, cancellationToken);
}

private async Task ReadLoopAsync(CancellationToken cancellationToken)
{
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var json = await LengthPrefixedJsonFrameCodec.ReadAsync(
                pipe, maxFrameBytes, cancellationToken);
            using var document = JsonDocument.Parse(json);
            var validation = ProtocolValidator.ValidateEnvelope(document.RootElement);
            if (!validation.IsValid)
                throw new InvalidDataException(
                    $"{ProtocolErrorCodes.InvalidPayload}: " +
                    $"{validation.Issues[0].Path} {validation.Issues[0].Message}");
            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(
                json, ProtocolJson.SerializerOptions)
                ?? throw new InvalidDataException("Envelope JSON was null.");
            await inbound.Writer.WriteAsync(envelope, cancellationToken);
        }
    }
    catch (Exception error) when (
        error is IOException or EndOfStreamException or InvalidDataException or JsonException)
    {
        Complete(new TransportDisconnect(
            ProtocolErrorCodes.TransportDisconnected, "Named pipe disconnected.", error));
    }
}

private async Task WriteFrameAsync(byte[] json, CancellationToken cancellationToken) =>
    await LengthPrefixedJsonFrameCodec.WriteAsync(pipe, json, cancellationToken);
```

非法长度、截断、非法 JSON 或非法 envelope 关闭当前连接；不捕获到全局总线。`Completion` 仅完成一次，断线时让所有 channel complete。

- [ ] **Step 4: 实现 endpoint 安全启动和握手**

`WindowsNodeEndpoint.StartAsync` 固定执行：

```csharp
pipeName = $"mytools-plugin-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";
pipe = SecureNamedPipeServer.Create(pipeName);
Trace.Add("pipe-created");
token = BootstrapToken.Create(timeProvider.GetUtcNow(), TimeSpan.FromSeconds(15));
process = SuspendedNodeProcess.Start("node", launch.EntryPath, launch.WorkingDirectory, limits);
Trace.Add("process-created-suspended");
Trace.Add("job-assigned");
process.Resume();
Trace.Add("process-resumed");
await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
{
    pipeName,
    launchToken = token.Value,
    expiresAtUtc = token.ExpiresAtUtc,
    pluginId = launch.Identity.PluginId,
    entryId = launch.Identity.EntryId,
    processId = process.ProcessId,
    processStartedAtUtc =
        PipePeerAuthenticator.GetProcessStartedAtUtc(process.ProcessHandle)
}));
process.StandardInput.Close();
BootstrapWriteCount++;
Trace.Add("bootstrap-written");
await pipe.WaitForConnectionAsync(startupTimeout.Token);
Trace.Add("pipe-connected");
var requestBytes = await LengthPrefixedJsonFrameCodec.ReadAsync(
    pipe, maxFrameBytes, handshakeTimeout.Token);
using var requestDocument = JsonDocument.Parse(requestBytes);
var requestValidation = ProtocolValidator.ValidateEnvelope(requestDocument.RootElement);
if (!requestValidation.IsValid)
    throw new AuthenticationException("Handshake envelope is invalid.");
var requestEnvelope = JsonSerializer.Deserialize<MessageEnvelope>(
    requestBytes, ProtocolJson.SerializerOptions)
    ?? throw new AuthenticationException("Handshake envelope is absent.");
if (requestEnvelope.Route != "bus.handshake"
    || requestEnvelope.Kind != MessageKind.Request
    || requestEnvelope.SessionId is not null
    || requestEnvelope.EndpointId is not null)
    throw new AuthenticationException("Handshake envelope binding is invalid.");
var request = requestEnvelope.Payload
    .Deserialize<HandshakeRequest>(ProtocolJson.SerializerOptions)
    ?? throw new AuthenticationException("Handshake payload is absent.");
Authenticate(request);
Trace.Add("handshake-authenticated");
```

握手 request envelope 的 `sessionId`、`endpointId` 必须为 `null`；子进程不能从 bootstrap、配置或 API 选取这两个值。`Authenticate` 先校验唯一允许 route 为 `bus.handshake`，再按 `launchToken` → `pluginId`/`entryId` → pipe client PID → 宿主持有句柄的 `processStartedAtUtc` → `supportedVersions` 顺序校验。失败返回非敏感 `HandshakeFailed` 后关闭 pipe、终止 Job；日志不得包含 token。成功后由 Host 发送 `new HandshakeResponse(selectedVersion, launch.Identity.SessionId, launch.Identity.EndpointId)`，Node 只接受响应中的绑定，再构造 `NamedPipeMessageTransport` 并调用 `INodeEndpointEvents.ConnectedAsync`。

- [ ] **Step 5: 运行测试确认绿灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter WindowsNodeEndpointTests
```

Expected: 启动顺序、一次 stdin、握手、重放和伪造 PID 测试全部 PASS。

- [ ] **Step 6: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows\NamedPipes MyTools.Host.Transports.Windows\WindowsNodeEndpoint.cs MyTools.Host.Transports.Windows\WindowsNodeEndpointFactory.cs MyTools.Host.Transports.Windows.Test\WindowsNodeEndpointTests.cs
git commit -m "feat: authenticate Node endpoints over named pipes" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 6: 实现 Host ping、断线停树和重启通知

**Files:**
- Modify: `MyTools.Host.Transports.Windows\WindowsNodeEndpoint.cs`
- Create: `MyTools.Host.Transports.Windows\NodeHeartbeatMonitor.cs`
- Create: `MyTools.Host.Transports.Windows.Test\NodeHeartbeatMonitorTests.cs`
- Create: `MyTools.Host.Transports.Windows.Test\NodeEndpointRecoveryTests.cs`

- [ ] **Step 1: 写 ping/pong 和假死测试**

```csharp
[TestFixture]
public sealed class NodeHeartbeatMonitorTests
{
    [Test]
    public async Task TickAsync_MatchesPongAndReportsMonotonicRtt()
    {
        var clock = new FakeMonotonicClock();
        var transport = new FakeMessageTransport();
        var monitor = new NodeHeartbeatMonitor(transport, clock, TimeSpan.FromSeconds(5), 3);
        var pending = monitor.TickAsync(CancellationToken.None);
        var ping = transport.Sent.Single(x => x.Route == "bus.ping");
        clock.Advance(TimeSpan.FromMilliseconds(37));
        transport.Receive(PongFor(ping));
        Assert.That(await pending, Is.EqualTo(TimeSpan.FromMilliseconds(37)));
    }

    [Test]
    public async Task TickAsync_AfterThreeTimeouts_DisconnectsAsUnhealthy()
    {
        var monitor = CreateMonitor(timeout: TimeSpan.FromMilliseconds(1), threshold: 3);
        await Assert.ThrowsAsync<TimeoutException>(() => monitor.TickAsync(default));
        await Assert.ThrowsAsync<TimeoutException>(() => monitor.TickAsync(default));
        var error = Assert.ThrowsAsync<TransportDisconnectedException>(
            () => monitor.TickAsync(default));
        Assert.That(error!.Disconnect.Reason, Is.EqualTo("heartbeat-timeout"));
    }
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter "NodeHeartbeatMonitorTests|NodeEndpointRecoveryTests"
```

Expected: FAIL，`NodeHeartbeatMonitor` 不存在。

- [ ] **Step 3: 实现 Host-only 心跳**

```csharp
public async Task<TimeSpan> TickAsync(CancellationToken cancellationToken)
{
    var ping = MessageEnvelopeFactory.Request(identity, "bus.ping", new { }, heartbeatTimeout);
    var sent = clock.GetTimestamp();
    try
    {
        var pong = await controlRequests.SendAsync(ping, cancellationToken);
        if (pong.Route != "bus.pong" || pong.CorrelationId != ping.Id || pong.TraceId != ping.TraceId)
            throw new InvalidDataException(ProtocolErrorCodes.InvalidPayload);
        consecutiveTimeouts = 0;
        var rtt = clock.GetElapsedTime(sent);
        await events.HeartbeatAsync(identity, rtt, cancellationToken);
        return rtt;
    }
    catch (TimeoutException) when (++consecutiveTimeouts >= timeoutThreshold)
    {
        throw new TransportDisconnectedException(new(
            ProtocolErrorCodes.TransportDisconnected, "heartbeat-timeout"));
    }
}
```

只由 Host 创建 `bus.ping`；不得实现 Node 主动 ping。ping 走 control 保留容量，不被 request/event 拥塞。

- [ ] **Step 4: 把断线统一收敛到 session restart 端口**

在 `WindowsNodeEndpoint` 中让 pipe EOF、非法帧、进程退出和 heartbeat threshold 都调用同一个幂等方法：

```csharp
private async Task DisconnectOnceAsync(TransportDisconnect reason)
{
    if (Interlocked.Exchange(ref disconnected, 1) != 0)
        return;
    lifetime.Cancel();
    await transport.DisposeAsync();
    process.Dispose(); // closes Job and terminates the full tree
    await events.DisconnectedAsync(launch.Identity, reason, CancellationToken.None);
}
```

不在 transport 层自行重启。Host Core 的 session actor 收到 `DisconnectedAsync` 后令 pending 请求失败为 `TransportDisconnected`、进入 `Restarting`，并按第 2/5 份计划的抖动指数退避创建全新的 sessionId、endpointId、pipeName 和 token；宿主重启时旧 pipe 不接受 resume。

- [ ] **Step 5: 验证真实崩溃与宿主重启语义**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter "NodeHeartbeatMonitorTests|NodeEndpointRecoveryTests"
```

Expected: pong 关联/RTT、连续三次超时、pipe EOF、Node exit、pending `TransportDisconnected`、旧 session 拒绝及 Host 分配的新 session/endpoint 绑定全部 PASS。

- [ ] **Step 6: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows\WindowsNodeEndpoint.cs MyTools.Host.Transports.Windows\NodeHeartbeatMonitor.cs MyTools.Host.Transports.Windows.Test\NodeHeartbeatMonitorTests.cs MyTools.Host.Transports.Windows.Test\NodeEndpointRecoveryTests.cs
git commit -m "feat: restart unhealthy Node endpoints" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 7: 建立 `host.call.worker.spawn` 信任链

**Files:**
- Create: `MyTools.Host.Transports.Windows\WorkerSpawnService.cs`
- Create: `MyTools.Host.Transports.Windows.Test\WorkerSpawnServiceTests.cs`

- [ ] **Step 1: 写主 endpoint、路径和 capability 子集测试**

```csharp
[TestFixture]
public sealed class WorkerSpawnServiceTests
{
    [Test]
    public async Task SpawnAsync_FromAuthenticatedMain_CreatesIndependentWorker()
    {
        var main = Context(isMain: true, capabilities: ["configuration.read", "clipboard.read"]);
        var result = await service.SpawnAsync(main, new(
            "workers/index.mjs", ["configuration.read"]), default);
        Assert.Multiple(() =>
        {
            Assert.That(result.Identity.PluginId, Is.EqualTo(main.Identity.PluginId));
            Assert.That(result.Identity.SessionId, Is.EqualTo(main.Identity.SessionId));
            Assert.That(result.Identity.EndpointId, Does.StartWith("node-worker-"));
            Assert.That(result.Capabilities, Is.EquivalentTo(new[] { "configuration.read" }));
            Assert.That(factory.LastRequest!.IsWorker, Is.True);
        });
    }

    [TestCase(false, "configuration.read", "CapabilityDenied")]
    [TestCase(true, "shell.execute", "CapabilityNotDeclared")]
    public void SpawnAsync_RejectsBrokenTrustChain(
        bool isMain, string requestedCapability, string expectedCode)
    {
        var error = Assert.ThrowsAsync<BusException>(() => service.SpawnAsync(
            Context(isMain, ["configuration.read"]),
            new("workers/index.mjs", [requestedCapability]), default).AsTask());
        Assert.That(error!.Error.Code, Is.EqualTo(expectedCode));
    }
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter WorkerSpawnServiceTests
```

Expected: FAIL，`WorkerSpawnService` 不存在。

- [ ] **Step 3: 实现可信创建链**

```csharp
public async ValueTask<WorkerSpawnResult> SpawnAsync(
    AuthenticatedEndpointContext caller,
    WorkerSpawnRequest request,
    CancellationToken cancellationToken)
{
    if (caller.Kind != EndpointKind.MainNode)
        throw new BusException(new(
            ProtocolErrorCodes.CapabilityDenied,
            "Only the authenticated main Node endpoint may spawn workers.",
            false));
    var capabilities = request.Capabilities.ToHashSet(StringComparer.Ordinal);
    if (!capabilities.IsSubsetOf(caller.GrantedCapabilities))
        throw new BusException(new(
            ProtocolErrorCodes.CapabilityNotDeclared,
            "Worker capabilities must be a subset of the main endpoint grant.",
            false));
    var fullPath = Path.GetFullPath(request.EntryPath, pluginRoot);
    if (!fullPath.StartsWith(pluginRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        throw new BusException(new(
            ProtocolErrorCodes.InvalidPayload,
            "Worker entry must stay inside the plugin package.",
            false));

    var identity = caller.Identity with
    {
        EndpointId = $"node-worker-{Interlocked.Increment(ref workerSequence)}"
    };
    var endpoint = factory.Create(new NodeLaunchRequest(
        identity, fullPath, pluginRoot, capabilities, IsWorker: true));
    await endpoint.StartAsync(cancellationToken);
    await registration.RegisterWorkerAsync(
        caller.Identity, identity, capabilities, endpoint.Transport, cancellationToken);
    return new WorkerSpawnResult(identity, capabilities);
}
```

调用上下文只能来自 Host Core 已认证 transport 绑定，不能从 payload 读取 `pluginId`、`entryId`、`sessionId`、主/Worker 标志或已授权 capability。Worker 使用独立进程、Job、pipe、token 和 endpointId；自行 `fork` 的子进程留在 Job 内但永远不注册 endpoint。

- [ ] **Step 4: 运行测试确认绿灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter WorkerSpawnServiceTests
```

Expected: 主 endpoint 成功；Worker 调 Worker、越界路径、超集 capability、伪造 payload 身份全部被拒绝。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows\WorkerSpawnService.cs MyTools.Host.Transports.Windows.Test\WorkerSpawnServiceTests.cs
git commit -m "feat: authorize host-created Node workers" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 8: 实现 Node SDK bootstrap、framing 与握手

**Files:**
- Create: `MyTools.PluginSdk.Node\src\bootstrap.ts`
- Create: `MyTools.PluginSdk.Node\src\framing.ts`
- Create: `MyTools.PluginSdk.Node\src\connection.ts`
- Create: `MyTools.PluginSdk.Node\test\bootstrap.test.ts`
- Create: `MyTools.PluginSdk.Node\test\framing.test.ts`

- [ ] **Step 1: 写 stdin 一次性读取和分片/粘包测试**

```ts
test("readBootstrap consumes one line and stops using stdin", async () => {
  const input = Readable.from(['{"pipeName":"p","launchToken":"t",',
    '"expiresAtUtc":"2026-08-13T12:00:10Z","pluginId":"p","entryId":"main",',
    '"processId":42,"processStartedAtUtc":"2026-08-13T12:00:00Z"}\nignored\n']);
  const value = await readBootstrap(input);
  assert.equal(value.pipeName, "p");
  assert.equal("sessionId" in value, false);
  assert.equal("endpointId" in value, false);
  assert.equal(input.listenerCount("data"), 0);
});

test("decoder handles fragmented and coalesced frames", () => {
  const decoder = new FrameDecoder(1024);
  const one = encodeFrame(Buffer.from('{"id":"1"}'));
  const two = encodeFrame(Buffer.from('{"id":"2"}'));
  assert.deepEqual(decoder.push(Buffer.concat([one.subarray(0, 3)])), []);
  assert.deepEqual(decoder.push(Buffer.concat([one.subarray(3), two]))
    .map(x => x.toString()), ['{"id":"1"}', '{"id":"2"}']);
});

test("decoder rejects zero and oversized lengths before allocation", () => {
  assert.throws(() => new FrameDecoder(8).push(Buffer.alloc(4)), /zero-length/);
  const header = Buffer.alloc(4); header.writeUInt32LE(9);
  assert.throws(() => new FrameDecoder(8).push(header), /MessageTooLarge/);
});

test("handshake request omits host-owned binding and response supplies it", () => {
  const bootstrap: Bootstrap = {
    pipeName: "pipe",
    launchToken: "t",
    expiresAtUtc: "2026-08-13T12:00:10Z",
    pluginId: "p",
    entryId: "main",
    processId: 42,
    processStartedAtUtc: "2026-08-13T12:00:00Z"
  };
  assert.deepEqual(buildHandshakeRequest(bootstrap), {
    supportedVersions: ["3.0"],
    launchToken: "t",
    pluginId: "p",
    entryId: "main",
    processId: 42,
    processStartedAtUtc: "2026-08-13T12:00:00Z"
  });
  assert.deepEqual(bindHandshakeResponse(bootstrap, {
    selectedVersion: "3.0",
    sessionId: "session-host",
    endpointId: "endpoint-host"
  }), {
    pluginId: "p",
    entryId: "main",
    sessionId: "session-host",
    endpointId: "endpoint-host"
  });
});
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm test; Pop-Location
```

Expected: TypeScript 报告 `readBootstrap`、`FrameDecoder`、`encodeFrame` 不存在。

- [ ] **Step 3: 实现 bootstrap 与 framing**

```ts
export interface Bootstrap {
  readonly pipeName: string;
  launchToken: string;
  readonly expiresAtUtc: string;
  readonly pluginId: string;
  readonly entryId: string;
  readonly processId: number;
  readonly processStartedAtUtc: string;
}

function assertBootstrap(value: unknown): asserts value is Bootstrap {
  if (typeof value !== "object" || value === null)
    throw new Error("Bootstrap must be an object.");
  const item = value as Record<string, unknown>;
  const keys = [
    "pipeName", "launchToken", "expiresAtUtc", "pluginId",
    "entryId", "processId", "processStartedAtUtc"
  ];
  if (Object.keys(item).length !== keys.length || keys.some(key => !(key in item)))
    throw new Error("Bootstrap fields do not match the launch contract.");
  if (["pipeName", "launchToken", "expiresAtUtc", "pluginId", "entryId",
       "processStartedAtUtc"].some(key => typeof item[key] !== "string")
      || !Number.isInteger(item.processId) || (item.processId as number) <= 0
      || Number.isNaN(Date.parse(item.expiresAtUtc as string))
      || Number.isNaN(Date.parse(item.processStartedAtUtc as string)))
    throw new Error("Bootstrap contains invalid values.");
}

export async function readBootstrap(input: NodeJS.ReadableStream = process.stdin): Promise<Bootstrap> {
  const line = await new Promise<string>((resolve, reject) => {
    let text = "";
    const onData = (chunk: Buffer | string) => {
      text += chunk.toString();
      const newline = text.indexOf("\n");
      if (newline >= 0) { cleanup(); resolve(text.slice(0, newline)); }
    };
    const onEnd = () => { cleanup(); reject(new Error("Bootstrap stdin closed before newline.")); };
    const cleanup = () => {
      input.off("data", onData); input.off("end", onEnd); input.pause();
    };
    input.on("data", onData); input.once("end", onEnd); input.resume();
  });
  const value: unknown = JSON.parse(line);
  assertBootstrap(value);
  return value;
}
```

```ts
export function encodeFrame(payload: Buffer): Buffer {
  if (payload.length === 0 || payload.length > MAX_FRAME_BYTES)
    throw new ProtocolSdkError("MessageTooLarge", `Invalid frame length ${payload.length}.`);
  const result = Buffer.allocUnsafe(4 + payload.length);
  result.writeUInt32LE(payload.length, 0);
  payload.copy(result, 4);
  return result;
}

export class FrameDecoder {
  #buffer = Buffer.alloc(0);
  constructor(readonly maxFrameBytes = MAX_FRAME_BYTES) {}
  push(chunk: Buffer): Buffer[] {
    this.#buffer = Buffer.concat([this.#buffer, chunk]);
    const frames: Buffer[] = [];
    while (this.#buffer.length >= 4) {
      const length = this.#buffer.readUInt32LE(0);
      if (length === 0) throw new Error("zero-length frame");
      if (length > this.maxFrameBytes) throw new ProtocolSdkError("MessageTooLarge", String(length));
      if (this.#buffer.length < length + 4) break;
      frames.push(this.#buffer.subarray(4, length + 4));
      this.#buffer = this.#buffer.subarray(length + 4);
    }
    return frames;
  }
}
```

`connection.ts` 用 `net.createConnection("\\\\.\\pipe\\" + pipeName)`；连接成功后发送唯一的 `bus.handshake` request。request envelope 的 `sessionId`/`endpointId` 固定为 `null`，payload 严格构造为：

```ts
const SUPPORTED_VERSIONS = ["3.0"] as const;

export function buildHandshakeRequest(bootstrap: Bootstrap): HandshakeRequest {
  return {
    supportedVersions: [...SUPPORTED_VERSIONS],
    launchToken: bootstrap.launchToken,
    pluginId: bootstrap.pluginId,
    entryId: bootstrap.entryId,
    processId: bootstrap.processId,
    processStartedAtUtc: bootstrap.processStartedAtUtc
  };
}

export function bindHandshakeResponse(
  bootstrap: Bootstrap,
  response: HandshakeResponse
): EndpointIdentity {
  if (!SUPPORTED_VERSIONS.includes(
      response.selectedVersion as (typeof SUPPORTED_VERSIONS)[number]))
    throw new Error(`Host selected unsupported protocol ${response.selectedVersion}.`);
  return {
    pluginId: bootstrap.pluginId,
    entryId: bootstrap.entryId,
    sessionId: response.sessionId,
    endpointId: response.endpointId
  };
}
```

收到协商响应前拒绝其他帧。先用 `validateEnvelope` 校验响应，再验证 `selectedVersion` 属于 `SUPPORTED_VERSIONS`，最后仅从 `HandshakeResponse.sessionId`/`endpointId` 建立 `EndpointIdentity`；SDK 不提供覆盖这两个字段的 option。握手成功后立刻把内存中的 `launchToken` 改为空字符串。

- [ ] **Step 4: 运行测试确认绿灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm test; Pop-Location
```

Expected: bootstrap、零长度、超限、截断、分片、粘包和握手测试全部 PASS。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.PluginSdk.Node
git commit -m "feat: connect Node SDK over framed named pipes" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 9: 实现 Node SDK 路由、Schema 校验和 AbortSignal

**Files:**
- Create: `MyTools.PluginSdk.Node\src\errors.ts`
- Create: `MyTools.PluginSdk.Node\src\route-manifest.ts`
- Create: `MyTools.PluginSdk.Node\src\client.ts`
- Create: `MyTools.PluginSdk.Node\src\index.ts`
- Create: `MyTools.PluginSdk.Node\test\route-manifest.test.ts`
- Create: `MyTools.PluginSdk.Node\test\client.test.ts`

- [ ] **Step 1: 写认证后 manifest 注册、失败关闭及身份生命周期测试**

在 `route-manifest.test.ts` 中使用已完成握手且 `identity` 固定为
`{ pluginId: "sample", entryId: "main", sessionId: "s1", endpointId: "e1" }`
的 `FakeConnection`。测试文件把 artifact 写到仓库内
`MyTools.PluginSdk.Node\.test-work`，并在 `afterEach` 删除，不能依赖机器全局临时目录：

```ts
const work = resolve(process.cwd(), ".test-work");
const identity = {
  pluginId: "sample", entryId: "main", sessionId: "s1", endpointId: "e1"
} as const;

async function writeArtifact(name: string, value: unknown): Promise<string> {
  await mkdir(work, { recursive: true });
  const path = join(work, `${name}.json`);
  await writeFile(path,
    typeof value === "string" ? value : `${JSON.stringify(value)}\n`, "utf8");
  return path;
}

function manifest(route: string, pluginId = "sample", entryId = "main") {
  return {
    protocolVersion: "3.0",
    routes: {
      [route]: {
        pluginId,
        entryId,
        request: {
          type: "object", additionalProperties: false, required: ["text"],
          properties: { text: { type: "string", minLength: 1 } }
        },
        response: {
          type: "object", additionalProperties: false, required: ["html"],
          properties: { html: { type: "string" } }
        }
      }
    }
  };
}

afterEach(async () => {
  await rm(work, { recursive: true, force: true });
});

test("startup registers an authenticated manifest before exposing the client", async () => {
  const path = await writeArtifact(
    "valid", manifest("plugin.call.sample.render"));
  const connection = new FakeConnection(identity);
  const client = await createAuthenticatedClientForTest(connection, {
    routeManifestPath: path
  });

  let invoked = false;
  client.handle("plugin.call.sample.render", async ({ text }) => {
    invoked = true;
    return { html: `<p>${text}</p>` };
  });
  connection.receive(request("r1", "plugin.call.sample.render", { text: "hello" }));
  await setImmediatePromise();

  assert.equal(invoked, true);
  assert.deepEqual(connection.responsesFor("r1")[0].payload,
    { html: "<p>hello</p>" });
  await client.close();
});

test("canonical routes work when no manifest is present or duplicated", async () => {
  const connection = new FakeConnection(identity);
  const client = await createAuthenticatedClientForTest(connection, {
    routeManifestPath: false
  });
  client.handle("plugin.call.saveConfiguration", async () =>
    ({ requiresRestart: false }));
  connection.receive(request("r2", "plugin.call.saveConfiguration", {
    changes: [{ fullPath: "General.Theme", value: "dark" }]
  }));
  await setImmediatePromise();
  assert.equal(connection.responsesFor("r2")[0].error, null);
  await client.close();
});

test("malformed, canonical-conflicting, and identity-mismatched manifests fail startup", async () => {
  const cases = [
    await writeArtifact("malformed", "{"),
    await writeArtifact("canonical",
      manifest("host.call.configuration.read")),
    await writeArtifact("wrong-identity",
      manifest("plugin.call.sample.render", "other", "main"))
  ];
  for (const routeManifestPath of cases) {
    const connection = new FakeConnection(identity);
    await assert.rejects(
      createAuthenticatedClientForTest(connection, { routeManifestPath }),
      (error: PluginSdkError) => error.code === "HandshakeFailed");
    assert.equal(connection.closed, true);
    assert.equal(connection.sent.length, 0);
  }
});

test("close removes only the authenticated pluginId and entryId ownership", async () => {
  const connection = new FakeConnection(identity);
  const client = await createAuthenticatedClientForTest(connection, {
    routeManifestPath: false
  });
  assert.deepEqual(activeManifestIdentityKeysForTest(), ["sample\u0000main"]);
  await client.close();
  assert.deepEqual(activeManifestIdentityKeysForTest(), []);
  const other = new FakeConnection({
    ...identity, pluginId: "other", sessionId: "s2", endpointId: "e2"
  });
  await assert.rejects(
    createAuthenticatedClientForTest(other, { routeManifestPath: false }),
    (error: PluginSdkError) => error.code === "HandshakeFailed");
  assert.equal(other.closed, true);
});
```

这里的 `createAuthenticatedClientForTest` 走与 `connectPlugin` 相同的
manifest 注册和 client 构造函数，只跳过 stdin/pipe 握手；它从
`src\client.ts` 直接导出供测试使用，不从包的 `src\index.ts` 公共入口导出。

- [ ] **Step 2: 写请求/响应方向、取消和 at-most-once 测试**

追加到 `client.test.ts`：

```ts
test("outgoing call validates request before writing a frame", async () => {
  const connection = new FakeConnection();
  const client = createClientForTest(connection);
  await assert.rejects(
    client.call("host.call.configuration.write", { key: 3 }),
    (error: PluginSdkError) =>
      error.code === "InvalidPayload" &&
      Array.isArray(error.details?.issues) && error.details.issues.length > 0);
  assert.equal(connection.sent.length, 0);
});

test("a successful call validates its response schema before resolving", async () => {
  const connection = new FakeConnection();
  const client = createClientForTest(connection);
  const pending = client.call(
    "host.call.configuration.read", { key: "theme" });
  connection.respondToLast({ wrong: true });
  await assert.rejects(pending,
    (error: PluginSdkError) => error.code === "InvalidPayload");
});

test("incoming handler validates request then successful response", async () => {
  const connection = new FakeConnection();
  const client = createClientForTest(connection);
  let calls = 0;
  client.handle("plugin.call.saveConfiguration", async () => {
    calls++;
    return { wrong: true };
  });
  connection.receive(request("bad-request", "plugin.call.saveConfiguration", {}));
  connection.receive(request("bad-response", "plugin.call.saveConfiguration", {
    changes: [{ fullPath: "General.Theme", value: "dark" }]
  }));
  await setImmediatePromise();
  assert.equal(calls, 1);
  assert.equal(connection.responsesFor("bad-request")[0].error?.code, "InvalidPayload");
  assert.equal(connection.responsesFor("bad-response")[0].error?.code, "InvalidPayload");
});

test("bus.cancel aborts the matching handler", async () => {
  const connection = new FakeConnection();
  const client = createClientForTest(connection);
  let signal: AbortSignal | undefined;
  client.handle("plugin.call.wait", async (_payload, context) => {
    signal = context.signal;
    await once(context.signal, "abort");
    throw context.signal.reason;
  });
  connection.receive(request("r1", "plugin.call.wait", {}));
  connection.receive(cancel("r1"));
  await setImmediatePromise();
  assert.equal(signal?.aborted, true);
  assert.equal(connection.responsesFor("r1")[0].error?.code, "Cancelled");
});

test("disconnect rejects pending calls without replay", async () => {
  const connection = new FakeConnection();
  const client = createClientForTest(connection);
  const call = client.call("host.call.configuration.read", { key: "theme" });
  connection.disconnect();
  await assert.rejects(call, (error: PluginSdkError) => error.code === "TransportDisconnected");
  assert.equal(connection.sent.filter(x => x.route === "host.call.configuration.read").length, 1);
});
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm test; Pop-Location
```

Expected: FAIL，`route-manifest.ts`、manifest startup、响应 Schema 校验和公共 client API 尚不存在；失败发生在实现代码缺失处，而不是 fixture 编译错误。

- [ ] **Step 4: 实现认证身份限定的 manifest 加载与注册**

先在 `src\errors.ts` 定义本任务所有失败共用的错误类型：

```ts
export class PluginSdkError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly retryable: boolean,
    readonly details?: Readonly<Record<string, unknown>>,
    options?: ErrorOptions
  ) {
    super(message, options);
    this.name = "PluginSdkError";
  }
}
```

创建 `src\route-manifest.ts`。默认只尝试插件工作目录下的
`dist\route-manifest.json`；默认文件不存在表示该插件只用 canonical routes，
而调用者显式给出的路径不存在属于启动失败。`false` 是测试和明确
canonical-only 构造使用的唯一关闭选项：

```ts
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import {
  registerRouteManifest,
  type EndpointIdentity,
  type RouteManifest
} from "@mytools/protocol";
import { PluginSdkError } from "./errors.js";

export interface ConnectPluginOptions {
  readonly routeManifestPath?: string | false;
}

export interface RouteManifestRegistration {
  close(): void;
}

const activeIdentities = new Map<string, symbol>();
let processOwnerKey: string | undefined;

function identityKey(identity: EndpointIdentity): string {
  return `${identity.pluginId}\u0000${identity.entryId}`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function containsRef(value: unknown): boolean {
  if (Array.isArray(value)) return value.some(containsRef);
  if (!isRecord(value)) return false;
  return "$ref" in value || Object.values(value).some(containsRef);
}

function parseArtifact(text: string): RouteManifest {
  const value: unknown = JSON.parse(text);
  if (!isRecord(value) || value.protocolVersion !== "3.0" ||
      !isRecord(value.routes) ||
      Object.keys(value).some(key => key !== "protocolVersion" && key !== "routes")) {
    throw new Error("Invalid route manifest root.");
  }
  for (const route of Object.values(value.routes)) {
    if (!isRecord(route) ||
        Object.keys(route).sort().join(",") !== "entryId,pluginId,request,response" ||
        typeof route.pluginId !== "string" || typeof route.entryId !== "string" ||
        !isRecord(route.request) || !isRecord(route.response) ||
        containsRef(route.request) || containsRef(route.response)) {
      throw new Error("Invalid route manifest entry.");
    }
  }
  return value as RouteManifest;
}

export async function registerAuthenticatedRouteManifest(
  identity: EndpointIdentity,
  options: ConnectPluginOptions
): Promise<RouteManifestRegistration> {
  const key = identityKey(identity);
  if (processOwnerKey !== undefined && processOwnerKey !== key) {
    throw new PluginSdkError(
      "HandshakeFailed", "Node process is bound to another plugin identity.", false);
  }
  if (activeIdentities.has(key)) {
    throw new PluginSdkError(
      "HandshakeFailed", "Authenticated plugin identity is already active.", false);
  }
  const owner = Symbol(key);
  try {
    const configured = options.routeManifestPath;
    if (configured !== false) {
      const path = configured ?? resolve(process.cwd(), "dist", "route-manifest.json");
      let text: string | undefined;
      try {
        text = await readFile(path, "utf8");
      } catch (error) {
        if (!(configured === undefined && isErrno(error, "ENOENT"))) throw error;
      }
      if (text !== undefined) {
        registerRouteManifest(
          parseArtifact(text), identity.pluginId, identity.entryId);
      }
    }
    processOwnerKey ??= key;
    activeIdentities.set(key, owner);
    return {
      close() {
        if (activeIdentities.get(key) === owner) activeIdentities.delete(key);
      }
    };
  } catch (cause) {
    if (cause instanceof PluginSdkError) throw cause;
    throw new PluginSdkError(
      "HandshakeFailed", "Route manifest registration failed.", false,
      undefined, { cause });
  }
}

function isErrno(error: unknown, code: string): boolean {
  return isRecord(error) && error.code === code;
}

export function activeManifestIdentityKeysForTest(): readonly string[] {
  return [...activeIdentities.keys()].sort();
}
```

给 `PluginSdkError` 增加内部可用的最后一个可选 `ErrorOptions` 构造参数并传给
`super(message, options)`，但公共 `code`、`retryable`、`details` 形状保持不变。
不把原始 JSON、Schema 或文件内容放进 wire error/details。

Protocol Foundation 的 `registerRouteManifest` 是进程级注册且没有公开删除函数，
因此这里采用明确的隔离语义而不伪造 unregister：`processOwnerKey` 把 Node 进程
永久绑定到首次成功注册的 `(pluginId, entryId)`，该进程不能再服务另一身份；
`close()` 删除该身份的 active SDK ownership、封闭 client 并关闭 pipe，Task 10
随后退出进程，进程退出即销毁 Protocol registry。
不同 plugin/entry 的测试必须使用独立 Node 子进程，不能在同一进程复用残留
validator；这既防止跨身份读取，也保留 canonical validator 的全局只读行为。

- [ ] **Step 5: 实现仅含既定方法的公共 API，并保证注册先于 client 暴露**

```ts
export interface HandlerContext {
  readonly signal: AbortSignal;
  readonly traceId: string;
  readonly deadline: number;
}

export interface PluginClient {
  call<T>(route: `host.call.${string}`, payload: unknown,
    options?: { timeoutMs?: number; signal?: AbortSignal }): Promise<T>;
  handle(route: `plugin.call.${string}`,
    handler: (payload: unknown, context: HandlerContext) => unknown | Promise<unknown>): () => void;
  publish(route: `plugin.event.${string}`, payload: unknown): Promise<void>;
  close(): Promise<void>;
}

export async function connectPlugin(
  options: ConnectPluginOptions = {}
): Promise<PluginClient> {
  const bootstrap = await readBootstrap();
  const connection = await PipeConnection.connect(bootstrap);
  return createAuthenticatedClient(connection, options);
}

async function createAuthenticatedClient(
  connection: PipeConnection,
  options: ConnectPluginOptions
): Promise<PluginClient> {
  try {
    const registration = await registerAuthenticatedRouteManifest(
      connection.identity, options);
    const client =
      new NodePluginClient(connection, connection.identity, registration);
    connection.activate(envelope => client.receive(envelope));
    return client;
  } catch (error) {
    await connection.close();
    throw error instanceof PluginSdkError
      ? error
      : new PluginSdkError(
          "HandshakeFailed", "Plugin startup validation failed.", false,
          undefined, { cause: error });
  }
}

export const createAuthenticatedClientForTest = createAuthenticatedClient;
```

`PipeConnection.connect` 在握手成功后立即 `socket.pause()`，且在
`activate` 之前不得向业务层派发或排队任何 post-handshake frame；
`activate` 只能调用一次，它先安装 envelope consumer 再 `socket.resume()`。
因此 manifest 文件读取、结构检查和 `registerRouteManifest` 必然发生在任一入站
`handle` 调用或出站 `call` 可达之前；manifest 失败时 socket 从未恢复。

`src\index.ts` 只导出 `connectPlugin`、`PluginClient`、`HandlerContext` 和
`ConnectPluginOptions`；不得导出 manifest registration/inspection helper，
也不得给 `PluginClient` 增加 `handle`、`call`、`publish`、`close` 之外的方法。
`NodePluginClient.close()` 首次调用时原子标记 closed，取消 pending/active
操作，然后在 `finally` 中依次调用 `registration.close()` 和
`connection.close()`；后续调用只等待同一个 close promise。

- [ ] **Step 6: 按消息方向实现请求与响应 Schema 校验**

`call` 和 `publish` 发送前执行 `validateRoutePayload(route, payload)`；
收到 `call` 的成功 response 后、resolve 前执行
`validateRouteResponsePayload(route, response.payload)`；error response
只规范化 `BusError`，不把 error 当成功 payload 校验。入站 request 在调用
handler 前执行 `validateRoutePayload`，handler 成功结果在发送 response 前执行
`validateRouteResponsePayload`。四处失败统一抛/返回：

```ts
throw new PluginSdkError("InvalidPayload", `Payload does not match ${route}.`, false, {
  issues: validation.errors.map(({ instancePath, keyword, message }) =>
    ({ path: instancePath, keyword, message }))
});
```

`call` 用 `crypto.randomUUID()` 生成 id/traceId，维护 `pending` Map；本地 `AbortSignal` 或 timeout 发送一次 `bus.cancel`，删除 pending，并以 `Cancelled`/`RequestTimeout` reject。入站 `bus.cancel.correlationId` 查找 `activeHandlers` 的 `AbortController`。handler 完成、失败、取消、timeout 或 disconnect 都必须删除 map；迟到响应记录后丢弃，绝不重放。

- [ ] **Step 7: 实现响应、handler 结果和错误规范化**

```ts
function toSdkError(error: BusError): PluginSdkError {
  return new PluginSdkError(
    error.code, error.message, error.retryable,
    error.details as Record<string, unknown> | undefined);
}

function invalidPayload(
  message: string,
  errors: readonly Readonly<{
    instancePath: string;
    keyword: string;
    message?: string;
  }>[]
): PluginSdkError {
    return new PluginSdkError("InvalidPayload", message, false, {
      issues: errors.map(({ instancePath, keyword, message: issueMessage }) => ({
        path: instancePath,
        keyword,
        message: issueMessage
      }))
    });
}

async function invokeHandler(request: MessageEnvelope): Promise<void> {
  const controller = new AbortController();
  activeHandlers.set(request.id, controller);
  try {
    const envelopeValidation = validateEnvelope(request);
    if (!envelopeValidation.valid)
      throw invalidPayload("Envelope validation failed.", envelopeValidation.errors);
    const payloadValidation = validateRoutePayload(request.route, request.payload);
    if (!payloadValidation.valid)
      throw invalidPayload(
        `Payload does not match ${request.route}.`, payloadValidation.errors);
    const result = await handlers.get(request.route)!(request.payload, {
      signal: controller.signal,
      traceId: request.traceId,
      deadline: performance.now() + request.timeoutMs!
    });
    const responseValidation =
      validateRouteResponsePayload(request.route, result);
    if (!responseValidation.valid)
      throw invalidPayload(
        `Response does not match ${request.route}.`,
        responseValidation.errors);
    await send(responseFor(request, result));
  } catch (error) {
    await send(errorResponseFor(request, normalizeError(error)));
  } finally {
    activeHandlers.delete(request.id);
  }
}
```

处理 pending success response 的分支必须在 `pending.delete(correlationId)` 后执行：

```ts
const validation = validateRouteResponsePayload(pending.route, envelope.payload);
if (!validation.valid) {
  pending.reject(invalidPayload(
    `Response does not match ${pending.route}.`, validation.errors));
} else {
  pending.resolve(envelope.payload);
}
```

未知 route 返回 `RouteNotFound`。Canonical route 始终直接来自 Protocol Foundation
生成 map，插件不得在 `route-manifest.json` 重复它；只有 manifest 声明的动态
route 进入动态 registry。SDK 校验只改善开发体验，Host Core/CapabilityGateway
仍须以同一认证 artifact 独立校验所有不可信 request payload。

- [ ] **Step 8: 运行测试确认绿灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm run check; npm test; Pop-Location
```

Expected: 类型检查通过；默认/显式 manifest 路径、canonical-only、认证身份匹配、
malformed/canonical conflict/identity mismatch → `HandshakeFailed` 且关闭连接、
close 清理 `(pluginId, entryId)` ownership、request/response 双向
`InvalidPayload`、call/handler/event/error、取消竞态、timeout、未知 route 和
断线不重放全部 PASS。

- [ ] **Step 9: 原子提交**

```powershell
git add MyTools.PluginSdk.Node
git commit -m "feat: add validated cancellable Node SDK calls" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 10: 实现 Node pong、watchdog、断线取消后退出

**Files:**
- Create: `MyTools.PluginSdk.Node\src\lifecycle.ts`
- Modify: `MyTools.PluginSdk.Node\src\client.ts`
- Modify: `MyTools.PluginSdk.Node\src\index.ts`
- Create: `MyTools.PluginSdk.Node\test\lifecycle.test.ts`

- [ ] **Step 1: 写立即 pong、watchdog 和断线退出测试**

```ts
test("bus.ping bypasses handlers and immediately returns correlated pong", async () => {
  const runtime = createRuntime({ watchdogMs: 1000 });
  runtime.connection.receive(request("ping-1", "bus.ping", {}, "trace-1"));
  await setImmediatePromise();
  assert.deepEqual(runtime.connection.sent[0], {
    ...responseIdentity(runtime.identity),
    kind: "response", route: "bus.pong",
    correlationId: "ping-1", traceId: "trace-1",
    payload: {}, error: null, timeoutMs: null
  });
});

test("missing host ping aborts handlers then requests process exit", async () => {
  const clock = new FakeClock();
  const exit = mock.fn();
  const runtime = createRuntime({ watchdogMs: 50, clock, exit });
  const signal = runtime.startHandler("r1");
  clock.tick(51);
  assert.equal(signal.aborted, true);
  assert.deepEqual(exit.mock.calls[0].arguments, [70]);
});

test("pipe disconnect aborts calls and handlers before exit", () => {
  const exit = mock.fn();
  const runtime = createRuntime({ exit });
  const handler = runtime.startHandler("r1");
  const pending = runtime.startPendingCall("r2");
  runtime.connection.disconnect();
  assert.equal(handler.aborted, true);
  assert.equal(pending.error.code, "TransportDisconnected");
  assert.deepEqual(exit.mock.calls[0].arguments, [71]);
});
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm test; Pop-Location
```

Expected: FAIL，`LifecycleController` 不存在。

- [ ] **Step 3: 实现单向心跳与有序退出**

```ts
export class LifecycleController {
  #lastPing = performance.now();
  #timer: NodeJS.Timeout;

  constructor(
    private readonly abortAll: (reason: PluginSdkError) => void,
    private readonly watchdogMs = 30_000,
    private readonly exit: (code: number) => never = process.exit
  ) {
    this.#timer = setInterval(() => this.#check(), Math.max(100, watchdogMs / 4));
    this.#timer.unref();
  }

  observedPing(): void { this.#lastPing = performance.now(); }

  disconnected(): void {
    clearInterval(this.#timer);
    this.abortAll(new PluginSdkError(
      "TransportDisconnected", "Host pipe disconnected.", true));
    queueMicrotask(() => this.exit(71));
  }

  #check(): void {
    if (performance.now() - this.#lastPing <= this.watchdogMs) return;
    clearInterval(this.#timer);
    this.abortAll(new PluginSdkError(
      "TransportDisconnected", "Host heartbeat watchdog expired.", true));
    queueMicrotask(() => this.exit(70));
  }
}
```

client 收到 `bus.ping` 时先 `observedPing()`，不进入 request channel/业务 handler，立即发送 correlationId/traceId 正确的 `bus.pong`。SDK 不创建主动 ping。socket `close`/`error` 只触发一次 `disconnected()`；先 abort 所有 handler、reject pending，再退出。宿主重启后旧 Node 必须退出，SDK 不实现 resume/reconnect。

- [ ] **Step 4: 运行测试确认绿灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm run check; npm test; Pop-Location
```

Expected: pong、watchdog、pipe close、pending 清理、handler abort 和单次 exit 全部 PASS。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.PluginSdk.Node
git commit -m "feat: stop Node plugins when hosts disappear" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 11: 添加真实 Node 双向协议与 Worker 集成测试

**Files:**
- Create: `MyTools.Host.Transports.Windows.Test\Fixtures\node-endpoint.mjs`
- Create: `MyTools.Host.Transports.Windows.Test\Fixtures\worker.mjs`
- Create: `MyTools.Host.Transports.Windows.Test\Fixtures\main-route-manifest.json`
- Create: `MyTools.Host.Transports.Windows.Test\Fixtures\worker-route-manifest.json`
- Create: `MyTools.Host.Transports.Windows.Test\RealNodeIntegrationTests.cs`

- [ ] **Step 1: 创建使用已构建 SDK 和认证 route manifest 的真实 fixture**

创建 `main-route-manifest.json`。只声明 fixture 自有动态 call；两个 ready event
已经是 Protocol Foundation canonical routes，不能在 artifact 中重复：

```json
{
  "protocolVersion": "3.0",
  "routes": {
    "plugin.call.echo": {
      "pluginId": "integration", "entryId": "main",
      "request": { "type": "object" },
      "response": { "type": "object" }
    },
    "plugin.call.callHost": {
      "pluginId": "integration", "entryId": "main",
      "request": {
        "type": "object", "additionalProperties": false, "required": ["key"],
        "properties": { "key": { "type": "string" } }
      },
      "response": {
        "type": "object", "additionalProperties": false, "required": ["value"],
        "properties": { "value": { "type": "string" } }
      }
    },
    "plugin.call.spawnWorker": {
      "pluginId": "integration", "entryId": "main",
      "request": {
        "type": "object", "additionalProperties": false,
        "required": ["entry", "capabilities"],
        "properties": {
          "entry": { "type": "string", "minLength": 1 },
          "capabilities": {
            "type": "array", "items": { "type": "string" }, "uniqueItems": true
          }
        }
      },
      "response": { "type": "object" }
    },
    "plugin.call.wait": {
      "pluginId": "integration", "entryId": "main",
      "request": { "type": "object", "additionalProperties": false },
      "response": { "type": "object" }
    },
    "plugin.call.crash": {
      "pluginId": "integration", "entryId": "main",
      "request": { "type": "object", "additionalProperties": false },
      "response": { "type": "object" }
    }
  }
}
```

创建 `worker-route-manifest.json`：

```json
{
  "protocolVersion": "3.0",
  "routes": {
    "plugin.call.worker.echo": {
      "pluginId": "integration", "entryId": "worker",
      "request": { "type": "object" },
      "response": { "type": "object" }
    }
  }
}
```

`RealNodeHost` 把 main/worker fixture 分别复制到各自隔离的插件工作目录，并把对应
JSON 复制为该目录的 `dist\route-manifest.json`；启动身份固定为
`("integration", "main")` 和 `("integration", "worker")`。Node 进程的
`WorkingDirectory` 必须是这个插件目录，以覆盖 `connectPlugin()` 的默认加载路径。

```js
import { connectPlugin } from "../../../MyTools.PluginSdk.Node/dist/src/index.js";

const client = await connectPlugin();
client.handle("plugin.call.echo", async (payload, { signal }) => {
  signal.throwIfAborted();
  return payload;
});
client.handle("plugin.call.callHost", async ({ key }) =>
  client.call("host.call.configuration.read", { key }));
client.handle("plugin.call.spawnWorker", async ({ entry, capabilities }) =>
  client.call("host.call.worker.spawn", { entry, capabilities }));
client.handle("plugin.call.wait", async (_payload, { signal }) => {
  await new Promise((_resolve, reject) =>
    signal.addEventListener("abort", () => reject(signal.reason), { once: true }));
  return {};
});
client.handle("plugin.call.crash", async () => process.exit(23));
await client.publish("plugin.event.ready", { pid: process.pid });
```

`worker.mjs`：

```js
import { connectPlugin } from "../../../MyTools.PluginSdk.Node/dist/src/index.js";
const client = await connectPlugin();
client.handle("plugin.call.worker.echo", async payload => payload);
await client.publish("plugin.event.worker.ready", { pid: process.pid });
```

- [ ] **Step 2: 写真实 Node 测试**

```csharp
[Test]
public async Task RealNode_HandshakesCallsHostPublishesAndCancels()
{
    await using var host = await RealNodeHost.StartAsync(
        "node-endpoint.mjs", "integration", "main", "main-route-manifest.json");
    Assert.That(await host.CallAsync<JsonElement>(
        "plugin.call.echo", new { text = "你好" }), Is.EqualTo(JsonSerializer.SerializeToElement(new { text = "你好" })));
    host.OnHostCall("host.call.configuration.read", payload =>
        new { value = payload.GetProperty("key").GetString() });
    var nested = await host.CallAsync<JsonElement>(
        "plugin.call.callHost", new { key = "theme" });
    Assert.That(nested.GetProperty("value").GetString(), Is.EqualTo("theme"));
    Assert.That(host.Events.Any(x => x.Route == "plugin.event.ready"), Is.True);
    Assert.That(await host.CancelAndObserveAsync("plugin.call.wait"), Is.EqualTo("Cancelled"));
}

[Test]
public async Task RealNode_SpawnsWorkerThroughAuthenticatedMainOnly()
{
    await using var host = await RealNodeHost.StartAsync(
        "node-endpoint.mjs", "integration", "main", "main-route-manifest.json");
    var worker = await host.SpawnWorkerAsync(
        "worker.mjs", "worker", "worker-route-manifest.json",
        ["configuration.read"]);
    Assert.That(worker.Identity.EndpointId, Does.StartWith("node-worker-"));
    Assert.That(await worker.CallAsync<JsonElement>(
        "plugin.call.worker.echo", new { value = 7 }),
        Is.EqualTo(JsonSerializer.SerializeToElement(new { value = 7 })));
}
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm run build; Pop-Location
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter RealNodeIntegrationTests
```

Expected: fixture 可以握手，但测试因尚缺 test host host-call/worker glue 而失败。

- [ ] **Step 4: 补齐真实 test host glue**

在 `RealNodeHost` 中使用真实 `WindowsNodeEndpointFactory` 和 Host Core `MessageBus`，不直接解析 fixture 消息。注册：

```csharp
bus.RegisterHostRoute("host.call.configuration.read", async (context, payload, cancellationToken) =>
    hostHandlers["host.call.configuration.read"](payload));
bus.RegisterHostRoute("host.call.worker.spawn", async (context, payload, cancellationToken) =>
    await workerSpawnService.SpawnAsync(
        context.AuthenticatedEndpoint,
        payload.Deserialize<WorkerSpawnRequest>(ProtocolJson.SerializerOptions)!,
        cancellationToken));
```

测试 teardown 调用 session stop，等待 Node/Worker 退出后再释放临时目录；stdout/stderr 只收集为带 pluginId、endpointId、pid 的日志，不解析协议。

- [ ] **Step 5: 运行真实集成测试确认绿灯**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node; npm run build; Pop-Location
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter RealNodeIntegrationTests
```

Expected: main/worker 均在认证后从各自工作目录加载
`dist\route-manifest.json`；动态 route 双向 Schema、无需 manifest 重复的 canonical
ready events、握手、UTF-8、Host→Node、Node→Host、取消和独立 Worker 全部 PASS；
stdout 中不存在协议 envelope、manifest 内容或 token。

- [ ] **Step 6: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows.Test\Fixtures MyTools.Host.Transports.Windows.Test\RealNodeIntegrationTests.cs
git commit -m "test: exercise real Node message bus endpoints" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 12: 添加安全、资源和恢复验收测试

**Files:**
- Create: `MyTools.Host.Transports.Windows.Test\SecurityIntegrationTests.cs`
- Create: `MyTools.Host.Transports.Windows.Test\ResourceLimitIntegrationTests.cs`
- Create: `MyTools.Host.Transports.Windows.Test\CrashRecoveryIntegrationTests.cs`

- [ ] **Step 1: 写安全矩阵**

```csharp
[TestCase(SecurityAttack.PipeNameSquat, "pipe-created-before-process")]
[TestCase(SecurityAttack.ReplayToken, "HandshakeFailed")]
[TestCase(SecurityAttack.ForgePluginId, "HandshakeFailed")]
[TestCase(SecurityAttack.ForgeEntryId, "HandshakeFailed")]
[TestCase(SecurityAttack.ChildSuppliedSessionId, "HandshakeFailed")]
[TestCase(SecurityAttack.ChildSuppliedEndpointId, "HandshakeFailed")]
[TestCase(SecurityAttack.WrongPid, "HandshakeFailed")]
[TestCase(SecurityAttack.WrongCreationTime, "HandshakeFailed")]
public async Task RejectsSecurityAttack(SecurityAttack attack, string expected)
{
    var result = await SecurityHarness.ExecuteAsync(attack);
    Assert.Multiple(() =>
    {
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Logs, Has.None.Contains(result.Token));
        Assert.That(result.ConnectedEndpoints, Is.Empty);
        Assert.That(result.ProcessTreeExited, Is.True);
    });
}
```

伪造测试必须从真实同用户攻击进程连接，确保 ACL 本身不被误当成同用户隔离；`ChildSuppliedSessionId`/`ChildSuppliedEndpointId` 分别把 handshake request envelope 的对应字段从 `null` 改为攻击者值，且必须在读取 payload 前被拒绝。PID 检查使用 `GetNamedPipeClientProcessId`，创建时间检查使用宿主持有的 process handle。

- [ ] **Step 2: 写资源与崩溃恢复矩阵**

```csharp
[Test]
public async Task JobLimitsMemoryCpuAndProcessCountAndKillsTree()
{
    var result = await ResourceHarness.RunAsync(new NodeProcessLimits(
        96L << 20, 160L << 20, 2, 20));
    Assert.Multiple(() =>
    {
        Assert.That(result.PeakJobMemoryBytes, Is.LessThanOrEqualTo(160L << 20));
        Assert.That(result.PeakActiveProcesses, Is.LessThanOrEqualTo(2));
        Assert.That(result.CpuRatePercent, Is.LessThanOrEqualTo(20.5));
        Assert.That(result.AllProcessesExitedAfterStop, Is.True);
    });
}

[Test]
public async Task CrashCreatesFreshSessionAndRejectsLateOldFrames()
{
    await using var host = await RestartHarness.StartAsync();
    var first = host.CurrentIdentity;
    await host.CallAsync("plugin.call.crash", new { });
    await host.WaitUntilReadyAsync();
    var second = host.CurrentIdentity;
    Assert.Multiple(() =>
    {
        Assert.That(second.SessionId, Is.Not.EqualTo(first.SessionId));
        Assert.That(second.EndpointId, Is.Not.EqualTo(first.EndpointId));
        Assert.That(host.PipeNames.Distinct().Count(), Is.EqualTo(2));
        Assert.That(host.AcceptLateFrame(first), Is.False);
        Assert.That(host.PendingFailureCode, Is.EqualTo("TransportDisconnected"));
    });
}
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter "SecurityIntegrationTests|ResourceLimitIntegrationTests|CrashRecoveryIntegrationTests"
```

Expected: 新攻击/资源 harness 缺失导致 FAIL。

- [ ] **Step 4: 实现 harness 并运行验收**

`SecurityHarness` 启动真实竞争 pipe client 和篡改握手的 Node fixture；`ResourceHarness` 使用 Job accounting 查询 peak memory/active process/CPU；`RestartHarness` 使用第 2/5 份计划的真实 session actor 与零抖动测试策略。每个 harness 在 `finally` 中关闭 endpoint、等待 Job active process count 变为 0，并验证 handle count 回到启动前 `±2`。

Run:

```powershell
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --filter "SecurityIntegrationTests|ResourceLimitIntegrationTests|CrashRecoveryIntegrationTests" -- NUnit.NumberOfTestWorkers=1
```

Expected: 抢占、重放、plugin/entry 伪造、子进程注入 session/endpoint、PID/创建时间、内存/CPU/进程数、崩溃重启、旧 session 和句柄回收全部 PASS。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Transports.Windows.Test\SecurityIntegrationTests.cs MyTools.Host.Transports.Windows.Test\ResourceLimitIntegrationTests.cs MyTools.Host.Transports.Windows.Test\CrashRecoveryIntegrationTests.cs
git commit -m "test: harden Node endpoint process security" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 13: 完成全量验证和计划输出检查

**Files:**
- Modify only if verification exposes a defect: files created in Tasks 1–12

- [ ] **Step 1: 验证 Node SDK 生成依赖、类型和测试**

Run:

```powershell
Push-Location MyTools.PluginSdk.Node
npm ci
npm run check
npm test
Pop-Location
```

Expected: install 使用锁文件成功；TypeScript 零错误；全部 Node 测试 PASS，包括
manifest startup/`HandshakeFailed`、canonical-only、双向 request/response Schema
以及 `(pluginId, entryId)` close isolation。

- [ ] **Step 2: 验证 .NET 单元与真实 Windows/Node 测试**

Run:

```powershell
dotnet restore MyTools.sln
dotnet test MyTools.Host.Transports.Windows.Test\MyTools.Host.Transports.Windows.Test.csproj --no-restore -- NUnit.NumberOfTestWorkers=1
```

Expected: 所有 Windows transport、进程、真实 Node、安全、资源及恢复测试 PASS，无 skipped test。

- [ ] **Step 3: 验证全仓库且不触碰示例迁移**

Run:

```powershell
dotnet test MyTools.sln --no-restore
Push-Location MyTools.Plugins\Examples; npm run check; Pop-Location
git diff --exit-code -- MyTools.Plugins\Examples MyTools.Plugins\NodePlugins\NodePluginProcessHost.cs
```

Expected: 现有 .NET 测试和示例 TypeScript check 通过；最后一条命令退出码为 0，证明未迁移插件、未修改旧 process host。

- [ ] **Step 4: 验证安全不变量和无残留**

Run:

```powershell
git grep -n -E 'token.*(Log|Console)|Console.*token' -- MyTools.Host.Transports.Windows MyTools.PluginSdk.Node
Get-Process node -ErrorAction SilentlyContinue | Where-Object Path -Like '*MyTools.Host.Transports.Windows.Test*'
git status --short
```

Expected: 前两条无输出；status 只列出本计划的项目、SDK、solution 和 central package 变更。

- [ ] **Step 5: 原子提交验证中发现的修正**

仅当 Steps 1–4 产生并已修复本计划缺陷时执行：

```powershell
git add MyTools.Host.Transports.Windows MyTools.Host.Transports.Windows.Test MyTools.PluginSdk.Node MyTools.sln Directory.Packages.props
git commit -m "fix: complete Node endpoint verification" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

Expected: 工作区不再包含本计划未提交实现变更；若无需修正，不创建空提交。

## Plan Coverage Checklist

- Task 2：随机 pipe 名的服务端基础、`FirstPipeInstance`、当前用户与 SYSTEM ACL、先占实例防抢占。
- Tasks 3/5：15 秒 256-bit stdin 一次性令牌、握手即作废、重放拒绝、plugin/entry 与进程校验、Host 响应分配 session/endpoint、pipe client PID、宿主持有句柄创建时间。
- Task 4：挂起创建消除加入 Job 前的 fork 竞态，kill-on-close、进程/Job 内存、CPU hard cap、活动进程数和整树回收。
- Tasks 5/6：先 pipe 后进程、stdout/stderr 纯日志、长度帧、非法帧断线、Host ping/Node pong/RTT、断线停树和 Host Core 重启。
- Task 7：只有已认证主 Node 可调用 `host.call.worker.spawn`，独立 endpoint/process/pipe/token，capability 只能取主授权子集，自行 fork 不成为 endpoint。
- Tasks 8–10：Node SDK 握手后暂停分发；返回 client 前可选加载默认/显式 `dist\route-manifest.json`，以认证 `pluginId`/`entryId` 调用 `registerRouteManifest`；malformed、canonical conflict、identity mismatch 统一 `HandshakeFailed` 并关闭；canonical routes 无需 manifest；request 用 `validateRoutePayload`，成功 response/handler result 用 `validateRouteResponsePayload`；close 清理 active ownership 且进程身份不可切换；另含结构化 `InvalidPayload`、AbortSignal、at-most-once、watchdog 和断线取消后退出。
- Tasks 11/12：真实 Node main/worker 从各自 `dist\route-manifest.json` 注册动态 route，同时直接使用 canonical event；覆盖双向调用、请求/响应 Schema、事件、取消、Worker、抢占、令牌重放、身份/PID/创建时间伪造、资源限制、崩溃与新 session 自动恢复。
- 本计划不包含 WebView2、现有 manifest/示例插件迁移或旧 stdio JSON-RPC 删除；只实现 Node SDK artifact 消费并添加隔离 fixture，迁移边界分别由第 4/5、5/5 份计划承接。
