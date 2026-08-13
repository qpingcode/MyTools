# Plugin Message Bus Migration and E2E Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete plan 5/5 of the approved plugin-host message bus by migrating manifests, example plugins, host wiring, authorization/diagnostics surfaces, and end-to-end coverage onto the outputs of plans 1-4, then deleting the legacy stdin/stdout JSON-RPC path.

**Architecture:** This is a convergence and removal pass, not a new infrastructure layer. It consumes the frozen Protocol Foundation and Host Core contracts from plans 1-2 (`MyTools.Protocol.V3`, `IMessageTransport`, `IPluginSessionAccessor`, `IPluginSessionEndpointRegistry`, `INodeEndpointEvents`, `IWorkerRegistration`), the Windows named-pipe Node SDK from plan 3, and the WebView2 transport/Web SDK from plan 4. Plan 5 first supplies the manifest-model and Desktop integration deliberately deferred by plan 4, then moves every example plugin and the settings surface onto `pluginId + entryId + capability` metadata, proves WebView → Node → host → Node → WebView, and finally removes the old `NodePluginProcessHost`/stdio JSON-RPC/control-forwarding path.

**Tech Stack:** .NET 8, C# 12, WPF, NUnit 4, Microsoft.Web.WebView2, TypeScript 7, Node.js 22, npm workspaces, plan 1's `@mytools/protocol`, plan 3's `@mytools/plugin-sdk-node`, and plan 4's browser SDK exported from `MyTools.Plugins\Examples\common`.

---

## Scope and verified repository baseline

- This is **the fifth of five implementation plans (plan 5/5)**, not “phase 5.” The design specification has seven implementation steps; the five plans are delivery documents that map onto those steps as shown below.
- It must not rebuild protocol framing, Host Core routing, Windows named pipes, or the WebView transport itself. Plan 1 already owns the complete canonical shared-route inventory and the dynamic plugin-route manifest mechanism. Task 2 verifies those outputs without editing them, then declares only plugin-specific business routes in each example workspace and generates the packaged route manifest consumed by the authenticated Node and Web SDK sessions.
- Public wire types are the plan 1 generated types `MyTools.Protocol.V3.MessageEnvelope`, `MyTools.Protocol.V3.BusError`, and `MyTools.Protocol.V3.EndpointIdentity`; TypeScript must import wire types and validators from `@mytools/protocol`, never copy them into an example or common package.
- Use the Host Core plan’s **第 2/5 输出契约** as the sole transport boundary. In C# files, import `MyTools.Protocol.V3` and `MyTools.Host.Core.Transports`; do not create a second `IMessageTransport` or event-based adapter:

```csharp
using MyTools.Protocol.V3;

public enum TransportPriority { ControlOrResponse, Request, Event }
public sealed record TransportDisconnect(string Code, string Reason, Exception? Exception = null);
public interface IMessageTransport : IAsyncDisposable
{
    EndpointIdentity Identity { get; }
    ValueTask SendAsync(MessageEnvelope envelope, TransportPriority priority, CancellationToken cancellationToken);
    IAsyncEnumerable<MessageEnvelope> ReadAllAsync(CancellationToken cancellationToken);
    Task Completion { get; }
}
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
    ValueTask ConnectedAsync(EndpointIdentity identity, IMessageTransport transport, CancellationToken cancellationToken);
    ValueTask DisconnectedAsync(EndpointIdentity identity, TransportDisconnect disconnect, CancellationToken cancellationToken);
    ValueTask HeartbeatAsync(EndpointIdentity identity, TimeSpan roundTripTime, CancellationToken cancellationToken);
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

- The five-plan mapping to the specification's seven ordered implementation steps is:

| Spec step | Owning implementation plan/task |
| --- | --- |
| 1. Protocol, transport abstraction, and fake transport tests | Plan 1 protocol output plus Plan 2 Task 1 transport/fakes |
| 2. Message bus, capability gateway, and session state machine | Plan 2 |
| 3. Windows named pipe, Node SDK, and process control | Plan 3 |
| 4. WebView2 unified transport | Plan 4 |
| 5. Manifest, Node SDK, and example migration | This plan, Tasks 1-3 |
| 6. Remove stdio JSON-RPC and control forwarding from concrete controls | This plan, Tasks 4 and 6 |
| 7. Security, recovery, and end-to-end verification | This plan, Tasks 4-5 and final Task 6 checks |

- Plan 4 intentionally deferred `NodePluginDetailContext.EntryId` and final Desktop composition ownership. Task 1 must add and test `EntryId` before Tasks 3-5 consume it; Task 4 owns the Desktop adapter/DI/window integration and must make it green before Task 5 starts.
- The repository currently still contains the legacy Node path (`MyTools.Plugins\NodePlugins\NodePluginProcessHost.cs`, `HostCallProtocol.cs`, `NodePluginProtocol.cs`) and the legacy browser helper (`MyTools.Plugins\Examples\common\src\client\index.ts`). This plan is responsible for deleting or replacing those only after the new paths are fully wired and tested.
- Example workspaces already present and must all be migrated: `hello-search`, `deepseek-chat`, `deepseek-translator`, `json-formatter`, `xml-formatter`, and `settings`.

## File structure map

### Created

- `MyTools.Plugins.Test\NodePlugins\NodePluginManifestMigrationTest.cs` — manifest identity/capability regression coverage.
- `MyTools.Desktop.Test\Services\SettingsPluginHostCallHandlerTests.cs` — authorization lookup/revoke and diagnostics host-call coverage.
- `MyTools.Desktop.Test\Components\NodePluginWebViewHostTests.cs` — entry identity, endpoint registration, and session replacement coverage for the deferred Desktop composition.
- `MyTools.Desktop.Test\Integration\PluginMessageBusMigrationE2ETests.cs` — full WebView → Node → host → Node → WebView and failure-mode coverage.
- `MyTools.Plugins\Examples\settings\test\permissions-panel.test.mjs` — settings authorization UI smoke test.
- `MyTools.Plugins\Examples\test\route-manifests.test.mjs` — canonical-route boundary, generated artifact, package, identity, and Node/Web manifest parity coverage.
- `MyTools.Plugins\Examples\hello-search\routes.json` — `hello-search:hello` plugin-specific route schemas.
- `MyTools.Plugins\Examples\deepseek-chat\routes.json` — `deepseek-chat:chat` plugin-specific route schemas.
- `MyTools.Plugins\Examples\deepseek-translator\routes.json` — `translator` and `ankicard` plugin-specific route schemas.
- `MyTools.Plugins\Examples\json-formatter\routes.json` — empty plugin-specific route declaration for `json-formatter:json-formatter`.
- `MyTools.Plugins\Examples\xml-formatter\routes.json` — empty plugin-specific route declaration for `xml-formatter:xml-formatter`.
- `MyTools.Plugins\Examples\settings\routes.json` — empty plugin-specific route declaration for `settings:main`; all settings routes are canonical.
- `MyTools.Plugins\Examples\settings\src\web\permissions-panel.ts` — capability grant rendering and revoke actions.
- `MyTools.Desktop\Services\SettingsConfigurationCapabilityHandlers.cs` — focused production `configuration.read`/`configuration.write` Host Core handlers.
- `MyTools.Desktop\Components\NodePluginWebViewHost.cs` — Desktop owner that binds a detail context to the current Host Core session and WebView transport.

### Modified

- `MyTools.Plugins\NodePlugins\NodePluginManifest.cs`
- `MyTools.Plugins\NodePlugins\NodePluginCatalog.cs`
- `MyTools.Plugins\NodePlugins\NodePluginFactory.cs`
- `MyTools.Plugins\NodePlugins\NodePlugin.cs`
- `MyTools.Plugins\NodePlugins\NodePluginDetailContext.cs`
- `MyTools.Plugins.Test\NodePlugins\NodePluginCatalogTest.cs`
- `MyTools.Plugins.Test\NodePlugins\NodePluginKeywordRouteTest.cs`
- `MyTools.Plugins.Test\NodePlugins\NodePluginLocalizationTest.cs`
- `MyTools.Plugins\Examples\common\package.json`
- `MyTools.Plugins\Examples\common\package-lock.json`
- `MyTools.Plugins\Examples\common\tsconfig.json`
- `MyTools.Plugins\Examples\common\src\client\index.ts`
- `MyTools.Plugins\Examples\common\src\shared\contracts.ts`
- `MyTools.Plugins\Examples\common\src\shared\events.ts`
- `MyTools.Plugins\Examples\common\test\client.test.mjs`
- `MyTools.PluginSdk.Node\src\route-manifest.ts`
- `MyTools.PluginSdk.Node\test\client.test.ts`
- `MyTools.Plugins\Examples\common\src\client\route-manifest.ts`
- `MyTools.Plugins\Examples\settings\plugin.json`
- `MyTools.Plugins\Examples\settings\src\backend\index.mts`
- `MyTools.Plugins\Examples\settings\src\web\main.ts`
- `MyTools.Plugins\Examples\settings\src\web\types.ts`
- `MyTools.Plugins\Examples\settings\src\web\common.ts`
- `MyTools.Plugins\Examples\deepseek-chat\plugin.json`
- `MyTools.Plugins\Examples\deepseek-chat\src\backend\index.mts`
- `MyTools.Plugins\Examples\deepseek-chat\src\web\main.ts`
- `MyTools.Plugins\Examples\deepseek-translator\plugin.json`
- `MyTools.Plugins\Examples\deepseek-translator\src\backend\Translator\index.mts`
- `MyTools.Plugins\Examples\deepseek-translator\src\backend\AnkiCard\index.mts`
- `MyTools.Plugins\Examples\deepseek-translator\src\web\Translator\main.ts`
- `MyTools.Plugins\Examples\deepseek-translator\src\web\AnkiCard\main.ts`
- `MyTools.Plugins\Examples\hello-search\plugin.json`
- `MyTools.Plugins\Examples\hello-search\src\backend\index.mts`
- `MyTools.Plugins\Examples\hello-search\src\web\main.ts`
- `MyTools.Plugins\Examples\json-formatter\plugin.json`
- `MyTools.Plugins\Examples\json-formatter\src\backend\index.mts`
- `MyTools.Plugins\Examples\json-formatter\src\web\main.ts`
- `MyTools.Plugins\Examples\xml-formatter\plugin.json`
- `MyTools.Plugins\Examples\xml-formatter\src\backend\index.mts`
- `MyTools.Plugins\Examples\xml-formatter\src\web\main.ts`
- `MyTools.Desktop\DesktopServiceCollectionExtensions.cs`
- `MyTools.Desktop\AppBootstrapper.cs`
- `MyTools.Desktop\Services\CapabilityGrantStore.cs`
- `MyTools.Desktop\Services\PluginWindowManager.cs`
- `MyTools.Desktop\Services\SettingsPluginHostCallHandler.cs`
- `MyTools.Desktop\Components\NodePluginDetailViewModel.cs`
- `MyTools.Desktop\Components\NodePluginDetailView.xaml.cs`
- `MyTools.Desktop\Views\PluginWindow.xaml.cs`
- `MyTools.Plugins.Test\NodePlugins\NodePluginCatalogTest.cs`
- `MyTools.Plugins.Test\NodePlugins\NodePluginKeywordRouteTest.cs`
- `MyTools.Plugins.Test\NodePlugins\NodePluginLocalizationTest.cs`

### Deleted

- `MyTools.Plugins\NodePlugins\NodePluginProcessHost.cs`
- `MyTools.Plugins\NodePlugins\HostCallProtocol.cs`
- `MyTools.Plugins\NodePlugins\NodePluginProtocol.cs` only if the final unused-symbol scan proves it is dead after migration.

## Task 1: Migrate manifest identity and capability metadata

**Files:**
- Modify: `MyTools.Plugins\NodePlugins\NodePluginManifest.cs`
- Modify: `MyTools.Plugins\NodePlugins\NodePluginCatalog.cs`
- Modify: `MyTools.Plugins\NodePlugins\NodePluginFactory.cs`
- Modify: `MyTools.Plugins\NodePlugins\NodePlugin.cs`
- Modify: `MyTools.Plugins\NodePlugins\NodePluginDetailContext.cs`
- Modify: `MyTools.Plugins.Test\NodePlugins\NodePluginCatalogTest.cs`
- Modify: `MyTools.Plugins.Test\NodePlugins\NodePluginKeywordRouteTest.cs`
- Modify: `MyTools.Plugins.Test\NodePlugins\NodePluginLocalizationTest.cs`
- Create: `MyTools.Plugins.Test\NodePlugins\NodePluginManifestMigrationTest.cs`

- [ ] **Step 1: Write the failing manifest migration tests**

```csharp
[TestFixture]
public class NodePluginManifestMigrationTest
{
    [Test]
    public void Reload_ShouldLoadPluginIdEntryIdAndCapabilitiesFromNewManifest()
    {
        var pluginPath = Path.Combine(rootPath, "settings");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "pluginId": "settings",
          "name": "Settings",
          "version": "0.0.6",
          "runtime": "node",
          "entries": [
            {
              "entryId": "main",
              "entry": "backend/index.mjs",
              "capabilities": [
                "configuration.read",
                "configuration.write",
                "keymap.read",
                "keymap.write",
                "gesture.read",
                "gesture.write",
                "hotkey.control",
                "application.restart",
                "authorization.read",
                "authorization.revoke",
                "diagnostics.read"
              ],
              "detail": { "type": "web", "entry": "web/index.html" }
            }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(pluginPath, "backend", "index.mjs"), "console.log('ok');");
        File.WriteAllText(Path.Combine(pluginPath, "web", "index.html"), "<html></html>");

        var plugin = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload().Single();

        Assert.That(plugin.PluginId, Is.EqualTo("settings"));
        Assert.That(plugin.EntryId, Is.EqualTo("main"));
        var context = plugin.CreateHotKeyDetailContext();
        Assert.That(context, Is.Not.Null);
        Assert.That(context!.EntryId, Is.EqualTo("main"));
        Assert.That(plugin.Capabilities, Is.EquivalentTo(new[]
        {
            "configuration.read",
            "configuration.write",
            "keymap.read",
            "keymap.write",
            "gesture.read",
            "gesture.write",
            "hotkey.control",
            "application.restart",
            "authorization.read",
            "authorization.revoke",
            "diagnostics.read"
        }));
    }

    [Test]
    public void Reload_ShouldRejectLegacyManifestWithoutPluginIdAndEntryId()
    {
        var pluginPath = Path.Combine(rootPath, "legacy-search");
        Directory.CreateDirectory(Path.Combine(pluginPath, "backend"));
        Directory.CreateDirectory(Path.Combine(pluginPath, "web"));
        File.WriteAllText(Path.Combine(pluginPath, "plugin.json"), """
        {
          "id": "legacy-search",
          "name": "Legacy Search",
          "version": "0.2.0",
          "runtime": "node",
          "protocolVersion": "2.0",
          "entries": [
            {
              "id": "hello",
              "entry": "backend/index.mjs",
              "detail": { "type": "web", "entry": "web/index.html" }
            }
          ]
        }
        """);

        var plugins = new NodePluginCatalog(rootPath, NullLogger<NodePluginCatalog>.Instance).Reload();

        Assert.That(plugins, Is.Empty);
    }
}
```

- [ ] **Step 2: Run the focused manifest test and confirm the red bar**

Run:

```powershell
dotnet test .\MyTools.Plugins.Test\MyTools.Plugins.Test.csproj --filter "FullyQualifiedName~NodePluginManifestMigrationTest|FullyQualifiedName~NodePluginCatalogTest|FullyQualifiedName~NodePluginKeywordRouteTest|FullyQualifiedName~NodePluginLocalizationTest"
```

Expected: FAIL to compile or FAIL assertions because `PluginId`, `EntryId`, and `Capabilities` are not yet wired into the manifest/catalog/runtime model.

- [ ] **Step 3: Implement the new manifest shape and propagate identity through the runtime model**

```csharp
public sealed class NodePluginManifest
{
    public string PluginId { get; init; } = string.Empty;
    public string EntryId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public string QualifiedId => $"{PluginId}:{EntryId}";
}
```

`NodePluginCatalog` should deserialize `pluginId`, `entries[*].entryId`, and `entries[*].capabilities`, reject manifests that still only declare the old `id`/`entry` shape, and set each `NodePluginManifest`’s `PluginId`, `EntryId`, and `Capabilities` from the new JSON. `NodePlugin` should expose `PluginId`, `EntryId`, and `Capabilities` directly, keep `Id` as the combined `pluginId:entryId` key only long enough for migration, and update `CreateHotKeyDetailContext()` / `CreateKeywordDetailContext()` to populate the new `EntryId` field. `NodePluginKeywordRouteTest` and `NodePluginLocalizationTest` should assert `PluginId == "deepseek-translator"`, `EntryId == "translator"`, and no longer assume the entry-qualified ID is the package ID.

This is the prerequisite that plan 4 deliberately left to plan 5. Add this property beside `PluginId` before any Desktop/WebView code reads it:

```csharp
public required string EntryId { get; init; }
```

In `NodePlugin.CreateDetailContext`, add this initializer beside `PluginId`; the value comes only from the parsed manifest entry, never from `ItemId`, keyword, HTML path, or window identity:

```csharp
EntryId = manifest.EntryId,
```

- [ ] **Step 4: Run the manifest and identity tests until they pass**

Run:

```powershell
dotnet test .\MyTools.Plugins.Test\MyTools.Plugins.Test.csproj --filter "FullyQualifiedName~NodePluginManifestMigrationTest|FullyQualifiedName~NodePluginCatalogTest|FullyQualifiedName~NodePluginKeywordRouteTest|FullyQualifiedName~NodePluginLocalizationTest"
```

Expected: PASS; new manifests parse, legacy manifests fail closed, and the runtime model exposes package ID, entry ID, and capabilities separately.

- [ ] **Step 5: Commit the manifest migration**

```powershell
git add MyTools.Plugins\NodePlugins\NodePluginManifest.cs MyTools.Plugins\NodePlugins\NodePluginCatalog.cs MyTools.Plugins\NodePlugins\NodePluginFactory.cs MyTools.Plugins\NodePlugins\NodePlugin.cs MyTools.Plugins\NodePlugins\NodePluginDetailContext.cs MyTools.Plugins.Test\NodePlugins\NodePluginCatalogTest.cs MyTools.Plugins.Test\NodePlugins\NodePluginKeywordRouteTest.cs MyTools.Plugins.Test\NodePlugins\NodePluginLocalizationTest.cs MyTools.Plugins.Test\NodePlugins\NodePluginManifestMigrationTest.cs
git commit -m "feat: migrate plugin manifest identity model" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 2: Migrate example Node backends to the plan-3 SDK

**Files:**
- Create: `MyTools.Plugins\Examples\test\route-manifests.test.mjs`
- Create: `MyTools.Plugins\Examples\hello-search\routes.json`
- Create: `MyTools.Plugins\Examples\deepseek-chat\routes.json`
- Create: `MyTools.Plugins\Examples\deepseek-translator\routes.json`
- Create: `MyTools.Plugins\Examples\json-formatter\routes.json`
- Create: `MyTools.Plugins\Examples\xml-formatter\routes.json`
- Create: `MyTools.Plugins\Examples\settings\routes.json`
- Create: `MyTools.PluginSdk.Node\src\route-manifest.ts`
- Create: `MyTools.Plugins\Examples\common\src\client\route-manifest.ts`
- Modify: `MyTools.PluginSdk.Node\src\index.ts`
- Modify: `MyTools.PluginSdk.Node\test\client.test.ts`
- Modify: `MyTools.Plugins\Examples\common\package.json`
- Modify: `MyTools.Plugins\Examples\common\package-lock.json`
- Modify: `MyTools.Plugins\Examples\common\tsconfig.json`
- Modify: `MyTools.Plugins\Examples\common\src\server\index.mts`
- Modify: `MyTools.Plugins\Examples\common\src\client\index.ts`
- Modify: `MyTools.Plugins\Examples\common\test\client.test.mjs`
- Modify: `MyTools.Plugins\Examples\settings\package.json`
- Modify: `MyTools.Plugins\Examples\settings\plugin.json`
- Modify: `MyTools.Plugins\Examples\settings\src\backend\index.mts`
- Modify: `MyTools.Plugins\Examples\deepseek-chat\package.json`
- Modify: `MyTools.Plugins\Examples\deepseek-chat\plugin.json`
- Modify: `MyTools.Plugins\Examples\deepseek-chat\src\backend\index.mts`
- Modify: `MyTools.Plugins\Examples\deepseek-translator\package.json`
- Modify: `MyTools.Plugins\Examples\deepseek-translator\plugin.json`
- Modify: `MyTools.Plugins\Examples\deepseek-translator\src\backend\Translator\index.mts`
- Modify: `MyTools.Plugins\Examples\deepseek-translator\src\backend\AnkiCard\index.mts`
- Modify: `MyTools.Plugins\Examples\hello-search\package.json`
- Modify: `MyTools.Plugins\Examples\hello-search\plugin.json`
- Modify: `MyTools.Plugins\Examples\hello-search\src\backend\index.mts`
- Modify: `MyTools.Plugins\Examples\json-formatter\package.json`
- Modify: `MyTools.Plugins\Examples\json-formatter\plugin.json`
- Modify: `MyTools.Plugins\Examples\json-formatter\src\backend\index.mts`
- Modify: `MyTools.Plugins\Examples\xml-formatter\package.json`
- Modify: `MyTools.Plugins\Examples\xml-formatter\plugin.json`
- Modify: `MyTools.Plugins\Examples\xml-formatter\src\backend\index.mts`
- Create: `MyTools.Plugins\Examples\settings\test\backend-registration.test.mjs`

- [ ] **Step 1: Write failing canonical-boundary, route-manifest, SDK parity, and backend registration tests**

Create `MyTools.Plugins\Examples\test\route-manifests.test.mjs`. It must read, but never write, plan 1's schema; assert every canonical route consumed by this plan exists; assert no workspace redeclares any canonical route; invoke the shipped Protocol generator for every workspace; and prove the Node and Web SDK loaders select byte-identical schemas for the authenticated entry:

```javascript
import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { fileURLToPath, pathToFileURL } from "node:url";
import path from "node:path";
import { loadAuthenticatedRouteManifest as loadNodeManifest } from "../../../MyTools.PluginSdk.Node/dist/src/route-manifest.js";
import { loadAuthenticatedRouteManifest as loadWebManifest } from "../common/dist/client/route-manifest.js";

const examples = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const root = path.resolve(examples, "..", "..");
const protocolSchema = JSON.parse(await readFile(
  path.join(root, "protocol", "schemas", "v3", "protocol.schema.json"), "utf8"));
const canonical = new Set(Object.keys(protocolSchema["x-routePayloadSchemas"]));
const requiredCanonical = [
  "plugin.call.initialize", "plugin.call.search",
  "plugin.call.getConfiguration", "plugin.call.saveConfiguration",
  "plugin.call.getKeymap", "plugin.call.saveKeymap", "plugin.call.validateKeymap",
  "plugin.call.getGestures", "plugin.call.saveGestures",
  "plugin.call.suspendGestures", "plugin.call.resumeGestures",
  "plugin.call.suspendHotkeys", "plugin.call.resumeHotkeys",
  "plugin.call.restart", "plugin.call.getAuthorizations",
  "plugin.call.revokeAuthorization", "plugin.call.getDiagnostics",
  "host.call.configuration.read", "host.call.configuration.write",
  "host.call.keymap.read", "host.call.keymap.write", "host.call.keymap.validate",
  "host.call.gesture.read", "host.call.gesture.write",
  "host.call.gesture.suspend", "host.call.gesture.resume",
  "host.call.hotkey.suspend", "host.call.hotkey.resume",
  "host.call.application.restart", "host.call.authorization.list",
  "host.call.authorization.revoke", "host.call.diagnostics.read"
];
const expectedDynamic = {
  "hello-search": ["plugin.call.refresh"],
  "deepseek-chat": ["plugin.call.newChat", "plugin.call.poll", "plugin.call.send"],
  "deepseek-translator": [
    "plugin.call.browse", "plugin.call.deleteCard", "plugin.call.favorite",
    "plugin.call.getFavorites", "plugin.call.getHistory", "plugin.call.load",
    "plugin.call.review", "plugin.call.saveCard", "plugin.call.setExpanded",
    "plugin.call.setSendMode", "plugin.call.translate"
  ],
  "json-formatter": [],
  "xml-formatter": [],
  "settings": []
};

test("canonical routes stay in Protocol and plugin manifests contain only business routes", async () => {
  assert.deepEqual(requiredCanonical.filter((route) => !canonical.has(route)), []);
  for (const [workspace, expected] of Object.entries(expectedDynamic)) {
    const source = JSON.parse(await readFile(path.join(examples, workspace, "routes.json"), "utf8"));
    const actual = source.flatMap((entry) => Object.keys(entry.routes)).sort();
    assert.deepEqual(actual, expected);
    assert.deepEqual(actual.filter((route) => canonical.has(route)), []);
  }
});

test("Protocol generator packages deterministic manifests loaded equally by authenticated SDKs", async () => {
  for (const workspace of Object.keys(expectedDynamic)) {
    const cwd = path.join(examples, workspace);
    const generated = spawnSync(process.execPath, [
      path.join(root, "MyTools.Protocol.TypeScript", "scripts", "generate-route-manifest.mjs"),
      path.join(cwd, "routes.json"),
      path.join(cwd, "dist", "route-manifest.json")
    ], { encoding: "utf8" });
    assert.equal(generated.status, 0, generated.stderr);
  }

  const identity = { pluginId: "hello-search", entryId: "hello" };
  const artifact = path.join(examples, "hello-search", "dist", "route-manifest.json");
  const bytes = await readFile(artifact);
  const nodeManifest = await loadNodeManifest(pathToFileURL(artifact), identity);
  const webManifest = await loadWebManifest(new Response(bytes), identity);
  assert.deepEqual(webManifest, nodeManifest);
  assert.deepEqual(Object.keys(nodeManifest.routes), ["plugin.call.refresh"]);
  await assert.rejects(
    loadWebManifest(new Response(bytes), { pluginId: "hello-search", entryId: "wrong" }),
    /identity mismatch/
  );
});
```

`loadAuthenticatedRouteManifest` in both SDKs must parse the artifact, reject a `protocolVersion` other than `3.0`, reject every route whose `pluginId` differs from the authenticated plugin, select only routes whose `entryId` equals the authenticated entry, and call plan 1's `registerRouteManifest` before the SDK sends or handles application traffic. The Node loader accepts a `URL`; the Web loader accepts a `Response`. Production `connectPlugin()` derives the URL from the authenticated plugin root, while `createWebPluginClient()` receives the same packaged bytes from the host-owned WebView bootstrap path. Neither public API accepts caller-supplied identity or caller-supplied schemas.

Also create the existing settings registration test:

```javascript
import test from "node:test";
import assert from "node:assert/strict";
import { registerSettingsPlugin } from "../dist/backend/index.mjs";

function fakeTool() {
  const routes = [];
  const handlers = new Map();
  const unsubscribed = [];
  return {
    routes,
    handlers,
    unsubscribed,
    handle(route, handler) {
      routes.push(route);
      handlers.set(route, handler);
      return () => {
        handlers.delete(route);
        unsubscribed.push(route);
      };
    },
    call: async () => ({})
  };
}

test("settings backend registers the new capability-gated routes", () => {
  const client = fakeTool();
  const unsubscribe = registerSettingsPlugin(client);

  assert.deepEqual(client.routes, [
    "plugin.call.initialize",
    "plugin.call.search",
    "plugin.call.getConfiguration",
    "plugin.call.saveConfiguration",
    "plugin.call.getKeymap",
    "plugin.call.saveKeymap",
    "plugin.call.validateKeymap",
    "plugin.call.getGestures",
    "plugin.call.saveGestures",
    "plugin.call.suspendGestures",
    "plugin.call.resumeGestures",
    "plugin.call.suspendHotkeys",
    "plugin.call.resumeHotkeys",
    "plugin.call.restart",
    "plugin.call.getAuthorizations",
    "plugin.call.revokeAuthorization",
    "plugin.call.getDiagnostics"
  ]);

  unsubscribe();
  assert.deepEqual(client.unsubscribed, [...client.routes].reverse());
});
```

- [ ] **Step 2: Run the example workspace checks and confirm the red bar**

Run:

```powershell
Push-Location .\MyTools.Plugins\Examples
npm ci
node --test .\test\route-manifests.test.mjs
Pop-Location
Push-Location .\MyTools.Plugins\Examples\settings
npm run build
node --test .\test\backend-registration.test.mjs
Pop-Location
```

Expected: FAIL because `routes.json`, generated `dist\route-manifest.json`, the authenticated Node/Web manifest loaders, `registerSettingsPlugin`, and the new SDK entrypoint are not present yet, while the backend still imports the legacy JSON-RPC helper. The canonical-route assertion must already pass against plan 1's untouched schema.

- [ ] **Step 3: Refactor every example backend to a `registerXPlugin` entrypoint plus `connectPlugin()` bootstrapping**

Do not edit `protocol\schemas\v3\protocol.schema.json`, `MyTools.Protocol\Generated\V3`, `MyTools.Protocol.TypeScript\src\generated`, or any plan-1 test/generator output. The shared routes below are already canonical. Task 2 only verifies their presence and uses them directly:

| Plugin route | Node action / host route |
| --- | --- |
| `plugin.call.initialize` | configure i18n; no host call |
| `plugin.call.search` | return search items; no host call |
| `plugin.call.getConfiguration` | `host.call.configuration.read` |
| `plugin.call.saveConfiguration` | `host.call.configuration.write` |
| `plugin.call.getKeymap` | `host.call.keymap.read` |
| `plugin.call.saveKeymap` | `host.call.keymap.write` |
| `plugin.call.validateKeymap` | `host.call.keymap.validate` |
| `plugin.call.getGestures` | `host.call.gesture.read` |
| `plugin.call.saveGestures` | `host.call.gesture.write` |
| `plugin.call.suspendGestures` | `host.call.gesture.suspend` |
| `plugin.call.resumeGestures` | `host.call.gesture.resume` |
| `plugin.call.suspendHotkeys` | `host.call.hotkey.suspend` |
| `plugin.call.resumeHotkeys` | `host.call.hotkey.resume` |
| `plugin.call.restart` | `host.call.application.restart` |
| `plugin.call.getAuthorizations` | `host.call.authorization.list` |
| `plugin.call.revokeAuthorization` | `host.call.authorization.revoke` |
| `plugin.call.getDiagnostics` | `host.call.diagnostics.read` |

Canonical routes must not appear in any workspace `routes.json`. In particular, `plugin.call.initialize`, `plugin.call.search`, every settings `plugin.call.*` route above, and every `host.call.*` target above remain solely in plan 1's `x-routePayloadSchemas`.

Create each `routes.json` as a JSON array accepted directly by `MyTools.Protocol.TypeScript\scripts\generate-route-manifest.mjs`. `settings`, `json-formatter`, and `xml-formatter` contain one declaration with the exact manifest identity and `"routes": {}`. The dynamic declarations are:

| Authenticated entry | Plugin-specific routes | Required request properties | Required successful-response properties |
| --- | --- | --- | --- |
| `hello-search:hello` | `plugin.call.refresh` | `currentQuery: string` | `itemId`, `query`, `lastEvent`, `payload`, `generatedAt` |
| `deepseek-chat:chat` | `plugin.call.send` | `conversationId: string`, `text: string` | `status`, `conversationId`, `messages`, `streaming`, `error` |
| `deepseek-chat:chat` | `plugin.call.poll` | `conversationId: string` | `status`, `conversationId`, `messages`, `streaming`, `error` |
| `deepseek-chat:chat` | `plugin.call.newChat` | none; closed empty object | `status`, `conversationId`, `messages`, `streaming`, `error` |
| `deepseek-translator:translator` | `plugin.call.translate` | `text: string` | `input`, `status`, `inputType`, `translation`, `phonetic`, `definitions`, `chineseTranslation`, `isValidWord`, `isFavorite`, `fromCache`, `tokenUsage`, `sendMode`, `isExpanded`, `error` |
| `deepseek-translator:translator` | `plugin.call.favorite` | `text: string`, `state: object` | `input`, `status`, `inputType`, `translation`, `phonetic`, `definitions`, `chineseTranslation`, `isValidWord`, `isFavorite`, `fromCache`, `tokenUsage`, `sendMode`, `isExpanded`, `error` |
| `deepseek-translator:translator` | `plugin.call.getHistory`, `plugin.call.getFavorites` | none; closed empty object | `status`, `input`, `entries`, `error`, `sendMode`, `isExpanded` |
| `deepseek-translator:translator` | `plugin.call.setSendMode` | `sendMode: "enter" \| "realtime"`, `state: object` | `input`, `status`, `inputType`, `translation`, `phonetic`, `definitions`, `chineseTranslation`, `isValidWord`, `isFavorite`, `fromCache`, `tokenUsage`, `sendMode`, `isExpanded`, `error` |
| `deepseek-translator:translator` | `plugin.call.setExpanded` | `isExpanded: boolean`, `state: object` | `input`, `status`, `inputType`, `translation`, `phonetic`, `definitions`, `chineseTranslation`, `isValidWord`, `isFavorite`, `fromCache`, `tokenUsage`, `sendMode`, `isExpanded`, `error` |
| `deepseek-translator:ankicard` | `plugin.call.load` | none; closed empty object | `status`, `summary`, `card`, `error` |
| `deepseek-translator:ankicard` | `plugin.call.review` | `cardId: string`, `rating: integer 1..4` | `status`, `summary`, `card`, `error` |
| `deepseek-translator:ankicard` | `plugin.call.browse` | `page: integer >= 0` | `status`, `summary`, `browse`, `error` |
| `deepseek-translator:ankicard` | `plugin.call.deleteCard` | `cardId: string`, `page: integer >= 0` | `status`, `summary`, `browse`, `error` |
| `deepseek-translator:ankicard` | `plugin.call.saveCard` | `card: object`, `page: integer >= 0` | `status`, `summary`, `browse`, `error` |

All schemas are self-contained draft-07 JSON Schema with `type`, `required`, `properties`, and `additionalProperties: false`; no `$ref` is allowed. Arrays describe their item objects, nullable values use `type: ["object", "null"]`, ISO timestamps use `format: "date-time"`, chat roles are `"user" | "assistant"`, and Anki card types are `"basic" | "choice-en-to-zh" | "choice-zh-to-en"`. Add one registration test per backend that asserts its complete exact handler array, including canonical initialize/search handlers, so migration cannot retain short legacy names.

For example, `MyTools.Plugins\Examples\hello-search\routes.json` is exactly:

```json
[
  {
    "pluginId": "hello-search",
    "entryId": "hello",
    "routes": {
      "plugin.call.refresh": {
        "request": {
          "type": "object",
          "additionalProperties": false,
          "required": ["currentQuery"],
          "properties": { "currentQuery": { "type": "string" } }
        },
        "response": {
          "type": "object",
          "additionalProperties": false,
          "required": ["itemId", "query", "lastEvent", "payload", "generatedAt"],
          "properties": {
            "itemId": { "type": "string" },
            "query": { "type": "string" },
            "lastEvent": { "const": "refresh" },
            "payload": { "type": "object" },
            "generatedAt": { "type": "string", "format": "date-time" }
          }
        }
      }
    }
  }
]
```

`MyTools.Plugins\Examples\settings\routes.json`:

```json
[{ "pluginId": "settings", "entryId": "main", "routes": {} }]
```

`MyTools.Plugins\Examples\json-formatter\routes.json`:

```json
[{ "pluginId": "json-formatter", "entryId": "json-formatter", "routes": {} }]
```

`MyTools.Plugins\Examples\xml-formatter\routes.json`:

```json
[{ "pluginId": "xml-formatter", "entryId": "xml-formatter", "routes": {} }]
```

`deepseek-translator\routes.json` contains two array elements, one for `entryId: "translator"` and one for `entryId: "ankicard"`; routes never bleed between those authenticated entries.

Each workspace `package.json` must run the Protocol generator after `build-plugin.mjs` has recreated `dist`, so the manifest is part of the same distributable directory as `plugin.json`:

```json
{
  "scripts": {
    "build": "npm run check && node build-plugin.mjs && node ../../../MyTools.Protocol.TypeScript/scripts/generate-route-manifest.mjs routes.json dist/route-manifest.json"
  }
}
```

For each workspace, replace the legacy `@qping/plugin-common/server` import with the plan-3 SDK import and move the top-level wiring into an exported registration function. Import `PluginClient` and canonical wire DTOs from their owning plan-3/plan-1 packages; plugin-specific payloads are local TypeScript types kept structurally identical to that workspace's `routes.json`. `@mytools/plugin-sdk-node` is the ergonomic runtime API, not a second protocol package. Plan 3 exposes only `handle`, `call`, `publish`, and `close`: there is no fluent `initialize`, `search`, or `start` API.

```ts
import { connectPlugin, type PluginClient } from "@mytools/plugin-sdk-node";
import { mytoolsI18n } from "@qping/plugin-common/i18n";

export function registerSettingsPlugin(client: PluginClient): () => void
{
  const unsubscribers = [
    client.handle("plugin.call.initialize", async (params) => {
      mytoolsI18n.configure(params);
      return {};
    }),
    client.handle("plugin.call.search", async (params) => ({
      items: [
        {
          id: "settings:main",
          title: mytoolsI18n.t("Plugin.Settings.Name", { defaultValue: "Settings" }),
          subtitle: mytoolsI18n.t("Plugin.Settings.Subtitle", { defaultValue: "Application settings" }),
          priority: 100,
          icon: { kind: "emoji", value: "⚙️" },
          actions: [{ id: "open-detail", title: mytoolsI18n.t("Plugin.Settings.Action.Open", { defaultValue: "Open Settings" }), kind: "detail" }]
        }
      ]
    })),
    client.handle("plugin.call.getConfiguration", async () =>
      client.call("host.call.configuration.read", {})),
    client.handle("plugin.call.saveConfiguration", async (payload) =>
      client.call("host.call.configuration.write", payload)),
    client.handle("plugin.call.getKeymap", async () =>
      client.call("host.call.keymap.read", {})),
    client.handle("plugin.call.saveKeymap", async (payload) =>
      client.call("host.call.keymap.write", payload)),
    client.handle("plugin.call.validateKeymap", async (payload) =>
      client.call("host.call.keymap.validate", payload)),
    client.handle("plugin.call.getGestures", async () =>
      client.call("host.call.gesture.read", {})),
    client.handle("plugin.call.saveGestures", async (payload) =>
      client.call("host.call.gesture.write", payload)),
    client.handle("plugin.call.suspendGestures", async () =>
      client.call("host.call.gesture.suspend", {})),
    client.handle("plugin.call.resumeGestures", async () =>
      client.call("host.call.gesture.resume", {})),
    client.handle("plugin.call.suspendHotkeys", async () =>
      client.call("host.call.hotkey.suspend", {})),
    client.handle("plugin.call.resumeHotkeys", async () =>
      client.call("host.call.hotkey.resume", {})),
    client.handle("plugin.call.restart", async () =>
      client.call("host.call.application.restart", {})),
    client.handle("plugin.call.getAuthorizations", async () =>
      client.call("host.call.authorization.list", {})),
    client.handle("plugin.call.revokeAuthorization", async (payload) =>
      client.call("host.call.authorization.revoke", payload)),
    client.handle("plugin.call.getDiagnostics", async () =>
      client.call("host.call.diagnostics.read", {}))
  ];

  return () => {
    for (const unsubscribe of unsubscribers.reverse()) unsubscribe();
  };
}

export async function main(): Promise<void>
{
  const client = await connectPlugin();
  const unsubscribe = registerSettingsPlugin(client);
  let closing: Promise<void> | undefined;
  const shutdown = () => closing ??= (async () => {
    unsubscribe();
    await client.close();
  })();
  const requestShutdown = () => {
    void shutdown().catch((error) => {
      console.error(error);
      process.exitCode = 1;
    });
  };

  process.once("SIGINT", requestShutdown);
  process.once("SIGTERM", requestShutdown);
}

void main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
```

The implementation and registration test must include every exact route in the table. Apply the same full-route `client.handle(...)` and saved-unsubscribe shutdown pattern to `hello-search`, `json-formatter`, `xml-formatter`, `deepseek-chat`, and both `deepseek-translator` entries. `settings` must declare `configuration.read`, `configuration.write`, `keymap.read`, `keymap.write`, `gesture.read`, `gesture.write`, `hotkey.control`, `application.restart`, `authorization.read`, `authorization.revoke`, and `diagnostics.read`; the formatter/search plugins can declare an empty capability array if they only search and publish locally. The deepseek plugins must keep their existing fetch logic and must not reintroduce a `tool.ready` or `tool.hostCall` bridge.

`plugin.json` must become `pluginId` + `entries[*].entryId` + `entries[*].capabilities` everywhere. For example, `MyTools.Plugins\Examples\settings\plugin.json` should declare `pluginId: "settings"`, `entryId: "main"`, and the capability list above; `deepseek-chat` and the formatter plugins should declare the new IDs even when their `capabilities` array is empty.

- [ ] **Step 4: Run the workspace build/check loop and the focused backend test until green**

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
Push-Location .\MyTools.PluginSdk.Node
npm run build
npm test
Pop-Location
Push-Location .\MyTools.Plugins\Examples
npm ci
npm run check
npm run build
node --test .\test\route-manifests.test.mjs
Pop-Location
Push-Location .\MyTools.Plugins\Examples\settings
node --test .\test\backend-registration.test.mjs
Pop-Location
```

Expected: PASS. Plan 1's existing Protocol tests and the read-only canonical assertion remain green without a plan-1 diff. Every workspace packages `dist\plugin.json` and `dist\route-manifest.json`; the generated manifest contains exactly its plugin-specific route set and no canonical route; Node SDK, Web SDK, and the authenticated entry select the same schemas; identity mismatch fails startup; every backend builds with plan 3's real `PluginClient` API; all returned unsubscribe functions run before `client.close()`; and no backend depends on the old stdin/stdout JSON-RPC helper.

- [ ] **Step 5: Commit the Node SDK migration**

```powershell
git diff --exit-code -- protocol\schemas\v3 MyTools.Protocol\Generated\V3 MyTools.Protocol.TypeScript\src\generated MyTools.Protocol.Test MyTools.Protocol.TypeScript\test
git add MyTools.PluginSdk.Node MyTools.Plugins\Examples\common MyTools.Plugins\Examples\test MyTools.Plugins\Examples\hello-search MyTools.Plugins\Examples\json-formatter MyTools.Plugins\Examples\xml-formatter MyTools.Plugins\Examples\deepseek-chat MyTools.Plugins\Examples\deepseek-translator MyTools.Plugins\Examples\settings
git commit -m "feat: migrate example Node plugins to the bus SDK" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 3: Migrate the browser SDK and the settings authorization UI

**Files:**
- Modify: `MyTools.Plugins\Examples\common\package.json`
- Modify: `MyTools.Plugins\Examples\common\package-lock.json`
- Modify: `MyTools.Plugins\Examples\common\tsconfig.json`
- Modify: `MyTools.Plugins\Examples\common\src\client\index.ts`
- Modify: `MyTools.Plugins\Examples\common\src\shared\contracts.ts`
- Modify: `MyTools.Plugins\Examples\common\src\shared\events.ts`
- Modify: `MyTools.Plugins\Examples\common\test\client.test.mjs`
- Modify: `MyTools.Plugins\Examples\settings\src\web\main.ts`
- Modify: `MyTools.Plugins\Examples\settings\src\web\types.ts`
- Modify: `MyTools.Plugins\Examples\settings\src\web\common.ts`
- Create: `MyTools.Plugins\Examples\settings\src\web\permissions-panel.ts`
- Create: `MyTools.Plugins\Examples\settings\test\permissions-panel.test.mjs`
- Modify: `MyTools.Plugins\Examples\hello-search\src\web\main.ts`
- Modify: `MyTools.Plugins\Examples\json-formatter\src\web\main.ts`
- Modify: `MyTools.Plugins\Examples\xml-formatter\src\web\main.ts`
- Modify: `MyTools.Plugins\Examples\deepseek-chat\src\web\main.ts`
- Modify: `MyTools.Plugins\Examples\deepseek-translator\src\web\Translator\main.ts`
- Modify: `MyTools.Plugins\Examples\deepseek-translator\src\web\AnkiCard\main.ts`

- [ ] **Step 1: Write the failing authorization-panel test**

```javascript
import test from "node:test";
import assert from "node:assert/strict";
import { renderPermissionsPanel } from "../dist/web/permissions-panel.js";

function fakeHost() {
  return {
    calls: [],
    async call(route, payload) {
      this.calls.push({ route, payload });
      if (route === "plugin.call.revokeAuthorization") {
        return { revoked: true };
      }
      return {
        items: [
          {
            pluginId: "settings",
            entryId: "main",
            capability: "authorization.revoke",
            scope: "session",
            grantedAtUtc: "2026-08-13T14:00:00Z",
            revocable: true
          }
        ]
      };
    }
  };
}

test("permissions panel renders granted capabilities and wires revoke buttons", async () => {
  const host = fakeHost();
  const panel = await renderPermissionsPanel(host);

  assert.match(panel.innerHTML, /authorization\.revoke/);
  panel.querySelector("button[data-action='revoke']").click();
  assert.equal(host.calls[0].route, "plugin.call.getAuthorizations");
  assert.equal(host.calls.at(-1).route, "plugin.call.revokeAuthorization");
});
```

- [ ] **Step 2: Run the web SDK and panel test commands and confirm the red bar**

Run:

```powershell
Push-Location .\MyTools.Plugins\Examples\settings
npm ci
npm run build
node --test .\test\permissions-panel.test.mjs
Pop-Location
```

Expected: FAIL because `renderPermissionsPanel` and the new `client`/permission routes are not yet exported.

- [ ] **Step 3: Replace the legacy browser helper with the plan-4 client and wire the new settings permissions view**

```ts
import { createWebPluginClient } from "@qping/plugin-common/client";

const client = createWebPluginClient({
  webview: window.chrome?.webview,
});

const hostEvents = client.events.host;

client.subscribe(hostEvents.initialize, async () => {
  await loadConfiguration();
});

client.subscribe(hostEvents.languageChanged, async () => {
  await loadConfiguration();
});

client.subscribe(hostEvents.themeChanged, async () => {
  await loadConfiguration();
});
```

Update every example web entrypoint to use `createWebPluginClient`, call `client.call("plugin.call.*", ...)` for plugin entrypoints, and stop posting legacy `tool.ready`, `tool-call`, `tool-response`, `tool-event`, `tool-subscribe`, and `tool-unsubscribe` envelopes. In `settings`, add a permissions category or panel that loads `plugin.call.getAuthorizations`, renders the returned grants, and calls `plugin.call.revokeAuthorization` for a single grant at a time. `types.ts` should add a `CapabilityGrant` model, and `common.ts` should keep the dirty-state / save-button helpers but no longer own the old browser protocol payload types.

Every affected package must resolve the plan-1 generated package through the workspace/lockfile. Do not define local `MessageEnvelope`, `BusError`, or validator copies:

```powershell
npm install --prefix .\MyTools.Plugins\Examples\common "@mytools/protocol@file:../../../MyTools.Protocol.TypeScript"
```

`MyTools.Plugins\Examples\common\src\shared\contracts.ts` must re-export, rather than redeclare, the public wire types:

```ts
export type {
  BusError,
  EndpointIdentity,
  MessageEnvelope
} from "@mytools/protocol";
export {
  validateEnvelope,
  validateRoutePayload
} from "@mytools/protocol";
```

Use these route names in the migrated UI:
- `plugin.call.initialize`
- `plugin.call.search`
- `plugin.call.getConfiguration`
- `plugin.call.saveConfiguration`
- `plugin.call.getKeymap`
- `plugin.call.saveKeymap`
- `plugin.call.validateKeymap`
- `plugin.call.getGestures`
- `plugin.call.saveGestures`
- `plugin.call.suspendGestures`
- `plugin.call.resumeGestures`
- `plugin.call.suspendHotkeys`
- `plugin.call.resumeHotkeys`
- `plugin.call.restart`
- `plugin.call.getAuthorizations`
- `plugin.call.revokeAuthorization`
- `plugin.call.getDiagnostics`

The deepseek and formatter pages should keep their current search and action behavior but stop depending on the old `tool.ready` bootstrap; they now load through the new browser client and host event subscriptions only.

- [ ] **Step 4: Run the browser build and the authorization-panel test until green**

Run:

```powershell
Push-Location .\MyTools.Plugins\Examples
npm ci
npm run check
npm run build
Pop-Location
Push-Location .\MyTools.Plugins\Examples\settings
node --test .\test\permissions-panel.test.mjs
Pop-Location
```

Expected: PASS; the browser client exports the new API, the settings page can list and revoke authorizations, and no example page still depends on the legacy `tool.*` bridge.

- [ ] **Step 5: Commit the browser migration**

```powershell
git add MyTools.Plugins\Examples\common MyTools.Plugins\Examples\hello-search MyTools.Plugins\Examples\json-formatter MyTools.Plugins\Examples\xml-formatter MyTools.Plugins\Examples\deepseek-chat MyTools.Plugins\Examples\deepseek-translator MyTools.Plugins\Examples\settings
git commit -m "feat: migrate plugin browser clients to the bus SDK" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 4: Wire host DI, window/session reuse, authorization lookup, and diagnostics

**Files:**
- Create: `MyTools.Desktop\Services\CapabilityGrantStore.cs`
- Create: `MyTools.Desktop\Services\SettingsConfigurationCapabilityHandlers.cs`
- Create: `MyTools.Desktop\Components\NodePluginWebViewHost.cs`
- Modify: `MyTools.Desktop\DesktopServiceCollectionExtensions.cs`
- Modify: `MyTools.Desktop\AppBootstrapper.cs`
- Modify: `MyTools.Desktop\Services\PluginWindowManager.cs`
- Modify: `MyTools.Desktop\Services\SettingsPluginHostCallHandler.cs`
- Modify: `MyTools.Desktop\Components\NodePluginDetailViewModel.cs`
- Modify: `MyTools.Desktop\Components\NodePluginDetailView.xaml.cs`
- Modify: `MyTools.Desktop\Views\PluginWindow.xaml.cs`
- Create: `MyTools.Desktop.Test\Services\SettingsPluginHostCallHandlerTests.cs`
- Create: `MyTools.Desktop.Test\Services\PluginWindowManagerTests.cs`
- Create: `MyTools.Desktop.Test\Components\NodePluginWebViewHostTests.cs`

- [ ] **Step 1: Write the failing host-call and window-reuse tests**

```csharp
[TestFixture]
public class SettingsPluginHostCallHandlerTests
{
    [Test]
    public async Task Configuration_read_and_write_use_the_canonical_routes()
    {
        var handler = CreateHandler();

        var configuration = await handler.InvokeAsync(
            Context("host.call.configuration.read"),
            JsonDocument.Parse("{}").RootElement,
            CancellationToken.None);
        Assert.That(configuration.TryGetProperty("categories", out _), Is.True);

        var save = await handler.InvokeAsync(
            Context("host.call.configuration.write"),
            JsonDocument.Parse("""
            { "changes": [{ "fullPath": "General.Language", "value": "en-US" }] }
            """).RootElement,
            CancellationToken.None);
        Assert.That(save.TryGetProperty("requiresRestart", out _), Is.True);
    }

    [Test]
    public async Task GetAuthorizations_and_revokeAuthorization_round_trip_the_grant_store()
    {
        var grants = new RecordingCapabilityGrantStore([
            new CapabilityGrantRow("settings", "main", "configuration.write", "session", DateTimeOffset.Parse("2026-08-13T14:00:00Z"), true)
        ]);
        var diagnostics = new RecordingHostDiagnostics
        {
            SnapshotJson = """
            {
              "recentEvents": [],
              "counters": {}
            }
            """
        };
        var handler = new SettingsPluginHostCallHandler(
            registry, themeService, languageService, logLevelService, autoStartService,
            keymapService, keymapOverrideProvider, gestureConfigProvider, gestureRegistry,
            mouseHelper, pluginLoader, hotKeyManager, grants, diagnostics, logger);

        var list = await handler.InvokeAsync(
            Context("host.call.authorization.list"),
            JsonDocument.Parse("{}").RootElement,
            CancellationToken.None);
        Assert.That(list.GetProperty("items").GetArrayLength(), Is.EqualTo(1));

        await handler.InvokeAsync(
            Context("host.call.authorization.revoke"),
            JsonDocument.Parse("""
            { "pluginId": "settings", "entryId": "main", "capability": "configuration.write" }
            """).RootElement,
            CancellationToken.None);

        Assert.That(grants.Revocations, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetDiagnostics_returns_the_host_snapshot()
    {
        var diagnostics = new RecordingHostDiagnostics
        {
            SnapshotJson = """
            {
              "recentEvents": [
                { "name": "CapabilityAuthorized", "summary": "settings/main" }
              ],
              "counters": { "CapabilityAuthorized": 1 }
            }
            """
        };
        var handler = CreateHandler(diagnostics: diagnostics);

        var result = await handler.InvokeAsync(
            Context("host.call.diagnostics.read"),
            JsonDocument.Parse("{}").RootElement,
            CancellationToken.None);

        Assert.That(result.GetProperty("recentEvents").GetArrayLength(), Is.EqualTo(1));
    }

    private static CapabilityInvocationContext Context(string route) =>
        new(
            new EndpointIdentity("settings", "main", "session-1", "node-main"),
            route,
            "trace-1",
            new RequestBudget(TimeProvider.System.GetTimestamp(), 5_000),
            TimeProvider.System);
}
```

```csharp
[TestFixture]
public class PluginWindowManagerTests
{
    [Test]
    public void Same_entry_reuses_the_window_and_different_entry_opens_separately()
    {
        var manager = new PluginWindowManager(serviceProvider);
        manager.ShowOrFocus(settingsMain, context);
        manager.ShowOrFocus(settingsMain, context);
        manager.ShowOrFocus(deepseekChat, context);

        Assert.That(manager.OpenWindowKeys, Is.EquivalentTo(new[] { "settings:main", "deepseek-chat:chat" }));
    }
}
```

```csharp
[TestFixture]
public class NodePluginWebViewHostTests
{
    [Test]
    public async Task Attach_uses_context_entry_id_and_rebinds_after_session_replacement()
    {
        var sessions = new FakePluginSessionAccessor("settings", "main", "session-1");
        var registry = new RecordingPluginSessionEndpointRegistry();
        await using var host = new NodePluginWebViewHost(sessions, registry, transportFactory);

        await host.AttachAsync(
            new NodePluginDetailContextBuilder()
                .WithPluginId("settings")
                .WithEntryId("main")
                .Build(),
            webViewChannel,
            CancellationToken.None);
        sessions.Replace("settings", "main", "session-2");
        await host.RebindCompletion;

        Assert.That(registry.Identities.Select(x => x.EntryId), Is.All.EqualTo("main"));
        Assert.That(registry.Identities.Select(x => x.SessionId), Is.EqualTo(new[] { "session-1", "session-2" }));
    }
}
```

- [ ] **Step 2: Run the focused Desktop tests and confirm the red bar**

Run:

```powershell
dotnet test .\MyTools.Desktop.Test\MyTools.Desktop.Test.csproj --filter "FullyQualifiedName~SettingsPluginHostCallHandlerTests|FullyQualifiedName~PluginWindowManagerTests|FullyQualifiedName~NodePluginWebViewHostTests"
```

Expected: FAIL because the Host Core capability handlers, grant store, entry-aware window reuse, and plan-4-deferred Desktop WebView composition are not wired yet.

- [ ] **Step 3: Implement the host integration layer and session-aware window wiring**

This task accepts the Desktop integration ownership deliberately deferred by plan 4. `NodePluginWebViewHost` must read the already-implemented `NodePluginDetailContext.PluginId` and `.EntryId`, obtain `PluginSessionSnapshot.SessionId`, create the plan-4 `WebView2Transport`, and register it through `IPluginSessionEndpointRegistry`. On `SessionReplaced`, it disposes the old endpoint and registers a replacement with the new session ID; closing a window disposes only the WebView endpoint and never stops Node.

`DesktopServiceCollectionExtensions.cs` should register the plan-2 Host Core services, `NodePluginWebViewHost`, the new `CapabilityGrantStore`, and the production Windows capability routes below. The route-to-capability map is supplied to plan 2's `CapabilityGateway`; the corresponding focused `ICapabilityHandler` instances are registered by capability. This is not test-harness-only wiring.

| Exact host route | Manifest declaration and authorization key | Generated request → response DTO | Production implementation |
| --- | --- | --- | --- |
| `host.call.configuration.read` | `configuration.read` | `ConfigurationReadRequest` → `ConfigurationReadResponse` | `ConfigurationReadCapabilityHandler` calls the read operation moved from `SettingsPluginHostCallHandler.GetConfiguration` |
| `host.call.configuration.write` | `configuration.write` | `ConfigurationWriteRequest` → `ConfigurationWriteResponse` | `ConfigurationWriteCapabilityHandler` calls the save operation moved from `SettingsPluginHostCallHandler.SaveConfiguration` and checks the budget immediately before `registry.SaveChanges()` |
| `host.call.keymap.read` | `keymap.read` | `KeymapReadRequest` → `KeymapReadResponse` | settings keymap read operation |
| `host.call.keymap.write` | `keymap.write` | `KeymapWriteRequest` → `OperationResult` | settings keymap save operation |
| `host.call.keymap.validate` | `keymap.read` | `KeymapValidateRequest` → `KeymapValidateResponse` | settings keymap validation operation |
| `host.call.gesture.read` | `gesture.read` | `GestureReadRequest` → `GestureReadResponse` | settings gesture read operation |
| `host.call.gesture.write` | `gesture.write` | `GestureWriteRequest` → `OperationResult` | settings gesture save operation |
| `host.call.gesture.suspend` | `gesture.write` | `EmptyRequest` → `OperationResult` | settings gesture suspend operation |
| `host.call.gesture.resume` | `gesture.write` | `EmptyRequest` → `OperationResult` | settings gesture resume operation |
| `host.call.hotkey.suspend` | `hotkey.control` | `EmptyRequest` → `OperationResult` | settings hotkey suspend operation |
| `host.call.hotkey.resume` | `hotkey.control` | `EmptyRequest` → `OperationResult` | settings hotkey resume operation |
| `host.call.application.restart` | `application.restart` | `EmptyRequest` → `OperationResult` | settings restart operation |
| `host.call.authorization.list` | `authorization.read` | `AuthorizationListRequest` → `AuthorizationListResponse` | existing grant-store list handler |
| `host.call.authorization.revoke` | `authorization.revoke` | `AuthorizationRevokeRequest` → `AuthorizationRevokeResponse` | existing grant-store revoke handler |
| `host.call.diagnostics.read` | `diagnostics.read` | `DiagnosticsReadRequest` → `DiagnosticsReadResponse` | existing diagnostics snapshot handler |

The DTO titles above are definitions added to the canonical protocol schema in Task 2 and therefore are generated in both languages. `ConfigurationReadResponse` carries the current `ConfigurationDto` fields (`categories`, `supportedLocales`, `supportedThemes`, `supportedLogLevels`); `ConfigurationWriteRequest` carries `changes[{fullPath,value}]`; `ConfigurationWriteResponse` carries `requiresRestart`. Delete the duplicate DTO declarations from `HostCallProtocol.cs` only after consumers use the generated types.

`SettingsPluginHostCallHandler.cs` must not define or accept `HostCallRequest`. It may remain the settings-domain service behind the focused handlers, or its operations may move into those handlers. In either layout, `ConfigurationReadCapabilityHandler.Capability` is exactly `configuration.read`, `ConfigurationWriteCapabilityHandler.Capability` is exactly `configuration.write`, both consume `CapabilityInvocationContext`, and only the write handler reports `HasExternalSideEffects == true`. Keep the authorization and diagnostics operations and handlers registered; adding configuration handlers must not replace them. The settings dispatch remains exhaustive over the canonical routes:

```csharp
switch (context.Route)
{
    case "host.call.configuration.read":
        return GetConfiguration();
    case "host.call.configuration.write":
        context.ThrowIfBudgetExpiredBeforeCommit();
        return SaveConfiguration(payload);
    case "host.call.keymap.read":
        return GetKeymap();
    case "host.call.keymap.write":
        context.ThrowIfBudgetExpiredBeforeCommit();
        return SaveKeymap(payload);
    case "host.call.keymap.validate":
        return ValidateKeymap(payload);
    case "host.call.gesture.read":
        return GetGestures();
    case "host.call.gesture.write":
        context.ThrowIfBudgetExpiredBeforeCommit();
        return SaveGestures(payload);
    case "host.call.gesture.suspend":
        return SuspendGestures();
    case "host.call.gesture.resume":
        return ResumeGestures();
    case "host.call.hotkey.suspend":
        return SuspendHotkeys();
    case "host.call.hotkey.resume":
        return ResumeHotkeys();
    case "host.call.application.restart":
        context.ThrowIfBudgetExpiredBeforeCommit();
        return Restart();
    case "host.call.authorization.list":
        return await grantStore.ListAsync(cancellationToken);
    case "host.call.authorization.revoke":
        await grantStore.RevokeAsync(
            context.Identity.PluginId,
            context.Identity.EntryId,
            payload.GetProperty("capability").GetString()!,
            cancellationToken);
        return new { revoked = true };
    case "host.call.diagnostics.read":
        return diagnostics.Snapshot();
    default:
        throw new ProtocolException(ProtocolErrorCodes.RouteNotFound, context.Route);
}
```

`AppBootstrapper.cs` should stop filtering only by `ParentId` and instead resolve the active `PluginId`/`EntryId` pair when opening `settings` or `deepseek-translator`. `PluginWindowManager.cs` should key windows by the entry-qualified ID (`settings:main`, `deepseek-translator:translator`, `deepseek-translator:ankicard`) so multiple entries in the same package no longer fight over a single window slot.

`NodePluginDetailViewModel.cs`, `NodePluginDetailView.xaml.cs`, and `PluginWindow.xaml.cs` should stop acting as business message brokers. They should only manage window chrome, focus, theme, and session refresh callbacks that come from Host Core or the WebView transport. Anything that still parses `tool-call`, `tool-event`, or `tool-subscribe` must be deleted here, not left behind as a compatibility shim.

- [ ] **Step 4: Run the focused Desktop tests and the solution build until green**

Run:

```powershell
dotnet test .\MyTools.Desktop.Test\MyTools.Desktop.Test.csproj --filter "FullyQualifiedName~SettingsPluginHostCallHandlerTests|FullyQualifiedName~PluginWindowManagerTests|FullyQualifiedName~NodePluginWebViewHostTests"
dotnet test .\MyTools.sln --configuration Release --no-restore --nologo --filter "FullyQualifiedName!~OpenAIServiceTest"
```

Expected: PASS; configuration read/write, authorization listing/revocation, and diagnostics retrieval round trip through production Host Core registrations, and opening/reopening `settings` or `deepseek` windows no longer relies on the old parent-id-only key.

- [ ] **Step 5: Commit the host integration**

```powershell
git add MyTools.Desktop\Services\CapabilityGrantStore.cs MyTools.Desktop\Services\SettingsConfigurationCapabilityHandlers.cs MyTools.Desktop\Components\NodePluginWebViewHost.cs MyTools.Desktop\DesktopServiceCollectionExtensions.cs MyTools.Desktop\AppBootstrapper.cs MyTools.Desktop\Services\PluginWindowManager.cs MyTools.Desktop\Services\SettingsPluginHostCallHandler.cs MyTools.Desktop\Components\NodePluginDetailViewModel.cs MyTools.Desktop\Components\NodePluginDetailView.xaml.cs MyTools.Desktop\Views\PluginWindow.xaml.cs MyTools.Desktop.Test\Services\SettingsPluginHostCallHandlerTests.cs MyTools.Desktop.Test\Services\PluginWindowManagerTests.cs MyTools.Desktop.Test\Components\NodePluginWebViewHostTests.cs
git commit -m "feat: wire plugin host settings and diagnostics through Host Core" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 5: Prove the full WebView → Node → host → Node → WebView flow and the failure modes

**Files:**
- Create: `MyTools.Desktop.Test\Integration\PluginMessageBusMigrationE2ETests.cs`
- Modify: `MyTools.Desktop.Test\MyTools.Desktop.Test.csproj`
- Modify: `MyTools.Plugins.Test\NodePlugins\NodePluginCatalogTest.cs` if any entry assumptions remain
- Modify: `MyTools.Desktop.Test\Services\SettingsPluginHostCallHandlerTests.cs` if the diagnostics payload shape changes

- [ ] **Step 1: Write the end-to-end tests first**

```csharp
[TestFixture]
[Apartment(ApartmentState.STA)]
public class PluginMessageBusMigrationE2ETests
{
    [Test]
    public async Task WebView_to_Node_to_host_and_back_preserves_the_entry_identity()
    {
        await using var harness = await PluginMessageBusHarness.StartAsync("settings", "main");
        var configuration = await harness.WebView.CallAsync<JsonElement>(
            "plugin.call.getConfiguration",
            new { });
        var result = await harness.WebView.CallAsync<JsonElement>(
            "plugin.call.saveConfiguration",
            new { changes = new[] { new { fullPath = "General.Language", value = "en-US" } } });

        Assert.That(configuration.TryGetProperty("categories", out _), Is.True);
        Assert.That(result.GetProperty("requiresRestart").GetBoolean(), Is.False);
        Assert.That(harness.Bus.Trace.Count(x => x.Route == "plugin.call.getConfiguration"), Is.EqualTo(1));
        Assert.That(harness.Bus.Trace.Count(x => x.Route == "host.call.configuration.read"), Is.EqualTo(1));
        Assert.That(harness.Bus.Trace.Count(x => x.Route == "plugin.call.saveConfiguration"), Is.EqualTo(1));
        Assert.That(harness.Bus.Trace.Count(x => x.Route == "host.call.configuration.write"), Is.EqualTo(1));
    }

    [Test]
    public async Task Crashing_the_main_Node_restarts_the_session_and_reopens_the_window()
    {
        await using var harness = await PluginMessageBusHarness.StartAsync("deepseek-translator", "translator");
        await harness.Node.CrashAsync();
        await harness.WaitForRestartAsync();

        Assert.That(harness.Session.State, Is.EqualTo("Restarting").Or.EqualTo("Ready"));
        Assert.That(harness.Windows.ReopenedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Worker_spawn_uses_the_declared_capability_subset()
    {
        await using var harness = await PluginMessageBusHarness.StartAsync("settings", "main");
        var worker = await harness.Node.SpawnWorkerAsync("worker.mjs", ["configuration.read"]);

        Assert.That(worker.Identity.PluginId, Is.EqualTo("settings"));
        Assert.That(worker.Identity.EntryId, Is.EqualTo("main"));
        Assert.That(worker.GrantedCapabilities, Is.EquivalentTo(new[] { "configuration.read" }));
    }

    [Test]
    public async Task Unauthorized_calls_and_diagnostics_requests_are_denied()
    {
        await using var harness = await PluginMessageBusHarness.StartAsync("hello-search", "hello");
        var error = await harness.WebView.CallAndReadErrorAsync("host.call.configuration.write", new { });
        var diagError = await harness.Diagnostics.CallAndReadErrorAsync("diagnostics.sessions.list", new { });

        Assert.That(error.Code, Is.EqualTo("CapabilityDenied"));
        Assert.That(diagError.Code, Is.EqualTo("CapabilityDenied"));
    }

    [Test]
    public async Task Repeated_restart_cycles_do_not_leak_handles_or_growth()
    {
        await using var harness = await PluginMessageBusHarness.StartAsync("settings", "main");
        for (var i = 0; i < 20; i++)
        {
            await harness.Node.CrashAsync();
            await harness.WaitForRestartAsync();
        }

        Assert.That(harness.Diagnostics.Snapshot().HandleCountDelta, Is.LessThan(3));
    }

    [Test]
    public async Task Event_flood_and_slow_WebView_stay_bounded()
    {
        await using var harness = await PluginMessageBusHarness.StartAsync("deepseek-chat", "chat");
        await harness.Node.PublishFloodAsync("plugin.event.progress", 10000);
        await harness.WebView.DelayResponsesAsync(TimeSpan.FromSeconds(2));

        Assert.That(harness.Diagnostics.Snapshot().DroppedEvents, Is.GreaterThan(0));
        Assert.That(harness.Diagnostics.Snapshot().MemoryBytes, Is.LessThan(64 * 1024 * 1024));
    }
}
```

- [ ] **Step 2: Run the focused E2E suite and confirm the red bar**

Run:

```powershell
dotnet test .\MyTools.Desktop.Test\MyTools.Desktop.Test.csproj --filter "FullyQualifiedName~PluginMessageBusMigrationE2ETests"
```

Expected: FAIL because the harness and the migrated flow are not yet present, and the legacy path still handles tool forwarding.

- [ ] **Step 3: Implement the harness against the plan-2/3/4 contracts and the migrated example plugins**

The harness should use plan 4's `WebView2Transport`, plan 2's `IPluginSessionAccessor` / `IPluginSessionEndpointRegistry`, and plan 3's Windows Node endpoint / Node SDK contracts only. It should not depend on `NodePluginProcessHost`, `HostCallRequest`, or any `tool-*` browser message. For `settings`, resolve the same production `DesktopServiceCollectionExtensions` registrations used by the application—do not install test-only route handlers—and assert both full chains: `plugin.call.getConfiguration` → Node backend → `host.call.configuration.read` → Node response → plugin response, and `plugin.call.saveConfiguration` → Node backend → `host.call.configuration.write` → Node response → plugin response. For `deepseek-translator`, assert both entries (`translator` and `ankicard`) can restart independently without stealing each other’s window or session.

The stress cases must be real, not mocked away:
- crash/restart must create a new session and rebind endpoints;
- reopening a window must reuse the existing plugin session and not start a new Node process;
- Worker creation must pass only the declared capability subset;
- unauthorized capability and diagnostics requests must be rejected;
- repeated restart cycles must be checked for handle leaks and not just logical state;
- a slow WebView and a 10,000-event flood must stay within the bounded queues and memory budget.

- [ ] **Step 4: Run the release build, workspace builds, and focused E2E tests until green**

Run:

```powershell
Push-Location .\MyTools.Plugins\Examples
npm ci
npm run check
npm run build
Pop-Location
dotnet test .\MyTools.Desktop.Test\MyTools.Desktop.Test.csproj --filter "FullyQualifiedName~PluginMessageBusMigrationE2ETests|FullyQualifiedName~SettingsPluginHostCallHandlerTests"
dotnet test .\MyTools.sln --configuration Release --no-restore --nologo --filter "FullyQualifiedName!~OpenAIServiceTest"
```

Expected: PASS; the complete WebView → Node → host → Node → WebView path works on the migrated examples, crash/restart and Worker behavior are stable, and the long-run tests stay within their handle and memory budgets.

- [ ] **Step 5: Commit the E2E proof**

```powershell
git add MyTools.Desktop.Test\Integration\PluginMessageBusMigrationE2ETests.cs MyTools.Desktop.Test\MyTools.Desktop.Test.csproj
git commit -m "test: prove plugin message-bus migration end to end" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Task 6: Remove the legacy stdio JSON-RPC and control-forwarding path

**Files:**
- Delete: `MyTools.Plugins\NodePlugins\NodePluginProcessHost.cs`
- Delete: `MyTools.Plugins\NodePlugins\HostCallProtocol.cs`
- Delete: `MyTools.Plugins.Test\NodePlugins\NodePluginEncodingTest.cs`
- Delete: `MyTools.Plugins\NodePlugins\NodePluginProtocol.cs` only if the final scan shows no remaining callers
- Modify: `MyTools.Plugins\NodePlugins\NodePlugin.cs`
- Modify: `MyTools.Plugins\NodePlugins\NodePluginFactory.cs`
- Modify: `MyTools.Plugins\NodePlugins\NodePluginCatalog.cs`
- Modify: `MyTools.Plugins\NodePlugins\NodePluginDetailContext.cs`
- Modify: `MyTools.Desktop\Components\NodePluginDetailViewModel.cs`
- Modify: `MyTools.Desktop\Components\NodePluginDetailView.xaml.cs`
- Modify: `MyTools.Desktop\Services\SettingsPluginHostCallHandler.cs`
- Modify: `MyTools.Desktop\Services\PluginWindowManager.cs`
- Modify: `MyTools.Desktop\AppBootstrapper.cs`
- Modify: `MyTools.Desktop\Views\PluginWindow.xaml.cs`
- Modify: `MyTools.Plugins\Examples\common\src\client\index.ts`

- [ ] **Step 1: Run the final legacy scan before deleting anything**

Run:

```powershell
rg -n "NodePluginProcessHost|HostCallRequest|tool-call|tool-response|tool-event|tool-subscribe|tool-unsubscribe|jsonrpc|stdio" .\MyTools.Plugins .\MyTools.Desktop .\MyTools.Plugins\Examples -g "!NodePluginProcessHost.cs" -g "!HostCallProtocol.cs" -g "!NodePluginProtocol.cs" -g "!**/test/**"
```

Expected: exit 1 with no remaining production callers after Tasks 1-5. The excluded legacy definition files are the deletion targets in Step 2; if any non-excluded match is present, migrate that caller before moving on.

- [ ] **Step 2: Delete the dead legacy host and browser bridge code**

Remove `NodePluginProcessHost.cs`, `HostCallProtocol.cs`, and the obsolete `NodePluginEncodingTest.cs`. Delete `NodePluginProtocol.cs` only if the final scan proves every type in the file is unused after the new SDKs and transports are wired. In `NodePluginDetailViewModel.cs` and `NodePluginDetailView.xaml.cs`, remove the branches that parse or emit `tool-call`, `tool-response`, `tool-event`, `tool-subscribe`, and `tool-unsubscribe`. In `SettingsPluginHostCallHandler.cs`, stop accepting the old `HostCallRequest` DTO and keep only the host-core-backed capability/diagnostics methods. In `AppBootstrapper.cs` and `PluginWindowManager.cs`, remove the old parent-id-only forwarding logic and leave only the entry-qualified session/window reuse path.

- [ ] **Step 3: Run the final no-legacy scan, enforce the cleanup allowlist, and run the full solution build**

Run:

```powershell
rg -n "NodePluginProcessHost|HostCallRequest|tool-call|tool-response|tool-event|tool-subscribe|tool-unsubscribe|jsonrpc|stdio" .\MyTools.Plugins .\MyTools.Desktop .\MyTools.Plugins\Examples -g "!**/test/**"

$allowed = @(
    "M`tMyTools.Plugins/NodePlugins/NodePlugin.cs",
    "M`tMyTools.Plugins/NodePlugins/NodePluginFactory.cs",
    "M`tMyTools.Plugins/NodePlugins/NodePluginCatalog.cs",
    "M`tMyTools.Plugins/NodePlugins/NodePluginDetailContext.cs",
    "D`tMyTools.Plugins/NodePlugins/NodePluginProcessHost.cs",
    "D`tMyTools.Plugins/NodePlugins/HostCallProtocol.cs",
    "D`tMyTools.Plugins.Test/NodePlugins/NodePluginEncodingTest.cs",
    "D`tMyTools.Plugins/NodePlugins/NodePluginProtocol.cs",
    "M`tMyTools.Desktop/Components/NodePluginDetailViewModel.cs",
    "M`tMyTools.Desktop/Components/NodePluginDetailView.xaml.cs",
    "M`tMyTools.Desktop/Services/SettingsPluginHostCallHandler.cs",
    "M`tMyTools.Desktop/Services/PluginWindowManager.cs",
    "M`tMyTools.Desktop/AppBootstrapper.cs",
    "M`tMyTools.Desktop/Views/PluginWindow.xaml.cs",
    "M`tMyTools.Plugins/Examples/common/src/client/index.ts"
)
$actual = @(git diff --name-status)
$unexpected = @($actual | Where-Object { $_ -notin $allowed })
if ($unexpected.Count -ne 0) {
    throw "Unexpected working-tree changes; preserve them and stop cleanup:`n$($unexpected -join "`n")"
}
$staged = @(git diff --cached --name-status)
if ($staged.Count -ne 0) {
    throw "Pre-existing staged changes must not be folded into cleanup:`n$($staged -join "`n")"
}
$untracked = @(git ls-files --others --exclude-standard)
if ($untracked.Count -ne 0) {
    throw "Untracked files must not be folded into cleanup:`n$($untracked -join "`n")"
}

dotnet test .\MyTools.sln --configuration Release --no-restore --nologo --filter "FullyQualifiedName!~OpenAIServiceTest"
```

Expected: `rg` prints no matches; every pending cleanup change is one of the explicit status/path pairs (the optional `NodePluginProtocol.cs` deletion may be absent); no staged, untracked, or unrelated user change can be swallowed; and the solution test pass remains green.

- [ ] **Step 4: Commit the cleanup**

```powershell
git add MyTools.Plugins\NodePlugins\NodePlugin.cs MyTools.Plugins\NodePlugins\NodePluginFactory.cs MyTools.Plugins\NodePlugins\NodePluginCatalog.cs MyTools.Plugins\NodePlugins\NodePluginDetailContext.cs MyTools.Plugins\NodePlugins\NodePluginProcessHost.cs MyTools.Plugins\NodePlugins\HostCallProtocol.cs MyTools.Plugins\NodePlugins\NodePluginProtocol.cs MyTools.Plugins.Test\NodePlugins\NodePluginEncodingTest.cs MyTools.Desktop\Components\NodePluginDetailViewModel.cs MyTools.Desktop\Components\NodePluginDetailView.xaml.cs MyTools.Desktop\Services\SettingsPluginHostCallHandler.cs MyTools.Desktop\Services\PluginWindowManager.cs MyTools.Desktop\AppBootstrapper.cs MyTools.Desktop\Views\PluginWindow.xaml.cs MyTools.Plugins\Examples\common\src\client\index.ts
git commit -m "refactor: remove legacy plugin JSON-RPC forwarding" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

- [ ] **Step 5: Verify the committed cleanup paths are clean**

Run:

```powershell
git diff --exit-code HEAD -- MyTools.Plugins\NodePlugins MyTools.Plugins.Test\NodePlugins\NodePluginEncodingTest.cs MyTools.Desktop\Components MyTools.Desktop\Services\SettingsPluginHostCallHandler.cs MyTools.Desktop\AppBootstrapper.cs MyTools.Desktop\Views\PluginWindow.xaml.cs MyTools.Plugins\Examples\common\src\client\index.ts
git diff --cached --exit-code
git status --short
```

Expected: both scoped post-commit diffs exit 0. `git status --short` is empty; if another user changes the shared checkout after the allowlist check, preserve those changes and investigate rather than amending them into the cleanup commit.

## 第 5/5 份计划输出契约

完成标准是 Task 6 的 no-legacy scan、Desktop/Plugins workspace build、settings authorization tests、E2E tests 和 Release solution test 全部符合预期，并且仓库中只剩下前四份计划交付的新消息总线路径。

后续工作只允许直接消费 `MyTools.Protocol.V3.MessageEnvelope`、`MyTools.Protocol.V3.BusError`、`MyTools.Protocol.V3.EndpointIdentity`、`@mytools/protocol`，以及第 2/5 份计划冻结的 `TransportPriority`、`IMessageTransport.SendAsync(..., priority, ...)`、`IMessageTransport.ReadAllAsync(...)`、`IMessageTransport.Completion`、`IPluginSessionAccessor`、`IPluginSessionEndpointRegistry`、`INodeEndpointEvents`、`IWorkerRegistration`、`ICapabilityAuthorizer`、`ICapabilityRateLimiter`、`ICapabilityHandler`、`IHostDiagnostics`，不得重新发明同名替代品。如果任何旧的 `NodePluginProcessHost`、`HostCallRequest` 或 `tool-*` 控制消息仍然存在，说明本计划未完成。

## Spec coverage checklist

| Approved design requirement | Implementation evidence |
| --- | --- |
| `pluginId` / `entryId` / `capability` manifest migration | Task 1, Task 2 |
| all example plugins and `settings` / `deepseek` migration | Task 2, Task 3 |
| host DI / window wiring | Task 4 |
| diagnostics connection | Task 4, Task 5 |
| authorization view / revoke | Task 3, Task 4 |
| full WebView → Node → host → Node → WebView flow | Task 5 |
| crash restart / window reopen / Worker / security / pressure / handle leak | Task 5 |
| final deletion of `NodePluginProcessHost` / stdio JSON-RPC / control forwarding | Task 6 |

## Self-review checklist

- Completeness scan: no incomplete markers or vague wording remains.
- Internal consistency: `PluginId` means package ID, `EntryId` means entry ID, and `Id`/`QualifiedId` is only the combined migration key until cleanup.
- Scope check: no task recreates protocol framing, Host Core routing, or transport primitives from plans 1-4.
- Ambiguity check: the only allowed legacy bridge is the temporary migration code that disappears in Task 6.
