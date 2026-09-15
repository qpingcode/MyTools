# API Tester

[简体中文](README.zh-CN.md)

Debug HTTP/HTTPS APIs inside MyTools, save reusable requests, and run a collection once in order. A Vue 3 detail page communicates with a separate Node backend over the v3 message bus.

## Features and usage

Manage requests in the left collection tree using icon actions for creation, duplication, deletion and ordering. Drag the divider, use arrow keys or Home/End, or double-click to reset its width. Edit request configuration through tabs below the URL; response tabs show the body, headers and assertions. `App.vue` composes focused components, while `useWorkspace`, `useDialogs` and `useRuns` own their respective state.

Open API Tester from MyTools (alias `api-tester`). Create a collection and request, configure its URL, parameters, headers, authentication and body, then send. Save explicitly; blue dots mark unsaved edits. Requests support Basic/Bearer/API Key authentication, JSON/text/form/multipart/binary bodies, and streamed file uploads. Saved file references must remain readable.

Create and select an environment, edit its variables, and reference `{{baseUrl}}`. To chain login, extract JSON Pointer `/token` to `token`, then reference `{{token}}` in a subsequent Bearer token. Manual tabs share temporary variables until the environment changes or the plugin restarts. Each batch starts with independent temporary variables and Cookies and discards them afterward.

Add visual assertions for status, time, headers, text, JSON existence/value/type. Select requests with their checkboxes, or run the whole active collection with none selected. Open tabs' current edits are used without automatically saving. Stop/continue and cancellation are supported. HTTP errors remain responses; the built-in success check expects HTTP 200. Reports and multi-iteration runs are not included.

Responses offer raw/formatted JSON/tree views, syntax colors, folding, case-insensitive search with match navigation, repeated headers, full body copying and saving. Defaults: 30-second timeout, ten redirects, 1 MiB preview, 20 MiB body reception, 40 MiB complete-body cache and 8 MiB preview cache across eight runs, and up to 1000 requests per batch. Cookie containers are retained for up to sixteen environments. Old complete bodies may expire while summaries remain. The UI supports English and Simplified Chinese, live language changes, host themes, and keyboard navigation.

## Import, export and history

The header has separate **Import**, **Export** and **History** buttons with icons, each opening its own dialog. Paste browser cURL or load API Tester JSON, Postman Collection v2.x, or Postman Environment JSON. Imports append data with fresh IDs and disable scripts until explicitly enabled. Unsupported cURL options, authentication and body modes fail rather than silently changing the request. Native JSON preserves assertions, extraction rules, scripts and request settings. Export the entire workspace, a collection or an environment; collections can also export as Postman v2.1. Postman export omits this plugin's assertions, extractions and request settings. Exports use saved data, so save tab edits first.

The copy icon next to request Save copies a POSIX-shell cURL command with collection configuration, the selected environment and session variables resolved. It does not execute pre-request scripts. Common browser flags, quoted bodies, duplicate/raw-encoded query parameters, form fields, multipart files and binary file paths are supported. File paths remain references and are read only when sending the request.

History is stored atomically in `history.json` beside the workspace. It retains the latest 100 actual requests, environment/variable snapshots, headers, test results, logs and up to 16 KiB of response preview. Full bodies are not retained. Reopening a successful send uses the actual sent values with scripts disabled to avoid applying request mutations twice. Failed requests may retain their original script configuration. History and exports may include credentials, just like saved requests.

## Collection configuration and assertions

Use the collection's settings icon to edit common headers, authentication and scripts. A request must select **Inherit from collection** to use collection authentication; **None** explicitly disables authentication. Request headers override same-named common headers case-insensitively, including disabled rows. Both manual sends and batch runs use these settings. Collection scripts run before request scripts in each phase, with independent lexical scopes and a shared variable context.

The **Assertions** request tab edits status, maximum response time (milliseconds), header presence, text inclusion, JSON Pointer existence, JSON value and JSON type checks. JSON paths use `/data/id`; JSON expected values use JSON syntax. Results appear in the response Assertions tab together with script tests.

## Scripts

The **Scripts** tab contains **Before request** and **After response** JavaScript. Scripts execute in a VM in a dedicated worker with a 2-second limit and a 64 MiB old-generation heap limit. Only JSON data enters the VM; Node objects, filesystem/network APIs, npm imports and dynamic code generation are not exposed. This provides a bounded local scripting environment, not full Postman sandbox compatibility. Top-level `await` is supported; `pm.test` callbacks must be synchronous. Request/response state returned by the script is limited to 2 MiB, and logs/tests are bounded.

Supported APIs:

- `pm.variables.get/set/unset/has/toObject/replaceIn`. `replaceIn` also supports `{{$timestamp}}` and `{{$randomInt}}`.
- `pm.environment` and `pm.collectionVariables` alias these session variables. They do not change saved environment values. Manual sends share variables until switching environments/restarting; batches isolate variables and Cookies.
- `pm.request.method`, `pm.request.url`, `pm.request.headers.get/has/add/upsert/remove/toObject`, and `pm.request.body.raw/update` for raw bodies.
- `pm.response.code/status/responseTime`, `pm.response.headers`, `pm.response.text()/json()` and `pm.response.to.have.status(code)` after a response.
- `pm.test(name, callback)` and `pm.expect(value)` with `equal`, `eql`, `deep.equal`, `include`, `above`, `below`, `within`, `a/an`, `property`, `match`, `lengthOf`, `not`, `ok`, `true/false/null/undefined`.
- `pm.crypto.sha256(text)`, `pm.crypto.hmacSha256(text, secret)` return hex; `pm.crypto.base64(text)` encodes UTF-8.
- `console.log/info/warn/error/debug`, displayed in **Script console**.

```js
// Before request
pm.variables.set('timestamp', String(Date.now()));
const signature = pm.crypto.hmacSha256(pm.variables.get('timestamp'), pm.variables.get('secret'));
pm.request.headers.upsert({ key: 'X-Signature', value: signature });

// After response
pm.test('HTTP 200', () => pm.response.to.have.status(200));
pm.test('token', () => pm.expect(pm.response.json().token).to.be.a('string'));
pm.variables.set('token', pm.response.json().token);
```

A before-script exception prevents sending. An after-script exception preserves the received response and fails the run. Variable writes are committed only when execution, decoding, extraction and scripts succeed; failed test assertions still participate in batch stop-on-failure behavior.

## Development

Source files are grouped by responsibility:

- `src/backend/execution`, `scripting`, and `persistence`: HTTP execution and runs, script workers and crypto helpers, and workspace/history storage. `index.mts` remains the plugin entry point.
- `src/shared`: domain models, workspace validation, and import/export formats shared by backend and frontend.
- `src/web/components/common` and `layout`: reusable controls/editors and split panes.
- `src/web/features/workspace`, `sidebar`, `request`, `response`, and `runs`: feature components, state, and related types/helpers.
- `src/web/services` and `localization`: host RPC and notifications, and translation resources access and reactive locale state.
- `src/web`: application composition, entry point, HTML, styles, and Vue type declarations.

```sh
npm install
npm run check
npm test
npm run test:ui
npm run build
npm run watch
```

Uses the published `@qping/plugin-bus@0.9.0` and Create Plugin's esbuild scaffolding, extended with `@vue/compiler-sfc`. The scaffold retains TypeScript 7; an isolated `typescript-vue` TypeScript 5 alias supplies the JavaScript compiler API required by vue-tsc. Browser smoke tests use installed Microsoft Edge in headless mode. Local HTTP integration tests build under `bin/AgentVerification/`.

The host must set `MYTOOLS_PLUGIN_DATA_DIR`; `workspace.json` is saved atomically there. Values are saved as entered; credential masking and secure storage are outside this version's scope.

The Desktop hook `window.mytoolsBeforeClose()` supplies save/discard/cancel prompts on window close and normal tray exit. Rebuild the Desktop host to use it; plugins without a hook keep their existing behavior.

## Publishing

Build, then run `npm run publish:hub` when explicitly publishing. This helper comes from Create Plugin's template and requires a Hub login. Increment manifest and package versions for subsequent releases. This change does not publish the plugin.

