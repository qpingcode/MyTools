# API Tester

[简体中文](README.zh-CN.md)

Debug HTTP/HTTPS APIs inside MyTools, save reusable requests, and run a collection once in order. A Vue 3 detail page communicates with a separate Node backend over the v3 message bus.

## Features and usage

Manage requests in the left collection tree using icon actions for creation, duplication, deletion and ordering. Drag the divider, use arrow keys or Home/End, or double-click to reset its width. Edit request configuration through tabs below the URL; response tabs show the body, headers and assertions. `App.vue` composes focused components, while `useWorkspace`, `useDialogs` and `useRuns` own their respective state.

Open API Tester from MyTools (alias `api-tester`). Create a collection and request, configure its URL, parameters, headers, authentication and body, then send. Save explicitly; blue dots mark unsaved edits. Requests support Basic/Bearer/API Key authentication, JSON/text/form/multipart/binary bodies, and streamed file uploads. Saved file references must remain readable.

Create and select an environment, edit its variables, and reference `{{baseUrl}}`. To chain login, extract JSON Pointer `/token` to `token`, then reference `{{token}}` in a subsequent Bearer token. Manual tabs share temporary variables until the environment changes or the plugin restarts. Each batch starts with independent temporary variables and Cookies and discards them afterward.

Add visual assertions for status, time, headers, text, JSON existence/value/type. Select requests with their checkboxes, or run the whole active collection with none selected. Open tabs' current edits are used without automatically saving. Stop/continue and cancellation are supported. HTTP errors are responses; without assertions they are untested. Results stay in the current session; no reports, history, scripts, import/export or multi-iteration runs are included.

Responses offer raw/JSON views, repeated headers, full body copying and saving. Defaults: 30-second timeout, ten redirects, 1 MiB preview, 20 MiB body reception, 40 MiB complete-body cache and 8 MiB preview cache across eight runs, twelve open tabs, and up to 1000 requests per batch. Cookie containers are retained for up to sixteen environments. Old complete bodies may expire while summaries remain. The UI supports English and Simplified Chinese, live language changes, host themes, and keyboard navigation.

## Development

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

