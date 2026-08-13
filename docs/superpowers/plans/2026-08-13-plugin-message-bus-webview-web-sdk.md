# Plugin Message Bus 第 4/5 份计划：WebView Transport 与 Web SDK Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成插件消息总线第 4/5 份计划：实现 spec 中 WebView transport 与 Web SDK 的相关工作，产出由可信 WPF 调用方分配 `EndpointIdentity` 并提供已认证的插件动态 route manifest、在导航前注入递归冻结 bootstrap 的 WebView host/transport，以及消费共享协议包且不能自选 identity/manifest 的浏览器 SDK。

**Architecture:** 新建 `MyTools.Host.Transports`，以 `IWebView2Channel` 隔离 CoreWebView2/Dispatcher，并严格实现第 2/5 份 Host Core 计划输出的 pull-based `IMessageTransport`。WPF WebView2 adapter 必须先用 `AddScriptToExecuteOnDocumentCreatedAsync` 注册包含宿主分配 identity 与该 identity 已认证动态 route manifest 的递归冻结 bootstrap，再导航到插件页面；Web SDK 只从这个可信 construction boundary 读取两者，并在安装消息 listener、发起 call 或 subscription 前调用 `@mytools/protocol` 的 `registerRouteManifest`。页面不能提供或覆盖 plugin/entry/manifest，wire 协议不增加握手路由，manifest generation 与 transport generation 均保留在宿主/构建内部。动态 route 的 outgoing request 与 incoming successful response 分别使用共享 request/response validator；canonical routes 继续使用 Protocol 内置映射，不复制进 manifest。`NodePluginDetailContext`、`NodePluginDetailView`、DI、session accessor/registry、manifest 与示例插件迁移全部留给第 5/5 份迁移计划。

**Tech Stack:** .NET 8 (`net8.0`), C# 12, `System.Threading.Channels`, `System.Text.Json`, NUnit 4.3.2, Moq 4.20.72, TypeScript 7, Node.js 22, Node built-in test runner, `@mytools/protocol`

---

## 计划位置、范围与冻结输入

- **这是第 4/5 份计划。** 它只覆盖 spec 的 WebView transport 和 Web SDK 相关工作，不把 spec 的设计步骤当作计划标签。
- 前置依赖是已合入的第 1/5 份 Protocol Foundation 与第 2/5 份 Host Core。第 3/5 份 Named Pipe/Node SDK 与本计划共享 Host Core 契约，但本计划不修改 Named Pipe、Node SDK 或进程生命周期。
- 第 5/5 份计划负责 `NodePluginDetailContext.EntryId`、`NodePluginDetailView` identity propagation、Desktop DI/registry/session replacement 接线、旧 bridge 删除、plugin manifest/示例插件迁移和 E2E；本计划只定义其可信 WPF construction boundary 必须提供的已认证 route-manifest 输入契约。
- 本计划不读取或新增 `NodePluginDetailContext.EntryId`。可信 WPF 调用方必须向 `WebViewPluginHost` 同时传入完整、宿主分配的 `EndpointIdentity` 与 Host Protocol loader 已按该 `(pluginId, entryId)` 认证并验证的动态 route manifest（或等价的已验证可序列化表示）；host 不从 `ItemId`、keyword、路径、窗口或页面消息推导 identity/manifest。页面 bootstrap 与 Web SDK API 都没有 generation 字段，也不接受 plugin/entry/session/endpoint/manifest override。
- 本计划的可信 bootstrap 契约覆盖第 5/5 份迁移计划中的旧 Web SDK manifest-loader 描述：不得创建公开的 `src\client\route-manifest.ts`，不得向 `createWebPluginClient` 传入 packaged bytes，也不得实现会按 `entryId` 过滤后继续启动的 `loadAuthenticatedRouteManifest`。第 5/5 份执行时只能在可信 WPF/Host 内部生成、加载、整体验证和认证 manifest，再通过本计划的 construction boundary 注入；任何不匹配项使页面不导航。

### Host Core 冻结输出

`IMessageTransport` 位于 `MyTools.Host.Core.Transports`。本计划不得增加事件、`StartAsync` 或两参数 `SendAsync`：

```csharp
using MyTools.Protocol.V3;

namespace MyTools.Host.Core.Transports;

public enum TransportPriority { ControlOrResponse, Request, Event }

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

### Protocol 冻结命名

- C# 使用冻结 namespace `MyTools.Protocol.V3` 中的 `EndpointIdentity`、`MessageEnvelope`、`MessageKind`、`BusError`、`ProtocolErrorCodes`、`ProtocolJson.SerializerOptions` 和 `IRoutePayloadValidator`，不创建替代 wire 类型或 validator interface。
- 原始 JSON envelope 校验调用 Protocol Foundation 的 `MyTools.Protocol.Validation.ProtocolValidator.ValidateEnvelope(JsonElement)`；route payload 校验使用 Host Core 消费的 `IRoutePayloadValidator`。
- TypeScript package 目录是仓库根目录下的 `MyTools.Protocol.TypeScript`，package 名是 `@mytools/protocol`。
- TypeScript 必须从 `@mytools/protocol` 导入 `registerRouteManifest`、`validateEnvelope`、`validateRoutePayload`、`validateRouteResponsePayload` 和 `RouteManifest`，不得使用 `validateMessageEnvelope`、复制 validator 或实现 permissive fallback。

## 文件映射

### 新建

- `MyTools.Host.Transports\MyTools.Host.Transports.csproj`
- `MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj`
- `MyTools.Host.Transports\WebView2\IWebView2Channel.cs`
- `MyTools.Host.Transports\WebView2\WebViewEndpointBinding.cs`
- `MyTools.Host.Transports\WebView2\WebViewBootstrapScript.cs`
- `MyTools.Host.Transports\WebView2\WebViewInboundEnvelope.cs`
- `MyTools.Host.Transports\WebView2\WebViewRoutePolicy.cs`
- `MyTools.Host.Transports\WebView2\WebView2TransportOptions.cs`
- `MyTools.Host.Transports\WebView2\WebView2Transport.cs`
- `MyTools.Host.Transports\WebView2\WebViewPluginHost.cs`
- `MyTools.Host.Transports\WebView2\PluginWebOriginPolicy.cs`
- `MyTools.Host.Transports.Test\WebView2\WebViewInboundEnvelopeTests.cs`
- `MyTools.Host.Transports.Test\WebView2\WebView2TransportTests.cs`
- `MyTools.Host.Transports.Test\WebView2\WebViewPluginHostTests.cs`
- `MyTools.Host.Transports.Test\WebView2\WebViewBootstrapScriptTests.cs`
- `MyTools.Host.Transports.Test\WebView2\PluginWebOriginPolicyTests.cs`
- `MyTools.Plugins\Examples\common\src\client\transport.ts`
- `MyTools.Plugins\Examples\common\src\client\host-bootstrap.ts`
- `MyTools.Plugins\Examples\common\src\client\pending-calls.ts`
- `MyTools.Plugins\Examples\common\src\client\subscriptions.ts`
- `MyTools.Plugins\Examples\common\test\client.test.mjs`

### 修改

- `MyTools.sln`
- `MyTools.Plugins\Examples\common\package.json`
- `MyTools.Plugins\Examples\common\package-lock.json`
- `MyTools.Plugins\Examples\common\tsconfig.json`
- `MyTools.Plugins\Examples\common\src\client\index.ts`
- `MyTools.Plugins\Examples\common\src\shared\contracts.ts`
- `MyTools.Plugins\Examples\common\src\shared\events.ts`

### 本计划明确不修改

- `MyTools.Plugins\NodePlugins\NodePluginDetailContext.cs`
- `MyTools.Desktop\Components\NodePluginDetailView.xaml.cs`
- `MyTools.Desktop\DesktopServiceCollectionExtensions.cs`
- 任何 plugin manifest 或具体示例插件

## Task 0: Scaffold transport projects before the first test

**Files:**
- Create: `MyTools.Host.Transports\MyTools.Host.Transports.csproj`
- Create: `MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj`
- Modify: `MyTools.sln`

- [ ] **Step 1: Create the production project with real repository conventions**

```xml
<!-- MyTools.Host.Transports\MyTools.Host.Transports.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyTools.Host.Transports</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyTools.Protocol\MyTools.Protocol.csproj" />
    <ProjectReference Include="..\MyTools.Host.Core\MyTools.Host.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the NUnit test project**

```xml
<!-- MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyTools.Host.Transports.Test</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Moq" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyTools.Host.Transports\MyTools.Host.Transports.csproj" />
    <ProjectReference Include="..\MyTools.Protocol\MyTools.Protocol.csproj" />
    <ProjectReference Include="..\MyTools.Host.Core\MyTools.Host.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add both projects to the solution**

```powershell
dotnet sln .\MyTools.sln add .\MyTools.Host.Transports\MyTools.Host.Transports.csproj
dotnet sln .\MyTools.sln add .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj
```

Expected: both commands report that one project was added.

- [ ] **Step 4: Restore and build the empty scaffold**

```powershell
dotnet restore .\MyTools.sln
dotnet build .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --no-restore --nologo
```

Expected: restore and build exit 0 with no warnings or errors.

- [ ] **Step 5: Commit the scaffold**

```powershell
git add MyTools.sln MyTools.Host.Transports\MyTools.Host.Transports.csproj MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj
git commit -m "build: scaffold WebView transport projects" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 1: Bind untrusted WebView input to caller-supplied identity

**Files:**
- Create: `MyTools.Host.Transports\WebView2\IWebView2Channel.cs`
- Create: `MyTools.Host.Transports\WebView2\WebViewEndpointBinding.cs`
- Create: `MyTools.Host.Transports\WebView2\WebViewRoutePolicy.cs`
- Create: `MyTools.Host.Transports\WebView2\WebViewInboundEnvelope.cs`
- Create: `MyTools.Host.Transports.Test\WebView2\WebViewInboundEnvelopeTests.cs`

- [ ] **Step 1: Write failing identity and route tests**

```csharp
using MyTools.Host.Transports.WebView2;
using MyTools.Protocol.V3;

namespace MyTools.Host.Transports.Test.WebView2;

public sealed class WebViewInboundEnvelopeTests
{
    private static readonly EndpointIdentity Identity =
        new("settings", "main", "session-1", "webview-1");

    [Test]
    public async Task ParseAsync_rejects_a_different_session()
    {
        var json = TestEnvelope.Json(
            pluginId: "settings", entryId: "main",
            sessionId: "old-session", endpointId: "webview-1",
            route: "plugin.call.save");

        var result = await WebViewInboundEnvelope.ParseAsync(
            json, Identity, new FakeRoutePayloadValidator(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(WebViewInboundError.IdentityMismatch));
        Assert.That(result.Envelope, Is.Null);
    }

    [Test]
    public async Task ParseAsync_canonicalizes_plugin_entry_and_endpoint()
    {
        var json = TestEnvelope.Json(
            pluginId: "forged", entryId: "admin",
            sessionId: "session-1", endpointId: "node-main",
            route: "plugin.call.save");

        var result = await WebViewInboundEnvelope.ParseAsync(
            json, Identity, new FakeRoutePayloadValidator(), CancellationToken.None);

        Assert.That(result.Envelope, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Envelope!.PluginId, Is.EqualTo(Identity.PluginId));
            Assert.That(result.Envelope.EntryId, Is.EqualTo(Identity.EntryId));
            Assert.That(result.Envelope.SessionId, Is.EqualTo(Identity.SessionId));
            Assert.That(result.Envelope.EndpointId, Is.EqualTo(Identity.EndpointId));
        });
    }

    [TestCase("plugin.call.save", true)]
    [TestCase("bus.subscribe", true)]
    [TestCase("bus.unsubscribe", true)]
    [TestCase("bus.cancel", true)]
    [TestCase("host.call.configuration.read", true)]
    [TestCase("diagnostics.sessions.list", false)]
    public void Route_policy_is_explicit(string route, bool allowed) =>
        Assert.That(WebViewRoutePolicy.AllowsRequest(route), Is.EqualTo(allowed));
}
```

- [ ] **Step 2: Run the fixture and confirm red**

```powershell
dotnet test .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --filter "FullyQualifiedName~WebViewInboundEnvelopeTests" --no-restore
```

Expected: compilation fails because the WebView binding/parser types do not exist.

- [ ] **Step 3: Add the channel and immutable binding**

```csharp
// IWebView2Channel.cs
namespace MyTools.Host.Transports.WebView2;

public interface IWebView2Channel
{
    event EventHandler<string>? JsonReceived;
    event EventHandler? Closed;
    ValueTask InvokeOnUiAsync(Func<ValueTask> action, CancellationToken cancellationToken);
    ValueTask PostJsonAsync(string json, CancellationToken cancellationToken);
    ValueTask AddScriptToExecuteOnDocumentCreatedAsync(
        string initializationScript,
        CancellationToken cancellationToken);
    ValueTask NavigateAsync(Uri documentUri, CancellationToken cancellationToken);
}
```

The WPF WebView2 adapter maps these methods to `CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(initializationScript)` and `CoreWebView2.Navigate(documentUri.AbsoluteUri)`. `WebViewPluginHost.CreateAsync` owns their ordering. The adapter must not expose another navigation path that can set `Source` or execute page script before registration succeeds.

```csharp
// WebViewEndpointBinding.cs
using MyTools.Protocol.V3;

namespace MyTools.Host.Transports.WebView2;

public sealed record WebViewEndpointBinding(EndpointIdentity Identity, Uri Origin);
```

- [ ] **Step 4: Implement schema-first parse and route checks**

`WebViewInboundEnvelope.ParseAsync` must perform these operations in order:

```csharp
JsonDocument document;
try
{
    document = JsonDocument.Parse(json);
}
catch (JsonException)
{
    return new(null, WebViewInboundError.InvalidEnvelope);
}
using (document)
{
    var validation = MyTools.Protocol.Validation.ProtocolValidator
        .ValidateEnvelope(document.RootElement);
    if (!validation.IsValid)
        return new(null, WebViewInboundError.InvalidEnvelope);

    var incoming = document.RootElement.Deserialize<MessageEnvelope>(
        ProtocolJson.SerializerOptions);
    if (incoming is null)
        return new(null, WebViewInboundError.InvalidEnvelope);

    if (!StringComparer.Ordinal.Equals(incoming.SessionId, identity.SessionId))
        return new(null, WebViewInboundError.IdentityMismatch);
    if (incoming.Kind != MessageKind.Request ||
        !WebViewRoutePolicy.AllowsRequest(incoming.Route))
        return new(null, WebViewInboundError.RouteDenied);
    if (!routePayloadValidator.Validate(incoming.Route, incoming.Payload).IsValid)
        return new(null, WebViewInboundError.InvalidPayload);

    return new(incoming with
    {
        PluginId = identity.PluginId,
        EntryId = identity.EntryId,
        SessionId = identity.SessionId,
        EndpointId = identity.EndpointId
    }, null);
}
```

`WebViewRoutePolicy.AllowsRequest` permits only `plugin.call.*`, `host.call.*`, `bus.subscribe`, `bus.unsubscribe`, and `bus.cancel`. WebView `host.call.*` requests still pass through Host Core `CapabilityGateway` authorization and are not trusted merely because transport admitted them. Topic payloads for subscribe/unsubscribe additionally permit only `plugin.event.*` and `host.event.*`; malformed payload returns `InvalidPayload`, forbidden topic or `diagnostics.*` request returns `RouteDenied`.

- [ ] **Step 5: Run all parser and route cases**

```powershell
dotnet test .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --filter "FullyQualifiedName~WebViewInboundEnvelopeTests"
```

Expected: PASS for valid plugin/host calls, invalid JSON/schema, forged identity, forbidden `diagnostics.*`, malformed route payload, and forbidden topic.

- [ ] **Step 6: Commit identity binding**

```powershell
git add MyTools.Host.Transports\WebView2 MyTools.Host.Transports.Test\WebView2\WebViewInboundEnvelopeTests.cs
git commit -m "feat: bind WebView input identity" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Implement the exact pull-based transport contract

**Files:**
- Create: `MyTools.Host.Transports\WebView2\WebView2TransportOptions.cs`
- Create: `MyTools.Host.Transports\WebView2\WebView2Transport.cs`
- Create: `MyTools.Host.Transports.Test\WebView2\WebView2TransportTests.cs`

- [ ] **Step 1: Write failing contract, inbound, priority, and completion tests**

```csharp
[Test]
public void Implements_the_frozen_Host_Core_contract()
{
    Assert.That(typeof(IMessageTransport).IsAssignableFrom(typeof(WebView2Transport)), Is.True);
    Assert.That(typeof(WebView2Transport).GetMethod("StartAsync"), Is.Null);
    Assert.That(typeof(WebView2Transport).GetEvents(), Is.Empty);
}

[Test]
public async Task ReadAllAsync_yields_canonicalized_inbound_messages()
{
    var channel = new FakeWebView2Channel();
    await using var transport = CreateTransport(channel);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    var read = transport.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
    channel.Receive(TestEnvelope.Json(sessionId: "session-1", route: "plugin.call.save"));

    Assert.That(await read.MoveNextAsync(), Is.True);
    Assert.That(read.Current.EndpointId, Is.EqualTo(transport.Identity.EndpointId));
    await read.DisposeAsync();
}

[Test]
public async Task SendAsync_preserves_order_and_uses_the_supplied_priorities()
{
    var channel = new BlockingWebView2Channel();
    await using var transport = CreateTransport(channel, capacity: 4);

    await transport.SendAsync(Event("event-1"), TransportPriority.Event, CancellationToken.None);
    await transport.SendAsync(Response("response-1"), TransportPriority.ControlOrResponse, CancellationToken.None);
    channel.Release();
    await channel.WaitForPostsAsync(2);

    Assert.That(channel.Posted.Select(TestEnvelope.ReadId),
        Is.EqualTo(new[] { "response-1", "event-1" }));
}

[Test]
public async Task Closing_completes_ReadAllAsync_and_Completion_once()
{
    var channel = new FakeWebView2Channel();
    await using var transport = CreateTransport(channel);
    channel.Close();

    await transport.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.That(async () => await transport.SendAsync(
        Event("late"), TransportPriority.Event, CancellationToken.None),
        Throws.TypeOf<TransportDisconnectedException>());
}
```

- [ ] **Step 2: Run the fixture and confirm red**

```powershell
dotnet test .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --filter "FullyQualifiedName~WebView2TransportTests"
```

Expected: compilation fails because `WebView2Transport` is absent.

- [ ] **Step 3: Implement constructor-owned inbound lifetime**

The constructor subscribes to `JsonReceived` and `Closed`; there is no `StartAsync`. It owns:

```csharp
private readonly Channel<MessageEnvelope> inbound = Channel.CreateUnbounded<MessageEnvelope>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
private readonly TaskCompletionSource completion =
    new(TaskCreationOptions.RunContinuationsAsynchronously);

public EndpointIdentity Identity { get; }
public Task Completion => completion.Task;

public IAsyncEnumerable<MessageEnvelope> ReadAllAsync(CancellationToken cancellationToken) =>
    inbound.Reader.ReadAllAsync(cancellationToken);
```

`OnJsonReceived` UTF-8 counts before parsing, drops messages above `MaxMessageBytes`, awaits `WebViewInboundEnvelope.ParseAsync`, and writes only accepted envelopes to `inbound`. Use a serialized async gate so channel events cannot reorder accepted messages.

- [ ] **Step 4: Implement the exact send method and one priority writer**

```csharp
public ValueTask SendAsync(
    MessageEnvelope envelope,
    TransportPriority priority,
    CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    ThrowIfClosed();
    var json = JsonSerializer.Serialize(envelope, ProtocolJson.SerializerOptions);
    return EnqueueAsync(new Outbound(json, Encoding.UTF8.GetByteCount(json)), priority, cancellationToken);
}
```

Maintain bounded `ControlOrResponse`, `Request`, and `Event` channels. One writer loop checks them in that order and posts only through:

```csharp
await channel.InvokeOnUiAsync(
    () => channel.PostJsonAsync(message.Json, shutdown.Token),
    shutdown.Token).ConfigureAwait(false);
```

Never silently drop `ControlOrResponse`; if it cannot be retained under `MaxQueuedBytes`, close with `TransportDisconnectedException`. Apply the configured `DropNewest`, `DropOldest`, or `CoalesceByKey` policy only to `Event`. Never use `.Wait()`, `.Result`, `Dispatcher.Invoke`, or a lock across `await`.

- [ ] **Step 5: Implement one close/dispose path**

`Closed`, writer failure, and `DisposeAsync` all call one idempotent close method that unsubscribes channel handlers, cancels writer work, completes all outbound writers, completes `inbound`, completes `Completion`, and causes future sends to throw `TransportDisconnectedException`. `DisposeAsync` awaits the writer task and never raises an event.

- [ ] **Step 6: Run the full transport fixture**

```powershell
dotnet test .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --filter "FullyQualifiedName~WebView2TransportTests" --logger "console;verbosity=normal"
```

Expected: PASS, including 10,000 slow-consumer events; peak queued bytes remain bounded, responses are retained, `ReadAllAsync` ends, and `Completion` completes exactly once.

- [ ] **Step 7: Commit the transport**

```powershell
git add MyTools.Host.Transports\WebView2\WebView2TransportOptions.cs MyTools.Host.Transports\WebView2\WebView2Transport.cs MyTools.Host.Transports.Test\WebView2\WebView2TransportTests.cs
git commit -m "feat: add pull-based WebView transport" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Add origin security and a trusted bootstrap construction boundary

**Files:**
- Create: `MyTools.Host.Transports\WebView2\PluginWebOriginPolicy.cs`
- Create: `MyTools.Host.Transports\WebView2\WebViewBootstrapScript.cs`
- Create: `MyTools.Host.Transports\WebView2\WebViewPluginHost.cs`
- Create: `MyTools.Host.Transports.Test\WebView2\PluginWebOriginPolicyTests.cs`
- Create: `MyTools.Host.Transports.Test\WebView2\WebViewBootstrapScriptTests.cs`
- Create: `MyTools.Host.Transports.Test\WebView2\WebViewPluginHostTests.cs`

- [ ] **Step 1: Write failing origin and host construction tests**

```csharp
[Test]
public void Origin_is_stable_without_disclosing_the_install_path()
{
    var first = PluginWebOriginPolicy.Create("settings", "main", @"C:\plugins\settings");
    var moved = PluginWebOriginPolicy.Create("settings", "main", @"D:\installed\settings");

    Assert.That(first.Origin, Is.EqualTo(moved.Origin));
    Assert.That(first.Origin.Host,
        Does.Match("^settings-main-[a-f0-9]{16}\\.mytools\\.localhost$"));
}

[Test]
public void Resource_resolution_rejects_encoded_parent_traversal()
{
    var policy = PluginWebOriginPolicy.Create("settings", "main", @"C:\plugins\settings");
    var uri = new Uri(policy.Origin, "%2e%2e/secret.txt");
    Assert.That(policy.TryResolveResource(uri, out _), Is.False);
}

[Test]
public void Bootstrap_contains_the_frozen_identity_and_authenticated_manifest()
{
    var identity = new EndpointIdentity("settings", "main", "session-1", "webview-9");
    using var manifest = JsonDocument.Parse("""
        {
          "protocolVersion": "3.0",
          "routes": {
            "plugin.call.settings.preview": {
              "pluginId": "settings",
              "entryId": "main",
              "request": { "type": "object" },
              "response": { "type": "object" }
            }
          }
        }
        """);
    var script = WebViewBootstrapScript.Create(identity, manifest.RootElement);
    using var value = JsonDocument.Parse(ExtractFrozenValue(script));
    var propertyNames = value.RootElement.EnumerateObject()
        .Select(property => property.Name)
        .Order()
        .ToArray();

    Assert.Multiple(() =>
    {
        Assert.That(propertyNames, Is.EqualTo(new[]
        {
            "endpointId", "entryId", "pluginId", "routeManifest", "sessionId"
        }));
        Assert.That(value.RootElement.GetProperty("routeManifest")
            .GetProperty("routes")
            .GetProperty("plugin.call.settings.preview")
            .GetProperty("pluginId").GetString(), Is.EqualTo(identity.PluginId));
        Assert.That(script, Does.Contain("const deepFreeze ="));
        Assert.That(script, Does.Contain("value: deepFreeze("));
        Assert.That(script, Does.Contain("writable: false"));
        Assert.That(script, Does.Contain("configurable: false"));
    });
}

[Test]
public async Task Host_registers_bootstrap_before_navigation()
{
    var identity = new EndpointIdentity("settings", "main", "session-1", "webview-9");
    using var manifest = JsonDocument.Parse(
        """{ "protocolVersion": "3.0", "routes": {} }""");
    var channel = new FakeWebView2Channel();
    await using var host = await WebViewPluginHost.CreateAsync(
        identity, manifest.RootElement, @"C:\plugins\settings", channel,
        routePayloadValidator,
        cancellationToken: CancellationToken.None);

    Assert.Multiple(() =>
    {
        Assert.That(host.Transport.Identity, Is.EqualTo(identity));
        Assert.That(host.Binding.Identity, Is.EqualTo(identity));
        Assert.That(channel.InitializationScript,
            Is.EqualTo(WebViewBootstrapScript.Create(
                identity, manifest.RootElement)));
        Assert.That(channel.Operations,
            Is.EqualTo(new[] { "register-initialization-script", "navigate" }));
    });
}

private static string ExtractFrozenValue(string script)
{
    const string prefix = "value: deepFreeze(";
    var start = script.IndexOf(prefix, StringComparison.Ordinal);
    Assert.That(start, Is.GreaterThanOrEqualTo(0));
    start += prefix.Length;
    var end = script.IndexOf("),", start, StringComparison.Ordinal);
    Assert.That(end, Is.GreaterThan(start));
    return script[start..end];
}
```

`ExtractFrozenValue` parses only the JSON object passed to `deepFreeze`; it does not execute JavaScript. Add tests that an empty `{ protocolVersion: "3.0", routes: {} }` manifest is accepted for a canonical-only entry, and that a manifest route carrying another `pluginId`/`entryId` is rejected by the trusted WPF manifest loader before `WebViewPluginHost.CreateAsync` is called. `FakeWebView2Channel.AddScriptToExecuteOnDocumentCreatedAsync` stores `InitializationScript` and records registration; `NavigateAsync` records navigation and throws unless registration already completed.

- [ ] **Step 2: Run both fixtures and confirm red**

```powershell
dotnet test .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --filter "FullyQualifiedName~PluginWebOriginPolicyTests|FullyQualifiedName~WebViewPluginHostTests"
```

Expected: compilation fails because the origin policy, bootstrap script, and host do not exist.

- [ ] **Step 3: Implement deterministic origin and path containment**

Hash `pluginId + "\n" + entryId` with SHA-256 and use the first 16 lowercase hex characters. Sanitize labels to ASCII lowercase alphanumeric/hyphen. Normalize the root with `Path.GetFullPath`; decode the URI path once, combine and normalize it, then require the candidate to start with the normalized root plus separator using `StringComparison.OrdinalIgnoreCase`. Permit only HTTPS, the exact generated host, and port 443.

- [ ] **Step 4: Generate a non-replaceable document bootstrap**

```csharp
// WebViewBootstrapScript.cs
using System.Text.Json;
using MyTools.Protocol.V3;

namespace MyTools.Host.Transports.WebView2;

public static class WebViewBootstrapScript
{
    private const string PropertyName = "__MYTOOLS_WEBVIEW_BOOTSTRAP__";

    public static string Create(
        EndpointIdentity identity,
        JsonElement authenticatedRouteManifest)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (authenticatedRouteManifest.ValueKind is not JsonValueKind.Object)
            throw new ArgumentException(
                "Authenticated route manifest must be an object.",
                nameof(authenticatedRouteManifest));
        var json = JsonSerializer.Serialize(new
        {
            identity.PluginId,
            identity.EntryId,
            identity.SessionId,
            identity.EndpointId,
            RouteManifest = authenticatedRouteManifest
        }, ProtocolJson.SerializerOptions);

        return $$"""
            const deepFreeze = value => {
              if (value && typeof value === "object" && !Object.isFrozen(value)) {
                Object.values(value).forEach(deepFreeze);
                Object.freeze(value);
              }
              return value;
            };
            Object.defineProperty(globalThis, "{{PropertyName}}", {
              value: deepFreeze({{json}}),
              writable: false,
              configurable: false,
              enumerable: false
            });
            """;
    }
}
```

The injected value has exactly `pluginId`, `entryId`, `sessionId`, `endpointId`, and `routeManifest`; `deepFreeze` recursively freezes the manifest, every route record, and both schemas. `authenticatedRouteManifest` is not raw page or plugin input: the trusted WPF owner obtains it from the internal Protocol manifest-generation/loading pipeline only after schema compilation, canonical-route conflict checks, and `(pluginId, entryId)` authentication. A canonical-only entry passes the empty manifest and continues to use Protocol's built-in route map; canonical routes must not be copied into `routeManifest`. Do not add lifecycle counters, a manifest path, capabilities, filesystem paths, tokens, or mutable configuration. `System.Text.Json` escaping remains enabled so identity/schema text cannot break out of the script literal.

- [ ] **Step 5: Construct the transport, register bootstrap, then navigate**

```csharp
public sealed class WebViewPluginHost : IAsyncDisposable
{
    private WebViewPluginHost(
        WebViewEndpointBinding binding,
        WebView2Transport transport)
    {
        Binding = binding;
        Transport = transport;
    }

    public WebViewEndpointBinding Binding { get; }
    public WebView2Transport Transport { get; }

    public static async Task<WebViewPluginHost> CreateAsync(
        EndpointIdentity identity,
        JsonElement authenticatedRouteManifest,
        string pluginRoot,
        IWebView2Channel channel,
        IRoutePayloadValidator routePayloadValidator,
        WebView2TransportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var policy = PluginWebOriginPolicy.Create(
            identity.PluginId, identity.EntryId, pluginRoot);
        var binding = new WebViewEndpointBinding(identity, policy.Origin);
        var transport = new WebView2Transport(
            binding, channel, routePayloadValidator, options ?? new());
        try
        {
            await channel.AddScriptToExecuteOnDocumentCreatedAsync(
                WebViewBootstrapScript.Create(
                    identity, authenticatedRouteManifest),
                cancellationToken).ConfigureAwait(false);
            await channel.NavigateAsync(
                binding.Origin,
                cancellationToken).ConfigureAwait(false);
            return new(binding, transport);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync() => Transport.DisposeAsync();
}
```

`CreateAsync` is the only page construction path. Its second argument is the authenticated manifest associated with `identity`, never a value read from page JavaScript; the trusted caller must fail construction before navigation when generation/loading, schema compilation, canonical conflict detection, or identity authentication fails. There is no empty/accept-all fallback: only a valid explicitly empty manifest represents a canonical-only entry. The WPF adapter owns the underlying CoreWebView2 call and must keep manifest generation and its internal transport generation out of `WebViewEndpointBinding`, page messages, logs exposed to the page, and Web SDK options. Do not reference Desktop, `NodePluginDetailContext`, session accessor, endpoint registry, DI, or process APIs in this standalone project.

- [ ] **Step 6: Run security, bootstrap, and host tests**

```powershell
dotnet test .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --filter "FullyQualifiedName~PluginWebOriginPolicyTests|FullyQualifiedName~WebViewBootstrapScriptTests|FullyQualifiedName~WebViewPluginHostTests"
```

Expected: PASS for moved roots, encoded traversal, UNC/root boundaries, external origins, exact identity preservation, exact bootstrap keys, recursively frozen authenticated manifest, canonical-only empty manifest, cross-identity rejection before construction, registration-before-navigation, and initialization-failure disposal.

- [ ] **Step 7: Commit the standalone host**

```powershell
git add MyTools.Host.Transports\WebView2\PluginWebOriginPolicy.cs MyTools.Host.Transports\WebView2\WebViewBootstrapScript.cs MyTools.Host.Transports\WebView2\WebViewPluginHost.cs MyTools.Host.Transports.Test\WebView2
git commit -m "feat: bootstrap trusted WebView identity" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Consume the generated TypeScript protocol package

**Files:**
- Modify: `MyTools.Plugins\Examples\common\package.json`
- Modify: `MyTools.Plugins\Examples\common\package-lock.json`
- Modify: `MyTools.Plugins\Examples\common\tsconfig.json`
- Modify: `MyTools.Plugins\Examples\common\src\shared\contracts.ts`
- Modify: `MyTools.Plugins\Examples\common\src\shared\events.ts`
- Create: `MyTools.Plugins\Examples\common\test\client.test.mjs`

- [ ] **Step 1: Add a failing package-boundary test**

```javascript
import test from "node:test";
import assert from "node:assert/strict";
import { createWebPluginClient } from "../dist/client/index.js";

test("invalid envelopes are rejected before postMessage", async () => {
  const { client, webview } = createBootstrappedClient();
  await assert.rejects(
    client.call("plugin.call.save", { invalidForRoute: true }),
    error => error.code === "InvalidPayload"
  );
  assert.equal(webview.posts.length, 0);
});
```

- [ ] **Step 2: Add scripts and verify red**

```json
"scripts": {
  "clean": "node -e \"fs.rmSync('dist',{recursive:true,force:true})\"",
  "build": "npm run clean && tsc -p tsconfig.json",
  "check": "tsc -p tsconfig.json --noEmit",
  "test": "npm run build && node --test test/*.test.mjs"
}
```

```powershell
npm test --prefix .\MyTools.Plugins\Examples\common
```

Expected: FAIL because the v3 client is not exported.

- [ ] **Step 3: Install the repository package using the correct relative path**

From `MyTools.Plugins\Examples\common`, the repository-root package is three parent levels away:

```powershell
npm install --prefix .\MyTools.Plugins\Examples\common "@mytools/protocol@file:../../../MyTools.Protocol.TypeScript"
```

Expected: `package.json` and `package-lock.json` contain package name `@mytools/protocol` and path `../../../MyTools.Protocol.TypeScript`.

- [ ] **Step 4: Replace handwritten wire contracts with package exports**

```typescript
export type {
  EndpointIdentity,
  MessageEnvelope,
  MessageKind,
  BusError,
  SubscriptionPayload
} from "@mytools/protocol";

export {
  registerRouteManifest,
  validateEnvelope,
  validateRoutePayload,
  validateRouteResponsePayload
} from "@mytools/protocol";
export type { RouteManifest } from "@mytools/protocol";

export interface CallOptions {
  readonly timeoutMs?: number;
  readonly signal?: AbortSignal;
}

export interface EventMeta {
  readonly topic: string;
  readonly traceId: string;
}
```

Set `"strict": true` in `tsconfig.json`. Do not define local envelope/error/validation-result/route-manifest wire types. The per-plugin build continues to invoke Protocol Foundation's internal `generate-route-manifest.mjs` and package the resulting artifact for the trusted host loader; do not expose generation in the browser SDK and do not add a bus route to fetch or negotiate it.

- [ ] **Step 5: Run generated-artifact drift and TypeScript checks**

`scripts\verify-protocol-generated.ps1` has `param()` and accepts no arguments:

```powershell
pwsh -NoProfile -File .\scripts\verify-protocol-generated.ps1
npm run check --prefix .\MyTools.Plugins\Examples\common
```

Expected: both commands exit 0; the script regenerates C# and TypeScript outputs internally and reports no tracked drift.

- [ ] **Step 6: Commit generated-contract consumption**

```powershell
git add MyTools.Plugins\Examples\common\package.json MyTools.Plugins\Examples\common\package-lock.json MyTools.Plugins\Examples\common\tsconfig.json MyTools.Plugins\Examples\common\src\shared MyTools.Plugins\Examples\common\test
git commit -m "refactor: consume generated Web protocol" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: Implement Web SDK calls and cancellation

**Files:**
- Create: `MyTools.Plugins\Examples\common\src\client\host-bootstrap.ts`
- Create: `MyTools.Plugins\Examples\common\src\client\transport.ts`
- Create: `MyTools.Plugins\Examples\common\src\client\pending-calls.ts`
- Modify: `MyTools.Plugins\Examples\common\src\client\index.ts`
- Modify: `MyTools.Plugins\Examples\common\test\client.test.mjs`

- [ ] **Step 1: Add failing response, timeout, abort, and denial tests**

```javascript
test("correlated response resolves one call", async () => {
  const { client, webview } = createBootstrappedClient();
  const pending = client.call("plugin.call.read", { key: "theme" });
  webview.receive(responseFor(webview.posts.at(-1), { value: "dark" }));
  assert.deepEqual(await pending, { value: "dark" });
});

test("abort posts bus.cancel with the original correlation", async () => {
  const { client, webview } = createBootstrappedClient();
  const controller = new AbortController();
  const pending = client.call("plugin.call.slow", {}, { signal: controller.signal });
  const requestId = webview.posts.at(-1).id;
  controller.abort();
  await assert.rejects(pending, error => error.code === "Cancelled");
  assert.equal(webview.posts.at(-1).route, "bus.cancel");
  assert.equal(webview.posts.at(-1).correlationId, requestId);
});

test("diagnostics call is rejected before posting", async () => {
  const { client, webview } = createBootstrappedClient();
  await assert.rejects(
    client.call("diagnostics.sessions.list", {}),
    error => error.code === "CapabilityDenied"
  );
  assert.equal(webview.posts.length, 0);
});

test("authenticated dynamic route validates request and successful response", async () => {
  const { client, webview } = createBootstrappedClient({
    routeManifest: renderRouteManifest("settings", "main")
  });
  await assert.rejects(
    client.call("plugin.call.settings.render", {}),
    error => error.code === "InvalidPayload"
  );
  assert.equal(webview.posts.length, 0);

  const pending = client.call(
    "plugin.call.settings.render", { text: "hello" });
  webview.receive(responseFor(webview.posts.at(-1), { unexpected: true }));
  await assert.rejects(pending, error => error.code === "InvalidPayload");

  const valid = client.call(
    "plugin.call.settings.render", { text: "world" });
  webview.receive(responseFor(
    webview.posts.at(-1), { html: "<p>world</p>" }));
  assert.deepEqual(await valid, { html: "<p>world</p>" });
});

test("manifest cannot claim another plugin entry", () => {
  assert.throws(
    () => createBootstrappedClient({
      routeManifest: renderRouteManifest("other", "admin")
    }),
    /identity mismatch/
  );
});

test("malformed or canonical-conflicting manifest aborts construction", () => {
  assert.throws(
    () => createBootstrappedClient({
      routeManifest: {
        protocolVersion: "3.0",
        routes: { "plugin.call.settings.bad": {
          pluginId: "settings", entryId: "main",
          request: { type: "not-a-json-schema-type" },
          response: { type: "object" }
        } }
      }
    })
  );
  assert.throws(
    () => createBootstrappedClient({
      routeManifest: manifestClaimingCanonicalRoute("bus.subscribe")
    }),
    /Reserved|Duplicate/
  );
});

test("canonical routes need only an empty manifest", async () => {
  const { client, webview } = createBootstrappedClient({
    routeManifest: { protocolVersion: "3.0", routes: {} }
  });
  client.subscribe("host.event.theme-changed", () => {});
  await webview.flush();
  assert.equal(postsFor(webview, "bus.subscribe").length, 1);
});

function renderRouteManifest(pluginId, entryId) {
  return {
    protocolVersion: "3.0",
    routes: {
      "plugin.call.settings.render": {
        pluginId,
        entryId,
        request: {
          type: "object",
          additionalProperties: false,
          required: ["text"],
          properties: { text: { type: "string", minLength: 1 } }
        },
        response: {
          type: "object",
          additionalProperties: false,
          required: ["html"],
          properties: { html: { type: "string" } }
        }
      }
    }
  };
}

function manifestClaimingCanonicalRoute(route) {
  return {
    protocolVersion: "3.0",
    routes: {
      [route]: {
        pluginId: "settings",
        entryId: "main",
        request: { type: "object" },
        response: { type: "object" }
      }
    }
  };
}
```

- [ ] **Step 2: Run tests and confirm red**

```powershell
npm test --prefix .\MyTools.Plugins\Examples\common
```

Expected: FAIL because call correlation and cancellation are absent.

- [ ] **Step 3: Implement the WebView port and validation boundary**

```typescript
import {
  registerRouteManifest,
  validateEnvelope,
  validateRoutePayload,
  validateRouteResponsePayload,
  type MessageEnvelope,
  type RouteManifest
} from "@mytools/protocol";

export interface WebViewPort {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: MessageEvent<unknown>) => void): void;
  removeEventListener(type: "message", listener: (event: MessageEvent<unknown>) => void): void;
}
```

`host-bootstrap.ts` declares the injected value and rejects missing, extra, mutable, or non-string fields before creating the client:

```typescript
export type HostBootstrap = Readonly<{
  pluginId: string;
  entryId: string;
  sessionId: string;
  endpointId: string;
  routeManifest: RouteManifest;
}>;

declare global {
  var __MYTOOLS_WEBVIEW_BOOTSTRAP__: unknown;
}

export function readHostBootstrap(): HostBootstrap {
  const descriptor = Object.getOwnPropertyDescriptor(
    globalThis, "__MYTOOLS_WEBVIEW_BOOTSTRAP__");
  const value = descriptor?.value;
  if (!descriptor || descriptor.writable || descriptor.configurable ||
      !isDeepFrozen(value) || typeof value !== "object" || value === null) {
    throw new Error("Trusted WebView bootstrap is unavailable");
  }
  const keys = Object.keys(value).sort();
  const expected = [
    "endpointId", "entryId", "pluginId", "routeManifest", "sessionId"
  ];
  if (keys.length !== expected.length ||
      keys.some((key, index) => key !== expected[index]) ||
      ["endpointId", "entryId", "pluginId", "sessionId"].some(key =>
        typeof (value as Record<string, unknown>)[key] !== "string")) {
    throw new Error("Trusted WebView bootstrap is invalid");
  }
  return value as HostBootstrap;
}

function isDeepFrozen(value: unknown): boolean {
  if (typeof value !== "object" || value === null || !Object.isFrozen(value)) {
    return false;
  }
  return Object.values(value as Record<string, unknown>).every(
    child => typeof child !== "object" || child === null || isDeepFrozen(child));
}
```

`createWebPluginClient({ webview })` immediately calls `readHostBootstrap()`, captures it in a closure, and synchronously calls `registerRouteManifest(bootstrap.routeManifest, bootstrap.pluginId, bootstrap.entryId)` before installing the WebView message listener or returning the client. Any protocol-version mismatch, malformed schema, reserved/canonical duplicate, already-registered conflict, or route identity mismatch throws and aborts construction; do not catch it, install an accept-all validator, retry without the manifest, or silently drop conflicting routes. Its public options type contains only `webview`, default timeout, and clock/test hooks; it has no identity, manifest, or lifecycle field. Test helper `createBootstrappedClient` installs the same recursively frozen, non-writable five-field property in an isolated test realm before constructing the client, so tests exercise the production reader and a fresh Protocol registry without adding a public identity/manifest override.

Every outbound request envelope, including `bus.cancel`, `bus.subscribe`, and `bus.unsubscribe`, is stamped from that captured identity and must pass `validateRoutePayload(route, payload)` and `validateEnvelope(envelope)` before `postMessage`. Every inbound value must pass `validateEnvelope(event.data)` and match all four captured identity fields before correlation or dispatch. For a successful correlated response, look up the original request route stored with the pending call and require `validateRouteResponsePayload(originalRoute, response.payload).valid` before resolving; an error response validates its `BusError` through the envelope contract and does not run the success schema. Invalid successful responses reject that call with `InvalidPayload`. Host parsing still canonicalizes identity and validates independently; SDK checks are defense in depth, not the trust boundary.

- [ ] **Step 4: Implement pending call cleanup**

Store `{ route, resolve, reject, timeout, abortCleanup, traceId }` by request ID so successful responses are checked against the request route's response schema. Response, timeout, abort, and dispose all delete the entry before settling it. Timeout/abort sends validated `bus.cancel` with `correlationId` set to the original request ID. Unknown/duplicate correlation and envelopes for any other identity are ignored; calls are never automatically retried or transferred to a reconstructed page.

- [ ] **Step 5: Run call lifecycle tests**

```powershell
npm test --prefix .\MyTools.Plugins\Examples\common
npm run check --prefix .\MyTools.Plugins\Examples\common
```

Expected: PASS for exact recursively frozen bootstrap validation, registration-before-use, authenticated dynamic request/success-response schemas, canonical-only empty manifest, malformed/canonical-conflicting/cross-identity manifest rejection without fallback, success/error correlation, invalid payload, timeout, pre-aborted signal, response/abort race, unknown correlation, mismatched identity, disposal, permitted schema-valid `host.call.*`, and forbidden `diagnostics.*`.

- [ ] **Step 6: Commit call support**

```powershell
git add MyTools.Plugins\Examples\common\src\client MyTools.Plugins\Examples\common\test\client.test.mjs
git commit -m "feat: add Web SDK calls and cancellation" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 6: Add subscriptions and deterministic page reconstruction

**Files:**
- Create: `MyTools.Plugins\Examples\common\src\client\subscriptions.ts`
- Modify: `MyTools.Plugins\Examples\common\src\client\transport.ts`
- Modify: `MyTools.Plugins\Examples\common\src\client\index.ts`
- Modify: `MyTools.Plugins\Examples\common\test\client.test.mjs`

- [ ] **Step 1: Add failing reference-count and reconstruction tests**

```javascript
test("first listener subscribes once and last removal unsubscribes once", async () => {
  const { client, webview } = createBootstrappedClient();
  const removeA = client.subscribe("host.event.theme-changed", () => {});
  const removeB = client.subscribe("host.event.theme-changed", () => {});
  await webview.flush();
  assert.equal(postsFor(webview, "bus.subscribe").length, 1);
  removeA();
  assert.equal(postsFor(webview, "bus.unsubscribe").length, 0);
  removeB();
  assert.equal(postsFor(webview, "bus.unsubscribe").length, 1);
});

test("reconstructed page startup registers one fresh subscription per topic", async () => {
  const oldPage = createBootstrappedClient({
    sessionId: "session-1", endpointId: "webview-1"
  });
  oldPage.client.subscribe("plugin.event.progress", () => {});
  const pending = oldPage.client.call("plugin.call.slow", {});
  oldPage.client.dispose();
  await assert.rejects(pending, error => error.code === "TransportDisconnected");

  const newPage = createBootstrappedClient({
    sessionId: "session-2", endpointId: "webview-2"
  });
  newPage.client.subscribe("plugin.event.progress", () => {});
  await newPage.webview.flush();

  assert.equal(postsFor(oldPage.webview, "bus.subscribe").length, 1);
  assert.equal(postsFor(newPage.webview, "bus.subscribe").length, 1);
  assert.equal(newPage.webview.posts[0].sessionId, "session-2");
  assert.equal(newPage.webview.posts[0].endpointId, "webview-2");
});
```

Each `createBootstrappedClient` call above creates an isolated page realm. Its identity argument configures the test harness before construction; it is not part of the production `createWebPluginClient` API.

- [ ] **Step 2: Run tests and confirm red**

```powershell
npm test --prefix .\MyTools.Plugins\Examples\common
```

Expected: FAIL because subscription ownership and reconstruction cleanup are absent.

- [ ] **Step 3: Implement explicit topic state**

```typescript
type TopicState = {
  readonly listeners: Set<(payload: unknown, meta: EventMeta) => void>;
  subscribed: boolean;
  subscribeInFlight: Promise<void> | null;
};
```

Permit only `plugin.event.*` and `host.event.*`. `registerRouteManifest` has already completed before `subscribe` can be called. First listener sends one schema-validated `bus.subscribe`; last removal sends one schema-validated `bus.unsubscribe`. Validate each event with `validateRoutePayload(topic, payload)` against the canonical map or authenticated dynamic manifest before invoking a copied listener array. Client construction emits nothing until application startup registers a listener.

Page reconstruction is a destruction/construction boundary, not a wire handshake. Before replacing or navigating the WebView, the WPF owner disposes the old host; `pagehide`/SDK `dispose()` rejects every pending call with `TransportDisconnected`, detaches the message listener, marks the client closed, and clears topic state without trying to unsubscribe through a closing channel. The newly constructed host injects its new immutable identity and authenticated manifest before navigation. The new page then registers that manifest during client construction, runs normal application startup, and registers its desired listeners; the first listener for each topic sends exactly one fresh `bus.subscribe`. No call, listener closure, manifest registry, acknowledgement, or event crosses page instances, and the SDK has no restoration signal or lifecycle counter.

- [ ] **Step 4: Bind cleanup to the page lifetime**

```typescript
function bindPageLifetime(dispose: () => void): () => void {
  const onPageHide = (): void => dispose();
  globalThis.addEventListener("pagehide", onPageHide, { once: true });
  return () => globalThis.removeEventListener("pagehide", onPageHide);
}
```

Call `bindPageLifetime` exactly once during client construction. The idempotent `dispose()` invokes the returned removal function, removes the WebView message listener, rejects and deletes all pending calls, clears every topic state, and prevents all later send/call/subscribe operations. Add tests for explicit dispose, `pagehide`, duplicate dispose, a late response from the old page, and construction of a new isolated page identity.

- [ ] **Step 5: Run reconstruction and snapshot cases**

```powershell
npm test --prefix .\MyTools.Plugins\Examples\common
npm run check --prefix .\MyTools.Plugins\Examples\common
```

Expected: PASS; duplicate listeners share one subscription, immediate state snapshots are delivered as ordinary validated events, disposal rejects old calls, late old-page messages are ignored, and 20 isolated page reconstructions each produce exactly one subscription per topic after application re-registration.

- [ ] **Step 6: Commit subscriptions**

```powershell
git add MyTools.Plugins\Examples\common\src\client MyTools.Plugins\Examples\common\test\client.test.mjs
git commit -m "feat: rebuild Web SDK subscriptions safely" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 7: Final contract and scope audit

**Files:**
- Modify only if a preceding assertion fails: files owned by Tasks 0–6

- [ ] **Step 1: Prove obsolete transport members and signatures are absent**

```powershell
rg -n 'event\s+.*(MessageReceived|Disconnected)|StartAsync|SendAsync\(MessageEnvelope envelope,\s*CancellationToken' .\MyTools.Host.Transports .\MyTools.Host.Transports.Test
```

Expected: exit 1 with no matches.

- [ ] **Step 2: Prove migration-only Desktop identity work is absent**

```powershell
rg -n 'NodePluginDetailContext|NodePluginDetailView|DesktopServiceCollectionExtensions|EntryId' .\MyTools.Host.Transports .\MyTools.Host.Transports.Test
```

Expected: `EntryId` appears only as `EndpointIdentity.EntryId`/origin input; the three migration type names have no matches.

- [ ] **Step 3: Prove TypeScript path and validator names are exact**

```powershell
rg -n 'MyTools\.Protocol[/\\]TypeScript|validateMessageEnvelope|GenerateProtocol|ProtocolError\b|ProtocolJson\.Options' .\MyTools.Host.Transports .\MyTools.Host.Transports.Test .\MyTools.Plugins\Examples\common
rg -n '"@mytools/protocol"|registerRouteManifest|validateEnvelope|validateRoutePayload|validateRouteResponsePayload|MyTools\.Protocol\.TypeScript' .\MyTools.Plugins\Examples\common
```

Expected: first command exits 1; second command shows the package dependency and all four validator/registration imports from the expected package/directory.

Also verify the browser-facing source did not revive the stale public loader:

```powershell
rg -n 'loadAuthenticatedRouteManifest|src[/\\]client[/\\]route-manifest|packagedManifestBytes' .\MyTools.Plugins\Examples\common\src\client
```

Expected: exit 1 with no matches. Manifest bytes are consumed only by the trusted WPF/Host loader; the browser sees only the recursively frozen bootstrap value.

- [ ] **Step 4: Prove the wire route inventory and lifecycle boundary**

```powershell
$forbiddenRoute = 'bus.transport.' + 'ready'
rg -n --fixed-strings $forbiddenRoute .\MyTools.Host.Transports .\MyTools.Host.Transports.Test .\MyTools.Plugins\Examples\common\src .\MyTools.Plugins\Examples\common\test
if ($LASTEXITCODE -ne 1) { exit 1 }
rg -n '\bgeneration\b' .\MyTools.Plugins\Examples\common\src\client .\MyTools.Plugins\Examples\common\test
if ($LASTEXITCODE -ne 1) { exit 1 }
rg -n '"(bus\.subscribe|bus\.unsubscribe|bus\.cancel)"|plugin\.call\.' .\MyTools.Host.Transports .\MyTools.Host.Transports.Test .\MyTools.Plugins\Examples\common\src\client .\MyTools.Plugins\Examples\common\test
```

Expected: the first two searches have zero matches. The route inventory contains only `plugin.call.*`, authorized `host.call.*`, `bus.subscribe`, `bus.unsubscribe`, and `bus.cancel` as page-originated request routes; manifest delivery/registration is bootstrap-only, with no construction, manifest, readiness, reconnection, or lifecycle wire route. Host responses and allowed `plugin.event.*`/`host.event.*` delivery remain ordinary protocol envelopes.

- [ ] **Step 5: Prove bootstrap shape and public options are fixed**

```powershell
rg -n 'AddScriptToExecuteOnDocumentCreatedAsync|NavigateAsync|__MYTOOLS_WEBVIEW_BOOTSTRAP__|pluginId|entryId|sessionId|endpointId|routeManifest|registerRouteManifest|validateRouteResponsePayload' .\MyTools.Host.Transports .\MyTools.Host.Transports.Test .\MyTools.Plugins\Examples\common\src\client .\MyTools.Plugins\Examples\common\test
```

Expected: the search proves bootstrap registration-before-navigation, the same exact four immutable identity keys plus recursively frozen authenticated `routeManifest` on both sides, and Protocol registration before any call/subscription. Tests also prove production client options expose no identity/manifest override, cross-plugin/entry and conflicting/malformed manifests abort construction without fallback, canonical-only entries use an empty manifest rather than duplicate canonical routes, outgoing requests use request schemas, and incoming successful responses use the original request route's response schema.

- [ ] **Step 6: Run the generated-artifact gate with its actual zero-argument interface**

```powershell
pwsh -NoProfile -File .\scripts\verify-protocol-generated.ps1
```

Expected: exit 0 with no generated-file drift.

- [ ] **Step 7: Run all affected tests and build**

```powershell
dotnet test .\MyTools.Host.Transports.Test\MyTools.Host.Transports.Test.csproj --configuration Release --nologo
npm test --prefix .\MyTools.Plugins\Examples\common
npm run check --prefix .\MyTools.Plugins\Examples\common
dotnet build .\MyTools.sln --configuration Release --nologo
```

Expected: every command exits 0; transport stress tests finish within 10 seconds.

- [ ] **Step 8: Audit changed-file scope**

```powershell
git status --short
git diff --name-only
```

Expected: changes are limited to the projects/files listed in this plan. There are no changes to Desktop, `NodePluginDetailContext`, source plugin manifests, Named Pipe, Node SDK, or process lifecycle. Manifest generation remains in the internal Protocol/plugin build pipeline and Desktop wiring remains owned by the 第 5/5 份迁移计划; no new bus route was added.

- [ ] **Step 9: Commit final corrections**

```powershell
git add MyTools.sln MyTools.Host.Transports MyTools.Host.Transports.Test MyTools.Plugins\Examples\common
git commit -m "test: verify WebView transport and Web SDK" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

本计划完成的判据是：WebView transport 严格实现 Host Core 的 `Identity`、三参数 `SendAsync`、`ReadAllAsync`、`Completion`、`IAsyncDisposable`；WebView host 只从可信 WPF 调用方传入的 `EndpointIdentity` 与按该 identity 认证/验证的动态 route manifest 构造，并在页面导航前原子注册递归冻结两者的初始化脚本；Web SDK 不接受 identity/manifest override，在任何 call/subscription 前以捕获的 plugin/entry 调用 `@mytools/protocol` 的 `registerRouteManifest`，动态 outgoing request 与 incoming successful response 分别通过 request/response schema，且 malformed/conflicting/cross-identity manifest 直接终止构造而无 permissive fallback；canonical routes 只使用 Protocol 内置映射，不在 manifest 重复；页面重建以销毁旧 client、创建新 client、重新注册 manifest、由应用启动重新登记订阅完成，manifest generation 与宿主 lifecycle generation 不进入 SDK API 或 wire envelope；协议不新增任何 construction/manifest/readiness/reconnection route；所有 Desktop identity propagation 与迁移接线仍属于第 5/5 份计划。
