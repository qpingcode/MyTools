# Plugin Message Bus Protocol Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the **第 1/5 份计划** of the plugin-host message bus: a platform-neutral, schema-first v3 protocol package with generated C# and TypeScript contracts and validators, deterministic version negotiation, stream-ready 4-byte little-endian JSON framing, fuzz coverage, and CI drift detection.

**Architecture:** Versioned JSON Schema files under `protocol\schemas\v3` are the only manually edited wire-contract source. A .NET generator emits the frozen `MyTools.Protocol.V3` C# surface and route validators, while a Node generator emits TypeScript declarations and Ajv standalone envelope/route validators; both generators assert every public output name instead of accepting library defaults. This plan stops at protocol and stream-ready framing primitives: later plans own MessageBus/session actors, real Named Pipe and WebView transports, SDK migration, and plugin migration.

**Tech Stack:** .NET 8, C# 12, NUnit 4.3.2, NJsonSchema 11.6.1, Node.js 22, TypeScript 7.0.2, Ajv 8.20.0, ajv-formats 3.0.1, json-schema-to-typescript 15.0.4, npm lockfile v3, GitHub Actions on `windows-latest`.

---

## Scope and verified repository baseline

- This is **第 1/5 份计划：Protocol Foundation**. It covers project structure, versioned schemas, generated contracts/validators, envelope/error/routes, handshake version negotiation, framing, fuzz tests, and generation consistency.
- The design document's seven implementation steps map to five executable plans as follows: design step 1 is split between plan 1 (protocol/framing) and plan 2 (transport abstraction/fake transport); design step 2 is plan 2; design step 3 is plan 3; design step 4 is plan 4; design steps 5 and 6 are plan 5; design step 7 is enforced incrementally by plans 1–4 and closed by plan 5 end-to-end/security gates.
- It explicitly does **not** implement `MessageBus`, endpoint routing, request correlation, session actors, capabilities, a real Named Pipe, WebView2 transport, Node/Web SDK behavior, process management, or migration.
- Outputs consumed by later plans:
  - Plan 2, Host Core, references `MyTools.Protocol.V3.MessageKind`, `EndpointIdentity`, `BusError`, `MessageEnvelope`, `ProtocolErrorCodes`, `ProtocolJson.SerializerOptions`, `IRoutePayloadValidator`, `ProtocolVersionNegotiator`, and `ProtocolRoutes`.
  - Plan 3, Named Pipe and Node SDK, references `LengthPrefixedJsonFrameCodec` and `@mytools/protocol`.
  - Plan 4, WebView transport, references the same envelope and TypeScript validators without using byte framing.
  - Plan 5, migration, consumes the package exports and removes old JSON-RPC contracts only after all new paths are integrated.
- Repository facts verified before planning:
  - Every current project targets `net8.0`; `release.yml` installs .NET `8.0.x`.
  - `release.yml` installs Node `22.x`; the existing npm lock resolves TypeScript `7.0.2`.
  - NuGet versions are centrally managed by `Directory.Packages.props`.
  - Existing release test command passes: `dotnet test .\MyTools.sln --configuration Release --no-restore --nologo --filter "FullyQualifiedName!~OpenAIServiceTest"` → 123 passed.
  - Existing Node commands pass in `MyTools.Plugins\Examples`: `npm ci`, `npm run check`, and `npm run build`.
  - Registry checks on 2026-08-13 confirmed NJsonSchema `11.6.1`, Ajv `8.20.0`, ajv-formats `3.0.1`, json-schema-to-typescript `15.0.4`, and TypeScript `7.0.2`.
- Dependency rule: NJsonSchema is necessary for C# generation and host-side schema validation; Ajv/json-schema-to-typescript are necessary for standalone client validation and TypeScript generation. No transport, actor, WPF, or pipe package is introduced.

## File structure map

### Created

- `protocol\schemas\v3\protocol.schema.json` — canonical v3 wire contract: envelope, errors, complete plans 2–5 route inventory, request/response/event payloads, and handshake payloads.
- `protocol\test-vectors\v3\version-negotiation.json` — language-neutral negotiation cases shared by C# and TypeScript tests.
- `MyTools.Protocol\MyTools.Protocol.csproj` — platform-neutral .NET 8 protocol library.
- `MyTools.Protocol\Generated\V3\ProtocolContracts.g.cs` — generated DTOs; never hand-edit.
- `MyTools.Protocol\Generated\V3\ProtocolSupport.g.cs` — generated `ProtocolErrorCodes`, `ProtocolJson`, schema IDs, and frozen-name assertions; never hand-edit.
- `MyTools.Protocol\Generated\V3\ProtocolRoutes.g.cs` — generated reserved bus-route constants; never hand-edit.
- `MyTools.Protocol\Validation\ProtocolValidator.cs` — NJsonSchema-backed envelope and route-payload validation, including `IRoutePayloadValidator`.
- `MyTools.Protocol\Versioning\ProtocolVersionNegotiator.cs` — exact-version negotiation with distinct major-mismatch and minor-overlap failures.
- `MyTools.Protocol\Framing\FrameReadResult.cs` — incremental decoder result contract.
- `MyTools.Protocol\Framing\LengthPrefixedJsonFrameCodec.cs` — 4-byte little-endian UTF-8 JSON framing with a 4 MiB default cap.
- `MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj` — deterministic C# code-generation console.
- `MyTools.Protocol.Generation\Program.cs` — generator entry point.
- `MyTools.Protocol.Test\MyTools.Protocol.Test.csproj` — NUnit protocol tests.
- `MyTools.Protocol.Test\Validation\ProtocolValidatorTest.cs` — envelope/error/route/handshake validation tests.
- `MyTools.Protocol.Test\Versioning\ProtocolVersionNegotiatorTest.cs` — shared-vector negotiation tests.
- `MyTools.Protocol.Test\Framing\LengthPrefixedJsonFrameCodecTest.cs` — framing and deterministic fuzz tests.
- `MyTools.Protocol.TypeScript\package.json` / `package-lock.json` — isolated protocol package and locked tooling.
- `MyTools.Protocol.TypeScript\tsconfig.json` — strict ESM compilation.
- `MyTools.Protocol.TypeScript\scripts\generate.mjs` — TypeScript and Ajv standalone generation.
- `MyTools.Protocol.TypeScript\scripts\generate-route-manifest.mjs` — deterministic compiler for plugin-declared dynamic route schemas.
- `MyTools.Protocol.TypeScript\src\generated\protocol.d.ts` — generated types; never hand-edit.
- `MyTools.Protocol.TypeScript\src\generated\validators.ts` — generated standalone validators; never hand-edit.
- `MyTools.Protocol.TypeScript\src\generated\routes.ts` — generated reserved bus-route constants; never hand-edit.
- `MyTools.Protocol.TypeScript\src\validation.ts` — stable client validation facade.
- `MyTools.Protocol.TypeScript\src\versioning.ts` — TypeScript negotiation implementation.
- `MyTools.Protocol.TypeScript\src\framing.ts` — Node/browser-neutral `Uint8Array` framing helpers.
- `MyTools.Protocol.TypeScript\src\index.ts` — package exports.
- `MyTools.Protocol.TypeScript\test\schema.test.mjs` — schema and generated-validator tests.
- `MyTools.Protocol.TypeScript\test\route-manifest.test.mjs` — declared-route generation, validation, collision, and reserved-route tests.
- `MyTools.Protocol.TypeScript\test\versioning.test.mjs` — shared-vector negotiation tests.
- `MyTools.Protocol.TypeScript\test\framing.test.mjs` — framing and deterministic fuzz tests.
- `scripts\verify-protocol-generated.ps1` — regenerate both languages and fail on tracked drift.

### Modified

- `Directory.Packages.props` — centrally pin NJsonSchema at `11.6.1`.
- `MyTools.sln` — add protocol library, generator, and test projects.
- `.github\workflows\release.yml` — cache/install the protocol npm package and run generation consistency before tests.

## Contract decisions fixed by this plan

- Wire version is a string `major.minor`; this plan defines supported v3 as `3.0`.
- Negotiation selects the highest exact version present on both sides. If the two valid, non-empty sets have no major in common it returns `ProtocolMismatch`; if they share a major but have no exact minor in common it returns `HandshakeFailed`. Malformed or empty version sets return `ProtocolMismatch`.
- `request`, `response`, and `event` share one envelope. Requests alone carry `timeoutMs`; responses alone carry `correlationId`; failed responses carry `error` and `payload: null`.
- `bus.handshake` is the only pre-auth route. Its request fields are exactly `supportedVersions`, `launchToken`, `pluginId`, `entryId`, `processId`, and `processStartedAtUtc`; it does **not** carry `sessionId` or `endpointId`. The host binds the already-created session and allocates the endpoint ID, then returns exactly `selectedVersion`, `sessionId`, and `endpointId` on success. A child process never chooses its own endpoint ID; plan 3 Named Pipe bootstrap/authentication must use this contract.
- Dynamic routes are constrained to `plugin.call.*`, `host.call.*`, `plugin.event.*`, `host.event.*`, and `diagnostics.*`; bus routes are a closed enum.
- `x-routePayloadSchemas` lists every route consumed by plans 2–5, including reserved bus routes, settings/search/initialization calls, host configuration/authorization/diagnostics/worker calls, and concrete host/plugin events. Call entries explicitly name distinct `request` and `response` definitions; event entries name an `event` definition. Generated C# `IRoutePayloadValidator.Validate(string route, JsonElement payload)` and TypeScript `validateRoutePayload(route, payload)` keep their frozen request/event signatures, while TypeScript `validateRouteResponsePayload` selects the response map; an unlisted route returns `RouteNotFound`.
- The settings proxy mapping is frozen: `get/saveConfiguration` → `host.call.configuration.read/write`; `get/save/validateKeymap` → `host.call.keymap.read/write/validate`; `get/save/suspend/resumeGestures` → `host.call.gesture.read/write/suspend/resume`; `suspend/resumeHotkeys` → `host.call.hotkey.suspend/resume`; `restart` → `host.call.application.restart`; `get/revokeAuthorization` → `host.call.authorization.list/revoke`; and `getDiagnostics` → `host.call.diagnostics.read`. Plan 5 must use these exact host route names rather than deriving names at runtime.
- Future plugin-specific business routes are declared, not guessed. A plugin manifest owns self-contained `routes[route].request` and `routes[route].response` JSON Schemas; `generate-route-manifest.mjs` rejects `$ref`, reserved `bus.*` names, malformed schemas, and duplicate routes, then emits `dist\route-manifest.json`. Node/Web SDK startup registers that artifact with its route validator before handlers are registered, and Host Core registers the same artifact before capability handlers. Declared routes use their generated validators; only routes absent from both the canonical map and the authenticated plugin's manifest return `RouteNotFound`. Conflicting or malformed manifests fail startup as `HandshakeFailed`; SDK validation remains an early developer error and `CapabilityGateway` repeats request-payload validation before invoking a handler.
- Framing is exactly unsigned 32-bit little-endian length plus UTF-8 JSON. `LengthPrefixedJsonFrameCodec.ReadAsync(Stream, ...)/WriteAsync(Stream, ...)` are the transport-facing API and may delegate to the incremental `TryRead`/`EncodeBytes` helpers. Zero, over-limit, truncated-at-end, and invalid JSON frames are rejected, and the 4 MiB limit is checked from the prefix before any payload-sized allocation or pool rental.

### Frozen generated C# surface

All generated public protocol types use namespace `MyTools.Protocol.V3`. Schema `title` values and generator postconditions freeze these names; the generator emits an explicit public template and never accepts inferred library names.

```csharp
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

### Task 1: Scaffold isolated protocol projects

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `MyTools.sln`
- Create: `MyTools.Protocol\MyTools.Protocol.csproj`
- Create: `MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj`
- Create: `MyTools.Protocol.Generation\Program.cs`
- Create: `MyTools.Protocol.Test\MyTools.Protocol.Test.csproj`
- Create: `MyTools.Protocol.TypeScript\package.json`
- Create: `MyTools.Protocol.TypeScript\package-lock.json`
- Create: `MyTools.Protocol.TypeScript\tsconfig.json`
- Create: `MyTools.Protocol.TypeScript\src\index.ts`

- [ ] **Step 1: Verify the structure is absent (red)**

Run:

```powershell
dotnet sln .\MyTools.sln list | Select-String 'MyTools.Protocol'
Test-Path .\MyTools.Protocol.TypeScript\package.json
```

Expected: the first command prints no match and the second prints `False`.

- [ ] **Step 2: Create the three .NET projects and add exact central dependencies**

Run:

```powershell
dotnet new classlib --framework net8.0 --name MyTools.Protocol
dotnet new console --framework net8.0 --name MyTools.Protocol.Generation
dotnet new nunit --framework net8.0 --name MyTools.Protocol.Test
Remove-Item .\MyTools.Protocol\Class1.cs
Remove-Item .\MyTools.Protocol.Test\UnitTest1.cs
dotnet sln .\MyTools.sln add .\MyTools.Protocol\MyTools.Protocol.csproj
dotnet sln .\MyTools.sln add .\MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj
dotnet sln .\MyTools.sln add .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj
dotnet add .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj reference .\MyTools.Protocol\MyTools.Protocol.csproj
```

Add to the central `ItemGroup` in `Directory.Packages.props`:

```xml
<PackageVersion Include="NJsonSchema" Version="11.6.1" />
```

Replace `MyTools.Protocol\MyTools.Protocol.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyTools.Protocol</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NJsonSchema" />
    <EmbeddedResource Include="..\protocol\schemas\v3\*.json"
                      Link="Schemas\V3\%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

Replace `MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NJsonSchema" />
  </ItemGroup>
</Project>
```

Replace `MyTools.Protocol.Test\MyTools.Protocol.Test.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit3TestAdapter" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyTools.Protocol\MyTools.Protocol.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="..\protocol\test-vectors\**\*.json"
          Link="TestVectors\%(RecursiveDir)%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create and lock the Node 22/TypeScript 7 package**

Create `MyTools.Protocol.TypeScript\package.json`:

```json
{
  "name": "@mytools/protocol",
  "version": "3.0.0",
  "private": true,
  "type": "module",
  "engines": {
    "node": ">=22"
  },
  "files": [
    "dist"
  ],
  "exports": {
    ".": {
      "types": "./dist/index.d.ts",
      "import": "./dist/index.js"
    }
  },
  "scripts": {
    "clean": "node -e \"fs.rmSync('dist',{recursive:true,force:true})\"",
    "generate": "node ./scripts/generate.mjs",
    "check": "tsc -p tsconfig.json --noEmit",
    "build": "npm run clean && tsc -p tsconfig.json",
    "test": "npm run build && node --test ./test/*.test.mjs"
  },
  "dependencies": {
    "ajv": "8.20.0",
    "ajv-formats": "3.0.1"
  },
  "devDependencies": {
    "json-schema-to-typescript": "15.0.4",
    "typescript": "7.0.2"
  }
}
```

Create `MyTools.Protocol.TypeScript\tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "rootDir": "src",
    "outDir": "dist",
    "declaration": true,
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "exactOptionalPropertyTypes": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*"]
}
```

Create `MyTools.Protocol.TypeScript\src\index.ts`:

```typescript
export {};
```

Generate, rather than hand-author, the lock:

```powershell
npm install --prefix .\MyTools.Protocol.TypeScript --package-lock-only
```

Expected: `package-lock.json` has lockfile version 3 and exact top-level versions shown above.

- [ ] **Step 4: Add a generator fail-fast entry point and verify the scaffold (green)**

Replace `MyTools.Protocol.Generation\Program.cs` with:

```csharp
var repositoryRoot = args.Length == 1
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Usage: MyTools.Protocol.Generation <repository-root>");
var schemaPath = Path.Combine(repositoryRoot, "protocol", "schemas", "v3", "protocol.schema.json");
if (!File.Exists(schemaPath))
{
    Console.Error.WriteLine($"Schema not found: {schemaPath}");
    return 2;
}

return 0;
```

Run:

```powershell
dotnet restore .\MyTools.sln
dotnet build .\MyTools.sln --configuration Debug --no-restore --nologo
npm ci --prefix .\MyTools.Protocol.TypeScript
```

Expected: restore/build/npm install succeed; the generator is compiled but is not run until the schema exists.

- [ ] **Step 5: Commit the scaffold atomically**

```powershell
git add Directory.Packages.props MyTools.sln MyTools.Protocol MyTools.Protocol.Generation MyTools.Protocol.Test MyTools.Protocol.TypeScript
git commit -m "build: scaffold protocol foundation projects" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 2: Define the v3 schema single source of truth

**Files:**
- Create: `protocol\schemas\v3\protocol.schema.json`
- Create: `MyTools.Protocol.TypeScript\test\schema.test.mjs`
- Create: `MyTools.Protocol.TypeScript\scripts\generate-route-manifest.mjs`
- Create: `MyTools.Protocol.TypeScript\test\route-manifest.test.mjs`

- [ ] **Step 1: Write schema tests first (red)**

Create `MyTools.Protocol.TypeScript\test\schema.test.mjs`:

```javascript
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import Ajv from "ajv";
import addFormats from "ajv-formats";

const schemaUrl = new URL("../../protocol/schemas/v3/protocol.schema.json", import.meta.url);

async function loadValidator(ref) {
  const schema = JSON.parse(await readFile(schemaUrl, "utf8"));
  const ajv = new Ajv({ allErrors: true, strict: true, allowUnionTypes: true });
  addFormats(ajv);
  ajv.addKeyword({ keyword: "x-routePayloadSchemas", schemaType: "object" });
  ajv.addSchema(schema);
  const validate = ajv.getSchema(`${schema.$id}#/definitions/${ref}`);
  assert.ok(validate, `missing validator for ${ref}`);
  return validate;
}

test("accepts a valid request envelope", async () => {
  const validate = await loadValidator("messageEnvelope");
  assert.equal(validate({
    version: "3.0",
    id: "01JREQUEST",
    correlationId: null,
    traceId: "01JREQUEST",
    sessionId: "01JSESSION",
    pluginId: "settings",
    entryId: "main",
    endpointId: "node-main",
    kind: "request",
    route: "plugin.call.saveConfiguration",
    timeoutMs: 30000,
    payload: {},
    error: null
  }), true, JSON.stringify(validate.errors));
});

test("rejects unknown bus routes and request errors", async () => {
  const validate = await loadValidator("messageEnvelope");
  const value = {
    version: "3.0",
    id: "01JREQUEST",
    correlationId: null,
    traceId: "01JREQUEST",
    sessionId: "01JSESSION",
    pluginId: "settings",
    entryId: "main",
    endpointId: "node-main",
    kind: "request",
    route: "bus.unknown",
    timeoutMs: 30000,
    payload: {},
    error: {
      code: "InternalError",
      message: "must not be present",
      retryable: false,
      details: null
    }
  };
  assert.equal(validate(value), false);
});

test("freezes request evidence and host-assigned handshake response", async () => {
  const validate = await loadValidator("messageEnvelope");
  assert.equal(validate({
    version: "3.0",
    id: "01JHANDSHAKE",
    correlationId: null,
    traceId: "01JHANDSHAKE",
    sessionId: null,
    pluginId: "settings",
    entryId: "main",
    endpointId: null,
    kind: "request",
    route: "bus.handshake",
    timeoutMs: 5000,
    payload: {
      supportedVersions: ["3.0"],
      launchToken: "one-time-secret",
      pluginId: "settings",
      entryId: "main",
      processId: 1234,
      processStartedAtUtc: "2026-08-13T12:00:00Z"
    },
    error: null
  }), true, JSON.stringify(validate.errors));
  assert.equal(validate({
    version: "3.0",
    id: "01JHANDSHAKE-RESPONSE",
    correlationId: "01JHANDSHAKE",
    traceId: "01JHANDSHAKE",
    sessionId: null,
    pluginId: "settings",
    entryId: "main",
    endpointId: null,
    kind: "response",
    route: "bus.handshake",
    timeoutMs: null,
    payload: {
      selectedVersion: "3.0",
      sessionId: "01JSESSION",
      endpointId: "node-main"
    },
    error: null
  }), true, JSON.stringify(validate.errors));
});
```

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: FAIL with `ENOENT` for `protocol\schemas\v3\protocol.schema.json`.

- [ ] **Step 2: Add the complete canonical schema**

Create `protocol\schemas\v3\protocol.schema.json`:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "$id": "https://mytools.local/protocol/v3/protocol.schema.json",
  "title": "MessageEnvelope",
  "$ref": "#/definitions/messageEnvelope",
  "x-routePayloadSchemas": {
    "bus.handshake": { "request": "handshakeRequest", "response": "handshakeResponse" },
    "bus.cancel": { "request": "emptyPayload", "response": "emptyPayload" },
    "bus.subscribe": { "request": "subscriptionPayload", "response": "emptyPayload" },
    "bus.unsubscribe": { "request": "subscriptionPayload", "response": "emptyPayload" },
    "bus.ping": { "event": "emptyPayload" },
    "bus.pong": { "event": "emptyPayload" },
    "plugin.call.initialize": { "request": "initializeRequest", "response": "initializeResponse" },
    "plugin.call.search": { "request": "searchRequest", "response": "searchResponse" },
    "plugin.call.getConfiguration": { "request": "getConfigurationRequest", "response": "getConfigurationResponse" },
    "plugin.call.saveConfiguration": { "request": "saveConfigurationRequest", "response": "saveConfigurationResponse" },
    "plugin.call.getKeymap": { "request": "getKeymapRequest", "response": "getKeymapResponse" },
    "plugin.call.saveKeymap": { "request": "saveKeymapRequest", "response": "saveKeymapResponse" },
    "plugin.call.validateKeymap": { "request": "validateKeymapRequest", "response": "validateKeymapResponse" },
    "plugin.call.getGestures": { "request": "getGesturesRequest", "response": "getGesturesResponse" },
    "plugin.call.saveGestures": { "request": "saveGesturesRequest", "response": "saveGesturesResponse" },
    "plugin.call.suspendGestures": { "request": "suspendGesturesRequest", "response": "suspendGesturesResponse" },
    "plugin.call.resumeGestures": { "request": "resumeGesturesRequest", "response": "resumeGesturesResponse" },
    "plugin.call.suspendHotkeys": { "request": "suspendHotkeysRequest", "response": "suspendHotkeysResponse" },
    "plugin.call.resumeHotkeys": { "request": "resumeHotkeysRequest", "response": "resumeHotkeysResponse" },
    "plugin.call.restart": { "request": "restartRequest", "response": "restartResponse" },
    "plugin.call.getAuthorizations": { "request": "getAuthorizationsRequest", "response": "getAuthorizationsResponse" },
    "plugin.call.revokeAuthorization": { "request": "revokeAuthorizationRequest", "response": "revokeAuthorizationResponse" },
    "plugin.call.getDiagnostics": { "request": "getDiagnosticsRequest", "response": "getDiagnosticsResponse" },
    "host.call.configuration.read": { "request": "configurationReadRequest", "response": "configurationReadResponse" },
    "host.call.configuration.write": { "request": "configurationWriteRequest", "response": "configurationWriteResponse" },
    "host.call.keymap.read": { "request": "getKeymapRequest", "response": "getKeymapResponse" },
    "host.call.keymap.write": { "request": "saveKeymapRequest", "response": "saveKeymapResponse" },
    "host.call.keymap.validate": { "request": "validateKeymapRequest", "response": "validateKeymapResponse" },
    "host.call.gesture.read": { "request": "getGesturesRequest", "response": "getGesturesResponse" },
    "host.call.gesture.write": { "request": "saveGesturesRequest", "response": "saveGesturesResponse" },
    "host.call.gesture.suspend": { "request": "suspendGesturesRequest", "response": "suspendGesturesResponse" },
    "host.call.gesture.resume": { "request": "resumeGesturesRequest", "response": "resumeGesturesResponse" },
    "host.call.hotkey.suspend": { "request": "suspendHotkeysRequest", "response": "suspendHotkeysResponse" },
    "host.call.hotkey.resume": { "request": "resumeHotkeysRequest", "response": "resumeHotkeysResponse" },
    "host.call.application.restart": { "request": "restartRequest", "response": "restartResponse" },
    "host.call.worker.spawn": { "request": "workerSpawnRequest", "response": "workerSpawnResponse" },
    "host.call.authorization.list": { "request": "getAuthorizationsRequest", "response": "getAuthorizationsResponse" },
    "host.call.authorization.revoke": { "request": "revokeAuthorizationRequest", "response": "revokeAuthorizationResponse" },
    "host.call.diagnostics.read": { "request": "getDiagnosticsRequest", "response": "getDiagnosticsResponse" },
    "plugin.event.progress": { "event": "progressEvent" },
    "plugin.event.ready": { "event": "processReadyEvent" },
    "plugin.event.worker.ready": { "event": "processReadyEvent" },
    "host.event.initialize": { "event": "initializeRequest" },
    "host.event.search": { "event": "searchRequest" },
    "host.event.key": { "event": "keyEvent" },
    "host.event.language-changed": { "event": "localeChangedEvent" },
    "host.event.theme-changed": { "event": "themeChangedEvent" }
  },
  "definitions": {
    "messageKind": {
      "title": "MessageKind",
      "type": "string",
      "enum": ["request", "response", "event"]
    },
    "protocolVersion": {
      "type": "string",
      "pattern": "^[1-9][0-9]*\\.(0|[1-9][0-9]*)$"
    },
    "messageId": {
      "type": "string",
      "minLength": 1,
      "maxLength": 128
    },
    "boundId": {
      "type": "string",
      "minLength": 1,
      "maxLength": 128
    },
    "endpointIdentity": {
      "title": "EndpointIdentity",
      "type": "object",
      "additionalProperties": false,
      "required": ["pluginId", "entryId", "sessionId", "endpointId"],
      "properties": {
        "pluginId": { "$ref": "#/definitions/boundId" },
        "entryId": { "$ref": "#/definitions/boundId" },
        "sessionId": { "$ref": "#/definitions/boundId" },
        "endpointId": { "$ref": "#/definitions/boundId" }
      }
    },
    "route": {
      "anyOf": [
        {
          "enum": [
            "bus.handshake",
            "bus.cancel",
            "bus.subscribe",
            "bus.unsubscribe",
            "bus.ping",
            "bus.pong"
          ]
        },
        {
          "type": "string",
          "pattern": "^(plugin\\.call|host\\.call|plugin\\.event|host\\.event|diagnostics)\\.[A-Za-z0-9][A-Za-z0-9._-]*$",
          "maxLength": 256
        }
      ]
    },
    "protocolErrorCodeValue": {
      "type": "string",
      "enum": [
        "ProtocolMismatch",
        "HandshakeFailed",
        "CapabilityNotDeclared",
        "CapabilityDenied",
        "InvalidPayload",
        "MessageTooLarge",
        "RouteNotFound",
        "RequestTimeout",
        "Cancelled",
        "TooManyRequests",
        "TransportDisconnected",
        "PluginUnavailable",
        "PluginCrashed",
        "InternalError"
      ]
    },
    "busError": {
      "title": "BusError",
      "type": "object",
      "additionalProperties": false,
      "required": ["code", "message", "retryable", "details"],
      "properties": {
        "code": { "$ref": "#/definitions/protocolErrorCodeValue" },
        "message": {
          "type": "string",
          "minLength": 1,
          "maxLength": 1024
        },
        "retryable": { "type": "boolean", "default": false },
        "details": {
          "type": ["object", "array", "string", "number", "boolean", "null"]
        }
      }
    },
    "handshakeRequest": {
      "title": "HandshakeRequest",
      "type": "object",
      "additionalProperties": false,
      "required": [
        "supportedVersions",
        "launchToken",
        "pluginId",
        "entryId",
        "processId",
        "processStartedAtUtc"
      ],
      "properties": {
        "supportedVersions": {
          "type": "array",
          "minItems": 1,
          "uniqueItems": true,
          "items": { "$ref": "#/definitions/protocolVersion" }
        },
        "launchToken": {
          "type": "string",
          "minLength": 1,
          "maxLength": 4096
        },
        "pluginId": { "$ref": "#/definitions/boundId" },
        "entryId": { "$ref": "#/definitions/boundId" },
        "processId": {
          "type": "integer",
          "minimum": 1,
          "maximum": 2147483647
        },
        "processStartedAtUtc": {
          "type": "string",
          "format": "date-time"
        }
      }
    },
    "handshakeResponse": {
      "title": "HandshakeResponse",
      "type": "object",
      "additionalProperties": false,
      "required": ["selectedVersion", "sessionId", "endpointId"],
      "properties": {
        "selectedVersion": { "$ref": "#/definitions/protocolVersion" },
        "sessionId": { "$ref": "#/definitions/boundId" },
        "endpointId": { "$ref": "#/definitions/boundId" }
      }
    },
    "emptyPayload": {
      "title": "EmptyPayload",
      "type": "object",
      "additionalProperties": false,
      "properties": {}
    },
    "subscriptionPayload": {
      "title": "SubscriptionPayload",
      "type": "object",
      "additionalProperties": false,
      "required": ["topic"],
      "properties": {
        "topic": {
          "type": "string",
          "pattern": "^(plugin\\.event|host\\.event)\\.[A-Za-z0-9][A-Za-z0-9._-]*$",
          "maxLength": 256
        }
      }
    },
    "handshakePayload": {
      "title": "HandshakePayload",
      "oneOf": [
        { "$ref": "#/definitions/handshakeRequest" },
        { "$ref": "#/definitions/handshakeResponse" }
      ]
    },
    "stringMap": {
      "type": "object",
      "additionalProperties": { "type": "string" }
    },
    "initializeRequest": {
      "type": "object", "additionalProperties": false,
      "required": ["locale", "fallbackLocale", "messages"],
      "properties": {
        "locale": { "type": "string", "minLength": 1 },
        "fallbackLocale": { "type": "string", "minLength": 1 },
        "messages": { "$ref": "#/definitions/stringMap" }
      }
    },
    "initializeResponse": { "$ref": "#/definitions/emptyPayload" },
    "initializePayload": {
      "anyOf": [
        { "$ref": "#/definitions/initializeRequest" },
        { "$ref": "#/definitions/initializeResponse" }
      ]
    },
    "searchRequest": {
      "type": "object", "additionalProperties": false,
      "required": ["query", "mode", "locale", "fallbackLocale"],
      "properties": {
        "query": { "type": "string" },
        "mode": { "type": "string", "minLength": 1 },
        "locale": { "type": "string", "minLength": 1 },
        "fallbackLocale": { "type": "string", "minLength": 1 }
      }
    },
    "searchIcon": {
      "type": "object", "additionalProperties": false,
      "required": ["kind", "value"],
      "properties": {
        "kind": { "type": "string", "minLength": 1 },
        "value": { "type": "string" }
      }
    },
    "searchAction": {
      "type": "object", "additionalProperties": false,
      "required": ["id", "title", "kind"],
      "properties": {
        "id": { "$ref": "#/definitions/boundId" },
        "title": { "type": "string", "minLength": 1 },
        "kind": { "type": "string", "minLength": 1 },
        "description": { "type": "string" }
      }
    },
    "searchItem": {
      "type": "object", "additionalProperties": false,
      "required": ["id", "title", "subtitle", "priority", "icon", "actions"],
      "properties": {
        "id": { "$ref": "#/definitions/boundId" },
        "title": { "type": "string", "minLength": 1 },
        "subtitle": { "type": "string" },
        "priority": { "type": "integer" },
        "icon": { "oneOf": [{ "$ref": "#/definitions/searchIcon" }, { "type": "null" }] },
        "actions": { "type": "array", "items": { "$ref": "#/definitions/searchAction" } }
      }
    },
    "searchResponse": {
      "type": "object", "additionalProperties": false,
      "required": ["items"],
      "properties": {
        "items": { "type": "array", "items": { "$ref": "#/definitions/searchItem" } }
      }
    },
    "searchPayload": {
      "anyOf": [
        { "$ref": "#/definitions/searchRequest" },
        { "$ref": "#/definitions/searchResponse" }
      ]
    },
    "configurationReadRequest": {
      "type": "object", "additionalProperties": false,
      "properties": { "key": { "type": "string", "minLength": 1 } }
    },
    "configurationReadResponse": {
      "type": "object", "additionalProperties": true
    },
    "configurationReadPayload": {
      "anyOf": [
        { "$ref": "#/definitions/configurationReadRequest" },
        { "$ref": "#/definitions/configurationReadResponse" }
      ]
    },
    "settingChange": {
      "type": "object", "additionalProperties": false,
      "required": ["fullPath", "value"],
      "properties": {
        "fullPath": { "type": "string", "minLength": 1 },
        "value": { "type": ["string", "null"] }
      }
    },
    "configurationWriteRequest": {
      "type": "object", "additionalProperties": false,
      "properties": {
        "changes": { "type": "array", "items": { "$ref": "#/definitions/settingChange" } },
        "key": { "type": "string", "minLength": 1 },
        "value": {}
      },
      "anyOf": [
        { "required": ["changes"] },
        { "required": ["key", "value"] }
      ]
    },
    "configurationWriteResponse": {
      "type": "object", "additionalProperties": false,
      "properties": {
        "success": { "type": "boolean" },
        "requiresRestart": { "type": "boolean" }
      },
      "minProperties": 1
    },
    "configurationWritePayload": {
      "anyOf": [
        { "$ref": "#/definitions/configurationWriteRequest" },
        { "$ref": "#/definitions/configurationWriteResponse" }
      ]
    },
    "getConfigurationRequest": { "$ref": "#/definitions/emptyPayload" },
    "getConfigurationResponse": { "$ref": "#/definitions/configurationReadResponse" },
    "getConfigurationPayload": {
      "anyOf": [
        { "$ref": "#/definitions/getConfigurationRequest" },
        { "$ref": "#/definitions/getConfigurationResponse" }
      ]
    },
    "saveConfigurationRequest": { "$ref": "#/definitions/configurationWriteRequest" },
    "saveConfigurationResponse": { "$ref": "#/definitions/configurationWriteResponse" },
    "saveConfigurationPayload": {
      "anyOf": [
        { "$ref": "#/definitions/saveConfigurationRequest" },
        { "$ref": "#/definitions/saveConfigurationResponse" }
      ]
    },
    "keymapOverride": {
      "type": "object", "additionalProperties": false,
      "properties": {
        "hotKey": { "type": ["string", "null"] },
        "keywords": {
          "oneOf": [
            { "type": "array", "items": { "type": "string" } },
            { "type": "null" }
          ]
        },
        "isEnabled": { "type": ["boolean", "null"] }
      }
    },
    "keymapPlugin": {
      "type": "object", "additionalProperties": false,
      "required": ["pluginId", "name", "defaultHotKey", "currentHotKey", "defaultKeywords", "currentKeywords", "isEnabled", "isNodePlugin"],
      "properties": {
        "pluginId": { "$ref": "#/definitions/boundId" },
        "name": { "type": "string", "minLength": 1 },
        "defaultHotKey": { "type": "string" },
        "currentHotKey": { "type": "string" },
        "defaultKeywords": { "type": "array", "items": { "type": "string" } },
        "currentKeywords": { "type": "array", "items": { "type": "string" } },
        "isEnabled": { "type": "boolean" },
        "isNodePlugin": { "type": "boolean" }
      }
    },
    "getKeymapRequest": { "$ref": "#/definitions/emptyPayload" },
    "getKeymapResponse": {
      "type": "object", "additionalProperties": false, "required": ["plugins"],
      "properties": {
        "plugins": { "type": "array", "items": { "$ref": "#/definitions/keymapPlugin" } }
      }
    },
    "getKeymapPayload": {
      "anyOf": [
        { "$ref": "#/definitions/getKeymapRequest" },
        { "$ref": "#/definitions/getKeymapResponse" }
      ]
    },
    "saveKeymapRequest": {
      "type": "object", "additionalProperties": false, "required": ["overrides"],
      "properties": {
        "overrides": {
          "type": "object",
          "additionalProperties": { "$ref": "#/definitions/keymapOverride" }
        }
      }
    },
    "successResponse": {
      "type": "object", "additionalProperties": false, "required": ["success"],
      "properties": { "success": { "type": "boolean" } }
    },
    "saveKeymapResponse": { "$ref": "#/definitions/successResponse" },
    "saveKeymapPayload": {
      "anyOf": [
        { "$ref": "#/definitions/saveKeymapRequest" },
        { "$ref": "#/definitions/saveKeymapResponse" }
      ]
    },
    "validateKeymapRequest": {
      "type": "object", "additionalProperties": false,
      "properties": {
        "hotKeys": {
          "type": "object",
          "additionalProperties": { "type": ["string", "null"] }
        },
        "keywords": {
          "type": "object",
          "additionalProperties": {
            "oneOf": [
              { "type": "array", "items": { "type": "string" } },
              { "type": "null" }
            ]
          }
        }
      },
      "minProperties": 1
    },
    "keymapConflict": {
      "type": "object", "additionalProperties": false,
      "required": ["pluginId", "field", "value", "conflictsWith"],
      "properties": {
        "pluginId": { "$ref": "#/definitions/boundId" },
        "field": { "type": "string", "minLength": 1 },
        "value": { "type": "string" },
        "conflictsWith": { "type": "string", "minLength": 1 }
      }
    },
    "validateKeymapResponse": {
      "type": "object", "additionalProperties": false, "required": ["conflicts"],
      "properties": {
        "conflicts": { "type": "array", "items": { "$ref": "#/definitions/keymapConflict" } }
      }
    },
    "validateKeymapPayload": {
      "anyOf": [
        { "$ref": "#/definitions/validateKeymapRequest" },
        { "$ref": "#/definitions/validateKeymapResponse" }
      ]
    },
    "gesture": {
      "type": "object", "additionalProperties": false,
      "required": ["id", "directions", "actionName", "actionType", "processNames", "isEnabled"],
      "properties": {
        "id": { "type": "string" },
        "directions": { "type": "array", "items": { "type": "string", "minLength": 1 } },
        "actionName": { "type": "string", "minLength": 1 },
        "actionType": { "type": "string", "minLength": 1 },
        "hotKey": { "type": ["string", "null"] },
        "mouseButton": { "type": ["string", "null"] },
        "processNames": { "type": "array", "items": { "type": "string" } },
        "isEnabled": { "type": "boolean" }
      }
    },
    "getGesturesRequest": { "$ref": "#/definitions/emptyPayload" },
    "getGesturesResponse": {
      "type": "object", "additionalProperties": false, "required": ["gestures"],
      "properties": {
        "gestures": { "type": "array", "items": { "$ref": "#/definitions/gesture" } }
      }
    },
    "getGesturesPayload": {
      "anyOf": [
        { "$ref": "#/definitions/getGesturesRequest" },
        { "$ref": "#/definitions/getGesturesResponse" }
      ]
    },
    "saveGesturesRequest": {
      "type": "object", "additionalProperties": false, "required": ["gestures"],
      "properties": {
        "gestures": { "type": "array", "items": { "$ref": "#/definitions/gesture" } }
      }
    },
    "saveGesturesResponse": { "$ref": "#/definitions/successResponse" },
    "saveGesturesPayload": {
      "anyOf": [
        { "$ref": "#/definitions/saveGesturesRequest" },
        { "$ref": "#/definitions/saveGesturesResponse" }
      ]
    },
    "suspendGesturesRequest": { "$ref": "#/definitions/emptyPayload" },
    "suspendGesturesResponse": { "$ref": "#/definitions/emptyPayload" },
    "suspendGesturesPayload": {
      "anyOf": [
        { "$ref": "#/definitions/suspendGesturesRequest" },
        { "$ref": "#/definitions/suspendGesturesResponse" }
      ]
    },
    "resumeGesturesRequest": { "$ref": "#/definitions/emptyPayload" },
    "resumeGesturesResponse": { "$ref": "#/definitions/emptyPayload" },
    "resumeGesturesPayload": {
      "anyOf": [
        { "$ref": "#/definitions/resumeGesturesRequest" },
        { "$ref": "#/definitions/resumeGesturesResponse" }
      ]
    },
    "suspendHotkeysRequest": { "$ref": "#/definitions/emptyPayload" },
    "suspendHotkeysResponse": { "$ref": "#/definitions/emptyPayload" },
    "suspendHotkeysPayload": {
      "anyOf": [
        { "$ref": "#/definitions/suspendHotkeysRequest" },
        { "$ref": "#/definitions/suspendHotkeysResponse" }
      ]
    },
    "resumeHotkeysRequest": { "$ref": "#/definitions/emptyPayload" },
    "resumeHotkeysResponse": { "$ref": "#/definitions/emptyPayload" },
    "resumeHotkeysPayload": {
      "anyOf": [
        { "$ref": "#/definitions/resumeHotkeysRequest" },
        { "$ref": "#/definitions/resumeHotkeysResponse" }
      ]
    },
    "restartRequest": { "$ref": "#/definitions/emptyPayload" },
    "restartResponse": { "$ref": "#/definitions/emptyPayload" },
    "restartPayload": {
      "anyOf": [
        { "$ref": "#/definitions/restartRequest" },
        { "$ref": "#/definitions/restartResponse" }
      ]
    },
    "capabilityGrant": {
      "type": "object", "additionalProperties": false,
      "required": ["pluginId", "entryId", "capability", "scope", "grantedAtUtc", "revocable"],
      "properties": {
        "pluginId": { "$ref": "#/definitions/boundId" },
        "entryId": { "$ref": "#/definitions/boundId" },
        "capability": { "type": "string", "minLength": 1 },
        "scope": { "type": "string", "minLength": 1 },
        "grantedAtUtc": { "type": "string", "format": "date-time" },
        "revocable": { "type": "boolean" }
      }
    },
    "getAuthorizationsRequest": { "$ref": "#/definitions/emptyPayload" },
    "getAuthorizationsResponse": {
      "type": "object", "additionalProperties": false, "required": ["items"],
      "properties": {
        "items": { "type": "array", "items": { "$ref": "#/definitions/capabilityGrant" } }
      }
    },
    "getAuthorizationsPayload": {
      "anyOf": [
        { "$ref": "#/definitions/getAuthorizationsRequest" },
        { "$ref": "#/definitions/getAuthorizationsResponse" }
      ]
    },
    "revokeAuthorizationRequest": {
      "type": "object", "additionalProperties": false,
      "required": ["pluginId", "entryId", "capability"],
      "properties": {
        "pluginId": { "$ref": "#/definitions/boundId" },
        "entryId": { "$ref": "#/definitions/boundId" },
        "capability": { "type": "string", "minLength": 1 }
      }
    },
    "revokeAuthorizationResponse": {
      "type": "object", "additionalProperties": false, "required": ["revoked"],
      "properties": { "revoked": { "type": "boolean" } }
    },
    "revokeAuthorizationPayload": {
      "anyOf": [
        { "$ref": "#/definitions/revokeAuthorizationRequest" },
        { "$ref": "#/definitions/revokeAuthorizationResponse" }
      ]
    },
    "diagnosticEvent": {
      "type": "object", "additionalProperties": false,
      "required": ["name", "summary"],
      "properties": {
        "name": { "type": "string", "minLength": 1 },
        "summary": { "type": "string" }
      }
    },
    "getDiagnosticsRequest": { "$ref": "#/definitions/emptyPayload" },
    "getDiagnosticsResponse": {
      "type": "object", "additionalProperties": false,
      "required": ["recentEvents", "counters"],
      "properties": {
        "recentEvents": { "type": "array", "items": { "$ref": "#/definitions/diagnosticEvent" } },
        "counters": {
          "type": "object",
          "additionalProperties": { "type": "integer", "minimum": 0 }
        }
      }
    },
    "getDiagnosticsPayload": {
      "anyOf": [
        { "$ref": "#/definitions/getDiagnosticsRequest" },
        { "$ref": "#/definitions/getDiagnosticsResponse" }
      ]
    },
    "workerSpawnRequest": {
      "type": "object", "additionalProperties": false,
      "required": ["entry", "capabilities"],
      "properties": {
        "entry": { "type": "string", "minLength": 1 },
        "capabilities": {
          "type": "array", "uniqueItems": true,
          "items": { "type": "string", "minLength": 1 }
        }
      }
    },
    "workerSpawnResponse": {
      "type": "object", "additionalProperties": false,
      "required": ["identity", "capabilities"],
      "properties": {
        "identity": { "$ref": "#/definitions/endpointIdentity" },
        "capabilities": {
          "type": "array", "uniqueItems": true,
          "items": { "type": "string", "minLength": 1 }
        }
      }
    },
    "workerSpawnPayload": {
      "anyOf": [
        { "$ref": "#/definitions/workerSpawnRequest" },
        { "$ref": "#/definitions/workerSpawnResponse" }
      ]
    },
    "progressEvent": {
      "type": "object", "additionalProperties": false,
      "required": ["value"],
      "properties": { "value": { "type": "number" } }
    },
    "processReadyEvent": {
      "type": "object", "additionalProperties": false,
      "required": ["pid"],
      "properties": { "pid": { "type": "integer", "minimum": 1 } }
    },
    "keyEvent": {
      "type": "object", "additionalProperties": false,
      "required": ["key"],
      "properties": { "key": { "type": "string", "minLength": 1 } }
    },
    "localeChangedEvent": {
      "type": "object", "additionalProperties": false,
      "required": ["locale"],
      "properties": { "locale": { "type": "string", "minLength": 1 } }
    },
    "themeChangedEvent": {
      "type": "object", "additionalProperties": false,
      "required": ["theme"],
      "properties": { "theme": { "type": "string", "minLength": 1 } }
    },
    "messageEnvelope": {
      "title": "MessageEnvelope",
      "type": "object",
      "additionalProperties": true,
      "required": [
        "version",
        "id",
        "correlationId",
        "traceId",
        "sessionId",
        "pluginId",
        "entryId",
        "endpointId",
        "kind",
        "route",
        "timeoutMs",
        "payload",
        "error"
      ],
      "properties": {
        "version": { "$ref": "#/definitions/protocolVersion" },
        "id": { "$ref": "#/definitions/messageId" },
        "correlationId": {
          "oneOf": [
            { "$ref": "#/definitions/messageId" },
            { "type": "null" }
          ]
        },
        "traceId": { "$ref": "#/definitions/messageId" },
        "sessionId": {
          "oneOf": [
            { "$ref": "#/definitions/boundId" },
            { "type": "null" }
          ]
        },
        "pluginId": { "$ref": "#/definitions/boundId" },
        "entryId": { "$ref": "#/definitions/boundId" },
        "endpointId": {
          "oneOf": [
            { "$ref": "#/definitions/boundId" },
            { "type": "null" }
          ]
        },
        "kind": { "$ref": "#/definitions/messageKind" },
        "route": { "$ref": "#/definitions/route" },
        "timeoutMs": {
          "oneOf": [
            {
              "type": "integer",
              "minimum": 1,
              "maximum": 2147483647
            },
            { "type": "null" }
          ]
        },
        "payload": {},
        "error": {
          "oneOf": [
            { "$ref": "#/definitions/busError" },
            { "type": "null" }
          ]
        }
      },
      "allOf": [
        {
          "if": {
            "properties": { "kind": { "const": "request" } },
            "required": ["kind"]
          },
          "then": {
            "properties": {
              "correlationId": { "type": "null" },
              "timeoutMs": {
                "type": "integer",
                "minimum": 1,
                "maximum": 2147483647
              },
              "error": { "type": "null" }
            }
          }
        },
        {
          "if": {
            "properties": { "kind": { "const": "event" } },
            "required": ["kind"]
          },
          "then": {
            "properties": {
              "correlationId": { "type": "null" },
              "timeoutMs": { "type": "null" },
              "error": { "type": "null" }
            }
          }
        },
        {
          "if": {
            "properties": { "kind": { "const": "response" } },
            "required": ["kind"]
          },
          "then": {
            "properties": {
              "correlationId": { "$ref": "#/definitions/messageId" },
              "timeoutMs": { "type": "null" }
            },
            "oneOf": [
              {
                "properties": {
                  "error": { "type": "null" }
                }
              },
              {
                "properties": {
                  "payload": { "type": "null" },
                  "error": { "$ref": "#/definitions/busError" }
                }
              }
            ]
          }
        },
        {
          "if": {
            "properties": { "route": { "const": "bus.handshake" } },
            "required": ["route"]
          },
          "then": {
            "properties": {
              "kind": { "enum": ["request", "response"] }
            },
            "allOf": [
              {
                "if": {
                  "properties": { "kind": { "const": "request" } },
                  "required": ["kind"]
                },
                "then": {
                  "properties": {
                    "sessionId": { "type": "null" },
                    "endpointId": { "type": "null" },
                    "payload": { "$ref": "#/definitions/handshakeRequest" }
                  }
                }
              },
              {
                "if": {
                  "properties": {
                    "kind": { "const": "response" },
                    "error": { "type": "null" }
                  },
                  "required": ["kind", "error"]
                },
                "then": {
                  "properties": {
                    "payload": { "$ref": "#/definitions/handshakeResponse" }
                  }
                }
              }
            ]
          },
          "else": {
            "properties": {
              "sessionId": { "$ref": "#/definitions/boundId" },
              "endpointId": { "$ref": "#/definitions/boundId" }
            }
          }
        }
      ]
    }
  }
}
```

`additionalProperties: true` on the envelope deliberately implements the approved rule that a negotiated connection ignores unknown optional envelope fields. Payload DTOs remain closed where this plan defines them.

- [ ] **Step 3: Run schema tests (green)**

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: 3 tests pass and TypeScript build succeeds.

- [ ] **Step 4: Verify invalid required fields and all standard error codes**

Append to `schema.test.mjs`:

```javascript
test("exposes every approved stable error code", async () => {
  const schema = JSON.parse(await readFile(schemaUrl, "utf8"));
  assert.deepEqual(schema.definitions.protocolErrorCodeValue.enum, [
    "ProtocolMismatch", "HandshakeFailed", "CapabilityNotDeclared",
    "CapabilityDenied", "InvalidPayload", "MessageTooLarge",
    "RouteNotFound", "RequestTimeout", "Cancelled", "TooManyRequests",
    "TransportDisconnected", "PluginUnavailable", "PluginCrashed",
    "InternalError"
  ]);
});

test("freezes public titles, handshake fields, and route payload mappings", async () => {
  const schema = JSON.parse(await readFile(schemaUrl, "utf8"));
  assert.equal(schema.definitions.messageKind.title, "MessageKind");
  assert.equal(schema.definitions.endpointIdentity.title, "EndpointIdentity");
  assert.equal(schema.definitions.busError.title, "BusError");
  assert.equal(schema.definitions.messageEnvelope.title, "MessageEnvelope");
  assert.deepEqual(schema.definitions.handshakeRequest.required, [
    "supportedVersions", "launchToken", "pluginId", "entryId",
    "processId", "processStartedAtUtc"
  ]);
  assert.equal("endpointId" in schema.definitions.handshakeRequest.properties, false);
  assert.deepEqual(schema.definitions.handshakeResponse.required, [
    "selectedVersion", "sessionId", "endpointId"
  ]);
  const routes = schema["x-routePayloadSchemas"];
  const requiredRoutes = [
    "bus.handshake", "bus.cancel", "bus.subscribe", "bus.unsubscribe", "bus.ping", "bus.pong",
    "plugin.call.initialize", "plugin.call.search", "plugin.call.getConfiguration",
    "plugin.call.saveConfiguration", "plugin.call.getKeymap", "plugin.call.saveKeymap",
    "plugin.call.validateKeymap", "plugin.call.getGestures", "plugin.call.saveGestures",
    "plugin.call.suspendGestures", "plugin.call.resumeGestures",
    "plugin.call.suspendHotkeys", "plugin.call.resumeHotkeys", "plugin.call.restart",
    "plugin.call.getAuthorizations", "plugin.call.revokeAuthorization",
    "plugin.call.getDiagnostics", "host.call.configuration.read",
    "host.call.configuration.write", "host.call.keymap.read", "host.call.keymap.write",
    "host.call.keymap.validate", "host.call.gesture.read", "host.call.gesture.write",
    "host.call.gesture.suspend", "host.call.gesture.resume",
    "host.call.hotkey.suspend", "host.call.hotkey.resume",
    "host.call.application.restart", "host.call.worker.spawn",
    "host.call.authorization.list", "host.call.authorization.revoke",
    "host.call.diagnostics.read", "plugin.event.progress", "plugin.event.ready",
    "plugin.event.worker.ready", "host.event.initialize", "host.event.search",
    "host.event.key", "host.event.language-changed", "host.event.theme-changed"
  ];
  assert.deepEqual(Object.keys(routes).sort(), requiredRoutes.sort());
  for (const [route, phases] of Object.entries(routes)) {
    assert.ok("event" in phases || ("request" in phases && "response" in phases),
      `${route} must declare event or request/response`);
    for (const definition of Object.values(phases)) {
      assert.ok(schema.definitions[definition], `${route} -> missing ${definition}`);
    }
  }
  for (const definition of [
    "initializeRequest", "initializeResponse", "searchRequest", "searchResponse",
    "getConfigurationRequest", "getConfigurationResponse",
    "saveConfigurationRequest", "saveConfigurationResponse",
    "getKeymapRequest", "getKeymapResponse", "saveKeymapRequest", "saveKeymapResponse",
    "validateKeymapRequest", "validateKeymapResponse",
    "getGesturesRequest", "getGesturesResponse", "saveGesturesRequest", "saveGesturesResponse",
    "suspendGesturesRequest", "suspendGesturesResponse",
    "resumeGesturesRequest", "resumeGesturesResponse",
    "suspendHotkeysRequest", "suspendHotkeysResponse",
    "resumeHotkeysRequest", "resumeHotkeysResponse", "restartRequest", "restartResponse",
    "getAuthorizationsRequest", "getAuthorizationsResponse",
    "revokeAuthorizationRequest", "revokeAuthorizationResponse",
    "getDiagnosticsRequest", "getDiagnosticsResponse",
    "configurationReadRequest", "configurationReadResponse",
    "configurationWriteRequest", "configurationWriteResponse",
    "workerSpawnRequest", "workerSpawnResponse"
  ]) {
    assert.ok(schema.definitions[definition], `missing ${definition}`);
  }
});

test("validates migration request/response payloads and rejects malformed writes", async () => {
  const save = await loadValidator("saveConfigurationPayload");
  assert.equal(save({ changes: [{ fullPath: "General.Theme", value: "dark" }] }), true);
  assert.equal(save({ requiresRestart: false }), true);
  assert.equal(save({ changes: [{ fullPath: "", value: "dark" }] }), false);

  const worker = await loadValidator("workerSpawnPayload");
  assert.equal(worker({ entry: "workers/index.mjs", capabilities: ["configuration.read"] }), true);
  assert.equal(worker({
    identity: {
      pluginId: "settings", entryId: "main", sessionId: "session",
      endpointId: "node-worker-1"
    },
    capabilities: ["configuration.read"]
  }), true);

  const authorization = await loadValidator("revokeAuthorizationPayload");
  assert.equal(authorization({
    pluginId: "settings", entryId: "main", capability: "configuration.write"
  }), true);
  assert.equal(authorization({ revoked: true }), true);

  const handshake = await loadValidator("handshakeRequest");
  const base = {
    supportedVersions: ["3.0"], launchToken: "token", pluginId: "settings",
    entryId: "main", processStartedAtUtc: "2026-08-13T12:00:00Z"
  };
  assert.equal(handshake({ ...base, processId: 2147483647 }), true);
  assert.equal(handshake({ ...base, processId: 2147483648 }), false);
});

test("rejects a missing required identity", async () => {
  const validate = await loadValidator("messageEnvelope");
  assert.equal(validate({
    version: "3.0",
    id: "id",
    correlationId: null,
    traceId: "id",
    sessionId: "session",
    pluginId: "plugin",
    entryId: "entry",
    kind: "event",
    route: "plugin.event.changed",
    timeoutMs: null,
    payload: {},
    error: null
  }), false);
});
```

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: 7 tests pass. The route inventory test proves every route consumed by plans 2–5 resolves to a concrete schema and every migrated call has separately named request/response definitions; payload cases cover settings writes, worker spawn, and authorization revoke.

- [ ] **Step 5: Implement and test deterministic dynamic-route manifest generation**

Create `MyTools.Protocol.TypeScript\scripts\generate-route-manifest.mjs`:

```javascript
import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import Ajv from "ajv";

const dynamicRoute = /^(plugin\.call|host\.call|plugin\.event|host\.event|diagnostics)\.[A-Za-z0-9][A-Za-z0-9._-]*$/;

function containsRef(value) {
  if (Array.isArray(value)) return value.some(containsRef);
  if (!value || typeof value !== "object") return false;
  return "$ref" in value || Object.values(value).some(containsRef);
}

export function buildRouteManifest(pluginManifests) {
  const ajv = new Ajv({ allErrors: true, strict: true, allowUnionTypes: true });
  const routes = {};
  for (const manifest of pluginManifests) {
    if (!manifest.pluginId || !manifest.entryId || !manifest.routes) {
      throw new Error("Route manifest requires pluginId, entryId, and routes.");
    }
    for (const [route, schemas] of Object.entries(manifest.routes)) {
      if (!dynamicRoute.test(route) || route.startsWith("bus.")) {
        throw new Error(`Reserved or invalid route: ${route}`);
      }
      if (route in routes) throw new Error(`Duplicate route: ${route}`);
      if (!schemas || typeof schemas !== "object" ||
          !schemas.request || !schemas.response) {
        throw new Error(`Route ${route} requires request and response schemas.`);
      }
      if (containsRef(schemas.request) || containsRef(schemas.response)) {
        throw new Error(`Route ${route} schemas must be self-contained; $ref is not allowed.`);
      }
      ajv.compile(schemas.request);
      ajv.compile(schemas.response);
      routes[route] = {
        pluginId: manifest.pluginId,
        entryId: manifest.entryId,
        request: schemas.request,
        response: schemas.response
      };
    }
  }
  return {
    protocolVersion: "3.0",
    routes: Object.fromEntries(
      Object.entries(routes).sort(([left], [right]) => left.localeCompare(right))
    )
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [input, output] = process.argv.slice(2);
  if (!input || !output) {
    throw new Error("Usage: generate-route-manifest <plugin-manifests.json> <output.json>");
  }
  const manifests = JSON.parse(await readFile(input, "utf8"));
  const generated = `${JSON.stringify(buildRouteManifest(manifests), null, 2)}\n`;
  await writeFile(output, generated, "utf8");
}
```

Create `MyTools.Protocol.TypeScript\test\route-manifest.test.mjs`:

```javascript
import assert from "node:assert/strict";
import { test } from "node:test";
import Ajv from "ajv";
import { buildRouteManifest } from "../scripts/generate-route-manifest.mjs";

const renderRoute = {
  pluginId: "sample",
  entryId: "main",
  routes: {
    "plugin.call.sample.render": {
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

test("generates validators for declared routes", () => {
  const generated = buildRouteManifest([renderRoute]);
  const schemas = generated.routes["plugin.call.sample.render"];
  const validate = new Ajv({ strict: true, allowUnionTypes: true }).compile(schemas.request);
  assert.equal(validate({ text: "hello" }), true);
  assert.equal(validate({}), false);
});

test("rejects duplicate, reserved, incomplete, and externalized route schemas", () => {
  assert.throws(() => buildRouteManifest([renderRoute, renderRoute]), /Duplicate route/);
  assert.throws(() => buildRouteManifest([{
    ...renderRoute,
    routes: { "bus.handshake": renderRoute.routes["plugin.call.sample.render"] }
  }]), /Reserved or invalid route/);
  assert.throws(() => buildRouteManifest([{
    ...renderRoute,
    routes: { "plugin.call.sample.bad": { request: { type: "object" } } }
  }]), /request and response/);
  assert.throws(() => buildRouteManifest([{
    ...renderRoute,
    routes: {
      "plugin.call.sample.ref": {
        request: { $ref: "./request.schema.json" },
        response: { type: "object" }
      }
    }
  }]), /\$ref is not allowed/);
});
```

Plan 3's Node SDK and plan 4's Web SDK must call this generator from each plugin build, load `dist\route-manifest.json` before registering handlers, compile `request` for outgoing calls/incoming handlers and `response` for successful replies, and merge those validators with the canonical generated map. Host Core loads the same artifact only after plugin identity authentication and keys it by `(pluginId, entryId, route)`. A duplicate canonical route, cross-plugin route claim, invalid schema, or identity mismatch fails startup with `HandshakeFailed`; no permissive fallback validator is installed.

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: 9 schema/route-manifest tests pass. `plugin.call.sample.render` validates because it was declared; `{}` fails its request schema; an undeclared route still reaches the frozen `RouteNotFound` path.

- [ ] **Step 6: Commit the canonical contract**

```powershell
git add protocol\schemas\v3\protocol.schema.json MyTools.Protocol.TypeScript\scripts\generate-route-manifest.mjs MyTools.Protocol.TypeScript\test\schema.test.mjs MyTools.Protocol.TypeScript\test\route-manifest.test.mjs
git commit -m "feat: define message bus v3 schema" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 3: Generate C# and TypeScript contracts and validators

**Files:**
- Modify: `MyTools.Protocol.Generation\Program.cs`
- Create: `MyTools.Protocol\Generated\V3\ProtocolContracts.g.cs`
- Create: `MyTools.Protocol\Generated\V3\ProtocolSupport.g.cs`
- Create: `MyTools.Protocol\Generated\V3\ProtocolRoutes.g.cs`
- Create: `MyTools.Protocol\Validation\ProtocolValidator.cs`
- Create: `MyTools.Protocol.Test\Validation\ProtocolValidatorTest.cs`
- Create: `MyTools.Protocol.TypeScript\scripts\generate.mjs`
- Create: `MyTools.Protocol.TypeScript\src\generated\protocol.d.ts`
- Create: `MyTools.Protocol.TypeScript\src\generated\validators.ts`
- Create: `MyTools.Protocol.TypeScript\src\generated\routes.ts`
- Create: `MyTools.Protocol.TypeScript\src\validation.ts`
- Modify: `MyTools.Protocol.TypeScript\src\index.ts`

- [ ] **Step 1: Write host validation tests first (red)**

Create `MyTools.Protocol.Test\Validation\ProtocolValidatorTest.cs`:

```csharp
using MyTools.Protocol.V3;
using MyTools.Protocol.Validation;
using NUnit.Framework;
using System.Text.Json;

namespace MyTools.Protocol.Test.Validation;

[TestFixture]
public class ProtocolValidatorTest
{
    [Test]
    public void ValidateEnvelope_ShouldAcceptValidRequest()
    {
        const string json = """
        {
          "version":"3.0","id":"request","correlationId":null,
          "traceId":"request","sessionId":"session","pluginId":"settings",
          "entryId":"main","endpointId":"node-main","kind":"request",
          "route":"plugin.call.saveConfiguration","timeoutMs":30000,
          "payload":{"changes":[{"fullPath":"General.Theme","value":"dark"}]},"error":null
        }
        """;

        using var document = JsonDocument.Parse(json);
        var result = ProtocolValidator.ValidateEnvelope(document.RootElement);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateEnvelope_ShouldReturnPathsForInvalidPayload()
    {
        const string json = """
        {
          "version":"3.0","id":"request","correlationId":null,
          "traceId":"request","sessionId":"session","pluginId":"settings",
          "entryId":"main","endpointId":"node-main","kind":"request",
          "route":"bus.unknown","timeoutMs":0,"payload":{},
          "error":{"code":"InternalError","message":"bad","retryable":false,"details":null}
        }
        """;

        using var document = JsonDocument.Parse(json);
        var result = ProtocolValidator.ValidateEnvelope(document.RootElement);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Issues, Has.Some.Property("Path").Contains("route"));
    }

    [Test]
    public void ValidateRoutePayload_ShouldSelectSchemaByExactRoute()
    {
        IRoutePayloadValidator validator = new RoutePayloadValidator();

        var valid = validator.Validate(
            "bus.subscribe",
            JsonSerializer.SerializeToElement(new { topic = "host.event.theme-changed" }));
        var invalid = validator.Validate(
            "bus.subscribe",
            JsonSerializer.SerializeToElement(new { topic = "host.call.not-an-event" }));
        var migrated = validator.Validate(
            "host.call.worker.spawn",
            JsonSerializer.SerializeToElement(new
            {
                entry = "workers/index.mjs",
                capabilities = new[] { "configuration.read" }
            }));
        var unknown = validator.Validate("plugin.call.unregistered", JsonSerializer.SerializeToElement(new { }));

        Assert.Multiple(() =>
        {
            Assert.That(valid.IsValid, Is.True);
            Assert.That(invalid.IsValid, Is.False);
            Assert.That(migrated.IsValid, Is.True);
            Assert.That(unknown.Issues.Single().Message, Is.EqualTo(ProtocolErrorCodes.RouteNotFound));
        });
    }
}
```

Run:

```powershell
dotnet test .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj --nologo
```

Expected: FAIL to compile because the frozen generated types, `ProtocolValidator`, and `RoutePayloadValidator` do not exist.

- [ ] **Step 2: Implement deterministic C# generation**

Replace `MyTools.Protocol.Generation\Program.cs` with:

```csharp
using System.Text;
using System.Text.Json;
using NJsonSchema;

var repositoryRoot = args.Length == 1
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("Usage: MyTools.Protocol.Generation <repository-root>");
var schemaPath = Path.Combine(repositoryRoot, "protocol", "schemas", "v3", "protocol.schema.json");
var outputDirectory = Path.Combine(repositoryRoot, "MyTools.Protocol", "Generated", "V3");
Directory.CreateDirectory(outputDirectory);

var schema = await JsonSchema.FromFileAsync(schemaPath);
using var rawSchema = JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath));
var definitions = rawSchema.RootElement.GetProperty("definitions");
var frozenTitles = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["messageKind"] = "MessageKind",
    ["endpointIdentity"] = "EndpointIdentity",
    ["busError"] = "BusError",
    ["messageEnvelope"] = "MessageEnvelope",
    ["handshakeRequest"] = "HandshakeRequest",
    ["handshakeResponse"] = "HandshakeResponse"
};
foreach (var (definition, expectedTitle) in frozenTitles)
{
    var actualTitle = definitions.GetProperty(definition).GetProperty("title").GetString();
    if (!StringComparer.Ordinal.Equals(actualTitle, expectedTitle))
        throw new InvalidOperationException(
            $"Definition '{definition}' must have title '{expectedTitle}', found '{actualTitle}'.");
}
var frozenRequired = new Dictionary<string, string[]>(StringComparer.Ordinal)
{
    ["handshakeRequest"] =
    [
        "supportedVersions", "launchToken", "pluginId", "entryId",
        "processId", "processStartedAtUtc"
    ],
    ["handshakeResponse"] = ["selectedVersion", "sessionId", "endpointId"]
};
foreach (var (definition, expectedFields) in frozenRequired)
{
    var actualFields = definitions.GetProperty(definition).GetProperty("required")
        .EnumerateArray().Select(item => item.GetString()).ToArray();
    if (!actualFields.SequenceEqual(expectedFields, StringComparer.Ordinal))
        throw new InvalidOperationException(
            $"Definition '{definition}' required fields must be: {string.Join(", ", expectedFields)}.");
}

const string contracts = """
// <auto-generated />
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

public sealed record HandshakeRequest(
    IReadOnlyList<string> SupportedVersions,
    string LaunchToken,
    string PluginId,
    string EntryId,
    int ProcessId,
    DateTimeOffset ProcessStartedAtUtc);

public sealed record HandshakeResponse(
    string SelectedVersion, string SessionId, string EndpointId);

public sealed record EmptyPayload;
public sealed record SubscriptionPayload(string Topic);
""";
await WriteNormalizedAsync(
    Path.Combine(outputDirectory, "ProtocolContracts.g.cs"),
    contracts);

var errorCodes = schema.Definitions["protocolErrorCodeValue"].Enumeration
    .OfType<string>()
    .Order(StringComparer.Ordinal)
    .ToArray();
var errorMembers = string.Join(
    "\n",
    errorCodes.Select(code => $"    public const string {code} = \"{code}\";"));
var routePayloadSchemas = rawSchema.RootElement.GetProperty("x-routePayloadSchemas");
var routePayloadMembers = string.Join(
    ",\n",
    routePayloadSchemas.EnumerateObject()
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .Select(property =>
        {
            var phases = property.Value;
            var definition = phases.TryGetProperty("request", out var request)
                ? request.GetString()
                : phases.GetProperty("event").GetString();
            return $"        [\"{property.Name}\"] = \"{definition}\"";
        }));

var support = $$"""
// <auto-generated />
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTools.Protocol.V3;

public static class ProtocolSchemaIds
{
    public const string Document = "https://mytools.local/protocol/v3/protocol.schema.json";
    public const string MessageEnvelope = Document + "#/definitions/messageEnvelope";
    public const string HandshakeRequest = Document + "#/definitions/handshakeRequest";
    public const string HandshakeResponse = Document + "#/definitions/handshakeResponse";
}

public static class ProtocolErrorCodes
{
{{errorMembers}}
}

public static class ProtocolJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed record ValidationIssue(string Path, string Message);
public sealed record ValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues);

public interface IRoutePayloadValidator
{
    ValidationResult Validate(string route, JsonElement payload);
}

internal static class ProtocolRoutePayloadSchemas
{
    internal static IReadOnlyDictionary<string, string> Definitions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
{{routePayloadMembers}}
        };
}
""";
await WriteNormalizedAsync(
    Path.Combine(outputDirectory, "ProtocolSupport.g.cs"),
    support);

var busRoutes = schema.Definitions["route"].AnyOf
    .SelectMany(option => option.Enumeration)
    .OfType<string>()
    .Order(StringComparer.Ordinal)
    .ToArray();
var routeMembers = string.Join(
    "\n",
    busRoutes.Select(route =>
        $"    public const string {ToIdentifier(route)} = \"{route}\";"));
await WriteNormalizedAsync(
    Path.Combine(outputDirectory, "ProtocolRoutes.g.cs"),
    $$"""
    // <auto-generated />
    namespace MyTools.Protocol.V3;

    public static class ProtocolRoutes
    {
    {{routeMembers}}
    }
    """);

return 0;

static string ToIdentifier(string route) =>
    string.Concat(route.Split('.', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

static Task WriteNormalizedAsync(string path, string content)
{
    var normalized = content.Replace("\r\n", "\n").TrimEnd() + "\n";
    return File.WriteAllTextAsync(path, normalized, new UTF8Encoding(false));
}
```

Run:

```powershell
dotnet run --project .\MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj -- .
```

Expected: all three `.g.cs` files are created with the `// <auto-generated />` header; the contracts contain exactly the frozen records/enums in namespace `MyTools.Protocol.V3`, `ProtocolErrorCodes` contains all 14 string constants, `ProtocolJson.SerializerOptions` uses camel-case string enums, and `ProtocolRoutes.BusHandshake` equals `bus.handshake`. The script reads and asserts every required schema `title` before emitting the fixed public template, so it never delegates public naming to NJsonSchema defaults.

- [ ] **Step 3: Implement the host validator minimally (green)**

Create `MyTools.Protocol\Validation\ProtocolValidator.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using MyTools.Protocol.V3;
using NJsonSchema;
using NJsonSchema.Validation;

namespace MyTools.Protocol.Validation;

public static class ProtocolValidator
{
    private const string ResourceSuffix = ".Schemas.V3.protocol.schema.json";
    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema);

    public static ValidationResult ValidateEnvelope(JsonElement envelope) =>
        ValidateDefinition("messageEnvelope", envelope);

    internal static ValidationResult ValidateDefinition(
        string definitionName,
        JsonElement value)
    {
        if (!Schema.Value.Definitions.TryGetValue(definitionName, out var definition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definitionName),
                definitionName,
                "Unknown protocol schema definition.");
        }
        var issues = definition.Validate(value.GetRawText()).Select(ToIssue).ToArray();
        return new(issues.Length == 0, issues);
    }

    private static ValidationIssue ToIssue(ValidationError error) =>
        new(error.Path ?? string.Empty, error.ToString());

    private static JsonSchema LoadSchema()
    {
        var assembly = typeof(ProtocolValidator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromJsonAsync(reader.ReadToEnd()).GetAwaiter().GetResult();
    }
}

public sealed class RoutePayloadValidator : IRoutePayloadValidator
{
    public ValidationResult Validate(string route, JsonElement payload)
    {
        if (!ProtocolRoutePayloadSchemas.Definitions.TryGetValue(route, out var definition))
        {
            return new(false, [new ValidationIssue("/route", ProtocolErrorCodes.RouteNotFound)]);
        }
        return ProtocolValidator.ValidateDefinition(definition, payload);
    }
}
```

Run:

```powershell
dotnet test .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj --nologo
```

Expected: 3 tests pass: envelope validation, exact route-schema selection for bus and migrated worker routes, and deterministic `RouteNotFound` only for an undeclared route. `CapabilityGateway` in plan 2 receives `IRoutePayloadValidator` and calls it again server-side even when an SDK already validated the payload.

- [ ] **Step 4: Write TypeScript generation and standalone-validation tests (red)**

Append to `MyTools.Protocol.TypeScript\test\schema.test.mjs`:

```javascript
test("generated client validator rejects an invalid route", async () => {
  const { BUS_ROUTES, validateEnvelope } = await import("../dist/index.js");
  assert.equal(BUS_ROUTES.BusHandshake, "bus.handshake");
  const result = validateEnvelope({
    version: "3.0",
    id: "event",
    correlationId: null,
    traceId: "event",
    sessionId: "session",
    pluginId: "settings",
    entryId: "main",
    endpointId: "node-main",
    kind: "event",
    route: "bus.unknown",
    timeoutMs: null,
    payload: {},
    error: null
  });
  assert.equal(result.valid, false);
  assert.ok(result.errors.length > 0);
});

test("generated client route validator selects exact payload schema", async () => {
  const { validateRoutePayload, validateRouteResponsePayload } =
    await import("../dist/index.js");
  assert.equal(
    validateRoutePayload("bus.subscribe", { topic: "host.event.theme-changed" }).valid,
    true
  );
  assert.equal(
    validateRoutePayload("bus.subscribe", { topic: "host.call.invalid" }).valid,
    false
  );
  assert.equal(
    validateRoutePayload("plugin.call.saveConfiguration", {
      changes: [{ fullPath: "General.Theme", value: "dark" }]
    }).valid,
    true
  );
  assert.equal(
    validateRoutePayload("plugin.call.saveConfiguration", { requiresRestart: false }).valid,
    false
  );
  assert.equal(
    validateRouteResponsePayload(
      "plugin.call.saveConfiguration",
      { requiresRestart: false }
    ).valid,
    true
  );
  assert.equal(
    validateRoutePayload("host.call.worker.spawn", {
      entry: "workers/index.mjs",
      capabilities: ["configuration.read"]
    }).valid,
    true
  );
  const unknown = validateRoutePayload("plugin.call.unregistered", {});
  assert.equal(unknown.valid, false);
  assert.equal(unknown.errors[0].message, "RouteNotFound");
});

test("manifest-declared routes register with the exported validators", async () => {
  const {
    registerRouteManifest,
    validateRoutePayload,
    validateRouteResponsePayload
  } = await import("../dist/index.js");
  registerRouteManifest({
    protocolVersion: "3.0",
    routes: {
      "plugin.call.sample.render": {
        pluginId: "sample",
        entryId: "main",
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
  }, "sample", "main");
  assert.equal(validateRoutePayload("plugin.call.sample.render", { text: "hello" }).valid, true);
  assert.equal(validateRoutePayload("plugin.call.sample.render", {}).valid, false);
  assert.equal(
    validateRouteResponsePayload("plugin.call.sample.render", { html: "<p>hello</p>" }).valid,
    true
  );
  assert.throws(() => registerRouteManifest({
    protocolVersion: "3.0",
    routes: {
      "plugin.call.sample.other": {
        pluginId: "other", entryId: "main",
        request: { type: "object" }, response: { type: "object" }
      }
    }
  }, "sample", "main"), /identity mismatch/);
});
```

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: FAIL because `validateEnvelope` is not exported.

- [ ] **Step 5: Implement deterministic TypeScript and Ajv standalone generation**

Create `MyTools.Protocol.TypeScript\scripts\generate.mjs`:

```javascript
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv from "ajv";
import addFormats from "ajv-formats";
import standaloneCode from "ajv/dist/standalone/index.js";
import { compile } from "json-schema-to-typescript";

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repositoryRoot = resolve(packageRoot, "..");
const schemaPath = resolve(repositoryRoot, "protocol", "schemas", "v3", "protocol.schema.json");
const output = resolve(packageRoot, "src", "generated");
const schema = JSON.parse(await readFile(schemaPath, "utf8"));
await mkdir(output, { recursive: true });

const declarations = await compile(schema, "MessageEnvelope", {
  bannerComment: "/* eslint-disable */\n// <auto-generated />",
  style: { singleQuote: false },
  unknownAny: false
});
await writeFile(resolve(output, "protocol.d.ts"), declarations.replaceAll("\r\n", "\n"), "utf8");

const busRoutes = schema.definitions.route.anyOf
  .flatMap(option => option.enum ?? [])
  .sort((left, right) => left.localeCompare(right));
const routeEntries = busRoutes
  .map(route => {
    const name = route.split(".")
      .map(part => part[0].toUpperCase() + part.slice(1))
      .join("");
    return `  ${name}: ${JSON.stringify(route)}`;
  })
  .join(",\n");
await writeFile(
  resolve(output, "routes.ts"),
  `// <auto-generated />\nexport const BUS_ROUTES = {\n${routeEntries}\n} as const;\nexport type BusRoute = typeof BUS_ROUTES[keyof typeof BUS_ROUTES];\n`,
  "utf8"
);

const ajv = new Ajv({
  allErrors: true,
  strict: true,
  allowUnionTypes: true,
  code: { source: true, esm: true }
});
addFormats(ajv);
ajv.addKeyword({ keyword: "x-routePayloadSchemas", schemaType: "object" });
ajv.addSchema(schema);
const envelopeId = `${schema.$id}#/definitions/messageEnvelope`;
if (!ajv.getSchema(envelopeId)) {
  throw new Error(`Unable to compile ${envelopeId}`);
}
const validatorIds = { validateEnvelopeSchema: envelopeId };
const requestValidatorNames = {};
const responseValidatorNames = {};
for (const [route, phases] of Object.entries(schema["x-routePayloadSchemas"])) {
  const routeName = route.split(/[.-]/)
    .map(part => part[0].toUpperCase() + part.slice(1)).join("");
  for (const [phase, definition] of Object.entries(phases)) {
    const exportName = `validate${routeName}${phase[0].toUpperCase() + phase.slice(1)}PayloadSchema`;
    const id = `${schema.$id}#/definitions/${definition}`;
    if (!ajv.getSchema(id)) throw new Error(`Unable to compile ${id}`);
    validatorIds[exportName] = id;
    if (phase === "response") responseValidatorNames[route] = exportName;
    else requestValidatorNames[route] = exportName;
  }
}
const validatorModule = standaloneCode(ajv, validatorIds);
const requestRouteMap = Object.entries(requestValidatorNames)
  .map(([route, name]) => `  ${JSON.stringify(route)}: ${name}`)
  .join(",\n");
const responseRouteMap = Object.entries(responseValidatorNames)
  .map(([route, name]) => `  ${JSON.stringify(route)}: ${name}`)
  .join(",\n");
await writeFile(
  resolve(output, "validators.ts"),
  `// @ts-nocheck\n// <auto-generated />\n${validatorModule.replaceAll("\r\n", "\n")}` +
    `\nexport const routePayloadValidators = {\n${requestRouteMap}\n};\n` +
    `export const routeResponsePayloadValidators = {\n${responseRouteMap}\n};\n`,
  "utf8"
);
```

Create `MyTools.Protocol.TypeScript\src\validation.ts`:

```typescript
import Ajv from "ajv";
import type { ErrorObject, ValidateFunction } from "ajv";
import {
  routePayloadValidators,
  routeResponsePayloadValidators,
  validateEnvelopeSchema
} from "./generated/validators.js";

export type ValidationResult =
  | { readonly valid: true; readonly errors: readonly [] }
  | { readonly valid: false; readonly errors: readonly ErrorObject[] };

export type RouteManifest = Readonly<{
  protocolVersion: "3.0";
  routes: Readonly<Record<string, Readonly<{
    pluginId: string;
    entryId: string;
    request: object;
    response: object;
  }>>>;
}>;

const manifestRequests = new Map<string, ValidateFunction>();
const manifestResponses = new Map<string, ValidateFunction>();
const dynamicRoute =
  /^(plugin\.call|host\.call|plugin\.event|host\.event|diagnostics)\.[A-Za-z0-9][A-Za-z0-9._-]*$/;

export function validateEnvelope(value: unknown): ValidationResult {
  if (validateEnvelopeSchema(value)) {
    return { valid: true, errors: [] };
  }

  return {
    valid: false,
    errors: [...(validateEnvelopeSchema.errors ?? [])]
  };
}

export function validateRoutePayload(route: string, payload: unknown): ValidationResult {
  const validator = routePayloadValidators[
    route as keyof typeof routePayloadValidators
  ] ?? manifestRequests.get(route);
  return runRouteValidator(route, payload, validator);
}

export function validateRouteResponsePayload(
  route: string,
  payload: unknown
): ValidationResult {
  return runRouteValidator(route, payload,
    routeResponsePayloadValidators[
      route as keyof typeof routeResponsePayloadValidators
    ] ?? manifestResponses.get(route));
}

export function registerRouteManifest(
  manifest: RouteManifest,
  pluginId: string,
  entryId: string
): void {
  if (manifest.protocolVersion !== "3.0") {
    throw new Error("Route manifest protocol version mismatch.");
  }
  const ajv = new Ajv({ allErrors: true, strict: true, allowUnionTypes: true });
  const additions: Array<readonly [string, ValidateFunction, ValidateFunction]> = [];
  for (const [route, schemas] of Object.entries(manifest.routes)) {
    if (!dynamicRoute.test(route)) {
      throw new Error(`Reserved or invalid route: ${route}`);
    }
    if (schemas.pluginId !== pluginId || schemas.entryId !== entryId) {
      throw new Error(`Route manifest identity mismatch: ${route}`);
    }
    if (route in routePayloadValidators ||
        manifestRequests.has(route) ||
        manifestResponses.has(route)) {
      throw new Error(`Duplicate route: ${route}`);
    }
    const request = ajv.compile(schemas.request);
    const response = ajv.compile(schemas.response);
    additions.push([route, request, response]);
  }
  for (const [route, request, response] of additions) {
    manifestRequests.set(route, request);
    manifestResponses.set(route, response);
  }
}

function runRouteValidator(
  route: string,
  payload: unknown,
  validator: ValidateFunction | undefined
): ValidationResult {
  if (!validator) {
    return {
      valid: false,
      errors: [{
        instancePath: "/route",
        schemaPath: "",
        keyword: "route",
        params: {},
        message: "RouteNotFound"
      }]
    };
  }
  if (validator(payload)) return { valid: true, errors: [] };
  return { valid: false, errors: [...(validator.errors ?? [])] };
}
```

Replace `MyTools.Protocol.TypeScript\src\index.ts` with:

```typescript
export type * from "./generated/protocol.js";
export { BUS_ROUTES } from "./generated/routes.js";
export type { BusRoute } from "./generated/routes.js";
export {
  registerRouteManifest,
  validateEnvelope,
  validateRoutePayload,
  validateRouteResponsePayload
} from "./validation.js";
export type { RouteManifest, ValidationResult } from "./validation.js";
```

Run:

```powershell
npm run generate --prefix .\MyTools.Protocol.TypeScript
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: generated declarations/validator files are created, TypeScript compilation succeeds, and all 12 schema/route-manifest tests pass. The generator reads `schema["x-routePayloadSchemas"]`; `validateEnvelope` and `validateRoutePayload` are independent top-level exports. Canonical and identity-matched manifest routes validate through the exported facade, response schemas are compiled separately, and only an undeclared route returns `RouteNotFound`.

- [ ] **Step 6: Verify both language outputs are deterministic**

Run:

```powershell
$files = @(
    '.\MyTools.Protocol\Generated\V3\ProtocolContracts.g.cs'
    '.\MyTools.Protocol\Generated\V3\ProtocolSupport.g.cs'
    '.\MyTools.Protocol\Generated\V3\ProtocolRoutes.g.cs'
    '.\MyTools.Protocol.TypeScript\src\generated\protocol.d.ts'
    '.\MyTools.Protocol.TypeScript\src\generated\validators.ts'
    '.\MyTools.Protocol.TypeScript\src\generated\routes.ts'
)
$before = $files | ForEach-Object { git hash-object $_ }
dotnet run --project .\MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj -- .
npm run generate --prefix .\MyTools.Protocol.TypeScript
$after = $files | ForEach-Object { git hash-object $_ }
Compare-Object $before $after
```

Expected: `Compare-Object` prints nothing.

- [ ] **Step 7: Commit generation and runtime validation**

```powershell
git add MyTools.Protocol.Generation MyTools.Protocol\Generated MyTools.Protocol\Validation MyTools.Protocol.Test\Validation MyTools.Protocol.TypeScript
git commit -m "feat: generate protocol contracts and validators" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 4: Implement identical version negotiation in both languages

**Files:**
- Create: `protocol\test-vectors\v3\version-negotiation.json`
- Create: `MyTools.Protocol\Versioning\ProtocolVersionNegotiator.cs`
- Create: `MyTools.Protocol.Test\Versioning\ProtocolVersionNegotiatorTest.cs`
- Create: `MyTools.Protocol.TypeScript\src\versioning.ts`
- Create: `MyTools.Protocol.TypeScript\test\versioning.test.mjs`
- Modify: `MyTools.Protocol.TypeScript\src\index.ts`

- [ ] **Step 1: Add shared vectors and failing C# tests (red)**

Create `protocol\test-vectors\v3\version-negotiation.json`:

```json
[
  {
    "name": "selects highest common minor regardless of input order",
    "local": ["3.0", "3.2", "3.1"],
    "remote": ["3.1", "3.0"],
    "selected": "3.1"
  },
  {
    "name": "accepts exact only common version",
    "local": ["3.0"],
    "remote": ["3.0"],
    "selected": "3.0"
  },
  {
    "name": "accepts arbitrarily large canonical components",
    "local": ["999999999999999999999999.1"],
    "remote": ["999999999999999999999999.1"],
    "selected": "999999999999999999999999.1"
  },
  {
    "name": "rejects different majors",
    "local": ["3.0"],
    "remote": ["4.0"],
    "error": "ProtocolMismatch"
  },
  {
    "name": "rejects no common minor",
    "local": ["3.0", "3.1"],
    "remote": ["3.2"],
    "error": "HandshakeFailed"
  },
  {
    "name": "rejects no common minor even when another remote major exists",
    "local": ["3.0", "4.1"],
    "remote": ["3.2", "5.0"],
    "error": "HandshakeFailed"
  },
  {
    "name": "rejects malformed versions",
    "local": ["3.0"],
    "remote": ["3"],
    "error": "ProtocolMismatch"
  }
]
```

Create `MyTools.Protocol.Test\Versioning\ProtocolVersionNegotiatorTest.cs`:

```csharp
using System.Text.Json;
using MyTools.Protocol.Versioning;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Versioning;

[TestFixture]
public class ProtocolVersionNegotiatorTest
{
    public sealed record Vector(
        string Name,
        string[] Local,
        string[] Remote,
        string? Selected,
        string? Error);

    public static IEnumerable<TestCaseData> Vectors()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "TestVectors", "v3", "version-negotiation.json");
        var vectors = JsonSerializer.Deserialize<Vector[]>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return vectors.Select(value => new TestCaseData(value).SetName(value.Name));
    }

    [TestCaseSource(nameof(Vectors))]
    public void Negotiate_ShouldMatchSharedVector(Vector vector)
    {
        var result = ProtocolVersionNegotiator.Negotiate(vector.Local, vector.Remote);

        Assert.Multiple(() =>
        {
            Assert.That(result.SelectedVersion, Is.EqualTo(vector.Selected));
            Assert.That(result.ErrorCode, Is.EqualTo(vector.Error));
            Assert.That(result.IsSuccess, Is.EqualTo(vector.Selected is not null));
            Assert.That(
                (result.SelectedVersion is null) ^ (result.ErrorCode is null),
                Is.True,
                "result must contain exactly one of SelectedVersion or ErrorCode");
        });
    }
}
```

Run:

```powershell
dotnet test .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj --nologo
```

Expected: FAIL to compile because `ProtocolVersionNegotiator` does not exist.

- [ ] **Step 2: Implement minimal C# negotiation (green)**

Create `MyTools.Protocol\Versioning\ProtocolVersionNegotiator.cs`:

```csharp
using System.Globalization;
using System.Numerics;
using MyTools.Protocol.V3;

namespace MyTools.Protocol.Versioning;

public sealed record ProtocolNegotiationResult
{
    private ProtocolNegotiationResult(string? selectedVersion, string? errorCode)
    {
        SelectedVersion = selectedVersion;
        ErrorCode = errorCode;
    }

    public string? SelectedVersion { get; }
    public string? ErrorCode { get; }
    public bool IsSuccess => SelectedVersion is not null;

    public static ProtocolNegotiationResult Success(string selectedVersion) =>
        new(selectedVersion, null);

    public static ProtocolNegotiationResult Failure(string errorCode) =>
        new(null, errorCode);
}

public static class ProtocolVersionNegotiator
{
    public static ProtocolNegotiationResult Negotiate(
        IEnumerable<string> localVersions,
        IEnumerable<string> remoteVersions)
    {
        var local = ParseAll(localVersions);
        var remote = ParseAll(remoteVersions);
        if (local is null || remote is null)
        {
            return ProtocolNegotiationResult.Failure(ProtocolErrorCodes.ProtocolMismatch);
        }

        var commonMajors = local.Select(version => version.Major)
            .Intersect(remote.Select(version => version.Major))
            .ToHashSet();
        if (commonMajors.Count == 0)
        {
            return ProtocolNegotiationResult.Failure(ProtocolErrorCodes.ProtocolMismatch);
        }
        var selected = local
            .Join(remote, left => left, right => right, (left, _) => left)
            .OrderByDescending(version => version.Major)
            .ThenByDescending(version => version.Minor)
            .FirstOrDefault();
        return selected is null
            ? ProtocolNegotiationResult.Failure(ProtocolErrorCodes.HandshakeFailed)
            : ProtocolNegotiationResult.Success(selected.ToString());
    }

    private static HashSet<ProtocolVersion>? ParseAll(IEnumerable<string> values)
    {
        var parsed = new HashSet<ProtocolVersion>();
        foreach (var value in values)
        {
            if (!ProtocolVersion.TryParse(value, out var version))
            {
                return null;
            }
            parsed.Add(version);
        }
        return parsed.Count == 0 ? null : parsed;
    }

    private sealed record ProtocolVersion(BigInteger Major, BigInteger Minor)
    {
        public static bool TryParse(string value, out ProtocolVersion version)
        {
            version = null!;
            var parts = value.Split('.');
            if (parts.Length != 2 ||
                !BigInteger.TryParse(
                    parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
                !BigInteger.TryParse(
                    parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
                major < 1 || minor < 0 ||
                parts[0] != major.ToString(CultureInfo.InvariantCulture) ||
                parts[1] != minor.ToString(CultureInfo.InvariantCulture))
            {
                return false;
            }
            version = new ProtocolVersion(major, minor);
            return true;
        }

        public override string ToString() =>
            $"{Major.ToString(CultureInfo.InvariantCulture)}.{Minor.ToString(CultureInfo.InvariantCulture)}";
    }
}
```

Run:

```powershell
dotnet test .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj --nologo
```

Expected: all protocol tests pass. Malformed/empty sets and disjoint majors produce `ProtocolMismatch`; a shared major without an exact shared minor produces `HandshakeFailed`; successful results contain only `SelectedVersion`, and failures contain only `ErrorCode`.

- [ ] **Step 3: Write the shared-vector TypeScript test (red)**

Create `MyTools.Protocol.TypeScript\test\versioning.test.mjs`:

```javascript
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { test } from "node:test";
import { negotiateProtocolVersion } from "../dist/index.js";

const vectors = JSON.parse(await readFile(
  new URL("../../protocol/test-vectors/v3/version-negotiation.json", import.meta.url),
  "utf8"
));

for (const vector of vectors) {
  test(vector.name, () => {
    assert.deepEqual(
      negotiateProtocolVersion(vector.local, vector.remote),
      vector.selected
        ? { selectedVersion: vector.selected }
        : { errorCode: vector.error }
    );
  });
}
```

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: FAIL because `negotiateProtocolVersion` is not exported.

- [ ] **Step 4: Implement TypeScript negotiation and export it (green)**

Create `MyTools.Protocol.TypeScript\src\versioning.ts`:

```typescript
type Version = Readonly<{ major: bigint; minor: bigint; text: string }>;
export type ProtocolNegotiationResult =
  | Readonly<{ selectedVersion: string }>
  | Readonly<{ errorCode: "ProtocolMismatch" | "HandshakeFailed" }>;

function parseVersion(value: string): Version | undefined {
  const match = /^([1-9][0-9]*)\.(0|[1-9][0-9]*)$/.exec(value);
  if (!match) return undefined;
  const major = BigInt(match[1]!);
  const minor = BigInt(match[2]!);
  return { major, minor, text: `${major}.${minor}` };
}

export function negotiateProtocolVersion(
  localVersions: readonly string[],
  remoteVersions: readonly string[]
): ProtocolNegotiationResult {
  const local = localVersions.map(parseVersion);
  const remote = new Set(remoteVersions.map(parseVersion).map(value => value?.text));
  if (local.length === 0 || remoteVersions.length === 0 ||
      local.some(value => !value) || remote.has(undefined)) {
    return { errorCode: "ProtocolMismatch" };
  }
  const parsedRemote = remoteVersions.map(parseVersion) as Version[];
  const remoteMajors = new Set(parsedRemote.map(value => value.major));
  if (!(local as Version[]).some(value => remoteMajors.has(value.major))) {
    return { errorCode: "ProtocolMismatch" };
  }
  const selected = (local as Version[])
    .filter(value => remote.has(value.text))
    .sort((left, right) => {
      if (left.major !== right.major) return right.major > left.major ? 1 : -1;
      if (left.minor !== right.minor) return right.minor > left.minor ? 1 : -1;
      return 0;
    })[0];
  return selected
    ? { selectedVersion: selected.text }
    : { errorCode: "HandshakeFailed" };
}
```

Append to `MyTools.Protocol.TypeScript\src\index.ts`:

```typescript
export { negotiateProtocolVersion } from "./versioning.js";
export type { ProtocolNegotiationResult } from "./versioning.js";
```

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: all 12 schema/route-manifest tests and 7 negotiation tests pass. For every shared vector, C# returns `(SelectedVersion=value, ErrorCode=null)` or `(SelectedVersion=null, ErrorCode=code)`, while TypeScript returns exactly `{ selectedVersion }` or `{ errorCode }`; both use arbitrary-precision components, classify disjoint major as `ProtocolMismatch`, and classify shared-major/no-common-minor as `HandshakeFailed`.

- [ ] **Step 5: Commit cross-language negotiation**

```powershell
git add protocol\test-vectors\v3 MyTools.Protocol\Versioning MyTools.Protocol.Test\Versioning MyTools.Protocol.TypeScript\src MyTools.Protocol.TypeScript\test
git commit -m "feat: negotiate protocol versions consistently" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 5: Add little-endian JSON framing and deterministic fuzz coverage

**Files:**
- Create: `MyTools.Protocol\Framing\FrameReadResult.cs`
- Create: `MyTools.Protocol\Framing\LengthPrefixedJsonFrameCodec.cs`
- Create: `MyTools.Protocol.Test\Framing\LengthPrefixedJsonFrameCodecTest.cs`
- Create: `MyTools.Protocol.TypeScript\src\framing.ts`
- Create: `MyTools.Protocol.TypeScript\test\framing.test.mjs`
- Modify: `MyTools.Protocol.TypeScript\src\index.ts`

- [ ] **Step 1: Write C# framing and fuzz tests first (red)**

Create `MyTools.Protocol.Test\Framing\LengthPrefixedJsonFrameCodecTest.cs`:

```csharp
using System.Buffers.Binary;
using System.Text;
using MyTools.Protocol.Framing;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Framing;

[TestFixture]
public class LengthPrefixedJsonFrameCodecTest
{
    [Test]
    public void Encode_ShouldPrefixUtf8ByteLengthAsLittleEndian()
    {
        var frame = LengthPrefixedJsonFrameCodec.Encode("""{"text":"雪"}""");
        Assert.Multiple(() =>
        {
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame), Is.EqualTo(frame.Length - 4));
            Assert.That(Encoding.UTF8.GetString(frame.AsSpan(4)), Is.EqualTo("""{"text":"雪"}"""));
        });
    }

    [Test]
    public async Task StreamApi_ShouldRoundTripOneFrame()
    {
        await using var stream = new MemoryStream();
        var payload = Encoding.UTF8.GetBytes("""{"text":"雪"}""");

        await LengthPrefixedJsonFrameCodec.WriteAsync(stream, payload, CancellationToken.None);
        stream.Position = 0;
        var decoded = await LengthPrefixedJsonFrameCodec.ReadAsync(
            stream, LengthPrefixedJsonFrameCodec.DefaultMaxFrameBytes, CancellationToken.None);

        Assert.That(decoded, Is.EqualTo(payload));
    }

    [Test]
    public void ReadAsync_ShouldRejectOversizedPrefixBeforeReadingPayload()
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, 4_194_305);
        using var stream = new MemoryStream(prefix);

        Assert.ThrowsAsync<ProtocolFrameException>(async () =>
            await LengthPrefixedJsonFrameCodec.ReadAsync(
                stream, LengthPrefixedJsonFrameCodec.DefaultMaxFrameBytes, CancellationToken.None));
        Assert.That(stream.Position, Is.EqualTo(4));
    }

    [Test]
    public void TryRead_ShouldHandleEverySplitAndTwoCoalescedFrames()
    {
        var first = LengthPrefixedJsonFrameCodec.Encode("""{"id":1}""");
        var second = LengthPrefixedJsonFrameCodec.Encode("""{"id":2}""");
        var joined = first.Concat(second).ToArray();

        for (var split = 0; split < first.Length; split++)
        {
            Assert.That(
                LengthPrefixedJsonFrameCodec.TryRead(joined.AsSpan(0, split)).Status,
                Is.EqualTo(FrameReadStatus.NeedMoreData));
        }

        var result = LengthPrefixedJsonFrameCodec.TryRead(joined);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(FrameReadStatus.Complete));
            Assert.That(result.Json, Is.EqualTo("""{"id":1}"""));
            Assert.That(result.ConsumedBytes, Is.EqualTo(first.Length));
        });
    }

    [TestCase(0u, FrameReadStatus.InvalidLength)]
    [TestCase(4_194_305u, FrameReadStatus.MessageTooLarge)]
    public void TryRead_ShouldRejectInvalidLengths(uint length, FrameReadStatus expected)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, length);
        Assert.That(LengthPrefixedJsonFrameCodec.TryRead(prefix).Status, Is.EqualTo(expected));
    }

    [Test]
    public void TryRead_ShouldRejectInvalidUtf8OrJson()
    {
        var invalidUtf8 = new byte[] { 2, 0, 0, 0, 0xC3, 0x28 };
        var invalidJson = LengthPrefixedJsonFrameCodec.EncodeBytes(Encoding.UTF8.GetBytes("{"));
        Assert.Multiple(() =>
        {
            Assert.That(LengthPrefixedJsonFrameCodec.TryRead(invalidUtf8).Status,
                Is.EqualTo(FrameReadStatus.InvalidJson));
            Assert.That(LengthPrefixedJsonFrameCodec.TryRead(invalidJson).Status,
                Is.EqualTo(FrameReadStatus.InvalidJson));
        });
    }

    [Test]
    public void TryRead_ShouldRejectTruncationAtEndOfInput()
    {
        var frame = LengthPrefixedJsonFrameCodec.Encode("""{"id":1}""");
        var result = LengthPrefixedJsonFrameCodec.TryRead(
            frame.AsSpan(0, frame.Length - 1),
            isFinalBlock: true);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(FrameReadStatus.Truncated));
            Assert.That(result.ConsumedBytes, Is.Zero);
        });
    }

    [Test]
    public void Fuzz_ShouldNeverConsumePartialFramesOrThrow()
    {
        var random = new Random(0x5EED);
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var bytes = new byte[random.Next(0, 512)];
            random.NextBytes(bytes);
            var result = LengthPrefixedJsonFrameCodec.TryRead(bytes, maxFrameBytes: 256);
            Assert.That(result.ConsumedBytes, Is.InRange(0, bytes.Length));
            if (result.Status == FrameReadStatus.NeedMoreData)
                Assert.That(result.ConsumedBytes, Is.Zero);
        }
    }
}
```

Run:

```powershell
dotnet test .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj --nologo
```

Expected: FAIL to compile because framing types do not exist.

- [ ] **Step 2: Implement the C# frame codec (green)**

Create `MyTools.Protocol\Framing\FrameReadResult.cs`:

```csharp
namespace MyTools.Protocol.Framing;

public enum FrameReadStatus
{
    Complete,
    NeedMoreData,
    Truncated,
    InvalidLength,
    MessageTooLarge,
    InvalidJson
}

public sealed record FrameReadResult(
    FrameReadStatus Status,
    int ConsumedBytes = 0,
    string? Json = null);
```

Create `MyTools.Protocol\Framing\LengthPrefixedJsonFrameCodec.cs`:

```csharp
using System.Buffers.Binary;
using System.Buffers;
using System.Text;
using System.Text.Json;
using MyTools.Protocol.V3;

namespace MyTools.Protocol.Framing;

public static class LengthPrefixedJsonFrameCodec
{
    public const int PrefixBytes = 4;
    public const int DefaultMaxFrameBytes = 4 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async ValueTask<byte[]> ReadAsync(
        Stream stream,
        int maxFrameBytes = DefaultMaxFrameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[PrefixBytes];
        await stream.ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (length == 0)
            throw new ProtocolFrameException(ProtocolErrorCodes.InvalidPayload, "Zero-length frame.");
        if (length > maxFrameBytes || length > int.MaxValue)
            throw new ProtocolFrameException(ProtocolErrorCodes.MessageTooLarge, $"Frame length {length}.");

        var frameBytes = checked((int)length);
        var rented = ArrayPool<byte>.Shared.Rent(frameBytes);
        try
        {
            await stream.ReadExactlyAsync(
                rented.AsMemory(0, frameBytes), cancellationToken).ConfigureAwait(false);
            ValidateJson(rented.AsSpan(0, frameBytes));
            return rented.AsSpan(0, frameBytes).ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default,
        int maxFrameBytes = DefaultMaxFrameBytes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (utf8Json.IsEmpty)
            throw new ProtocolFrameException(ProtocolErrorCodes.InvalidPayload, "Zero-length frame.");
        if (utf8Json.Length > maxFrameBytes)
            throw new ProtocolFrameException(
                ProtocolErrorCodes.MessageTooLarge, $"Frame length {utf8Json.Length}.");
        ValidateJson(utf8Json.Span);

        var prefix = new byte[PrefixBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, checked((uint)utf8Json.Length));
        await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(utf8Json, cancellationToken).ConfigureAwait(false);
    }

    public static byte[] Encode(string json, int maxFrameBytes = DefaultMaxFrameBytes)
    {
        using var _ = JsonDocument.Parse(json);
        return EncodeBytes(StrictUtf8.GetBytes(json), maxFrameBytes);
    }

    public static byte[] EncodeBytes(
        ReadOnlySpan<byte> utf8Json,
        int maxFrameBytes = DefaultMaxFrameBytes)
    {
        if (utf8Json.Length == 0) throw new ArgumentException("Frame payload cannot be empty.");
        if (utf8Json.Length > maxFrameBytes) throw new ArgumentOutOfRangeException(nameof(utf8Json));
        var output = new byte[PrefixBytes + utf8Json.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(output, checked((uint)utf8Json.Length));
        utf8Json.CopyTo(output.AsSpan(PrefixBytes));
        return output;
    }

    private static void ValidateJson(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var json = StrictUtf8.GetString(utf8Json);
            using var _ = JsonDocument.Parse(json);
        }
        catch (Exception exception) when (
            exception is DecoderFallbackException or JsonException)
        {
            throw new ProtocolFrameException(
                ProtocolErrorCodes.InvalidPayload, "Frame is not valid UTF-8 JSON.", exception);
        }
    }

    public static FrameReadResult TryRead(
        ReadOnlySpan<byte> input,
        int maxFrameBytes = DefaultMaxFrameBytes,
        bool isFinalBlock = false)
    {
        if (input.Length < PrefixBytes)
            return input.IsEmpty || !isFinalBlock
                ? new(FrameReadStatus.NeedMoreData)
                : new(FrameReadStatus.Truncated);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(input);
        if (length == 0) return new(FrameReadStatus.InvalidLength);
        if (length > maxFrameBytes || length > int.MaxValue)
            return new(FrameReadStatus.MessageTooLarge);
        var frameBytes = checked((int)length);
        if (input.Length - PrefixBytes < frameBytes)
            return new(isFinalBlock
                ? FrameReadStatus.Truncated
                : FrameReadStatus.NeedMoreData);
        try
        {
            var json = StrictUtf8.GetString(input.Slice(PrefixBytes, frameBytes));
            using var _ = JsonDocument.Parse(json);
            return new(FrameReadStatus.Complete, PrefixBytes + frameBytes, json);
        }
        catch (Exception exception) when (
            exception is DecoderFallbackException or JsonException)
        {
            return new(FrameReadStatus.InvalidJson);
        }
    }
}

public sealed class ProtocolFrameException : Exception
{
    public ProtocolFrameException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
```

Run:

```powershell
dotnet test .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj --nologo
```

Expected: all 19 protocol NUnit cases pass. `ReadAsync` consumes only the four-byte header before rejecting 4 MiB + 1, and no payload-sized allocation/rental occurs until the unsigned length is non-zero, `<= maxFrameBytes`, and `<= int.MaxValue`.

- [ ] **Step 3: Write TypeScript framing tests first (red)**

Create `MyTools.Protocol.TypeScript\test\framing.test.mjs`:

```javascript
import assert from "node:assert/strict";
import { test } from "node:test";
import { encodeJsonFrame, tryReadJsonFrame } from "../dist/index.js";

test("encodes little-endian UTF-8 and reads coalesced frames", () => {
  const first = encodeJsonFrame({ text: "雪" });
  const second = encodeJsonFrame({ id: 2 });
  assert.equal(new DataView(first.buffer, first.byteOffset, 4).getUint32(0, true), first.length - 4);
  const joined = new Uint8Array(first.length + second.length);
  joined.set(first);
  joined.set(second, first.length);
  const result = tryReadJsonFrame(joined);
  assert.deepEqual(result, {
    status: "complete",
    consumedBytes: first.length,
    value: { text: "雪" }
  });
});

test("handles every fragmentation boundary", () => {
  const frame = encodeJsonFrame({ id: 1 });
  for (let split = 0; split < frame.length; split++) {
    assert.deepEqual(tryReadJsonFrame(frame.subarray(0, split)), {
      status: "needMoreData",
      consumedBytes: 0
    });
  }
});

test("rejects zero, oversized, invalid UTF-8, and invalid JSON", () => {
  assert.equal(tryReadJsonFrame(Uint8Array.of(0, 0, 0, 0)).status, "invalidLength");
  assert.equal(tryReadJsonFrame(Uint8Array.of(1, 0, 0, 1), 256).status, "messageTooLarge");
  assert.equal(tryReadJsonFrame(Uint8Array.of(2, 0, 0, 0, 0xC3, 0x28)).status, "invalidJson");
  assert.equal(tryReadJsonFrame(Uint8Array.of(1, 0, 0, 0, 0x7B)).status, "invalidJson");
});

test("rejects a truncated final block", () => {
  const frame = encodeJsonFrame({ id: 1 });
  assert.deepEqual(
    tryReadJsonFrame(frame.subarray(0, frame.length - 1), 4 * 1024 * 1024, true),
    { status: "truncated", consumedBytes: 0 }
  );
});

test("deterministic fuzz never consumes partial input or throws", () => {
  let state = 0x5EED;
  const next = () => {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    return state;
  };
  for (let iteration = 0; iteration < 2000; iteration++) {
    const bytes = new Uint8Array(next() % 512);
    for (let index = 0; index < bytes.length; index++) bytes[index] = next() & 0xFF;
    const result = tryReadJsonFrame(bytes, 256);
    assert.ok(result.consumedBytes >= 0 && result.consumedBytes <= bytes.length);
    if (result.status === "needMoreData") assert.equal(result.consumedBytes, 0);
  }
});
```

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: FAIL because framing functions are not exported.

- [ ] **Step 4: Implement TypeScript framing and export it (green)**

Create `MyTools.Protocol.TypeScript\src\framing.ts`:

```typescript
const PREFIX_BYTES = 4;
export const DEFAULT_MAX_FRAME_BYTES = 4 * 1024 * 1024;
const encoder = new TextEncoder();
const decoder = new TextDecoder("utf-8", { fatal: true });

export type FrameReadResult =
  | Readonly<{ status: "complete"; consumedBytes: number; value: unknown }>
  | Readonly<{ status: "needMoreData" | "truncated" | "invalidLength" | "messageTooLarge" | "invalidJson"; consumedBytes: 0 }>;

export function encodeJsonFrame(
  value: unknown,
  maxFrameBytes = DEFAULT_MAX_FRAME_BYTES
): Uint8Array {
  const json = JSON.stringify(value);
  if (json === undefined) throw new TypeError("Value is not JSON serializable.");
  const payload = encoder.encode(json);
  if (payload.length === 0) throw new RangeError("Frame payload cannot be empty.");
  if (payload.length > maxFrameBytes) throw new RangeError("Frame exceeds maxFrameBytes.");
  const frame = new Uint8Array(PREFIX_BYTES + payload.length);
  new DataView(frame.buffer).setUint32(0, payload.length, true);
  frame.set(payload, PREFIX_BYTES);
  return frame;
}

export function tryReadJsonFrame(
  input: Uint8Array,
  maxFrameBytes = DEFAULT_MAX_FRAME_BYTES,
  isFinalBlock = false
): FrameReadResult {
  if (input.length < PREFIX_BYTES) {
    return input.length === 0 || !isFinalBlock
      ? { status: "needMoreData", consumedBytes: 0 }
      : { status: "truncated", consumedBytes: 0 };
  }
  const length = new DataView(input.buffer, input.byteOffset, PREFIX_BYTES).getUint32(0, true);
  if (length === 0) return { status: "invalidLength", consumedBytes: 0 };
  if (length > maxFrameBytes) return { status: "messageTooLarge", consumedBytes: 0 };
  if (input.length - PREFIX_BYTES < length) {
    return isFinalBlock
      ? { status: "truncated", consumedBytes: 0 }
      : { status: "needMoreData", consumedBytes: 0 };
  }
  try {
    const json = decoder.decode(input.subarray(PREFIX_BYTES, PREFIX_BYTES + length));
    return {
      status: "complete",
      consumedBytes: PREFIX_BYTES + length,
      value: JSON.parse(json) as unknown
    };
  } catch {
    return { status: "invalidJson", consumedBytes: 0 };
  }
}
```

Append to `MyTools.Protocol.TypeScript\src\index.ts`:

```typescript
export {
  DEFAULT_MAX_FRAME_BYTES,
  encodeJsonFrame,
  tryReadJsonFrame
} from "./framing.js";
export type { FrameReadResult } from "./framing.js";
```

Run:

```powershell
npm test --prefix .\MyTools.Protocol.TypeScript
```

Expected: schema, negotiation, framing, explicit final-block truncation, fragmentation, and 2,000-case fuzz tests all pass.

- [ ] **Step 5: Commit framing atomically**

```powershell
git add MyTools.Protocol\Framing MyTools.Protocol.Test\Framing MyTools.Protocol.TypeScript\src MyTools.Protocol.TypeScript\test
git commit -m "feat: add protocol JSON frame codecs" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

### Task 6: Enforce generated consistency in CI and run the complete gate

**Files:**
- Create: `scripts\verify-protocol-generated.ps1`
- Modify: `.github\workflows\release.yml`

- [ ] **Step 1: Write the drift check and prove it detects drift (red)**

Create `scripts\verify-protocol-generated.ps1`:

```powershell
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet run --project .\MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj -- .
    if ($LASTEXITCODE -ne 0) { throw 'C# protocol generation failed.' }

    npm run generate --prefix .\MyTools.Protocol.TypeScript
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript protocol generation failed.' }

    $generated = @(
        'MyTools.Protocol/Generated',
        'MyTools.Protocol.TypeScript/src/generated'
    )
    $diff = git status --short --untracked-files=all -- @generated
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect generated protocol files.' }
    if ($diff) {
        Write-Error "Generated protocol files are stale.`n$diff"
        exit 1
    }
}
finally {
    Pop-Location
}
```

Temporarily add a schema route so regeneration must change tracked outputs, then restore the schema:

```powershell
$schemaPath = '.\protocol\schemas\v3\protocol.schema.json'
$original = [IO.File]::ReadAllText((Resolve-Path $schemaPath))
try {
    $changed = $original.Replace('"bus.pong"', '"bus.pong", "bus.test-only"')
    [IO.File]::WriteAllText((Resolve-Path $schemaPath), $changed, [Text.UTF8Encoding]::new($false))
    pwsh -NoProfile -File .\scripts\verify-protocol-generated.ps1
    if ($LASTEXITCODE -eq 0) { throw 'Expected stale generation check to fail.' }
}
finally {
    [IO.File]::WriteAllText((Resolve-Path $schemaPath), $original, [Text.UTF8Encoding]::new($false))
    dotnet run --project .\MyTools.Protocol.Generation\MyTools.Protocol.Generation.csproj -- .
    npm run generate --prefix .\MyTools.Protocol.TypeScript
}
```

Expected: the first script call exits 1 with `Generated protocol files are stale`; the `finally` block restores the schema and canonical outputs. Run `.\scripts\verify-protocol-generated.ps1` once more and expect exit code 0.

- [ ] **Step 2: Add protocol npm caching and generation to the existing release workflow**

In `.github\workflows\release.yml`, extend `Set up Node.js`:

```yaml
          cache-dependency-path: |
            MyTools.Plugins/Examples/package-lock.json
            MyTools.Protocol.TypeScript/package-lock.json
```

Insert after `Build Node plugin examples` and before `.NET` restore:

```yaml
      - name: Install protocol generation dependencies
        shell: pwsh
        run: npm ci --prefix .\MyTools.Protocol.TypeScript

      - name: Verify generated protocol contracts
        shell: pwsh
        run: .\scripts\verify-protocol-generated.ps1

      - name: Test TypeScript protocol package
        shell: pwsh
        run: npm test --prefix .\MyTools.Protocol.TypeScript
```

This preserves the existing release trigger and packaging flow; it only adds protocol gates.

- [ ] **Step 3: Run the exact local CI generation and protocol gates**

Run:

```powershell
dotnet restore .\MyTools.sln
npm ci --prefix .\MyTools.Protocol.TypeScript
.\scripts\verify-protocol-generated.ps1
npm test --prefix .\MyTools.Protocol.TypeScript
dotnet test .\MyTools.Protocol.Test\MyTools.Protocol.Test.csproj --configuration Release --no-restore --nologo
```

Expected: generation has no diff; all TypeScript tests and all protocol NUnit tests pass.

- [ ] **Step 4: Run the repository-wide existing gates**

Run:

```powershell
npm ci --prefix .\MyTools.Plugins\Examples
npm run check --prefix .\MyTools.Plugins\Examples
npm run build --prefix .\MyTools.Plugins\Examples
dotnet test .\MyTools.sln --configuration Release --no-restore --nologo --filter "FullyQualifiedName!~OpenAIServiceTest"
git status --short
```

Expected:

- Node example check/build succeeds.
- Existing 123 tests plus the new protocol tests pass, with 0 failures.
- `git status --short` lists only files intentionally added/modified by these six tasks; no `bin`, `obj`, `dist`, or `node_modules` paths appear.

- [ ] **Step 5: Audit plan boundaries and generated ownership**

Run:

```powershell
rg -n "MessageBus|PluginSession|NamedPipe|WebView2|migration|stdio" .\MyTools.Protocol .\MyTools.Protocol.TypeScript\src .\protocol
rg -n "<auto-generated />" .\MyTools.Protocol\Generated .\MyTools.Protocol.TypeScript\src\generated
rg -n "ProtocolMismatch|HandshakeFailed|CapabilityNotDeclared|CapabilityDenied|InvalidPayload|MessageTooLarge|RouteNotFound|RequestTimeout|Cancelled|TooManyRequests|TransportDisconnected|PluginUnavailable|PluginCrashed|InternalError" .\protocol\schemas\v3\protocol.schema.json
$legacyNames = @(
    'Protocol' + 'Validators',
    'ProtocolError' + 'Code',
    'LengthPrefixed' + 'FrameCodec',
    'MyToolsProtocol' + 'V3',
    'Protocol' + 'Envelope'
) -join '|'
rg -n "\b($legacyNames)\b" .\MyTools.Protocol .\MyTools.Protocol.Generation .\MyTools.Protocol.Test .\MyTools.Protocol.TypeScript
rg -n "namespace MyTools.Protocol.V3|MessageKind|EndpointIdentity|BusError|MessageEnvelope|ProtocolErrorCodes|ProtocolJson|IRoutePayloadValidator|validateEnvelope|validateRoutePayload|LengthPrefixedJsonFrameCodec" .\MyTools.Protocol .\MyTools.Protocol.TypeScript
```

Expected:

- The first command has no implementation hits; schema references to route names are acceptable, but there are no bus/session/transport classes.
- Every generated file has the ownership marker.
- All 14 approved stable error codes are present.
- The old-name scan prints nothing; the frozen-name scan finds every required C#/TypeScript public contract.

- [ ] **Step 6: Commit CI enforcement**

```powershell
git add scripts\verify-protocol-generated.ps1 .github\workflows\release.yml
git commit -m "ci: verify generated protocol contracts" -m "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>"
```

## Final acceptance checklist

- `protocol\schemas\v3\protocol.schema.json` is the only hand-edited wire contract.
- Generated C# public contracts are exactly `MyTools.Protocol.V3.MessageKind`, `EndpointIdentity`, `BusError`, `MessageEnvelope`, `ProtocolErrorCodes`, `ProtocolJson.SerializerOptions`, and `IRoutePayloadValidator`; schema titles and generator assertions prevent default-name drift.
- Handshake request fields are exactly `supportedVersions`, `launchToken`, `pluginId`, `entryId`, `processId`, and `processStartedAtUtc`; only the successful response contains host-assigned `selectedVersion`, `sessionId`, and `endpointId`.
- Both host and client validation reject malformed envelopes and unknown bus routes. C# keeps `IRoutePayloadValidator.Validate(string route, JsonElement payload)`; TypeScript keeps independent top-level `validateEnvelope` and `validateRoutePayload(route, payload)` exports and adds `validateRouteResponsePayload`/`registerRouteManifest` without changing either frozen signature.
- Canonical `x-routePayloadSchemas` contains every concrete route consumed by plans 2–5, including all settings migration calls, host configuration/authorization/diagnostics/worker calls, reserved bus routes, and concrete events; every migrated call has separately named request and response definitions.
- Plugin-specific routes outside that frozen inventory must be declared by manifest and emitted by `generate-route-manifest.mjs`; SDK and host register the same generated artifact, reject conflicts during startup, validate declared routes, and return `RouteNotFound` only for routes absent from both registries. SDK route validation is advisory; plan 2 `CapabilityGateway` always revalidates the route payload server-side before handler dispatch.
- Unknown optional envelope fields remain accepted after negotiation.
- Version tests use one shared vector file and agree on highest common exact `major.minor`: disjoint major is `ProtocolMismatch`, while a shared major with no common minor is `HandshakeFailed`; C# and TypeScript results contain exactly one success or error member.
- Both codecs use unsigned 4-byte little-endian lengths, strict UTF-8 JSON, a 4 MiB default maximum, incremental fragmentation, and coalesced-frame consumption. C# publicly exposes `LengthPrefixedJsonFrameCodec.ReadAsync(Stream, ...)/WriteAsync(Stream, ...)` and checks the prefix before payload-sized allocation/rental.
- Deterministic fuzz covers 2,000 arbitrary inputs in each language plus explicit zero, oversized, truncated, fragmented, coalesced, invalid UTF-8, and invalid JSON cases.
- CI installs locked Node dependencies, regenerates both language outputs, fails on drift, and preserves the existing release workflow.
- No MessageBus/session actor, real Named Pipe, WebView, capability implementation, process runtime, or migration is included.
