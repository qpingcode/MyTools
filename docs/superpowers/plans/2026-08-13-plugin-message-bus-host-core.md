# Plugin Message Bus Host Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成批准设计的第 2/5 份计划 Host Core，交付可用 fake transport 验证的消息总线、每 entry 会话 actor、双向有界优先级通道、capability 授权网关和结构化诊断。

**Architecture:** 新建平台无关的 `MyTools.Host.Core`，以 `IMessageTransport` 隔离物理连接，以 `EndpointRuntime` 在每个 endpoint 的两个方向分别维护 control/response、request、event 三类有界队列，再由 `MessageBus` 完成关联、路由、订阅、取消、端到端预算和 trace。每个 `pluginId + entryId` 只有一个 `PluginSessionActor` 串行修改状态、generation、endpoint 与重启计数；capability I/O 和 transport I/O 都在 actor 外执行，完成结果携带 generation 回投。

**Tech Stack:** .NET 8, C# 12, `System.Threading.Channels`, `System.Text.Json`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, NUnit 4, Moq

---

## 计划位置、输入依赖、输出与边界

- **这是第 2/5 份计划。** 输入依赖是已合入的第 1/5 份计划 Protocol foundation。开始前必须存在 `MyTools.Protocol\MyTools.Protocol.csproj`，并提供本节列出的协议类型、生成 DTO/validator、错误码和 JSON 序列化入口。
- **本计划输出给后续计划。** 第 3/5 份计划 Named Pipe/Node SDK 消费 `IMessageTransport`、`INodeEndpointEvents`、`IWorkerRegistration`、`MessageBus`、`PluginSessionManager` 和进程控制端口；第 4/5 份计划 WebView2 消费同一 transport、session accessor、endpoint registry 和 state topic；第 5/5 份计划迁移只组合这些 API，不回写 Host Core 路由语义。
- **本计划不实现** Named Pipe、Windows ACL/Job Object、真实进程启动、WebView2、Node/Web SDK、manifest 迁移、示例插件迁移或旧 stdio 删除。
- `IMessageTransport` 位于 `MyTools.Host.Core`，因为第 2/5 份计划必须可独立以 fake transport 测试；真实 platform transport 位于后续项目。
- 所有下列 checkbox 都是单一的 2–5 分钟动作。每个 task 严格按红灯、最小绿灯、重构、聚焦验证、原子提交顺序执行。

这 5 份计划是实施文档拆分，不等同于 spec“实施边界”的 7 个步骤：第 1/5 份计划覆盖步骤 1 的 Protocol 部分；本第 2/5 份计划覆盖步骤 1 的 transport/fake 部分及步骤 2；第 3/5 份计划覆盖步骤 3；第 4/5 份计划覆盖步骤 4；第 5/5 份计划覆盖步骤 5–7。该映射只说明交付归属，不改变本计划范围。

### Protocol foundation 输入契约

执行 Task 1 前确认 Protocol foundation 暴露以下冻结 API；缺少任何成员时先合入第 1/5 份计划，不在 Host Core 中复制 wire DTO，也不另造同义类型或序列化选项：

```csharp
using System.Text.Json;

namespace MyTools.Protocol.V3;

public enum MessageKind { Request, Response, Event }

public sealed record EndpointIdentity(
    string PluginId, string EntryId, string SessionId, string EndpointId);

public sealed record BusError(
    string Code, string Message, bool Retryable, JsonElement? Details = null);

public sealed record MessageEnvelope(
    string Version,
    string Id,
    string? CorrelationId,
    string TraceId,
    string? SessionId,
    string PluginId,
    string EntryId,
    string? EndpointId,
    MessageKind Kind,
    string Route,
    int? TimeoutMs,
    JsonElement Payload,
    BusError? Error);

public static class ProtocolErrorCodes
{
    public const string ProtocolMismatch = "ProtocolMismatch";
    public const string HandshakeFailed = "HandshakeFailed";
    public const string CapabilityNotDeclared = "CapabilityNotDeclared";
    public const string CapabilityDenied = "CapabilityDenied";
    public const string InvalidPayload = "InvalidPayload";
    public const string MessageTooLarge = "MessageTooLarge";
    public const string RouteNotFound = "RouteNotFound";
    public const string RequestTimeout = "RequestTimeout";
    public const string Cancelled = "Cancelled";
    public const string TooManyRequests = "TooManyRequests";
    public const string TransportDisconnected = "TransportDisconnected";
    public const string PluginUnavailable = "PluginUnavailable";
    public const string PluginCrashed = "PluginCrashed";
    public const string InternalError = "InternalError";
}

public static class ProtocolJson
{
    public static JsonSerializerOptions SerializerOptions { get; }
}

public sealed record ValidationIssue(string Path, string Message);
public sealed record ValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues);

public interface IRoutePayloadValidator
{
    ValidationResult Validate(string route, JsonElement payload);
}
```

Host Core 的所有协议 JSON 读写统一使用 `ProtocolJson.SerializerOptions`。路由 payload 校验只调用 `IRoutePayloadValidator.Validate(string route, JsonElement payload)`，并且只使用冻结 `ValidationResult` 的 `result.IsValid` 和 `result.Issues`。

Host Core 只接受 transport 已认证并绑定的 `EndpointIdentity`。除 `bus.handshake` 外，transport 交给 Host Core 的 envelope 四元身份必须和绑定完全相等；Host Core 再做一次一致性检查，旧 `sessionId` 在关联或路由前丢弃并发安全诊断。

## 文件结构映射

### 项目与组合

| 文件 | 操作 | 单一职责 |
| --- | --- | --- |
| `MyTools.Host.Core\MyTools.Host.Core.csproj` | 创建 | 平台无关 Host Core 及 Protocol/DI/logging 引用 |
| `MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj` | 创建 | NUnit Host Core 单元和 fake 组件测试 |
| `MyTools.Host.Core\DependencyInjection\HostCoreServiceCollectionExtensions.cs` | 创建 | 与现有 extension pattern 一致的 singleton 注册 |
| `MyTools.sln` | 修改 | 加入 Host Core 与测试项目 |

### Transport、队列与总线

| 文件 | 操作 | 单一职责 |
| --- | --- | --- |
| `MyTools.Host.Core\Transports\IMessageTransport.cs` | 创建 | 已认证 endpoint 的异步收发和断线契约 |
| `MyTools.Host.Core\Transports\TransportContracts.cs` | 创建 | `TransportPriority`、disconnect、异常 |
| `MyTools.Host.Core\Transports\EndpointQueueOptions.cs` | 创建 | 三类容量、双向 64 in-flight、总字节默认值 |
| `MyTools.Host.Core\Transports\EndpointRuntime.cs` | 创建 | 双向分类、优先调度、单写者和断线清理 |
| `MyTools.Host.Core\Transports\PriorityMailbox.cs` | 创建 | control/request/event 三个有界逻辑通道 |
| `MyTools.Host.Core\Transports\EventOverflowPolicy.cs` | 创建 | drop newest、drop oldest、coalesce by key |
| `MyTools.Host.Core\Messaging\MessageBus.cs` | 创建 | endpoint 注册、请求、响应、事件和控制路由 |
| `MyTools.Host.Core\Messaging\PendingRequestRegistry.cs` | 创建 | correlation、deadline、取消和断线完成 |
| `MyTools.Host.Core\Messaging\SubscriptionRegistry.cs` | 创建 | 当前连接内的会话隔离 topic 订阅 |
| `MyTools.Host.Core\Messaging\StateTopicStore.cs` | 创建 | 状态型 host event 当前快照 |
| `MyTools.Host.Core\Messaging\MessageEnvelopeFactory.cs` | 创建 | identity、trace、timeout 和 response 构造 |

### Session

| 文件 | 操作 | 单一职责 |
| --- | --- | --- |
| `MyTools.Host.Core\Sessions\PluginSessionState.cs` | 创建 | 状态枚举和允许转换表 |
| `MyTools.Host.Core\Sessions\PluginSessionSnapshot.cs` | 创建 | 对外不可变会话快照 |
| `MyTools.Host.Core\Sessions\PluginSessionActor.cs` | 创建 | 每 entry 单读者 actor、generation 和状态 mutation |
| `MyTools.Host.Core\Sessions\PluginSessionManager.cs` | 创建 | 创建、查找、停止、reload、transport 回调入口 |
| `MyTools.Host.Core\Sessions\SessionContracts.cs` | 创建 | accessor、endpoint registry、process/endpoint 端口 |
| `MyTools.Host.Core\Sessions\RestartPolicy.cs` | 创建 | 抖动指数退避和窗口内重启上限 |

### Capability 与诊断

| 文件 | 操作 | 单一职责 |
| --- | --- | --- |
| `MyTools.Host.Core\Capabilities\CapabilityGateway.cs` | 创建 | 每次调用的身份、声明、授权、限流、DTO、预算检查 |
| `MyTools.Host.Core\Capabilities\CapabilityContracts.cs` | 创建 | authorizer、handler、rate limiter 和调用上下文 |
| `MyTools.Host.Core\Diagnostics\HostDiagnosticEvent.cs` | 创建 | 无 payload 的结构化事件模型 |
| `MyTools.Host.Core\Diagnostics\IHostDiagnostics.cs` | 创建 | 事件与聚合计数 sink |
| `MyTools.Host.Core\Diagnostics\HostDiagnostics.cs` | 创建 | 线程安全计数和只读快照 |

### 测试支撑

| 文件 | 操作 | 单一职责 |
| --- | --- | --- |
| `MyTools.Host.Core.Test\Fakes\FakeMessageTransport.cs` | 创建 | 可控发送、注入、乱序和断线 |
| `MyTools.Host.Core.Test\Fakes\ManualTimeProvider.cs` | 创建 | timeout、预算和 restart 的确定性单调时间 |
| `MyTools.Host.Core.Test\Transports\EndpointRuntimeTests.cs` | 创建 | 双向优先级、容量、字节、overflow |
| `MyTools.Host.Core.Test\Messaging\MessageBusRequestTests.cs` | 创建 | request/response/trace/timeout/cancel |
| `MyTools.Host.Core.Test\Messaging\MessageBusSubscriptionTests.cs` | 创建 | 订阅、快照、隔离和断线清除 |
| `MyTools.Host.Core.Test\Sessions\PluginSessionActorTests.cs` | 创建 | 状态机、串行 mutation、旧 generation |
| `MyTools.Host.Core.Test\Sessions\PluginSessionManagerTests.cs` | 创建 | endpoint、断线、reload、restart 上限 |
| `MyTools.Host.Core.Test\Capabilities\CapabilityGatewayTests.cs` | 创建 | 声明、授权、DTO、限流、预算和 WebView 拒绝 |
| `MyTools.Host.Core.Test\Diagnostics\HostDiagnosticsTests.cs` | 创建 | 脱敏事件与精确计数 |
| `MyTools.Host.Core.Test\Components\FakeTransportBusTests.cs` | 创建 | 多 endpoint、乱序、断线、迟到响应和取消竞态 |

## Task 1: 建立 Host Core 项目和依赖边界

**Files:**
- Create: `MyTools.Host.Core\MyTools.Host.Core.csproj`
- Create: `MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj`
- Modify: `MyTools.sln`

- [ ] **Step 1: 验证 Protocol 输入和当前仓库基线**

Run:

```powershell
Test-Path .\MyTools.Protocol\MyTools.Protocol.csproj
dotnet sln .\MyTools.sln list
dotnet test .\MyTools.sln
```

Expected: `Test-Path` 输出 `True`；solution 包含 `MyTools.Protocol`；当前 NUnit 项目全部 PASS。若第一项为 `False`，停止本计划并先合入第 1/5 份计划。

- [ ] **Step 2: 创建生产项目**

```xml
<!-- MyTools.Host.Core\MyTools.Host.Core.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyTools.Host.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyTools.Protocol\MyTools.Protocol.csproj" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: 创建 NUnit 测试项目**

```xml
<!-- MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyTools.Host.Core.Test</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Moq" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit3TestAdapter" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyTools.Host.Core\MyTools.Host.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: 加入 solution 并验证骨架**

Run:

```powershell
dotnet sln .\MyTools.sln add .\MyTools.Host.Core\MyTools.Host.Core.csproj .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj
dotnet build .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj
```

Expected: `Project(s) added to the solution successfully`；随后 `Build succeeded`，0 errors。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.sln MyTools.Host.Core\MyTools.Host.Core.csproj MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj
git commit -m "build: add plugin Host Core projects" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: 定义 transport 契约并提供可控 fake

**Files:**
- Create: `MyTools.Host.Core\Transports\IMessageTransport.cs`
- Create: `MyTools.Host.Core\Transports\TransportContracts.cs`
- Create: `MyTools.Host.Core.Test\Fakes\FakeMessageTransport.cs`
- Create: `MyTools.Host.Core.Test\Transports\MessageTransportContractTests.cs`

- [ ] **Step 1: 写 fake 收发和断线红灯测试**

```csharp
using MyTools.Host.Core.Test.Fakes;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.V3;

namespace MyTools.Host.Core.Test.Transports;

[TestFixture]
public sealed class MessageTransportContractTests
{
    [Test]
    public async Task Fake_preserves_bound_identity_and_completes_on_disconnect()
    {
        var identity = new EndpointIdentity("settings", "main", "session-1", "node-main");
        await using var transport = new FakeMessageTransport(identity);
        var envelope = TestEnvelope.Request(identity, "plugin.call.echo", "request-1");

        await transport.SendAsync(envelope, TransportPriority.Request, default);
        transport.Disconnect(new(ProtocolErrorCodes.TransportDisconnected, "test-disconnect"));

        Assert.Multiple(() =>
        {
            Assert.That(transport.Identity, Is.EqualTo(identity));
            Assert.That(transport.Sent.Single().Envelope, Is.SameAs(envelope));
            Assert.That(transport.Sent.Single().Priority, Is.EqualTo(TransportPriority.Request));
            Assert.That(transport.Completion.IsCompletedSuccessfully, Is.True);
        });
    }
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageTransportContractTests"
```

Expected: FAIL，编译器报告 `IMessageTransport`、`TransportPriority` 和 `FakeMessageTransport` 不存在。

- [ ] **Step 3: 定义唯一 transport API**

```csharp
// MyTools.Host.Core\Transports\TransportContracts.cs
namespace MyTools.Host.Core.Transports;

public enum TransportPriority { ControlOrResponse, Request, Event }

public sealed record TransportDisconnect(string Code, string Reason, Exception? Exception = null);

public sealed class TransportDisconnectedException(TransportDisconnect disconnect)
    : Exception(disconnect.Reason, disconnect.Exception)
{
    public TransportDisconnect Disconnect { get; } = disconnect;
}
```

```csharp
// MyTools.Host.Core\Transports\IMessageTransport.cs
using MyTools.Protocol.V3;

namespace MyTools.Host.Core.Transports;

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
```

- [ ] **Step 4: 实现可注入、乱序和断线的 fake**

```csharp
// MyTools.Host.Core.Test\Fakes\FakeMessageTransport.cs
using System.Threading.Channels;
using MyTools.Host.Core.Transports;
using MyTools.Protocol.V3;

namespace MyTools.Host.Core.Test.Fakes;

public sealed class FakeMessageTransport(EndpointIdentity identity) : IMessageTransport
{
    private readonly Channel<MessageEnvelope> inbound = Channel.CreateUnbounded<MessageEnvelope>();
    private readonly TaskCompletionSource completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int disconnected;

    public EndpointIdentity Identity { get; } = identity;
    public List<(MessageEnvelope Envelope, TransportPriority Priority)> Sent { get; } = [];
    public Task Completion => completion.Task;

    public ValueTask SendAsync(
        MessageEnvelope envelope, TransportPriority priority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref disconnected) != 0)
            throw new TransportDisconnectedException(
                new(ProtocolErrorCodes.TransportDisconnected, "Fake transport is disconnected."));
        Sent.Add((envelope, priority));
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<MessageEnvelope> ReadAllAsync(CancellationToken cancellationToken) =>
        inbound.Reader.ReadAllAsync(cancellationToken);

    public void Receive(MessageEnvelope envelope) =>
        inbound.Writer.TryWrite(envelope);

    public void Disconnect(TransportDisconnect reason)
    {
        if (Interlocked.Exchange(ref disconnected, 1) != 0) return;
        inbound.Writer.TryComplete(new TransportDisconnectedException(reason));
        completion.TrySetResult();
    }

    public ValueTask DisposeAsync()
    {
        Disconnect(new(ProtocolErrorCodes.TransportDisconnected, "Fake transport disposed."));
        return ValueTask.CompletedTask;
    }
}
```

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageTransportContractTests"
```

Expected: PASS, 1 test。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Transports MyTools.Host.Core.Test\Fakes\FakeMessageTransport.cs MyTools.Host.Core.Test\Transports\MessageTransportContractTests.cs
git commit -m "feat: define plugin message transport contract" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: 实现每方向三类有界优先级通道

**Files:**
- Create: `MyTools.Host.Core\Transports\EndpointQueueOptions.cs`
- Create: `MyTools.Host.Core\Transports\EventOverflowPolicy.cs`
- Create: `MyTools.Host.Core\Transports\PriorityMailbox.cs`
- Create: `MyTools.Host.Core.Test\Transports\PriorityMailboxTests.cs`

- [ ] **Step 1: 写默认值、响应保留、request 上限和 event overflow 红灯测试**

```csharp
[TestFixture]
public sealed class PriorityMailboxTests
{
    [Test]
    public void Defaults_reserve_64_inflight_in_both_directions()
    {
        var options = new EndpointQueueOptions();
        Assert.Multiple(() =>
        {
            Assert.That(options.HostToEndpointMaxInFlight, Is.EqualTo(64));
            Assert.That(options.EndpointToHostMaxInFlight, Is.EqualTo(64));
            Assert.That(options.ControlOrResponseCapacity, Is.GreaterThanOrEqualTo(64));
            Assert.That(options.MaxQueuedBytes, Is.EqualTo(4 * 1024 * 1024));
        });
    }

    [Test]
    public void Full_event_queue_drops_oldest_without_dropping_response()
    {
        var diagnostics = new RecordingHostDiagnostics();
        var mailbox = new PriorityMailbox(
            new EndpointQueueOptions
            {
                ControlOrResponseCapacity = 2,
                RequestCapacity = 1,
                EventCapacity = 2,
                MaxQueuedBytes = 4096
            },
            diagnostics);

        mailbox.TryEnqueue(Item("event-1", TransportPriority.Event, 20), EventOverflowPolicy.DropOldest);
        mailbox.TryEnqueue(Item("event-2", TransportPriority.Event, 20), EventOverflowPolicy.DropOldest);
        mailbox.TryEnqueue(Item("event-3", TransportPriority.Event, 20), EventOverflowPolicy.DropOldest);
        mailbox.TryEnqueue(Item("response-1", TransportPriority.ControlOrResponse, 20), null);

        Assert.That(mailbox.Drain().Select(x => x.Id),
            Is.EqualTo(new[] { "response-1", "event-2", "event-3" }));
        Assert.That(diagnostics.Counter("droppedEvents"), Is.EqualTo(1));
    }
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PriorityMailboxTests"
```

Expected: FAIL，缺少 queue options、mailbox 和 overflow policy。

- [ ] **Step 3: 定义固定默认值和事件策略**

```csharp
namespace MyTools.Host.Core.Transports;

public sealed record EndpointQueueOptions
{
    public int HostToEndpointMaxInFlight { get; init; } = 64;
    public int EndpointToHostMaxInFlight { get; init; } = 64;
    public int ControlOrResponseCapacity { get; init; } = 64;
    public int RequestCapacity { get; init; } = 64;
    public int EventCapacity { get; init; } = 256;
    public long MaxQueuedBytes { get; init; } = 4 * 1024 * 1024;
}

public enum EventOverflowMode { DropNewest, DropOldest, CoalesceByKey }

public sealed record EventOverflowPolicy(EventOverflowMode Mode, Func<MessageEnvelope, string?>? KeySelector = null)
{
    public static EventOverflowPolicy DropNewest { get; } = new(EventOverflowMode.DropNewest);
    public static EventOverflowPolicy DropOldest { get; } = new(EventOverflowMode.DropOldest);
    public static EventOverflowPolicy Coalesce(Func<MessageEnvelope, string?> keySelector) =>
        new(EventOverflowMode.CoalesceByKey, keySelector);
}
```

- [ ] **Step 4: 实现三个独立 bounded channel、总字节和固定优先级读取**

`PriorityMailbox` 使用三个 `Channel<QueuedEnvelope>`；control/request 使用 `BoundedChannelFullMode.Wait`，event 使用显式 `TryWrite`。每次 enqueue 先以 UTF-8 serialized byte count 做 `Interlocked` 总字节预留，dequeue 后归还。读取顺序固定为 control/response → request → event；每发送 16 个 control 后若 request 可读则发送 1 个 request，每发送 16 个 request 后若 event 可读则发送 1 个 event，避免低优先级永久饥饿。

```csharp
public sealed record QueuedEnvelope(
    MessageEnvelope Envelope, TransportPriority Priority, int ByteCount)
{
    public string Id => Envelope.Id;
}

private bool TryReserve(int bytes)
{
    while (true)
    {
        var current = Interlocked.Read(ref queuedBytes);
        if (bytes < 0 || current + bytes > options.MaxQueuedBytes) return false;
        if (Interlocked.CompareExchange(ref queuedBytes, current + bytes, current) == current)
            return true;
    }
}

private void Release(QueuedEnvelope item) =>
    Interlocked.Add(ref queuedBytes, -item.ByteCount);
```

`CoalesceByKey` 只替换相同 route 和 key 的尚未发送 event，并增加 `coalescedEvents`；无 key 时按 `DropNewest`。control/response 无容量或字节可保留时返回 `MailboxWriteResult.Disconnect`，request 满时返回 `MailboxWriteResult.TooManyRequests`，event 满时按策略返回 `Dropped` 或 `Coalesced`。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PriorityMailboxTests"
```

Expected: PASS；默认双向 in-flight 均为 64，响应先于 event，queue bytes 从不超过 4 MiB，三种 overflow 计数准确。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Transports\EndpointQueueOptions.cs MyTools.Host.Core\Transports\EventOverflowPolicy.cs MyTools.Host.Core\Transports\PriorityMailbox.cs MyTools.Host.Core.Test\Transports\PriorityMailboxTests.cs
git commit -m "feat: bound endpoint priority mailboxes" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: 把双向 mailbox 组合为 endpoint runtime

**Files:**
- Create: `MyTools.Host.Core\Transports\EndpointRuntime.cs`
- Create: `MyTools.Host.Core.Test\Transports\EndpointRuntimeTests.cs`

- [ ] **Step 1: 写双向分类、单写者和 overload 红灯测试**

```csharp
[Test]
public async Task Runtime_prioritizes_both_directions_and_caps_each_direction_at_64()
{
    var identity = Identity("session-1", "node-main");
    var transport = new BlockingFakeMessageTransport(identity);
    await using var runtime = CreateRuntime(transport);
    await runtime.StartAsync(default);

    for (var index = 0; index < 64; index++)
        Assert.That(runtime.TryQueueOutbound(Request(identity, $"out-{index}")), Is.EqualTo(QueueResult.Accepted));
    Assert.That(runtime.TryQueueOutbound(Request(identity, "out-65")), Is.EqualTo(QueueResult.TooManyRequests));

    for (var index = 0; index < 64; index++)
        transport.Receive(Request(identity, $"in-{index}", route: "host.call.test"));
    transport.Receive(Request(identity, "in-65", route: "host.call.test"));
    await runtime.WhenIdleAsync();

    Assert.That(runtime.OutboundInFlight, Is.EqualTo(64));
    Assert.That(runtime.InboundInFlight, Is.EqualTo(64));
    Assert.That(runtime.RejectedInbound.Single().Error!.Code,
        Is.EqualTo(ProtocolErrorCodes.TooManyRequests));
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~EndpointRuntimeTests"
```

Expected: FAIL，`EndpointRuntime` 和 `QueueResult` 不存在。

- [ ] **Step 3: 实现双向 runtime 和严格分类**

```csharp
public enum QueueResult { Accepted, TooManyRequests, Dropped, Coalesced, Disconnect }

public static TransportPriority Classify(MessageEnvelope envelope, Func<string, bool> isPending)
{
    if (envelope.Route is "bus.ping" or "bus.pong" or "bus.cancel")
        return TransportPriority.ControlOrResponse;
    if (envelope.Kind == MessageKind.Response && envelope.CorrelationId is not null
        && isPending(envelope.CorrelationId))
        return TransportPriority.ControlOrResponse;
    return envelope.Kind == MessageKind.Event
        ? TransportPriority.Event
        : TransportPriority.Request;
}
```

`EndpointRuntime` 拥有 `outbound` 和 `inbound` 两个 `PriorityMailbox`。outbound 单写 loop 是 transport 唯一 `SendAsync` 调用者；inbound 单读 loop 是 transport 唯一 `ReadAllAsync` 消费者。未知 `correlationId` response 不进入 mailbox，记录 `UnknownCorrelation` 后丢弃。inbound request 满时通过 outbound control 通道发送 `TooManyRequests` response；inbound event 满时应用该 route 的 event policy。

- [ ] **Step 4: 实现完成计数和断线收敛**

```csharp
public void CompleteRequest(RequestDirection direction)
{
    ref var counter = ref direction == RequestDirection.HostToEndpoint
        ? ref outboundInFlight
        : ref inboundInFlight;
    var value = Interlocked.Decrement(ref counter);
    if (value < 0)
        throw new InvalidOperationException("In-flight request count became negative.");
}

private void DisconnectOnce(TransportDisconnect disconnect)
{
    if (Interlocked.Exchange(ref disconnected, 1) != 0) return;
    lifetime.Cancel();
    outbound.Complete();
    inbound.Complete();
    Disconnected?.Invoke(this, disconnect);
}
```

响应、取消、timeout 和 disconnect 只调用一次 `CompleteRequest`。response 保留失败立即断开 endpoint，不能回退到 unbounded queue。所有 loop 使用 `ConfigureAwait(false)`，不在 lock 中 await。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~EndpointRuntimeTests"
```

Expected: PASS；两个方向分别接受 64、拒绝第 65 个；匹配 response 在 event flood 前发送；同一 transport 最大并发写为 1；断线后两个计数归零。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Transports\EndpointRuntime.cs MyTools.Host.Core.Test\Transports\EndpointRuntimeTests.cs
git commit -m "feat: schedule bidirectional endpoint traffic" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: 实现请求/响应关联、trace、预算、取消和 timeout

**Files:**
- Create: `MyTools.Host.Core\Messaging\MessageEnvelopeFactory.cs`
- Create: `MyTools.Host.Core\Messaging\PendingRequestRegistry.cs`
- Create: `MyTools.Host.Core.Test\Fakes\ManualTimeProvider.cs`
- Create: `MyTools.Host.Core.Test\Messaging\MessageBusRequestTests.cs`

- [ ] **Step 1: 写关联、嵌套 trace 和剩余预算红灯测试**

```csharp
[Test]
public async Task Nested_call_keeps_trace_and_reduces_budget_with_monotonic_time()
{
    var clock = new ManualTimeProvider();
    var registry = new PendingRequestRegistry(clock, diagnostics);
    var request = MessageEnvelopeFactory.Request(
        Identity("session-1", "webview-1"),
        "plugin.call.save",
        JsonSerializer.SerializeToElement(new { value = 1 }, ProtocolJson.SerializerOptions),
        TimeSpan.FromSeconds(30),
        traceId: "trace-root",
        id: "request-1");
    var pending = registry.Add(request, targetEndpointId: "node-main", default);

    clock.Advance(TimeSpan.FromMilliseconds(1250));
    var forwarded = registry.Forward(request, Identity("session-1", "node-main"), "request-2");
    registry.Complete(Response(forwarded, new { ok = true }));

    Assert.Multiple(() =>
    {
        Assert.That(forwarded.TraceId, Is.EqualTo("trace-root"));
        Assert.That(forwarded.TimeoutMs, Is.EqualTo(28_750));
        Assert.That(pending.IsCompletedSuccessfully, Is.True);
    });
}
```

- [ ] **Step 2: 写 timeout 与取消竞态红灯测试**

```csharp
[Test]
public async Task Timeout_completes_once_and_emits_best_effort_cancel()
{
    var harness = RequestHarness.Create(timeout: TimeSpan.FromMilliseconds(50));
    var call = harness.CallAsync("plugin.call.wait");
    harness.Clock.Advance(TimeSpan.FromMilliseconds(51));
    await harness.RunTimersAsync();
    harness.ReceiveSuccessForLastRequest();

    var error = Assert.ThrowsAsync<BusException>(async () => await call);
    Assert.That(error!.Error.Code, Is.EqualTo(ProtocolErrorCodes.RequestTimeout));
    Assert.That(harness.Sent.Count(x => x.Route == "bus.cancel"), Is.EqualTo(1));
    Assert.That(harness.PendingCount, Is.Zero);
}
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageBusRequestTests"
```

Expected: FAIL，factory、pending registry、manual time 和 `BusException` 不存在。

- [ ] **Step 4: 实现单调 deadline 和一次性 completion**

```csharp
public sealed class BusException(BusError error) : Exception(error.Message)
{
    public BusError Error { get; } = error;
}

internal sealed class PendingRequest
{
    public required MessageEnvelope Request { get; init; }
    public required string TargetEndpointId { get; init; }
    public required long StartedTimestamp { get; init; }
    public required long DeadlineTimestamp { get; init; }
    public required TaskCompletionSource<MessageEnvelope> Completion { get; init; }
    public CancellationTokenRegistration CancellationRegistration { get; set; }
    public int Completed;
}
```

`PendingRequestRegistry` 以 request `id` 为 key。`TryComplete` 首先 `Interlocked.Exchange(ref Completed, 1)`，再从 dictionary 删除、dispose cancellation registration、减少对应方向 in-flight，最后完成 task。timeout 和调用方取消分别创建 `RequestTimeout`/`Cancelled`，并经 control 通道发送一次 `bus.cancel`，其 `CorrelationId` 指向原 request，`TraceId` 沿用原 trace。迟到或重复 response 记录并丢弃。

预算计算使用：

```csharp
var elapsed = timeProvider.GetElapsedTime(pending.StartedTimestamp, timeProvider.GetTimestamp());
var remaining = Math.Max(0, pending.Request.TimeoutMs!.Value - (int)Math.Ceiling(elapsed.TotalMilliseconds));
if (remaining == 0)
    throw new BusException(new(ProtocolErrorCodes.RequestTimeout, "Request budget exhausted.", false));
return request with { TimeoutMs = remaining, TraceId = pending.Request.TraceId };
```

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageBusRequestTests"
```

Expected: PASS；乱序 correlation 正确、嵌套 trace 不变、预算只减不增、response/timeout/cancel 竞态只完成一次、未知 correlation 被丢弃。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Messaging\MessageEnvelopeFactory.cs MyTools.Host.Core\Messaging\PendingRequestRegistry.cs MyTools.Host.Core.Test\Fakes\ManualTimeProvider.cs MyTools.Host.Core.Test\Messaging\MessageBusRequestTests.cs
git commit -m "feat: correlate cancellable plugin requests" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 6: 实现 MessageBus endpoint、请求和响应路由

**Files:**
- Create: `MyTools.Host.Core\Messaging\MessageBus.cs`
- Create: `MyTools.Host.Core\Messaging\MessageBusContracts.cs`
- Modify: `MyTools.Host.Core.Test\Messaging\MessageBusRequestTests.cs`

- [ ] **Step 1: 写 route、session 隔离和 unavailable 红灯测试**

```csharp
[Test]
public async Task WebView_request_routes_to_main_Node_and_response_returns_only_to_origin()
{
    await using var harness = await BusHarness.StartReadySessionAsync("settings", "main", "session-1");
    var web = harness.AddEndpoint("webview-1", EndpointKind.WebView);
    var node = harness.AddEndpoint("node-main", EndpointKind.MainNode);

    web.Receive(Request(web.Identity, "request-1", "plugin.call.save"));
    await node.WaitForSentAsync(1);
    node.Receive(Response(node.Identity, "response-1", "request-1"));
    await web.WaitForSentAsync(1);

    Assert.That(node.Sent.Single().CorrelationId, Is.Null);
    Assert.That(web.Sent.Single().CorrelationId, Is.EqualTo("request-1"));
    Assert.That(harness.OtherEndpoints.SelectMany(x => x.Sent), Is.Empty);
}

[TestCase(PluginSessionState.Starting)]
[TestCase(PluginSessionState.Handshaking)]
[TestCase(PluginSessionState.Restarting)]
[TestCase(PluginSessionState.Stopping)]
[TestCase(PluginSessionState.Stopped)]
public async Task Non_ready_session_rejects_new_request(PluginSessionState state)
{
    await using var harness = await BusHarness.StartSessionAsync(state);
    var error = await harness.CallAndReadErrorAsync("plugin.call.save");
    Assert.That(error.Code, Is.EqualTo(ProtocolErrorCodes.PluginUnavailable));
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageBusRequestTests"
```

Expected: FAIL，`MessageBus`、`EndpointKind` 和 bus harness 所需注册 API 不存在。

- [ ] **Step 3: 定义 endpoint context 和 bus API**

```csharp
public enum EndpointKind { Host, MainNode, Worker, WebView, Diagnostics }

public sealed record AuthenticatedEndpointContext(
    EndpointIdentity Identity,
    EndpointKind Kind,
    IReadOnlySet<string> DeclaredCapabilities,
    IReadOnlySet<string> GrantedCapabilities);

public interface IMessageBus
{
    ValueTask<EndpointRegistration> RegisterAsync(
        AuthenticatedEndpointContext context,
        IMessageTransport transport,
        CancellationToken cancellationToken);
    Task<MessageEnvelope> CallAsync(
        EndpointIdentity target,
        string route,
        JsonElement payload,
        TimeSpan timeout,
        string? traceId,
        CancellationToken cancellationToken);
    ValueTask PublishAsync(
        EndpointIdentity publisher,
        string route,
        JsonElement payload,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: 实现严格路由和 identity/session 防线**

`MessageBus` 以完整 `EndpointIdentity` 注册 runtime。入站第一步比较 envelope 的 plugin/entry/session/endpoint 与 transport binding；任一不等即记录 `SecurityIdentityMismatch` 并丢弃，不能调用 pending registry。路由表固定为：

```csharp
private static RouteFamily ClassifyRoute(string route) => route switch
{
    var value when value.StartsWith("plugin.call.", StringComparison.Ordinal) => RouteFamily.PluginCall,
    var value when value.StartsWith("host.call.", StringComparison.Ordinal) => RouteFamily.HostCall,
    var value when value.StartsWith("plugin.event.", StringComparison.Ordinal) => RouteFamily.PluginEvent,
    var value when value.StartsWith("host.event.", StringComparison.Ordinal) => RouteFamily.HostEvent,
    "bus.cancel" => RouteFamily.Cancel,
    "bus.subscribe" => RouteFamily.Subscribe,
    "bus.unsubscribe" => RouteFamily.Unsubscribe,
    "bus.ping" or "bus.pong" => RouteFamily.Heartbeat,
    var value when value.StartsWith("diagnostics.", StringComparison.Ordinal) => RouteFamily.Diagnostics,
    _ => RouteFamily.Unknown
};
```

`plugin.call.*` 只路由到同一 session 的 main Node 或明确选择的 Worker；`host.call.*` 只进入 `CapabilityGateway`；unknown 返回 `RouteNotFound`。响应只发回 pending 保存的 origin endpoint。session 不为 Ready/Degraded 时新 request 立即返回 `PluginUnavailable`，不排队。`EndpointRegistration.DisposeAsync` 删除 runtime、该 endpoint 的订阅，并以 `TransportDisconnected` 完成相关 pending。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageBusRequestTests"
```

Expected: PASS；同 session WebView→main Node→WebView 完成，跨 plugin/entry/session 路由失败，旧 session response 不能完成新请求，非服务状态不排队。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Messaging\MessageBus.cs MyTools.Host.Core\Messaging\MessageBusContracts.cs MyTools.Host.Core.Test\Messaging\MessageBusRequestTests.cs
git commit -m "feat: route isolated plugin bus requests" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 7: 实现事件订阅、取消和状态快照

**Files:**
- Create: `MyTools.Host.Core\Messaging\SubscriptionRegistry.cs`
- Create: `MyTools.Host.Core\Messaging\StateTopicStore.cs`
- Create: `MyTools.Host.Core.Test\Messaging\MessageBusSubscriptionTests.cs`
- Modify: `MyTools.Host.Core\Messaging\MessageBus.cs`

- [ ] **Step 1: 写会话隔离订阅和断线清除红灯测试**

```csharp
[Test]
public async Task Event_reaches_only_subscribed_endpoints_in_same_session()
{
    await using var harness = await BusHarness.StartReadySessionAsync("settings", "main", "session-1");
    var subscribed = harness.AddEndpoint("webview-1", EndpointKind.WebView);
    var unregistered = harness.AddEndpoint("webview-2", EndpointKind.WebView);
    var otherSession = harness.AddEndpoint("webview-old", EndpointKind.WebView, sessionId: "session-old");

    subscribed.Receive(Subscribe(subscribed.Identity, "plugin.event.progress"));
    await harness.PublishFromMainAsync("plugin.event.progress", new { value = 7 });

    Assert.That(subscribed.Sent.Any(x => x.Route == "plugin.event.progress"), Is.True);
    Assert.That(unregistered.Sent, Is.Empty);
    Assert.That(otherSession.Sent, Is.Empty);
}
```

- [ ] **Step 2: 写状态 topic 即时快照红灯测试**

```csharp
[Test]
public async Task State_topic_sends_current_snapshot_after_subscribe_response()
{
    await using var harness = await BusHarness.StartReadySessionAsync("settings", "main", "session-1");
    await harness.StateTopics.SetAsync(
        harness.SessionIdentity,
        "host.event.theme-changed",
        JsonSerializer.SerializeToElement(new { theme = "dark" }, ProtocolJson.SerializerOptions),
        default);
    var web = harness.AddEndpoint("webview-1", EndpointKind.WebView);

    web.Receive(Subscribe(web.Identity, "host.event.theme-changed", requestId: "subscribe-1"));
    await web.WaitForSentAsync(2);

    Assert.That(web.Sent.Select(x => (x.Kind, x.Route)),
        Is.EqualTo(new[]
        {
            (MessageKind.Response, "bus.subscribe"),
            (MessageKind.Event, "host.event.theme-changed")
        }));
}
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageBusSubscriptionTests"
```

Expected: FAIL，subscription registry 和 state topic store 不存在。

- [ ] **Step 4: 实现 connection-local subscription 和 snapshot 顺序**

```csharp
internal readonly record struct SubscriptionKey(
    string PluginId, string EntryId, string SessionId, string EndpointId, string Topic);

public bool AllowsTopic(EndpointKind kind, string topic) =>
    kind switch
    {
        EndpointKind.WebView =>
            topic.StartsWith("plugin.event.", StringComparison.Ordinal) ||
            topic.StartsWith("host.event.", StringComparison.Ordinal),
        EndpointKind.MainNode or EndpointKind.Worker =>
            topic.StartsWith("host.event.", StringComparison.Ordinal),
        EndpointKind.Diagnostics => topic.StartsWith("diagnostics.", StringComparison.Ordinal),
        _ => false
    };
```

`bus.subscribe`/`bus.unsubscribe` payload 使用 Protocol generated DTO 和 `IRoutePayloadValidator` 校验；订阅 key 包含完整会话与 endpoint。subscribe 成功先发送 response，再读取同 session 的 `StateTopicStore` 并发送当前快照。endpoint dispose 清除其全部订阅；Host Core 不跨连接持久化，也不重放重启窗口事件。`bus.cancel` 只查找发起 endpoint 拥有的 pending request，找不到时无操作并记录诊断。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~MessageBusSubscriptionTests"
```

Expected: PASS；同 session 精确投递、subscribe response 在 snapshot 前、unsubscribe/断线后无投递、WebView 非法 topic 返回 `CapabilityDenied`、跨插件无事件。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Messaging\SubscriptionRegistry.cs MyTools.Host.Core\Messaging\StateTopicStore.cs MyTools.Host.Core\Messaging\MessageBus.cs MyTools.Host.Core.Test\Messaging\MessageBusSubscriptionTests.cs
git commit -m "feat: add session-scoped bus subscriptions" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 8: 实现授权抽象和 CapabilityGateway

**Files:**
- Create: `MyTools.Host.Core\Capabilities\CapabilityContracts.cs`
- Create: `MyTools.Host.Core\Capabilities\CapabilityGateway.cs`
- Create: `MyTools.Host.Core.Test\Capabilities\CapabilityGatewayTests.cs`
- Modify: `MyTools.Host.Core\Messaging\MessageBus.cs`

- [ ] **Step 1: 写声明、授权、WebView 和 DTO 红灯矩阵**

```csharp
[TestCase(false, true, EndpointKind.MainNode, "CapabilityNotDeclared")]
[TestCase(true, false, EndpointKind.MainNode, "CapabilityDenied")]
[TestCase(true, true, EndpointKind.WebView, "CapabilityDenied")]
public async Task Invoke_rejects_before_handler(
    bool declared, bool approved, EndpointKind kind, string expectedCode)
{
    var handler = new RecordingCapabilityHandler("clipboard.read");
    var gateway = Gateway(
        declared ? new[] { "clipboard.read" } : [],
        approved ? CapabilityAuthorizationDecision.Allow : CapabilityAuthorizationDecision.Deny,
        handler);

    var error = Assert.ThrowsAsync<BusException>(() => gateway.InvokeAsync(
        Context(kind),
        "host.call.clipboard.read",
        JsonSerializer.SerializeToElement(new { }, ProtocolJson.SerializerOptions),
        RequestBudget.FromMilliseconds(1000),
        default).AsTask());

    Assert.That(error!.Error.Code, Is.EqualTo(expectedCode));
    Assert.That(handler.InvocationCount, Is.Zero);
}

[Test]
public async Task Invalid_payload_never_reaches_capability_handler()
{
    validator.Reject("host.call.configuration.write", "/key", "must be string");
    var error = Assert.ThrowsAsync<BusException>(() => Invoke(new { key = 42 }));
    Assert.That(error!.Error.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
    Assert.That(error.Error.Details!.Value.GetProperty("issues")[0].GetProperty("path").GetString(),
        Is.EqualTo("/key"));
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~CapabilityGatewayTests"
```

Expected: FAIL，capability contracts 和 gateway 不存在。

- [ ] **Step 3: 定义可替换授权、限流和 handler 契约**

```csharp
public enum CapabilityAuthorizationDecision { Allow, Deny }

public enum CapabilityGrantLifetime { Persistent, Session, PerCall }

public sealed record PluginTrustIdentity(
    string PluginId,
    string PublisherIdentity,
    string InstallationSource,
    bool IsLocalDevelopment);

public sealed record CapabilityAuthorizationRequest(
    PluginTrustIdentity Plugin,
    string EntryId,
    string Capability,
    CapabilityGrantLifetime Lifetime,
    EndpointKind EndpointKind,
    string EndpointId);

public interface ICapabilityAuthorizer
{
    ValueTask<CapabilityAuthorizationDecision> AuthorizeAsync(
        CapabilityAuthorizationRequest request,
        CancellationToken cancellationToken);
}

public interface ICapabilityRateLimiter
{
    ValueTask<bool> TryAcquireAsync(
        CapabilityAuthorizationRequest request,
        CancellationToken cancellationToken);
}

public interface ICapabilityHandler
{
    string Capability { get; }
    bool HasExternalSideEffects { get; }
    ValueTask<JsonElement> InvokeAsync(
        CapabilityInvocationContext context,
        JsonElement payload,
        CancellationToken cancellationToken);
}

public readonly record struct RequestBudget(long StartedTimestamp, int TimeoutMs)
{
    public bool IsExpired(TimeProvider timeProvider) =>
        timeProvider.GetElapsedTime(StartedTimestamp, timeProvider.GetTimestamp())
            >= TimeSpan.FromMilliseconds(TimeoutMs);
}

public sealed record CapabilityInvocationContext(
    EndpointIdentity Identity,
    string Route,
    string TraceId,
    RequestBudget Budget,
    TimeProvider TimeProvider)
{
    public void ThrowIfBudgetExpiredBeforeCommit()
    {
        if (Budget.IsExpired(TimeProvider))
            throw Errors.RequestTimeout(Route);
    }
}
```

- [ ] **Step 4: 实现固定检查顺序和提交前预算检查**

`CapabilityGateway.InvokeAsync` 的顺序固定为 endpoint kind → route 映射 → manifest 声明 → authorizer → rate limiter → generated DTO validator → handler lookup → side-effect 提交前预算 → handler。每一步失败都不调用后续组件。

```csharp
if (caller.Kind == EndpointKind.WebView)
    throw Errors.CapabilityDenied("WebView endpoints cannot call host capabilities.");
if (!caller.DeclaredCapabilities.Contains(capability))
    throw Errors.CapabilityNotDeclared(capability);
if (await authorizer.AuthorizeAsync(request, cancellationToken).ConfigureAwait(false)
    != CapabilityAuthorizationDecision.Allow)
    throw Errors.CapabilityDenied(capability);
if (!await rateLimiter.TryAcquireAsync(request, cancellationToken).ConfigureAwait(false))
    throw Errors.TooManyRequests(capability);
var result = payloadValidator.Validate(route, payload);
if (!result.IsValid)
    throw Errors.InvalidPayload(result.Issues);
if (budget.IsExpired(timeProvider))
    throw Errors.RequestTimeout(route);
return await handlers[capability].InvokeAsync(
    new(caller.Identity, route, traceId, budget), payload, cancellationToken)
    .ConfigureAwait(false);
```

有外部副作用的 handler 必须在不可逆提交语句前调用
`context.ThrowIfBudgetExpiredBeforeCommit()`；测试 handler 在 gateway 检查后推进
`ManualTimeProvider`，证明提交前第二次检查返回 `RequestTimeout` 且未执行写入。禁止 handler 返回宿主内部对象、process handle 或命令执行 delegate；返回值必须是 `JsonElement` 并再次执行 route response validator。授权 key 使用 `PluginTrustIdentity + capability`，publisher identity、installation source、声明扩张或本地开发身份变化都不能继承旧批准；授权允许/拒绝写诊断，但不记录 payload。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~CapabilityGatewayTests"
```

Expected: PASS；声明、批准、endpoint kind、限流、请求/响应 DTO、预算和未知 capability 全部有稳定错误码；handler 仅在全部检查通过后调用一次。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Capabilities MyTools.Host.Core\Messaging\MessageBus.cs MyTools.Host.Core.Test\Capabilities
git commit -m "feat: gate plugin host capabilities" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 9: 实现会话状态机、快照和 restart policy

**Files:**
- Create: `MyTools.Host.Core\Sessions\PluginSessionState.cs`
- Create: `MyTools.Host.Core\Sessions\PluginSessionSnapshot.cs`
- Create: `MyTools.Host.Core\Sessions\RestartPolicy.cs`
- Create: `MyTools.Host.Core.Test\Sessions\PluginSessionActorTests.cs`

- [ ] **Step 1: 写允许与禁止转换红灯测试**

```csharp
[TestCase(PluginSessionState.Created, PluginSessionState.Starting)]
[TestCase(PluginSessionState.Starting, PluginSessionState.Handshaking)]
[TestCase(PluginSessionState.Handshaking, PluginSessionState.Ready)]
[TestCase(PluginSessionState.Ready, PluginSessionState.Degraded)]
[TestCase(PluginSessionState.Degraded, PluginSessionState.Ready)]
[TestCase(PluginSessionState.Ready, PluginSessionState.Restarting)]
[TestCase(PluginSessionState.Restarting, PluginSessionState.Starting)]
[TestCase(PluginSessionState.Ready, PluginSessionState.Stopping)]
[TestCase(PluginSessionState.Stopping, PluginSessionState.Stopped)]
public void State_machine_allows_design_transitions(
    PluginSessionState from, PluginSessionState to) =>
    Assert.That(PluginSessionTransitions.CanMove(from, to), Is.True);

[TestCase(PluginSessionState.Created, PluginSessionState.Ready)]
[TestCase(PluginSessionState.Stopped, PluginSessionState.Starting)]
[TestCase(PluginSessionState.Stopping, PluginSessionState.Ready)]
public void State_machine_rejects_invalid_transitions(
    PluginSessionState from, PluginSessionState to) =>
    Assert.That(PluginSessionTransitions.CanMove(from, to), Is.False);
```

- [ ] **Step 2: 写 degraded 和 restart 上限红灯测试**

```csharp
[Test]
public void WebView_close_does_not_degrade_but_noncritical_worker_failure_does()
{
    var snapshot = ReadySnapshot();
    Assert.That(snapshot.WithoutEndpoint("webview-1").State, Is.EqualTo(PluginSessionState.Ready));
    Assert.That(snapshot.WithFailedWorker("node-worker-1").State, Is.EqualTo(PluginSessionState.Degraded));
}

[Test]
public void Restart_policy_stops_after_window_limit()
{
    var policy = new RestartPolicy(3, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(30), jitterRatio: 0);
    var now = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
    Assert.That(policy.Decide([now, now.AddSeconds(1), now.AddSeconds(2)], now.AddSeconds(3)).Allowed,
        Is.False);
}
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PluginSessionActorTests"
```

Expected: FAIL，state、transition、snapshot 和 restart policy 不存在。

- [ ] **Step 4: 实现显式转换表与不可变快照**

```csharp
public enum PluginSessionState
{
    Created, Starting, Handshaking, Ready, Degraded, Restarting, Stopping, Stopped
}

private static readonly IReadOnlySet<(PluginSessionState From, PluginSessionState To)> Allowed =
    new HashSet<(PluginSessionState, PluginSessionState)>
    {
        (PluginSessionState.Created, PluginSessionState.Starting),
        (PluginSessionState.Starting, PluginSessionState.Handshaking),
        (PluginSessionState.Handshaking, PluginSessionState.Ready),
        (PluginSessionState.Ready, PluginSessionState.Degraded),
        (PluginSessionState.Degraded, PluginSessionState.Ready),
        (PluginSessionState.Starting, PluginSessionState.Restarting),
        (PluginSessionState.Handshaking, PluginSessionState.Restarting),
        (PluginSessionState.Ready, PluginSessionState.Restarting),
        (PluginSessionState.Degraded, PluginSessionState.Restarting),
        (PluginSessionState.Restarting, PluginSessionState.Starting),
        (PluginSessionState.Created, PluginSessionState.Stopping),
        (PluginSessionState.Starting, PluginSessionState.Stopping),
        (PluginSessionState.Handshaking, PluginSessionState.Stopping),
        (PluginSessionState.Ready, PluginSessionState.Stopping),
        (PluginSessionState.Degraded, PluginSessionState.Stopping),
        (PluginSessionState.Restarting, PluginSessionState.Stopping),
        (PluginSessionState.Stopping, PluginSessionState.Stopped),
        (PluginSessionState.Restarting, PluginSessionState.Stopped)
    };
```

`PluginSessionSnapshot` 包含 `PluginTrustIdentity`、`EntryId`、`SessionId`、`Generation`、`State`、main Node、Worker/WebView endpoint 集合、declared/granted capabilities、health、restart count。所有集合以 `ImmutableDictionary`/`ImmutableHashSet` 暴露。`RestartPolicy` 使用 `baseDelay * 2^attempt`、max delay、注入随机源计算 jitter，并以时间窗口内 attempt 数决定 `Allowed`。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PluginSessionActorTests"
```

Expected: PASS；设计图所有转换通过，非法跳转抛 `InvalidOperationException`，WebView 关闭不降级，非关键 Worker 失败降级，超过重启上限进入 Stopped。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Sessions\PluginSessionState.cs MyTools.Host.Core\Sessions\PluginSessionSnapshot.cs MyTools.Host.Core\Sessions\RestartPolicy.cs MyTools.Host.Core.Test\Sessions\PluginSessionActorTests.cs
git commit -m "feat: define plugin session lifecycle" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 10: 实现每 entry actor 和 generation 防陈旧回调

**Files:**
- Create: `MyTools.Host.Core\Sessions\PluginSessionActor.cs`
- Modify: `MyTools.Host.Core.Test\Sessions\PluginSessionActorTests.cs`

- [ ] **Step 1: 写并发 mutation 串行化红灯测试**

```csharp
[Test]
public async Task Concurrent_commands_are_applied_by_one_actor_in_enqueue_order()
{
    await using var actor = PluginSessionActor.Create(FixtureDefinition());
    var commands = Enumerable.Range(1, 100)
        .Select(index => actor.PostAsync(new RecordSequence(index), default).AsTask())
        .ToArray();
    await Task.WhenAll(commands);
    await actor.WhenIdleAsync();

    Assert.That(actor.TestSequence, Is.EqualTo(Enumerable.Range(1, 100)));
    Assert.That(actor.MaximumConcurrentCommandHandlers, Is.EqualTo(1));
}
```

- [ ] **Step 2: 写 generation 和新 sessionId 红灯测试**

```csharp
[Test]
public async Task New_start_increments_generation_and_drops_old_completion()
{
    var ids = new Queue<string>(["session-1", "session-2"]);
    await using var actor = PluginSessionActor.Create(FixtureDefinition(), () => ids.Dequeue());
    await actor.StartAsync(default);
    var oldGeneration = actor.Snapshot.Generation;
    await actor.ReloadAsync(default);
    await actor.PostAsync(new StartCompleted(oldGeneration, Success: true), default);
    await actor.WhenIdleAsync();

    Assert.Multiple(() =>
    {
        Assert.That(actor.Snapshot.Generation, Is.EqualTo(oldGeneration + 1));
        Assert.That(actor.Snapshot.SessionId, Is.EqualTo("session-2"));
        Assert.That(actor.Snapshot.State, Is.EqualTo(PluginSessionState.Starting));
        Assert.That(diagnostics.Events.Any(x => x.Name == "StaleGenerationCallback"), Is.True);
    });
}
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PluginSessionActorTests"
```

Expected: FAIL，`PluginSessionActor` 和 actor commands 不存在。

- [ ] **Step 4: 实现 bounded 单读者 actor，不在 handler 内等待 I/O**

```csharp
private readonly Channel<ISessionCommand> commands =
    Channel.CreateBounded<ISessionCommand>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    });

private async Task RunAsync()
{
    await foreach (var command in commands.Reader.ReadAllAsync(lifetime.Token)
        .ConfigureAwait(false))
    {
        if (command.Generation is long captured && captured != snapshot.Generation)
        {
            diagnostics.Emit(HostDiagnosticEvent.StaleGeneration(snapshot, command.GetType().Name));
            command.CompleteIgnored();
            continue;
        }
        Apply(command);
    }
}
```

进入 `Starting` 前执行：

```csharp
snapshot = snapshot with
{
    Generation = checked(snapshot.Generation + 1),
    SessionId = idGenerator.NewSessionId(),
    State = PluginSessionState.Starting,
    Endpoints = ImmutableDictionary<string, SessionEndpointSnapshot>.Empty
};
var generation = snapshot.Generation;
_ = endpointController.StartAsync(snapshot, lifetime.Token).ContinueWith(
    task => PostCompletion(new StartCompleted(generation, task)),
    CancellationToken.None,
    TaskContinuationOptions.ExecuteSynchronously,
    TaskScheduler.Default);
```

`Apply` 只更新 snapshot、发起后台 operation 或完成 caller TCS，不 await transport/process/capability。每个 operation completion 捕获 generation 并回投。endpoint add/remove、restart count 和 state 只能由 `Apply` 修改。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PluginSessionActorTests"
```

Expected: PASS；100 个命令最大并发 handler 为 1；每次 start generation 和 sessionId 更新；旧 start/handshake/exit/health completion 全部丢弃并诊断；actor queue 保持有界。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Sessions\PluginSessionActor.cs MyTools.Host.Core.Test\Sessions\PluginSessionActorTests.cs
git commit -m "feat: serialize plugin sessions with actors" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 11: 实现 SessionManager、endpoint registry 和后续 transport 端口

**Files:**
- Create: `MyTools.Host.Core\Sessions\SessionContracts.cs`
- Create: `MyTools.Host.Core\Sessions\PluginSessionManager.cs`
- Create: `MyTools.Host.Core.Test\Sessions\PluginSessionManagerTests.cs`
- Modify: `MyTools.Host.Core\Messaging\MessageBus.cs`

- [ ] **Step 1: 写每 entry 唯一 actor 与 endpoint 注册红灯测试**

```csharp
[Test]
public async Task Manager_creates_one_actor_per_plugin_entry_and_registers_multiple_endpoints()
{
    await using var manager = CreateManager();
    var first = await manager.StartAsync(Definition("settings", "main"), default);
    var same = await manager.StartAsync(Definition("settings", "main"), default);
    var other = await manager.StartAsync(Definition("settings", "history"), default);
    await manager.RegisterMainNodeAsync(first.Identity("node-main"), new FakeMessageTransport(
        first.Identity("node-main")), default);
    await manager.RegisterWorkerAsync(
        first.Identity("node-main"), first.Identity("node-worker-1"),
        new HashSet<string>(["configuration.read"]),
        new FakeMessageTransport(first.Identity("node-worker-1")), default);

    Assert.Multiple(() =>
    {
        Assert.That(same.ActorId, Is.EqualTo(first.ActorId));
        Assert.That(other.ActorId, Is.Not.EqualTo(first.ActorId));
        Assert.That(manager.GetRequired("settings", "main").Endpoints.Keys,
            Is.EquivalentTo(new[] { "node-main", "node-worker-1" }));
    });
}
```

- [ ] **Step 2: 写 main disconnect、Worker disconnect 和 reload 红灯测试**

```csharp
[TestCase("node-main", PluginSessionState.Restarting)]
[TestCase("node-worker-1", PluginSessionState.Degraded)]
public async Task Disconnect_has_role_specific_state(string endpointId, PluginSessionState expected)
{
    await using var manager = await ReadyManagerAsync();
    await manager.DisconnectedAsync(
        manager.CurrentIdentity(endpointId),
        new(ProtocolErrorCodes.TransportDisconnected, "test"),
        default);
    await manager.WhenIdleAsync("settings", "main");
    Assert.That(manager.GetRequired("settings", "main").State, Is.EqualTo(expected));
}
```

- [ ] **Step 3: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PluginSessionManagerTests"
```

Expected: FAIL，manager 和 session contracts 不存在。

- [ ] **Step 4: 定义后续阶段稳定输出并实现 manager**

```csharp
public interface IPluginSessionAccessor
{
    PluginSessionSnapshot GetRequired(string pluginId, string entryId);
    event EventHandler<PluginSessionReplacedEventArgs> SessionReplaced;
}

public interface IPluginSessionEndpointRegistry
{
    ValueTask<EndpointRegistration> RegisterWebViewAsync(
        EndpointIdentity identity, IMessageTransport transport, CancellationToken cancellationToken);
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

public interface IWorkerRegistration
{
    ValueTask RegisterWorkerAsync(
        EndpointIdentity mainIdentity,
        EndpointIdentity workerIdentity,
        IReadOnlySet<string> capabilities,
        IMessageTransport transport,
        CancellationToken cancellationToken);
}

public interface INodeEndpointController
{
    Task StartAsync(PluginSessionSnapshot session, CancellationToken cancellationToken);
    Task StopProcessTreeAsync(PluginSessionSnapshot session, CancellationToken cancellationToken);
}
```

`PluginSessionManager` 使用 `(PluginId, EntryId)` ordinal key 保存 actor。main disconnect 先由 MessageBus 以 `TransportDisconnected` 完成 pending，再回投 actor 进入 Restarting、调用 process tree stop、按 restart policy 延时，然后新 generation start。Worker disconnect 只删除 Worker 并进入 Degraded；WebView dispose 只删除 endpoint，状态保持。reload 无论当前 Ready/Degraded 都进入 Restarting。stop 进入 Stopping，拒绝新请求，后台 graceful stop 完成或超时后回投 Stopped。超过 restart 上限进入 Stopped，不自动再启动。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~PluginSessionManagerTests"
```

Expected: PASS；每 entry 唯一 actor；main/Worker/WebView 行为不同；reload 创建新 generation/sessionId；旧 endpoint 无法注册到新 session；restart 上限后 Stopped。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Sessions\SessionContracts.cs MyTools.Host.Core\Sessions\PluginSessionManager.cs MyTools.Host.Core\Messaging\MessageBus.cs MyTools.Host.Core.Test\Sessions\PluginSessionManagerTests.cs
git commit -m "feat: manage plugin entry sessions" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 12: 实现结构化诊断事件和聚合指标

**Files:**
- Create: `MyTools.Host.Core\Diagnostics\HostDiagnosticEvent.cs`
- Create: `MyTools.Host.Core\Diagnostics\IHostDiagnostics.cs`
- Create: `MyTools.Host.Core\Diagnostics\HostDiagnostics.cs`
- Create: `MyTools.Host.Core.Test\Diagnostics\HostDiagnosticsTests.cs`
- Modify: `MyTools.Host.Core\Messaging\MessageBus.cs`
- Modify: `MyTools.Host.Core\Capabilities\CapabilityGateway.cs`
- Modify: `MyTools.Host.Core\Sessions\PluginSessionActor.cs`

- [ ] **Step 1: 写脱敏和计数红灯测试**

```csharp
[Test]
public void Diagnostics_store_metadata_without_payload_or_token()
{
    var diagnostics = new HostDiagnostics(TimeProvider.System);
    diagnostics.RequestCompleted(
        Identity("session-1", "node-main"),
        "trace-1",
        "host.call.configuration.write",
        TimeSpan.FromMilliseconds(12),
        "CapabilityDenied");
    diagnostics.TransportDisconnected(
        Identity("session-1", "node-main"),
        "handshake-token=secret-value");

    var snapshot = diagnostics.Snapshot();
    var json = JsonSerializer.Serialize(snapshot, ProtocolJson.SerializerOptions);
    Assert.Multiple(() =>
    {
        Assert.That(json, Does.Contain("trace-1"));
        Assert.That(json, Does.Contain("CapabilityDenied"));
        Assert.That(json, Does.Not.Contain("secret-value"));
        Assert.That(json, Does.Not.Contain("\"payload\""));
    });
}

[TestCase("droppedEvents")]
[TestCase("coalescedEvents")]
[TestCase("backpressureRejected")]
[TestCase("unknownCorrelation")]
public void Counters_are_atomic(string name)
{
    Parallel.For(0, 10_000, _ => diagnostics.Increment(name));
    Assert.That(diagnostics.Snapshot().Counters[name], Is.EqualTo(10_000));
}
```

- [ ] **Step 2: 运行测试确认红灯**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~HostDiagnosticsTests"
```

Expected: FAIL，diagnostic model 和 implementation 不存在。

- [ ] **Step 3: 定义不含业务 payload 的事件模型**

```csharp
public sealed record HostDiagnosticEvent(
    DateTimeOffset Timestamp,
    string Name,
    string PluginId,
    string EntryId,
    string? SessionId,
    string? EndpointId,
    string? TraceId,
    string? Route,
    string Outcome,
    double? DurationMs,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record HostDiagnosticsSnapshot(
    IReadOnlyList<HostDiagnosticEvent> RecentEvents,
    IReadOnlyDictionary<string, long> Counters);

public interface IHostDiagnostics
{
    void Emit(HostDiagnosticEvent diagnosticEvent);
    void Increment(string counter, long value = 1);
    HostDiagnosticsSnapshot Snapshot();
}
```

- [ ] **Step 4: 接入设计要求的所有事件点**

使用固定 event name：`SessionStateChanged`、`TransportConnected`、`TransportDisconnected`、`RequestCompleted`、`HeartbeatObserved`、`CapabilityAuthorized`、`CapabilityDenied`、`PluginRestarted`、`InvalidMessageDropped`、`BackpressureRejected`、`EventDropped`、`EventCoalesced`、`StaleGenerationCallback`、`UnknownCorrelation`。`HostDiagnostics` 用 `ConcurrentDictionary<string,long>` 和固定 1024 条 ring buffer；exception 只保留 type 与脱敏摘要，摘要删除 `token=` 后到空白分隔符的值、完整 JSON 和换行后的内容。默认不存 `MessageEnvelope.Payload`、authorization token 或凭据。

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~HostDiagnosticsTests"
```

Expected: PASS；全部事件包含 plugin/entry，适用时包含 session/endpoint/trace/route/duration/outcome；四个计数并发精确；payload/token 不出现在 snapshot。

- [ ] **Step 5: 原子提交**

```powershell
git add MyTools.Host.Core\Diagnostics MyTools.Host.Core\Messaging\MessageBus.cs MyTools.Host.Core\Capabilities\CapabilityGateway.cs MyTools.Host.Core\Sessions\PluginSessionActor.cs MyTools.Host.Core.Test\Diagnostics
git commit -m "feat: record plugin host diagnostics" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 13: 注册 DI 并完成 fake transport 组件验收

**Files:**
- Create: `MyTools.Host.Core\DependencyInjection\HostCoreServiceCollectionExtensions.cs`
- Create: `MyTools.Host.Core.Test\DependencyInjection\HostCoreServiceCollectionExtensionsTests.cs`
- Create: `MyTools.Host.Core.Test\Components\FakeTransportBusTests.cs`

- [ ] **Step 1: 写与仓库现有 pattern 一致的 DI 红灯测试**

```csharp
using System.Text.Json;
using MyTools.Protocol.V3;

[Test]
public void AddHostCore_registers_stateful_services_as_singletons()
{
    var services = new ServiceCollection();
    services.AddSingleton<IRoutePayloadValidator>(new AcceptingRoutePayloadValidator());
    services.AddSingleton<ICapabilityAuthorizer>(new DenyingCapabilityAuthorizer());
    services.AddSingleton<ICapabilityRateLimiter>(new AllowingCapabilityRateLimiter());
    services.AddSingleton<INodeEndpointController>(new FakeNodeEndpointController());
    services.AddHostCore();
    using var provider = services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,
        ValidateScopes = true
    });

    Assert.Multiple(() =>
    {
        Assert.That(provider.GetRequiredService<IMessageBus>(),
            Is.SameAs(provider.GetRequiredService<IMessageBus>()));
        Assert.That(provider.GetRequiredService<IPluginSessionAccessor>(),
            Is.SameAs(provider.GetRequiredService<PluginSessionManager>()));
        Assert.That(provider.GetRequiredService<IHostDiagnostics>(),
            Is.SameAs(provider.GetRequiredService<HostDiagnostics>()));
    });
}

private sealed class AcceptingRoutePayloadValidator : IRoutePayloadValidator
{
    public ValidationResult Validate(string route, JsonElement payload) => new(true, []);
}
```

- [ ] **Step 2: 实现 extension 注册**

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace MyTools.Host.Core.DependencyInjection;

public static class HostCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHostCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<HostDiagnostics>();
        services.AddSingleton<IHostDiagnostics>(sp => sp.GetRequiredService<HostDiagnostics>());
        services.AddSingleton<SubscriptionRegistry>();
        services.AddSingleton<StateTopicStore>();
        services.AddSingleton<PendingRequestRegistry>();
        services.AddSingleton<CapabilityGateway>();
        services.AddSingleton<MessageBus>();
        services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<MessageBus>());
        services.AddSingleton<PluginSessionManager>();
        services.AddSingleton<IPluginSessionAccessor>(
            sp => sp.GetRequiredService<PluginSessionManager>());
        services.AddSingleton<IPluginSessionEndpointRegistry>(
            sp => sp.GetRequiredService<PluginSessionManager>());
        services.AddSingleton<INodeEndpointEvents>(
            sp => sp.GetRequiredService<PluginSessionManager>());
        services.AddSingleton<IWorkerRegistration>(
            sp => sp.GetRequiredService<PluginSessionManager>());
        return services;
    }
}
```

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj --filter "FullyQualifiedName~HostCoreServiceCollectionExtensionsTests"
```

Expected: PASS；`ValidateOnBuild` 成功，stateful service 与其 interface 指向同一 singleton。

- [ ] **Step 3: 写多 endpoint fake 组件验收**

```csharp
[Test]
public async Task Fake_transports_cover_out_of_order_disconnect_stale_session_and_cancel_race()
{
    await using var host = await FakeBusHost.StartAsync();
    var node = await host.ConnectMainAsync("settings", "main");
    var worker = await host.ConnectWorkerAsync("node-worker-1", ["configuration.read"]);
    var webA = await host.ConnectWebViewAsync("webview-a");
    var webB = await host.ConnectWebViewAsync("webview-b");
    await webA.SubscribeAsync("plugin.event.progress");

    var first = webA.CallAsync("plugin.call.first");
    var second = webB.CallAsync("plugin.call.second");
    node.Respond(second.RequestId, new { order = 2 });
    node.Respond(first.RequestId, new { order = 1 });
    node.Publish("plugin.event.progress", new { value = 3 });
    first.Cancel();
    node.Respond(first.RequestId, new { late = true });
    var oldIdentity = node.Identity;
    node.Disconnect();
    await host.WaitUntilReadyAsync();
    host.Inject(oldIdentity, Response(oldIdentity, "late-old", second.RequestId));

    Assert.Multiple(() =>
    {
        Assert.That(second.Result.GetProperty("order").GetInt32(), Is.EqualTo(2));
        Assert.That(webA.Events.Single().Route, Is.EqualTo("plugin.event.progress"));
        Assert.That(webB.Events, Is.Empty);
        Assert.That(first.Error.Code, Is.EqualTo(ProtocolErrorCodes.Cancelled));
        Assert.That(host.Diagnostics.Counter("unknownCorrelation"), Is.GreaterThanOrEqualTo(1));
        Assert.That(host.CurrentSessionId, Is.Not.EqualTo(oldIdentity.SessionId));
        Assert.That(worker.Identity.EndpointId, Is.EqualTo("node-worker-1"));
    });
}
```

- [ ] **Step 4: 运行 Host Core 全套和 solution 回归**

Run:

```powershell
dotnet test .\MyTools.Host.Core.Test\MyTools.Host.Core.Test.csproj -- NUnit.NumberOfTestWorkers=1
dotnet test .\MyTools.sln
dotnet build .\MyTools.sln --configuration Release
```

Expected: Host Core 测试无 skipped、全部 PASS；现有 Common/Desktop/Plugins NUnit 测试继续 PASS；Release build 0 errors。

- [ ] **Step 5: 运行边界与不变量扫描**

Run:

```powershell
rg -n "NamedPipe|WebView2|CoreWebView2|node:net|ProcessStartInfo|JobObject" .\MyTools.Host.Core .\MyTools.Host.Core.Test
rg -n "\.Result|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)" .\MyTools.Host.Core
rg -n "Channel\.CreateUnbounded" .\MyTools.Host.Core
git diff --exit-code -- .\MyTools.Plugins .\MyTools.Desktop
git status --short
```

Expected: 前三条 `rg` 均 exit 1 且无输出；Desktop/Plugins diff 为 0；status 只包含 `MyTools.sln`、`MyTools.Host.Core\**` 和 `MyTools.Host.Core.Test\**`。

- [ ] **Step 6: 原子提交**

```powershell
git add MyTools.Host.Core\DependencyInjection MyTools.Host.Core.Test\DependencyInjection MyTools.Host.Core.Test\Components
git commit -m "test: verify plugin Host Core integration" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## 第 2/5 份计划输出契约

本节是 Host Core 稳定输出的唯一基准；前文实现必须与其一致，后续计划必须直接消费以下类型，不创建同名替代品：

```csharp
using MyTools.Protocol.V3;

// Transport
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

// Sessions and downstream transport integration
public interface IPluginSessionAccessor
{
    PluginSessionSnapshot GetRequired(string pluginId, string entryId);
    event EventHandler<PluginSessionReplacedEventArgs> SessionReplaced;
}
public interface IPluginSessionEndpointRegistry
{
    ValueTask<EndpointRegistration> RegisterWebViewAsync(
        EndpointIdentity identity, IMessageTransport transport, CancellationToken cancellationToken);
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
public interface IWorkerRegistration
{
    ValueTask RegisterWorkerAsync(
        EndpointIdentity mainIdentity,
        EndpointIdentity workerIdentity,
        IReadOnlySet<string> capabilities,
        IMessageTransport transport,
        CancellationToken cancellationToken);
}
```

第 3/5 份计划负责真实 Named Pipe、进程和 heartbeat 驱动，但重启决策仍回到 `PluginSessionManager`。第 4/5 份计划只注册 WebView endpoint 并读取 session/state topic；窗口关闭不停止 session。第 5/5 份计划只迁移 manifest、SDK 和插件，不改变 64 in-flight、三类通道、at-most-once、授权或 generation 规则。

## Spec Coverage Checklist

| 批准设计要求 | 实施与证据 |
| --- | --- |
| `IMessageTransport` 抽象和 memory fake | Tasks 2、13；`MessageTransportContractTests`、`FakeTransportBusTests` |
| request/response correlation 和 origin-only response | Tasks 5、6；`MessageBusRequestTests` |
| event、subscribe/unsubscribe、state snapshot、session 隔离 | Task 7；`MessageBusSubscriptionTests` |
| best-effort cancel、timeout、单调预算、trace 传播 | Task 5；timeout/cancel race 与 nested trace 测试 |
| at-most-once、断线不重放 | Tasks 5、6、13；pending 清理和 fake disconnect 测试 |
| 每 manifest entry 一个 session actor | Tasks 10、11；actor identity 与 manager key 测试 |
| generation、新 sessionId、旧 callback/frame 拒绝 | Tasks 10、11、13；stale generation 和 old session 注入测试 |
| 完整状态机、Degraded、Stopping、restart 上限 | Task 9；转换矩阵和 restart policy 测试 |
| actor 不等待 transport/process/capability I/O | Task 10；单读者 apply 与 completion 回投测试 |
| 双向三类独立有界通道 | Tasks 3、4；两个方向分类和优先级测试 |
| 默认每 endpoint 每方向 64 in-flight | Tasks 3、4；第 65 个 request 拒绝测试 |
| response 保留、event 三种 overflow、总字节 | Tasks 3、4；drop/coalesce/response/4 MiB 测试 |
| `CapabilityGateway` 每次调用完整检查 | Task 8；kind/声明/授权/限流/DTO/预算矩阵 |
| 可替换授权抽象 | Task 8；`ICapabilityAuthorizer` 和 fake decision 测试 |
| 诊断事件与 dropped/coalesced/backpressure 计数 | Task 12；脱敏和 10,000 次并发计数测试 |
| Host Core 平台/UI 无关 | Tasks 1、13；禁止 API 扫描 |
| 不实现 Named Pipe、WebView2、SDK 或迁移 | Task 13 changed-file 与 forbidden-symbol 扫描 |

完成标准是 Task 13 的 Host Core 测试、solution 测试、Release build 和边界扫描全部符合预期，并且第 2/5 份计划输出契约可由第 3/5 份计划、第 4/5 份计划直接引用。
