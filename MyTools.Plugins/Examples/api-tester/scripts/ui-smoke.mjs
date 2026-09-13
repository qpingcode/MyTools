import { chromium } from 'playwright';
import { build } from 'esbuild';
import http from 'node:http';
import { once } from 'node:events';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import {
  ProtocolVersion,
  MessageKind,
  Routes as BusRoutes,
} from '@qping/plugin-bus/protocol';

const TestTimeoutMs = 10_000;
const FixtureStatus = 200;
const output = path.resolve('bin/AgentVerification/ui-fixture.mjs');
await build({
  entryPoints: ['tests/ui-fixture.mts'],
  outfile: output,
  bundle: true,
  platform: 'node',
  format: 'esm',
  packages: 'external',
  target: 'es2024',
});
const { Runner, emptyWorkspace, Routes } = await import(
  pathToFileURL(output).href
);
const runner = new Runner();
let workspace = emptyWorkspace();
const en = JSON.parse(await readFile('i18n/locales/en-US.json', 'utf8'));
const zh = JSON.parse(await readFile('i18n/locales/zh-CN.json', 'utf8'));
const contentTypes = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.css': 'text/css',
};
const assets = new Set(['/index.html', '/main.js', '/style.css']);
const server = http.createServer(async (req, res) => {
  if (req.url === '/login') {
    res.writeHead(FixtureStatus, {
      'Content-Type': 'application/json',
      'Set-Cookie': 'session=abc; Path=/',
    });
    res.end('{"token":"abc"}');
    return;
  }
  if (req.url === '/user') {
    res.writeHead(FixtureStatus, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ auth: req.headers.authorization || '' }));
    return;
  }
  if (!assets.has(req.url)) {
    res.writeHead(404);
    res.end();
    return;
  }
  res.setHeader('Content-Type', contentTypes[path.extname(req.url)]);
  res.end(await readFile(path.join('dist/web', req.url.slice(1))));
});
server.listen(0, '127.0.0.1');
await once(server, 'listening');
const url = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const page = await browser.newPage({
    viewport: { width: 1280, height: 960 },
  });
  page.setDefaultTimeout(TestTimeoutMs);
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.exposeFunction('testBusRequest', async (envelope) => {
    if (envelope.route === BusRoutes.Bus.Handshake)
      return { negotiatedVersion: ProtocolVersion };
    const method = envelope.route.slice(BusRoutes.Prefix.PluginCall.length);
    const payload = envelope.payload;
    let value;
    if (method === Routes.load) value = workspace;
    else if (method === Routes.save) {
      workspace = structuredClone(payload);
      value = true;
    } else if (method === Routes.environment)
      runner.switchEnvironment(payload.id);
    else if (method === Routes.cookies) runner.clearCookies();
    else if (method === Routes.listCookies) value = await runner.listCookies();
    else if (method === Routes.start) value = runner.start(payload);
    else if (method === Routes.poll) {
      const view = runner.poll(payload.id);
      value = { ...view, results: view.results.slice(payload.from || 0) };
    } else if (method === Routes.cancel) runner.cancel(payload.id);
    else if (method === Routes.release) runner.release(payload.id);
    else if (method === Routes.download)
      value = runner.download(payload.id, payload.index);
    else throw new Error(`Unexpected route: ${method}`);
    return { ok: true, value };
  });
  await page.addInitScript(
    ({ messages, kind, routes, version }) => {
      const listeners = [];
      const dispatch = (envelope) => {
        for (const listener of listeners) listener({ data: envelope });
      };
      window.testHostEvent = (route, payload) =>
        dispatch({ kind: kind.Event, route, payload });
      window.chrome = window.chrome || {};
      window.chrome.webview = {
        addEventListener: (_name, listener) => listeners.push(listener),
        removeEventListener: () => {},
        postMessage: async (envelope) => {
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
    {
      messages: en,
      kind: MessageKind,
      routes: BusRoutes,
      version: ProtocolVersion,
    },
  );
  await page.goto(url + '/index.html');
  await page
    .getByRole('heading', { name: 'API Tester', exact: true })
    .waitFor();
  const environmentMenu = page.locator('.environment-bar details');
  const menuTrigger = environmentMenu.locator('summary');
  const environmentSelect = page.getByRole('combobox', {
    name: 'Environments',
    exact: true,
  });
  assert.equal(
    await page
      .locator('html')
      .evaluate((element) => getComputedStyle(element).colorScheme),
    'dark',
  );
  const optionColors = await environmentSelect
    .locator('option')
    .first()
    .evaluate((element) => ({
      background: getComputedStyle(element).backgroundColor,
      text: getComputedStyle(element).color,
    }));
  assert.notEqual(optionColors.background, optionColors.text);
  await menuTrigger.click();
  assert.equal(await environmentMenu.evaluate((element) => element.open), true);
  await environmentSelect.click();
  assert.equal(
    await environmentMenu.evaluate((element) => element.open),
    false,
  );
  await environmentSelect.press('Escape');
  await menuTrigger.click();
  await menuTrigger.press('Escape');
  assert.equal(
    await environmentMenu.evaluate((element) => element.open),
    false,
  );
  await menuTrigger.click();
  await page
    .getByRole('button', { name: 'Default execution settings', exact: true })
    .click();
  assert.equal(
    await environmentMenu.evaluate((element) => element.open),
    false,
  );
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Cancel', exact: true })
    .click();
  // Divider supports pointer capture and keyboard resizing.
  const divider = page.locator('.splitter');
  const initialWidth = Number(await divider.getAttribute('aria-valuenow'));
  const dividerBox = await divider.boundingBox();
  const DragDistance = 90;
  await page.mouse.move(
    dividerBox.x + dividerBox.width / 2,
    dividerBox.y + 100,
  );
  await page.mouse.down();
  await page.mouse.move(dividerBox.x + DragDistance, dividerBox.y + 100);
  await page.mouse.up();
  assert.ok(Number(await divider.getAttribute('aria-valuenow')) > initialWidth);
  await divider.press('Home');
  assert.equal(
    await divider.getAttribute('aria-valuenow'),
    await divider.getAttribute('aria-valuemin'),
  );
  await divider.dblclick();
  assert.equal(
    Number(await divider.getAttribute('aria-valuenow')),
    initialWidth,
  );
  const responseDivider = page.locator('.response-splitter');
  const requestPane = page.locator('.request-pane');
  const initialRequestHeight = (await requestPane.boundingBox()).height;
  const initialRequestPercent = await responseDivider.getAttribute('aria-valuenow');
  const responseDividerBox = await responseDivider.boundingBox();
  const VerticalDragDistance = 80;
  const centerX = responseDividerBox.x + responseDividerBox.width / 2;
  const centerY = responseDividerBox.y + responseDividerBox.height / 2;
  await page.mouse.move(centerX, centerY);
  await page.mouse.down();
  await page.mouse.move(centerX, centerY + VerticalDragDistance);
  await page.mouse.up();
  assert.ok((await requestPane.boundingBox()).height > initialRequestHeight);
  await responseDivider.press('Home');
  assert.equal(await responseDivider.getAttribute('aria-valuenow'),
    await responseDivider.getAttribute('aria-valuemin'));
  await responseDivider.dblclick();
  assert.equal(await responseDivider.getAttribute('aria-valuenow'), initialRequestPercent);
  await page
    .getByRole('button', { name: 'New collection', exact: true })
    .click();
  await page.getByRole('dialog').getByRole('textbox').fill('Users');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Save', exact: true })
    .click();
  await page.getByRole('button', { name: /Users/ }).waitFor();
  async function create(name, endpoint) {
    await page
      .locator('aside')
      .getByRole('button', { name: 'New request', exact: true })
      .click();
    await page
      .locator('section.workspace')
      .getByRole('textbox', { name: 'Name', exact: true })
      .first()
      .fill(name);
    await page
      .getByRole('textbox', { name: 'URL', exact: true })
      .fill(url + endpoint);
    await page.getByRole('textbox', { name: 'URL', exact: true }).press('Tab');
  }
  async function save() {
    await page
      .locator('section.workspace')
      .getByRole('button', { name: 'Save', exact: true })
      .click();
  }
  await create('Login', '/login');
  await page
    .getByRole('tab', { name: 'JSON variable extraction', exact: true })
    .click();
  const extraction = page.getByRole('tabpanel');
  await extraction.getByRole('button', { name: 'Add', exact: true }).click();
  await extraction
    .getByRole('textbox', { name: 'JSON Pointer / header name', exact: true })
    .fill('/token');
  await extraction
    .getByRole('textbox', { name: 'Target variable name', exact: true })
    .fill('token');
  await save();
  await page.getByRole('button', { name: 'Send', exact: true }).click();
  await page.locator('.response pre').filter({ hasText: 'abc' }).waitFor();
  const settingsTab = page.getByRole('tab', {
    name: 'Settings',
    exact: true,
  });
  const cookiesButton = page.getByRole('button', {
    name: 'Cookies',
    exact: true,
  });
  const settingsBox = await settingsTab.boundingBox();
  const cookiesBox = await cookiesButton.boundingBox();
  assert.ok(cookiesBox.x > settingsBox.x);
  await cookiesButton.click();
  const cookiesDialog = page.getByRole('dialog', { name: 'Cookies' });
  await cookiesDialog.getByRole('heading', { name: '127.0.0.1' }).waitFor();
  await cookiesDialog.getByText('session', { exact: true }).waitFor();
  await cookiesDialog.getByText('abc', { exact: true }).waitFor();
  await cookiesDialog
    .getByRole('button', { name: 'Clear cookies', exact: true })
    .click();
  await cookiesDialog
    .getByText('No cookies stored for the current environment.')
    .waitFor();
  await cookiesDialog.getByRole('button', { name: 'Close', exact: true }).click();
  await cookiesDialog.waitFor({ state: 'detached' });
  await create('User', '/user');
  // Default input rows are visual drafts, not saved configuration.
  assert.equal(await page.getByRole('tabpanel').locator('.pair').count(), 1);
  assert.equal(workspace.collections[0].requests.at(-1).params.length, 0);
  // Configuration tabs retain their fields and support keyboard navigation.
  await page
    .getByRole('tab', { name: 'Parameters', exact: true })
    .press('ArrowRight');
  const config = page.getByRole('tabpanel');
  assert.equal(await config.getByRole('button', { name: 'Add', exact: true }).count(), 0);
  await config
    .getByRole('combobox', { name: 'Name', exact: true })
    .fill('X-Test');
  await config
    .getByRole('textbox', { name: 'Value', exact: true })
    .first()
    .fill('Bearer {{token}}');
  assert.equal(await config.locator('.variable-token').innerText(), '{{token}}');
  assert.equal(await config.getByRole('textbox', { name: 'Value', exact: true }).first().inputValue(), 'Bearer {{token}}');
  const urlInput = page.locator('[data-primary-input]');
  const originalUrl = await urlInput.inputValue();
  await urlInput.fill('{{baseUrl}}/users/{{userId}}');
  assert.deepEqual(await page.locator('.url-bar .variable-token').allTextContents(), ['{{baseUrl}}', '{{userId}}']);
  await page.screenshot({ path: 'bin/AgentVerification/variable-highlighting.png' });
  await urlInput.fill(originalUrl);
  await config
    .getByRole('textbox', { name: 'Value', exact: true })
    .first()
    .fill('tab-state');
  assert.equal(await config.locator('.pair').count(), 2);
  await page.getByRole('tab', { name: 'Body', exact: true }).click();
  await config
    .getByRole('combobox', { name: 'Body', exact: true })
    .selectOption('json');
  await config
    .getByRole('textbox', { name: 'Body', exact: true })
    .fill('{"value":1}');
  await page
    .getByRole('tab', { name: 'Settings', exact: true })
    .click();
  await config.getByRole('checkbox').first().check();
  assert.equal(
    await config.getByRole('spinbutton').inputValue(),
    String(workspace.defaults.timeoutMs),
  );
  await page.getByRole('tab', { name: /^Headers/ }).click();
  assert.equal(
    await config
      .getByRole('textbox', { name: 'Value', exact: true })
      .first()
      .inputValue(),
    'tab-state',
  );
  await page.getByRole('tab', { name: 'Body', exact: true }).click();
  assert.equal(
    await config
      .getByRole('textbox', { name: 'Body', exact: true })
      .inputValue(),
    '{"value":1}',
  );
  await config
    .getByRole('combobox', { name: 'Body', exact: true })
    .selectOption('none');

  await page.getByRole('tab', { name: 'Authentication', exact: true }).click();
  const auth = page.getByRole('tabpanel');
  await auth
    .getByRole('radio', { name: 'Bearer Token', exact: true })
    .check();
  await auth
    .getByRole('textbox', { name: 'Token', exact: true })
    .fill('{{token}}');
  await save();
  await page.getByRole('button', { name: 'Send', exact: true }).click();
  await page
    .locator('.response pre')
    .filter({ hasText: 'Bearer abc' })
    .waitFor();
  await page
    .getByRole('button', {
      name: 'Run collection / selected requests',
      exact: true,
    })
    .click();
  await page.getByText(/2 \/ 2 requests/).waitFor();
  await page
    .locator('.workspace-scroll > div')
    .filter({
      has: page.getByRole('heading', { name: 'Run results', exact: true }),
    })
    .locator('details > summary')
    .filter({ hasText: /^User/ })
    .click();
  assert.equal(workspace.collections[0].requests.length, 2);
  // CRUD controls operate from the sidebar, including unopened requests.
  const userRow = page
    .locator('.request-row')
    .filter({ hasText: 'User' })
    .last();
  await userRow.hover();
  await userRow.getByRole('button', { name: 'Move up', exact: true }).click();
  assert.equal(workspace.collections[0].requests[0].name, 'User');
  await userRow.hover();
  await userRow.getByRole('button', { name: 'Move down', exact: true }).click();
  assert.equal(workspace.collections[0].requests[0].name, 'Login');
  await userRow.hover();
  await userRow.getByRole('button', { name: 'Duplicate', exact: true }).click();
  assert.equal(workspace.collections[0].requests.length, 3);
  const copiedRow = page.locator('.request-row').last();
  await copiedRow.hover();
  await copiedRow.getByRole('button', { name: 'Delete', exact: true }).click();
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Continue', exact: true })
    .click();
  await page.getByRole('dialog').waitFor({ state: 'detached' });
  assert.equal(workspace.collections[0].requests.length, 2);
  await page.getByRole('tab', { name: /^Authentication/ }).click();
  assert.equal(
    await page
      .getByRole('textbox', { name: 'Token', exact: true })
      .inputValue(),
    '{{token}}',
  );
  await page
    .locator('.workspace-scroll')
    .evaluate((element) => (element.scrollTop = 0));
  await page.screenshot({
    path: 'bin/AgentVerification/ui-smoke-dark.png',
    fullPage: true,
  });

  await page
    .locator('section.workspace')
    .getByRole('textbox', { name: 'Name', exact: true })
    .first()
    .fill('User changed');
  const close = page.evaluate(() => window.mytoolsBeforeClose());
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Cancel', exact: true })
    .click();
  assert.equal(await close, false);
  await page.evaluate(
    ({ route, messages }) =>
      window.testHostEvent(route, {
        locale: 'zh-CN',
        fallbackLocale: 'en-US',
        messages,
      }),
    { route: BusRoutes.HostEvent.LanguageChanged, messages: zh },
  );
  await page.getByRole('combobox', { name: '环境', exact: true }).waitFor();
  await page.getByRole('button', { name: '发送', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Cookie', exact: true }).waitFor();
  await page.evaluate(
    (route) =>
      window.testHostEvent(route, {
        theme: 'light',
        themeTokens: {
          '--mt-surface-bg': '#ffffff',
          '--mt-surface': '#f8fafc',
          '--mt-surface-alt': '#f1f5f9',
          '--mt-surface-hover': '#e2e8f0',
          '--mt-text': '#111111',
          '--mt-text-muted': '#475569',
          '--mt-border': '#cbd5e1',
          '--mt-border-subtle': '#e2e8f0',
          '--mt-accent': '#2563eb',
          '--mt-accent-foreground': '#ffffff',
        },
      }),
    BusRoutes.HostEvent.ThemeChanged,
  );
  assert.equal(await page.locator('html').getAttribute('data-theme'), 'light');
  assert.equal(
    await page
      .locator('html')
      .evaluate((element) => getComputedStyle(element).colorScheme),
    'light',
  );
  await page
    .locator('.workspace-scroll')
    .evaluate((element) => (element.scrollTop = 0));
  await page.screenshot({
    path: 'bin/AgentVerification/ui-smoke.png',
    fullPage: true,
  });
  assert.deepEqual(errors, []);
  console.log(
    'UI smoke passed: divider dragging/keyboard resizing, sidebar request CRUD and ordering, configuration tabs, saving, login chaining, assertions, batch results, close cancellation, live Chinese and theme changes.',
  );
} finally {
  await browser.close();
  server.closeAllConnections();
  await new Promise((resolve) => server.close(resolve));
}
