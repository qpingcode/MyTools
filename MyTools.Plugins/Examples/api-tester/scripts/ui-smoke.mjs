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
const WideViewport = {width: 1280, height: 960};
const CompactViewport = {width: 640, height: 960};
const CompactPanelSelectWidth = 180;
const RequestPanelCollapseWidth = 640;
const ResponseSearchInputWidth = 200;
const VisibleTreeGuideOpacity = 0.5;
const RequestDropX = 40;
const RequestDropBeforeY = 4;
const RequestDropAfterY = 32;
const TreeAutoScrollViewportPx = 96;
const TreeAutoScrollPointerInsetPx = 4;
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
const { Runner, emptyWorkspace, Routes, ErrorKind } = await import(
  pathToFileURL(output).href
);
let historyEntries = [];
const runner = new Runner(async entry => { const snapshot = structuredClone(entry); snapshot.result.bodyAvailable = false; delete snapshot.result.sentRequest; historyEntries.unshift(snapshot); });
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
    viewport: WideViewport,
  });
  page.setDefaultTimeout(TestTimeoutMs);
  const errors = [];
  let failWorkspaceLoad = true;
  page.on('pageerror', (error) => errors.push(error.message));
  await page.exposeFunction('testBusRequest', async (envelope) => {
    if (envelope.route === BusRoutes.Bus.Handshake)
      return { negotiatedVersion: ProtocolVersion };
    const method = envelope.route.slice(BusRoutes.Prefix.PluginCall.length);
    const payload = envelope.payload;
    let value;
    if (method === Routes.load) {
      if (failWorkspaceLoad) {
        failWorkspaceLoad = false;
        return { ok: false, error: { kind: ErrorKind.Storage } };
      }
      value = workspace;
    }
    else if (method === Routes.save) {
      workspace = structuredClone(payload);
      value = true;
    } else if (method === Routes.environment)
      runner.switchEnvironment(payload.id);
    else if (method === Routes.curl) value = runner.curl(payload);
    else if (method === Routes.history) value = historyEntries;
    else if (method === Routes.clearHistory)
      historyEntries = payload?.requestId
        ? historyEntries.filter(entry => entry.request.id !== payload.requestId)
        : [];
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
  await page.getByText('Could not load the workspace. Please retry.', { exact: true }).waitFor();
  assert.equal(await page.locator('.workspace-overlay').evaluate(element => getComputedStyle(element).position), 'fixed');
  assert.equal((await page.locator('.app-header').boundingBox()).y, 0);
  assert.equal(await page.locator('.app-shell').evaluate(element => element.inert), true);
  await page.evaluate(({ route, messages }) => window.testHostEvent(route, { locale: 'zh-CN', fallbackLocale: 'en-US', messages }), { route: BusRoutes.HostEvent.LanguageChanged, messages: zh });
  await page.getByText('无法加载工作区，请重试。', { exact: true }).waitFor();
  await page.getByRole('button', { name: '重试', exact: true }).click();
  await page.waitForFunction(() => !document.querySelector('.app-shell').inert);
  assert.equal(await page.locator('.workspace-overlay').count(), 0);
  assert.equal((await page.locator('.app-header').boundingBox()).y, 0);
  await page.evaluate(({ route, messages }) => window.testHostEvent(route, { locale: 'en-US', fallbackLocale: 'en-US', messages }), { route: BusRoutes.HostEvent.LanguageChanged, messages: en });
  await page
    .getByRole('button', { name: 'Import', exact: true })
    .waitFor();
  const environmentMenu = page.locator('.environment-bar details:not(.environment-selector)');
  const menuTrigger = environmentMenu.locator('summary');
  const environmentSelect = page.locator('.environment-selector > summary');
  assert.equal(
    await page
      .locator('html')
      .evaluate((element) => getComputedStyle(element).colorScheme),
    'dark',
  );
  const optionColors = await page.locator('.environment-option-list > button')
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
  // Search stays above the list, and the create action accepts variables immediately.
  const SearchFixtureEnvironmentName = 'Search Fixture';
  await environmentSelect.click();
  const environmentOptions = page.locator('.environment-options');
  await environmentOptions.getByRole('button', { name: 'New environment', exact: true }).click();
  assert.equal(await page.locator('.environment-selector').evaluate(element => element.open), false);
  const newEnvironmentDialog = page.getByRole('dialog', { name: 'New environment', exact: true });
  assert.equal(await newEnvironmentDialog.getByRole('button', { name: 'Save', exact: true }).isDisabled(), true);
  await newEnvironmentDialog.getByRole('textbox', { name: 'Environment name', exact: true }).fill(SearchFixtureEnvironmentName);
  await newEnvironmentDialog.getByRole('textbox', { name: 'Name', exact: true }).first().fill('fixtureToken');
  await newEnvironmentDialog.getByRole('textbox', { name: 'Value', exact: true }).first().fill('fixtureValue');
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
  await environmentSelect.click();
  const environmentSearch = environmentOptions.getByRole('searchbox', { name: 'Search environments', exact: true });
  const searchBox = await environmentSearch.boundingBox();
  const optionListBox = await page.locator('.environment-option-list').boundingBox();
  assert.ok(searchBox.width > (await environmentOptions.boundingBox()).width / 2);
  assert.ok(searchBox.y < optionListBox.y);
  await environmentSearch.fill('SEARCH FIX');
  await environmentOptions.getByRole('button', { name: SearchFixtureEnvironmentName, exact: true }).waitFor();
  await environmentSearch.fill('unmatched environment');
  assert.equal(await environmentOptions.getByRole('button', { name: SearchFixtureEnvironmentName, exact: true }).count(), 0);
  await environmentOptions.getByText('No matching environments.', { exact: true }).waitFor();
  await environmentOptions.getByRole('button', { name: 'New environment', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await environmentSelect.click();
  assert.equal(await environmentSearch.inputValue(), '');
  await environmentOptions.getByRole('button', { name: SearchFixtureEnvironmentName, exact: true }).waitFor();
  await environmentSearch.press('Escape');
  await environmentSelect.click();
  await environmentOptions.getByRole('button', { name: SearchFixtureEnvironmentName, exact: true }).click();
  await page.getByRole('button', { name: 'Edit environment', exact: true }).click();
  const editEnvironmentDialog = page.getByRole('dialog', { name: 'Edit environment', exact: true });
  assert.equal(await editEnvironmentDialog.getByRole('textbox', { name: 'Name', exact: true }).first().inputValue(), 'fixtureToken');
  assert.equal(await editEnvironmentDialog.getByRole('textbox', { name: 'Value', exact: true }).first().inputValue(), 'fixtureValue');
  await editEnvironmentDialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await environmentSelect.click();
  await environmentOptions.getByRole('button', { name: 'No environment', exact: true }).click();

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
  await page.locator('aside').getByRole('button', { name: 'Add subcollection', exact: true }).click();
  await page.getByRole('dialog').getByRole('textbox').fill('Accounts');
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
  const usersCollection = () => workspace.collections.find(item => item.name === 'Users');
  const accountsCollection = () => workspace.collections.find(item => item.name === 'Accounts');
  assert.equal(accountsCollection().parentId, usersCollection().id);
  const nestedCollectionAlign = await page.evaluate(() => {
    const tree = document.querySelector('.collection-tree');
    const nested = document.querySelector('.collection-tree > .collection > .request-branches > .collection');
    return {
      treeLeft: tree.getBoundingClientRect().left,
      nestedLeft: nested.getBoundingClientRect().left,
      nestedMarginLeft: getComputedStyle(nested).marginLeft,
    };
  });
  assert.equal(nestedCollectionAlign.nestedMarginLeft, '0px');
  assert.equal(nestedCollectionAlign.nestedLeft, nestedCollectionAlign.treeLeft);
  await page.locator('.collection-title').filter({ hasText: 'Users' }).click();
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
  assert.equal(usersCollection().requests.length, 1);
  assert.equal(accountsCollection().requests.length, 0);
  const sidebar = page.locator('aside');
  await sidebar.getByRole('button', { name: 'New collection', exact: true }).waitFor();
  assert.equal(await page.locator('.collection-heading.selected').count(), 0);
  await page.locator('.collection-title').filter({ hasText: 'Users' }).click();
  assert.equal(await page.locator('.collection-heading.selected').count(), 1);
  await sidebar.getByRole('button', { name: 'Add subcollection', exact: true }).waitFor();
  await page.locator('.request-row.selected .request-title').click();
  await sidebar.getByRole('button', { name: 'New collection', exact: true }).waitFor();
  assert.equal(await page.locator('.collection-heading.selected').count(), 0);
  assert.equal(await page.getByRole('tab', {name: 'Assertions', exact: true}).count(), 0);
  assert.equal(await page.getByRole('tab', {name: 'JSON variable extraction', exact: true}).count(), 0);
  await page.getByRole('tab', {name: 'Scripts', exact: true}).click();
  await page.getByRole('button', {name: 'After response', exact: true}).click();
  await page.getByRole('textbox', {name: 'After response', exact: true})
    .fill("pm.variables.set('token', pm.response.json().token);");
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
  await cookiesDialog.locator('.dialog-titlebar').getByRole('button', { name: 'Close', exact: true }).click();
  await cookiesDialog.waitFor({ state: 'detached' });
  await create('User', '/user');
  const selectedCollection = page.locator('.collection-tree > .collection');
  const requestBranches = selectedCollection.locator(':scope > .request-branches');
  assert.equal(await requestBranches.evaluate(element => getComputedStyle(element, '::before').opacity), '0');
  await selectedCollection.hover();
  await page.waitForFunction(minimumOpacity => {
    const branches = document.querySelector('.collection:hover .request-branches');
    return branches && Number.parseFloat(getComputedStyle(branches, '::before').opacity) > minimumOpacity;
  }, VisibleTreeGuideOpacity);
  const branchGuideStyle = await requestBranches.evaluate(element => {
    const vertical = getComputedStyle(element, '::before');
    const horizontal = getComputedStyle(element.querySelector('.request-row'), '::before');
    return {
      verticalStyle: vertical.borderLeftStyle,
      horizontalStyle: horizontal.borderTopStyle,
      verticalLeft: vertical.left,
      horizontalLeft: horizontal.left,
    };
  });
  assert.equal(branchGuideStyle.verticalStyle, 'dashed');
  assert.equal(branchGuideStyle.horizontalStyle, 'dashed');
  assert.equal(branchGuideStyle.verticalLeft, branchGuideStyle.horizontalLeft);
  const requestHistoryButton = page.getByRole('button', {name: 'History · User', exact: true});
  await requestHistoryButton.waitFor();
  assert.equal(await page.locator('.response-toolbar').getByRole('button', {name: 'History · User', exact: true}).count(), 1);
  assert.equal(await page.locator('.config-bar').getByRole('button', {name: 'History · User', exact: true}).count(), 0);
  const requestPanelSelect = page.locator('.request-panel-select');
  assert.equal(await requestPanelSelect.isHidden(), true);
  await page.setViewportSize(CompactViewport);
  const compactRequestEditorWidth = await page.locator('.request-editor').evaluate(element => element.getBoundingClientRect().width);
  assert.ok(compactRequestEditorWidth <= RequestPanelCollapseWidth,
    `Compact request editor width: ${compactRequestEditorWidth}px`);
  await requestPanelSelect.waitFor({state: 'visible'});
  assert.equal(await page.locator('.config-tabs').isHidden(), true);
  const restingRequestSelectStyle = await requestPanelSelect.evaluate(element => ({
    background: getComputedStyle(element).backgroundColor,
    border: getComputedStyle(element).borderColor,
  }));
  await requestPanelSelect.hover();
  const hoveredRequestSelectStyle = await requestPanelSelect.evaluate(element => ({
    background: getComputedStyle(element).backgroundColor,
    border: getComputedStyle(element).borderColor,
  }));
  assert.notDeepEqual(hoveredRequestSelectStyle, restingRequestSelectStyle);
  await requestPanelSelect.selectOption('Headers');
  assert.equal((await requestPanelSelect.locator('option:checked').textContent()).trim(), 'Headers');
  await page.setViewportSize(WideViewport);
  await page.getByRole('tab', {name: 'Headers', exact: true}).waitFor();
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
  assert.equal(await urlInput.getAttribute('placeholder'), 'Enter a URL, e.g. https://api.example.com/users');
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
  const runCollectionHeading = page.locator('.collection-heading').first();
  await runCollectionHeading.click({button: 'right'});
  await page.getByRole('menuitem', {name: 'Run collection', exact: true}).click();
  await page.locator('.runner-options').getByRole('button', { name: 'Run collection', exact: true }).click();
  await page.getByText(/2 \/ 2 requests/).waitFor();
  const runnerWorkspace = page.locator('.runner-workspace');
  await runnerWorkspace.locator('.runner-result-list > button').filter({ hasText: /^User/ }).click();
  const runnerTab = page.locator('.document-tabs > .document-tab').filter({hasText: 'Run results'});
  assert.equal(await page.locator('.sidebar-run').count(), 0);
  assert.equal(await page.locator('.runner-document-tab').count(), 0);
  await runnerTab.getByRole('button', { name: 'Close', exact: true }).click();
  assert.equal(await runnerWorkspace.count(), 0);
  await runCollectionHeading.click({button: 'right'});
  await page.getByRole('menuitem', {name: 'Run collection', exact: true}).click();
  await page.getByRole('heading', {name: 'Collection Runner', exact: true}).waitFor();
  await page.locator('.document-tab.selected').getByRole('button', {name: 'Close', exact: true}).click();
  assert.equal(workspace.collections[0].requests.length, 2);
  await page.locator('.collection-title').filter({ hasText: 'Accounts' }).click();
  await page.locator('aside').getByRole('button', { name: 'New request', exact: true }).click();
  const nestedSelectedRow = page.locator('.request-branches > .collection .request-row.selected');
  await nestedSelectedRow.waitFor();
  const nestedHighlight = await page.evaluate(() => {
    const tree = document.querySelector('.collection-tree');
    const row = document.querySelector('.request-branches > .collection .request-row.selected');
    const treeBox = tree.getBoundingClientRect();
    const rowBox = row.getBoundingClientRect();
    return {
      treeLeft: treeBox.left,
      rowLeft: rowBox.left,
      rowWidth: rowBox.width,
      treeClientWidth: tree.clientWidth,
    };
  });
  assert.equal(nestedHighlight.rowLeft, nestedHighlight.treeLeft);
  assert.equal(nestedHighlight.rowWidth, nestedHighlight.treeClientWidth);
  await nestedSelectedRow.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Delete', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Continue', exact: true }).click();
  await page.getByRole('dialog').waitFor({ state: 'detached' });
  await page.locator('.request-title').filter({ hasText: 'User' }).click();
  // CRUD controls operate from the sidebar, including unopened requests.
  const userRow = page
    .locator('.request-row')
    .filter({ hasText: 'User' })
    .last();
  await userRow.hover();
  assert.equal(await userRow.locator('.request-title').evaluate(element => getComputedStyle(element).cursor), 'pointer');
  assert.equal(await userRow.locator('.tree-actions').count(), 0);
  await userRow.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Move up', exact: true }).click();
  assert.equal(workspace.collections[0].requests[0].name, 'User');
  await userRow.locator('.request-title').press('Shift+F10');
  assert.equal(await page.getByRole('menuitem', { name: 'Move up', exact: true }).isDisabled(), true);
  await page.getByRole('menuitem', { name: 'Move down', exact: true }).click();
  assert.equal(workspace.collections[0].requests[0].name, 'Login');
  const loginRow = page.locator('.request-row').filter({ hasText: 'Login' });
  await userRow.dragTo(loginRow, { targetPosition: { x: RequestDropX, y: RequestDropBeforeY } });
  assert.equal(workspace.collections[0].requests[0].name, 'User');
  assert.equal(workspace.collections[0].requests[1].name, 'Login');
  await userRow.dragTo(page.locator('.collection-heading').filter({ hasText: 'Accounts' }));
  assert.equal(usersCollection().requests.some(item => item.name === 'User'), false);
  assert.equal(accountsCollection().requests.some(item => item.name === 'User'), true);
  await userRow.dragTo(loginRow, { targetPosition: { x: RequestDropX, y: RequestDropAfterY } });
  assert.deepEqual(usersCollection().requests.map(item => item.name), ['Login', 'User']);
  assert.equal(accountsCollection().requests.length, 0);
  const tree = page.locator('.collection-tree');
  await tree.evaluate((element, height) => {
    element.style.height = `${height}px`;
    element.style.flex = 'none';
  }, TreeAutoScrollViewportPx);
  await userRow.hover();
  const scrolledDown = await tree.evaluate(element => element.scrollTop);
  assert.ok(scrolledDown > 0, `Tree should overflow before auto-scroll, scrollTop=${scrolledDown}`);
  const treeBox = await tree.boundingBox();
  const userBox = await userRow.boundingBox();
  assert.ok(treeBox && userBox);
  await page.mouse.move(userBox.x + RequestDropX, userBox.y + RequestDropAfterY);
  await page.mouse.down();
  await page.mouse.move(treeBox.x + RequestDropX, treeBox.y + TreeAutoScrollPointerInsetPx);
  await page.waitForFunction(start => {
    const element = document.querySelector('.collection-tree');
    return Boolean(element && element.scrollTop < start);
  }, scrolledDown);
  const dropBox = await userRow.boundingBox();
  assert.ok(dropBox);
  await page.mouse.move(dropBox.x + RequestDropX, dropBox.y + dropBox.height / 2);
  await page.mouse.up();
  await tree.evaluate(element => {
    element.style.height = '';
    element.style.flex = '';
  });
  assert.deepEqual(usersCollection().requests.map(item => item.name), ['Login', 'User']);
  await userRow.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Duplicate', exact: true }).click();
  assert.equal(workspace.collections[0].requests.length, 3);
  const copiedRow = page.locator('.request-row').last();
  await copiedRow.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Delete', exact: true }).click();
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
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.getByRole('button', { name: 'Copy as cURL', exact: true }).click();
  await page.getByText('Copied.', { exact: true }).waitFor();
  assert.match(await page.evaluate(() => navigator.clipboard.readText()), /authorization: Bearer abc/i);

  // Collection configuration saves public headers and authentication.
  const collectionHeading = page.locator('.collection-heading').first();
  await collectionHeading.hover();
  assert.equal(await collectionHeading.locator('.tree-actions').count(), 0);
  await collectionHeading.locator('.collection-title').press('Shift+F10');
  await page.getByRole('menuitem', { name: 'Rename', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await collectionHeading.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Duplicate', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await collectionHeading.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Run collection', exact: true }).waitFor();
  await page.getByRole('menuitem', { name: 'Add request', exact: true }).waitFor();
  await page.getByRole('menuitem', { name: 'Add subcollection', exact: true }).waitFor();
  await page.keyboard.press('Escape');
  await collectionHeading.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Delete', exact: true }).click();
  await page.getByText(/Delete this collection and its \d+ requests\?/, { exact: true }).waitFor();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await collectionHeading.click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Collection settings', exact: true }).click();
  const settingsDialog = page.locator('.feature-dialog');
  await settingsDialog.getByLabel('Name', { exact: true }).last().fill('X-Common');
  await settingsDialog.getByLabel('Value', { exact: true }).first().fill('shared');
  await settingsDialog.getByRole('checkbox', { name: 'Override global settings for this collection', exact: true }).check();
  await settingsDialog.getByRole('spinbutton', { name: 'Timeout (milliseconds)', exact: true }).fill('15000');
  await settingsDialog.getByRole('button', { name: 'Save', exact: true }).click();
  assert.equal(workspace.collections[0].headers[0].value, 'shared');
  assert.equal(workspace.collections[0].settings.timeoutMs, 15000);

  // Post-response scripts own tests and response-value extraction.
  await page.getByRole('tab', { name: 'Scripts', exact: true }).click();
  await page.getByRole('textbox', { name: 'Before request', exact: true }).fill("pm.request.headers.upsert({key:'X-Ui', value:'yes'}); console.log('ui-before');");
  await page.getByRole('button', { name: 'After response', exact: true }).click();
  await page.getByRole('textbox', { name: 'After response', exact: true }).fill("pm.test('ui-script', () => pm.response.to.have.status(200)); pm.test('ui-status', () => pm.expect(pm.response.code).to.equal(200)); pm.variables.set('ui-token', pm.response.json().auth); console.log('ui-after');");
  await save();
  await page.getByRole('button', { name: 'Send', exact: true }).click();
  const response = page.locator('.response');
  await response.locator('.status-badge').filter({ hasText: '200' }).waitFor();
  assert.equal(await response.locator('.result-badge').count(), 0);
  assert.match(await response.locator('.response-meta > .muted').innerText(), /^\d+ ms · \d+ bytes$/);
  assert.equal(await response.locator('.response-panel-select').isHidden(), true);
  const bodyPanelBounds = await response.getByRole('button', {name: 'Body', exact: true}).boundingBox();
  const responseMetaBounds = await response.locator('.response-meta').boundingBox();
  assert.ok(bodyPanelBounds && responseMetaBounds && bodyPanelBounds.x < responseMetaBounds.x);
  await page.setViewportSize(CompactViewport);
  const responsePanelSelect = response.locator('.response-panel-select');
  await responsePanelSelect.waitFor({state: 'visible'});
  const compactSelectBounds = await responsePanelSelect.boundingBox();
  const compactMetaBounds = await response.locator('.response-meta').boundingBox();
  assert.ok(compactSelectBounds && compactMetaBounds && compactSelectBounds.x < compactMetaBounds.x);
  assert.equal(compactSelectBounds.width, CompactPanelSelectWidth);
  await responsePanelSelect.selectOption('ScriptConsole');
  assert.equal((await responsePanelSelect.locator('option:checked').textContent()).trim(), 'Script console');
  await response.locator('.script-console').filter({ hasText: 'ui-after' }).waitFor();
  await page.setViewportSize(WideViewport);
  assert.equal(workspace.collections[0].requests.find(r => r.name === 'User').scripts.enabled, true);
  await response.getByRole('button', { name: 'Test results', exact: true }).click();
  await response.getByText('Pass · ui-script', { exact: true }).waitFor();
  await response.getByText('Pass · ui-status', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'History · User', exact: true }).click();
  const requestHistoryDialog = page.getByRole('dialog', { name: 'History · User', exact: true });
  await requestHistoryDialog.locator('.history-row').first().waitFor();
  assert.equal(await requestHistoryDialog.locator('.history-row').filter({hasText: 'Login'}).count(), 0);
  const requestHistorySnapshot = structuredClone(historyEntries);
  const userRequestId = workspace.collections[0].requests.find(request => request.name === 'User').id;
  assert.ok(historyEntries.some(entry => entry.request.id !== userRequestId));
  await requestHistoryDialog.getByRole('button', {name: 'Clear request history', exact: true}).click();
  const clearRequestHistoryDialog = page.getByRole('dialog', {name: 'Clear request history', exact: true});
  await clearRequestHistoryDialog.getByText('Clear saved history for User?', {exact: true}).waitFor();
  await clearRequestHistoryDialog.getByRole('button', {name: 'Continue', exact: true}).click();
  await requestHistoryDialog.getByText('No matching request history.', {exact: true}).waitFor();
  assert.equal(historyEntries.some(entry => entry.request.id === userRequestId), false);
  assert.ok(historyEntries.some(entry => entry.request.id !== userRequestId));
  historyEntries = requestHistorySnapshot;
  await requestHistoryDialog.getByRole('button', { name: 'Cancel', exact: true }).click();

  // JSON tree folding and response search highlight the payload safely.
  await page.locator('.response').getByRole('button', { name: 'Body', exact: true }).click();
  const responseBodyToolbar = page.locator('.response .body-toolbar');
  const responseSearchInput = responseBodyToolbar.getByRole('textbox', {name: 'Search response', exact: true});
  await responseSearchInput.waitFor();
  const rawButtonBounds = await responseBodyToolbar.getByRole('button', {name: 'Raw', exact: true}).boundingBox();
  const responseSearchBounds = await responseSearchInput.boundingBox();
  assert.ok(rawButtonBounds && responseSearchBounds && responseSearchBounds.x > rawButtonBounds.x);
  assert.equal(responseSearchBounds.width, ResponseSearchInputWidth);
  await page.locator('.response').getByRole('button', { name: 'JSON tree', exact: true }).click();
  await page.locator('.response').getByRole('button', { name: 'Collapse all', exact: true }).click();
  assert.equal(await page.locator('.response .json-tree details').first().evaluate(element => element.open), false);
  await page.locator('.response').getByRole('button', { name: 'Expand all', exact: true }).click();
  await page.locator('.response').getByRole('textbox', { name: 'Search response', exact: true }).fill('Bearer');
  assert.equal(await page.locator('.response mark').count(), 1);
  await page.locator('.response').getByRole('button', { name: 'Next', exact: true }).click();
  assert.equal(await page.locator('.response mark.active-match').count(), 1);

  // Export downloads a real backup; importing it merges with the existing workspace.
  const importButton = page.locator('.app-header').getByRole('button', { name: 'Import', exact: true });
  const exportButton = page.locator('.app-header').getByRole('button', { name: 'Export', exact: true });
  const historyButton = page.locator('.app-header').getByRole('button', { name: 'History', exact: true });
  await exportButton.click();
  const toolsDialog = page.locator('.workspace-tools-dialog');
  await page.getByRole('dialog', { name: 'Export', exact: true }).waitFor();
  const downloadPromise = page.waitForEvent('download');
  await toolsDialog.getByRole('button', { name: 'Export', exact: true }).last().click();
  const download = await downloadPromise;
  const backup = await readFile(await download.path(), 'utf8');
  const parsedBackup = JSON.parse(backup);
  assert.equal(parsedBackup.collections.length, workspace.collections.length);
  await toolsDialog.getByRole('button', { name: 'Close', exact: true }).click();
  await importButton.click();
  await page.getByRole('dialog', { name: 'Import', exact: true }).waitFor();
  await toolsDialog.getByRole('combobox', { name: 'Import format', exact: true }).selectOption('json');
  await toolsDialog.getByRole('textbox', { name: 'Paste cURL or JSON', exact: true }).fill(backup);
  // Cancelling the native file chooser bubbles from the input, not the dialog.
  await toolsDialog.locator('input[type=file]').evaluate(input => {
    input.dispatchEvent(new Event('cancel', { bubbles: true }));
  });
  assert.equal(await toolsDialog.evaluate(element => element.open), true);
  assert.equal(await toolsDialog.getByRole('textbox', { name: 'Paste cURL or JSON', exact: true }).inputValue(), backup);
  await page.keyboard.press('Escape');
  await toolsDialog.waitFor({ state: 'detached' });
  await importButton.click();
  await page.getByRole('dialog', { name: 'Import', exact: true }).waitFor();
  assert.equal(await toolsDialog.getByRole('textbox', { name: 'Paste cURL or JSON', exact: true }).inputValue(), backup);
  const collectionCount = workspace.collections.length;
  await toolsDialog.getByRole('button', { name: 'Import', exact: true }).last().click();
  await toolsDialog.waitFor({ state: 'detached' });
  assert.equal(workspace.collections.length, collectionCount * 2);
  const importedUsers = workspace.collections.filter(item => item.name === 'Users').at(-1);
  assert.equal(importedUsers.requests.find(r => r.name === 'User').scripts.enabled, false);

  // History shows the sent headers and can reopen the recorded request.
  const globalHistorySnapshot = structuredClone(historyEntries);
  const paginationFixture = historyEntries[0];
  for (let index = 0; index < 21; index++)
    historyEntries.push({...structuredClone(paginationFixture), id: `pagination-${index}`});
  const paginationTotal = historyEntries.length;
  await historyButton.click();
  await page.getByRole('dialog', { name: 'History', exact: true }).waitFor();
  const historyPagination = toolsDialog.getByRole('navigation', {name: 'History pages', exact: true});
  await historyPagination.getByText(`${paginationTotal} records`, {exact: true}).waitFor();
  await historyPagination.getByText(/Page 1 of \d+/).waitFor();
  await historyPagination.getByRole('button', {name: 'Next page', exact: true}).click();
  await historyPagination.getByText(/Page 2 of \d+/).waitFor();
  await historyPagination.getByRole('button', {name: 'Previous page', exact: true}).click();
  historyEntries = globalHistorySnapshot;
  const historyActions = toolsDialog.locator('.dialog-actions');
  const reopenRequestButton = historyActions.getByRole('button', { name: 'Reopen request', exact: true });
  assert.equal(await reopenRequestButton.isDisabled(), true);
  assert.ok((await historyActions.getByRole('button', { name: 'Cancel', exact: true }).boundingBox()).x < (await historyActions.getByRole('button', { name: 'Refresh', exact: true }).boundingBox()).x);
  await historyActions.getByRole('button', { name: 'Clear history', exact: true }).click();
  const clearHistoryDialog = page.getByRole('dialog', { name: 'Clear history', exact: true });
  await clearHistoryDialog.getByRole('heading', { name: 'Clear history', exact: true }).waitFor();
  await clearHistoryDialog.getByText('Clear all saved request history?', { exact: true }).waitFor();
  await clearHistoryDialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await toolsDialog.locator('.history-row').first().click();
  assert.equal(await reopenRequestButton.isDisabled(), false);
  await toolsDialog.getByRole('button', { name: 'Effective request headers', exact: true }).click();
  await toolsDialog.getByRole('cell', { name: 'X-Ui', exact: true }).waitFor();
  await reopenRequestButton.click();
  await toolsDialog.waitFor({ state: 'detached' });
  // Pasted browser cURL opens a usable request tab.
  await importButton.click();
  await page.getByRole('dialog', { name: 'Import', exact: true }).waitFor();
  await toolsDialog.getByRole('combobox', { name: 'Import format', exact: true }).selectOption('curl');
  await toolsDialog.getByRole('textbox', { name: 'Paste cURL or JSON', exact: true }).fill(`curl '${url}/user' -H 'Authorization: Bearer curl-ui'`);
  await toolsDialog.getByRole('button', { name: 'Import', exact: true }).last().click();
  await toolsDialog.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Send', exact: true }).click();
  await page.locator('.response pre').filter({ hasText: 'Bearer curl-ui' }).waitFor();
  // A narrow window moves complete request tabs into the overflow menu.
  const OverflowViewport = { width: 720, height: 960 };
  const DefaultViewport = { width: 1280, height: 960 };
  await page.setViewportSize(OverflowViewport);
  const hiddenRequestsButton = page.getByRole('button', { name: 'Hidden requests', exact: true });
  await hiddenRequestsButton.waitFor();
  assert.equal(await page.locator('.document-tabs').evaluate(element => getComputedStyle(element).overflowX), 'hidden');
  await hiddenRequestsButton.click();
  const hiddenRequest = page.locator('.request-tabs-menu').getByRole('menuitem').first();
  const hiddenRequestName = await hiddenRequest.locator('.tab-name').innerText();
  await hiddenRequest.click();
  await page.locator('.document-tab.selected .tab-name').getByText(hiddenRequestName, { exact: true }).waitFor();
  assert.equal(await page.locator('.request-tabs-menu').count(), 0);
  await hiddenRequestsButton.press('ArrowDown');
  await page.locator('.request-tabs-menu').waitFor();
  await page.keyboard.press('Escape');
  assert.equal(await hiddenRequestsButton.getAttribute('aria-expanded'), 'false');
  await page.setViewportSize(DefaultViewport);
  await page.evaluate(() => new Promise(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const userTab = page.locator('.document-tab').filter({ hasText: 'User' }).first();
  if (await userTab.count()) await userTab.locator('button').first().click();
  else {
    await hiddenRequestsButton.click();
    await page.locator('.request-tabs-menu').getByRole('menuitem').filter({ hasText: 'User' }).first().click();
  }
  const activeTab = page.locator('.document-tab.selected');
  await activeTab.click({button: 'right'});
  for (const item of ['New request', 'Duplicate tab', 'Close tab', 'Close other tabs', 'Close all tabs', 'Reveal in sidebar'])
    await page.getByRole('menuitem', {name: item, exact: true}).waitFor();
  await page.keyboard.press('Escape');
  await page.getByRole('tab', { name: 'Authentication', exact: true }).click();
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
  const unsavedDialog = page.getByRole('dialog', {name: 'Unsaved changes', exact: true});
  await unsavedDialog.getByRole('heading', {name: 'Unsaved changes', exact: true}).waitFor();
  await unsavedDialog.getByText('Save changes to User changed?', {exact: true}).waitFor();
  await page
    .getByRole('dialog', {name: 'Unsaved changes', exact: true})
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
  await page.locator('.environment-selector > summary[aria-label="环境"]').waitFor();
  await page.getByRole('button', { name: '发送', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Cookie', exact: true }).waitFor();
  for (const label of ['导入', '导出', '历史记录']) await page.locator('.app-header').getByRole('button', { name: label, exact: true }).waitFor();
  await page.getByRole('tab', { name: '脚本', exact: true }).click();
  await page.getByRole('button', { name: '请求前', exact: true }).waitFor();
  await page.getByRole('button', { name: '响应后', exact: true }).waitFor();
  await page.getByRole('tab', { name: '认证', exact: true }).click();
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
    'UI smoke passed: divider dragging/keyboard resizing, sidebar request CRUD and ordering, configuration tabs, saving, login chaining, script tests, batch results, close cancellation, live Chinese and theme changes; collection headers, scripts, JSON tree/search, backup export/import and request history.',
  );
} finally {
  await browser.close();
  server.closeAllConnections();
  await new Promise((resolve) => server.close(resolve));
}
