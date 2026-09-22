import { test as base } from '@playwright/test';
import { build } from 'esbuild';
import http from 'node:http';
import { once } from 'node:events';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import {
  ProtocolVersion,
  MessageKind,
  Routes as BusRoutes,
} from '@qping/plugin-bus/protocol';

const FixtureStatus = 200;
const FixtureOutputPrefix = 'ui-fixture';
const PngPixelBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=';
const WebAssets = new Set(['/index.html', '/main.js', '/response-formatter.worker.js', '/style.css']);
const ContentTypes = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.css': 'text/css',
};

async function createRuntime(workerIndex) {
  const fixtureOutput = path.resolve(
    `bin/AgentVerification/${FixtureOutputPrefix}-${workerIndex}.mjs`,
  );
  await build({
    entryPoints: ['tests/ui-fixture.mts'],
    outfile: fixtureOutput,
    bundle: true,
    platform: 'node',
    format: 'esm',
    packages: 'external',
    target: 'es2024',
  });
  const backend = await import(`${pathToFileURL(fixtureOutput).href}?${Date.now()}`);
  const messages = {
    en: JSON.parse(await readFile('i18n/locales/en-US.json', 'utf8')),
    zh: JSON.parse(await readFile('i18n/locales/zh-CN.json', 'utf8')),
  };
  let changingResponseCount = 0;
  const server = http.createServer(async (request, response) => {
    if (request.url === '/login') {
      response.writeHead(FixtureStatus, {
        'Content-Type': 'application/json',
        'Set-Cookie': 'session=abc; Path=/',
      });
      response.end('{"token":"abc"}');
      return;
    }
    if (request.url === '/user') {
      response.writeHead(FixtureStatus, {'Content-Type': 'application/json'});
      response.end(JSON.stringify({auth: request.headers.authorization || ''}));
      return;
    }
    if (request.url === '/html-response') {
      response.writeHead(FixtureStatus, {'Content-Type': 'text/html'});
      response.end('<!doctype html><html lang="en"><body><h1>Preview fixture</h1><script>document.body.dataset.script="ran"</script></body></html>');
      return;
    }
    if (request.url === '/xml-response') {
      response.writeHead(FixtureStatus, {'Content-Type': 'application/xml'});
      response.end('<?xml version="1.0"?><root><item>value</item></root>');
      return;
    }
    if (request.url === '/javascript-response') {
      response.writeHead(FixtureStatus, {'Content-Type': 'text/javascript'});
      response.end('// fixture\nconst answer = 42;\nconst ready = true;\nconsole.log(`answer: ${answer}`);');
      return;
    }
    if (request.url === '/png-response') {
      response.writeHead(FixtureStatus, {'Content-Type': 'image/png'});
      response.end(Buffer.from(PngPixelBase64, 'base64'));
      return;
    }
    if (request.url === '/changing-response') {
      changingResponseCount++;
      const html = changingResponseCount % 2 === 1;
      response.writeHead(FixtureStatus, {'Content-Type': html ? 'text/html' : 'application/xml'});
      response.end(html ? '<!doctype html><html><body>Changing preview</body></html>' : '<root>Changed format</root>');
      return;
    }
    if (request.url === '/text-response') {
      response.writeHead(FixtureStatus, {'Content-Type': 'text/plain'});
      response.end('plain response');
      return;
    }
    if (request.url === '/redirect-status') {
      response.writeHead(301);
      response.end();
      return;
    }
    if (!WebAssets.has(request.url)) {
      response.writeHead(404);
      response.end();
      return;
    }
    response.setHeader('Content-Type', ContentTypes[path.extname(request.url)]);
    response.end(await readFile(path.join('dist/web', request.url.slice(1))));
  });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  return {
    ...backend,
    messages,
    server,
    url: `http://127.0.0.1:${server.address().port}`,
  };
}

async function installHostBridge(page, runtime, failInitialWorkspaceLoad) {
  const historyEntries = [];
  const workspace = runtime.emptyWorkspace();
  const runner = new runtime.Runner(async entry => {
    const snapshot = structuredClone(entry);
    snapshot.result.bodyAvailable = false;
    delete snapshot.result.sentRequest;
    historyEntries.unshift(snapshot);
  });
  const bridgeFailures = {
    failNextSave: false,
    failRelease: false,
    failedReleaseAttempts: 0,
  };
  let viewState = null;
  let failWorkspaceLoad = failInitialWorkspaceLoad;
  await page.exposeFunction('testBusRequest', async envelope => {
    if (envelope.route === BusRoutes.Bus.Handshake)
      return {negotiatedVersion: ProtocolVersion};
    const method = envelope.route.slice(BusRoutes.Prefix.PluginCall.length);
    const payload = envelope.payload;
    let value;
    if (method === runtime.Routes.load) {
      if (failWorkspaceLoad) {
        failWorkspaceLoad = false;
        return {ok: false, error: {kind: runtime.ErrorKind.Storage}};
      }
      value = workspace;
    } else if (method === runtime.Routes.loadViewState) {
      value = structuredClone(viewState);
    } else if (method === runtime.Routes.saveViewState) {
      viewState = structuredClone(payload);
      value = true;
    } else if (method === runtime.Routes.save) {
      if (bridgeFailures.failNextSave) {
        bridgeFailures.failNextSave = false;
        return {ok: false, error: {kind: runtime.ErrorKind.Storage}};
      }
      const snapshot = structuredClone(payload);
      for (const key of Object.keys(workspace)) delete workspace[key];
      Object.assign(workspace, snapshot);
      value = true;
    } else if (method === runtime.Routes.environment) {
      runner.switchEnvironment(payload.id);
    } else if (method === runtime.Routes.curl) value = runner.curl(payload);
    else if (method === runtime.Routes.history) value = historyEntries;
    else if (method === runtime.Routes.clearHistory) {
      const remaining = payload?.requestId
        ? historyEntries.filter(entry => entry.request.id !== payload.requestId)
        : [];
      historyEntries.splice(0, historyEntries.length, ...remaining);
    } else if (method === runtime.Routes.cookies) runner.clearCookies();
    else if (method === runtime.Routes.listCookies) value = await runner.listCookies();
    else if (method === runtime.Routes.start) value = runner.start(payload);
    else if (method === runtime.Routes.poll) {
      const view = runner.poll(payload.id);
      value = {...view, results: view.results.slice(payload.from || 0)};
    } else if (method === runtime.Routes.cancel) runner.cancel(payload.id);
    else if (method === runtime.Routes.release) {
      if (bridgeFailures.failRelease) {
        bridgeFailures.failedReleaseAttempts++;
        return {ok: false, error: {kind: runtime.ErrorKind.Cache}};
      }
      runner.release(payload.id);
    }
    else if (method === runtime.Routes.download)
      value = runner.download(payload.id, payload.index);
    else throw new Error(`Unexpected route: ${method}`);
    return {ok: true, value};
  });
  await page.addInitScript(
    ({messages, kind, routes}) => {
      const listeners = [];
      const dispatch = envelope => {
        for (const listener of listeners) listener({data: envelope});
      };
      window.testHostEvent = (route, payload) =>
        dispatch({kind: kind.Event, route, payload});
      window.chrome = window.chrome || {};
      window.chrome.webview = {
        addEventListener: (_name, listener) => listeners.push(listener),
        removeEventListener: () => {},
        postMessage: async envelope => {
          const payload = await window.testBusRequest(envelope);
          dispatch({
            kind: kind.Response,
            route: envelope.route,
            correlationId: envelope.id,
            payload,
          });
          if (envelope.route === routes.Bus.Handshake)
            dispatch({
              kind: kind.Event,
              route: routes.HostEvent.Initialize,
              payload: {
                locale: 'en-US',
                fallbackLocale: 'en-US',
                messages,
                theme: 'dark',
              },
            });
        },
      };
    },
    {messages: runtime.messages.en, kind: MessageKind, routes: BusRoutes},
  );
  return {bridgeFailures, historyEntries, runner, workspace};
}

export const test = base.extend({
  failInitialWorkspaceLoad: [false, {option: true}],
  runtime: [async ({}, use, workerInfo) => {
    const runtime = await createRuntime(workerInfo.workerIndex);
    await use(runtime);
    runtime.server.closeAllConnections();
    await new Promise(resolve => runtime.server.close(resolve));
  }, {scope: 'worker'}],
  app: async ({page, runtime, failInitialWorkspaceLoad}, use) => {
    const state = await installHostBridge(page, runtime, failInitialWorkspaceLoad);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(runtime.url + '/index.html');
    await use({
      ...state,
      runtime,
      errors,
      messages: runtime.messages,
      routes: BusRoutes,
      url: runtime.url,
    });
  },
});

export { expect } from '@playwright/test';
