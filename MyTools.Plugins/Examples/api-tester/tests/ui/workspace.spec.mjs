import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

const WideViewport = {width: 1280, height: 960};
const CompactViewport = {width: 640, height: 960};
const CompactPanelSelectWidth = 180;
const RequestPanelCollapseWidth = 640;
const ResponseSearchInputWidth = 200;
const VisibleTreeGuideOpacity = 0.5;
const RequestDropX = 40;
const RequestDropBeforeY = 4;
const RequestDropAfterY = 32;
const DocumentTabDropY = 20;
const DocumentTabDropBeforeX = 16;
const DocumentTabDropAfterX = 160;
const TreeAutoScrollViewportPx = 96;
const TreeAutoScrollPointerInsetPx = 4;

test('authors, executes, and manages an API request workspace', async ({app, page}) => {
  const {
    bridgeFailures,
    errors,
    historyEntries,
    messages: {en, zh},
    routes: BusRoutes,
    url,
    workspace,
  } = app;
  await page.getByRole('button', {name: 'Import', exact: true}).waitFor();
  let accountsCollection;
  let save;
  let userTab;
  let usersCollection;

  await test.step('creates collections and chains login into an authenticated request', async () => {
  await page
    .getByRole('button', { name: 'New collection', exact: true })
    .click();
  await page.getByRole('dialog').getByRole('textbox').fill('Users');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Save', exact: true })
    .click();
  await page.getByRole('button', { name: /Users/ }).waitFor();
  await page.locator('.collection-title').filter({ hasText: 'Users' }).click({ button: 'right' });
  await page.getByRole('menuitem', { name: 'Add subcollection', exact: true }).click();
  await page.getByRole('dialog').getByRole('textbox').fill('Accounts');
  await page.getByRole('dialog').getByRole('button', { name: 'Save', exact: true }).click();
  usersCollection = () => workspace.collections.find(item => item.name === 'Users');
  accountsCollection = () => workspace.collections.find(item => item.name === 'Accounts');
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
  save = async () => {
    await page
      .locator('section.workspace')
      .getByRole('button', { name: 'Save', exact: true })
      .click();
  };
  await create('Login', '/login');
  assert.equal(usersCollection().requests.length, 1);
  assert.equal(accountsCollection().requests.length, 0);
  const sidebar = page.locator('aside');
  await sidebar.getByRole('button', { name: 'New collection', exact: true }).waitFor();
  assert.equal(await sidebar.getByRole('button', { name: 'Add subcollection', exact: true }).count(), 0);
  assert.equal(await page.locator('.collection-heading.selected').count(), 0);
  await page.locator('.collection-title').filter({ hasText: 'Users' }).click();
  assert.equal(await page.locator('.collection-heading.selected').count(), 1);
  const usersToggle = page.locator('.collection-tree > .collection > .collection-heading .collection-toggle');
  assert.equal(await usersToggle.getAttribute('aria-expanded'), 'false');
  await page.locator('.collection-title').filter({ hasText: 'Users' }).click();
  assert.equal(await usersToggle.getAttribute('aria-expanded'), 'true');
  await sidebar.getByRole('button', { name: 'New collection', exact: true }).waitFor();
  await page.locator('.request-row.selected .request-title').click();
  await sidebar.getByRole('button', { name: 'New collection', exact: true }).waitFor();
  assert.equal(await page.locator('.collection-heading.selected').count(), 0);
  assert.equal(await usersToggle.getAttribute('aria-expanded'), 'true');
  await usersToggle.click();
  assert.equal(await usersToggle.getAttribute('aria-expanded'), 'false');
  assert.equal(await page.locator('.collection-heading.selected').count(), 0);
  assert.equal(await page.locator('.collection-tree > .collection > .request-branches').count(), 0);
  await usersToggle.click();
  assert.equal(await usersToggle.getAttribute('aria-expanded'), 'true');
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
  });

  await test.step('edits request configuration and runs the collection', async () => {
  assert.equal(await page.getByRole('tabpanel').locator('.pair').count(), 1);
  assert.equal(workspace.collections[0].requests.at(-1).params.length, 0);
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
  assert.equal(await urlInput.getAttribute('placeholder'), 'Enter URL');
  const originalUrl = await urlInput.inputValue();
  await urlInput.fill('example.com/users?active=true');
  await urlInput.press('Tab');
  assert.equal(await urlInput.inputValue(), 'https://example.com/users?active=true');
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
  const accountsToggle = page.locator('.request-branches > .collection > .collection-heading .collection-toggle');
  assert.equal(await accountsToggle.getAttribute('aria-expanded'), 'false');
  await page.locator('aside').getByRole('button', { name: 'New request', exact: true }).click();
  const nestedSelectedRow = page.locator('.request-branches > .collection .request-row.selected');
  await nestedSelectedRow.waitFor();
  assert.equal(await accountsToggle.getAttribute('aria-expanded'), 'true');
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
  });

  await test.step('manages sidebar requests and reorders document tabs', async () => {
  const userRow = page
    .locator('.request-row')
    .filter({ hasText: 'User' })
    .last();
  await userRow.hover();
  assert.equal(await userRow.locator('.request-title').evaluate(element => getComputedStyle(element).cursor), 'pointer');
  assert.equal(await userRow.locator('.tree-actions').count(), 0);
  await userRow.click({ button: 'right' });
  assert.equal(await page.getByRole('menuitem', { name: 'Move up', exact: true }).count(), 0);
  assert.equal(await page.getByRole('menuitem', { name: 'Move down', exact: true }).count(), 0);
  await page.keyboard.press('Escape');
  await userRow.locator('.request-title').press('Shift+F10');
  await page.getByRole('menuitem', { name: 'Duplicate', exact: true }).waitFor();
  await page.keyboard.press('Escape');
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
  const loginTab = page.locator('.document-tabs > .document-tab').filter({ hasText: 'Login' });
  userTab = page.locator('.document-tabs > .document-tab').filter({ hasText: 'User' });
  assert.deepEqual(
    await page.locator('.document-tabs > .document-tab .tab-name').allTextContents(),
    ['Login', 'User'],
  );
  await userTab.hover();
  assert.equal(
    await userTab.locator('button').first().evaluate(element => getComputedStyle(element).cursor),
    'pointer',
  );
  await userTab.locator('button').first().click();
  assert.equal(await page.locator('.document-tab.selected .tab-name').innerText(), 'User');
  await loginTab.dragTo(userTab, { targetPosition: { x: DocumentTabDropAfterX, y: DocumentTabDropY } });
  assert.deepEqual(
    await page.locator('.document-tabs > .document-tab .tab-name').allTextContents(),
    ['User', 'Login'],
  );
  await userTab.dragTo(loginTab, { targetPosition: { x: DocumentTabDropAfterX, y: DocumentTabDropY } });
  assert.deepEqual(
    await page.locator('.document-tabs > .document-tab .tab-name').allTextContents(),
    ['Login', 'User'],
  );
  await loginTab.dragTo(userTab, { targetPosition: { x: DocumentTabDropBeforeX, y: DocumentTabDropY } });
  assert.deepEqual(
    await page.locator('.document-tabs > .document-tab .tab-name').allTextContents(),
    ['Login', 'User'],
  );
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
  });

  await test.step('configures collection defaults and context actions', async () => {
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
  });

  await test.step('runs scripts, assertions, and request-scoped history', async () => {
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
  await requestHistoryDialog.getByRole('button', {name: 'Response headers', exact: true}).click();
  assert.equal(await requestHistoryDialog.getByRole('button', {name: 'Response headers', exact: true}).getAttribute('aria-pressed'), 'true');
  const mainResponsePanel = response.locator(':scope > .response-panel');
  const mainResponseTabs = mainResponsePanel.locator(':scope > .response-toolbar > .response-tabs');
  assert.equal(await mainResponseTabs.getByRole('button', {name: 'Test results', exact: true}).getAttribute('aria-pressed'), 'true');
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
  historyEntries.splice(0, historyEntries.length, ...requestHistorySnapshot);
  await requestHistoryDialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  });

  await test.step('searches and folds the JSON response tree', async () => {
  await page.locator('.response').getByRole('button', { name: 'Body', exact: true }).click();
  const responseBodyToolbar = page.locator('.response .body-toolbar');
  const responseSearchInput = responseBodyToolbar.getByRole('textbox', {name: 'Search response', exact: true});
  await responseSearchInput.waitFor();
  const formatSelect = responseBodyToolbar.getByRole('combobox', {name: 'Response format', exact: true});
  assert.equal(await formatSelect.inputValue(), 'auto');
  assert.equal((await formatSelect.locator('option:checked').textContent()).trim(), 'Auto (JSON)');
  const formatSelectBounds = await formatSelect.boundingBox();
  const responseSearchBounds = await responseSearchInput.boundingBox();
  assert.ok(formatSelectBounds && responseSearchBounds && responseSearchBounds.x > formatSelectBounds.x);
  assert.equal(responseSearchBounds.width, ResponseSearchInputWidth);
  const previewButton = responseBodyToolbar.getByRole('button', {name: 'Preview', exact: true});
  assert.equal(await previewButton.isEnabled(), true);
  await formatSelect.selectOption('xml');
  assert.equal(await previewButton.isDisabled(), true);
  await formatSelect.selectOption('json');
  assert.equal(await previewButton.isEnabled(), true);
  await previewButton.click();
  await page.locator('.response').getByRole('button', { name: 'Collapse all', exact: true }).click();
  assert.equal(await page.locator('.response .json-tree details').first().evaluate(element => element.open), false);
  await page.locator('.response').getByRole('button', { name: 'Expand all', exact: true }).click();
  await page.locator('.response').getByRole('textbox', { name: 'Search response', exact: true }).fill('Bearer');
  assert.equal(await page.locator('.response mark').count(), 1);
  await page.locator('.response').getByRole('button', { name: 'Next', exact: true }).click();
  assert.equal(await page.locator('.response mark.active-match').count(), 1);
  await previewButton.click();
  await formatSelect.selectOption('html');
  assert.equal(await previewButton.isEnabled(), true);
  await previewButton.click();
  await page.locator('.response iframe[title="HTML response preview"]').waitFor();
  await formatSelect.selectOption('auto');
  });

  const importButton = page.locator('.app-header').getByRole('button', { name: 'Import', exact: true });
  const exportButton = page.locator('.app-header').getByRole('button', { name: 'Export', exact: true });
  const historyButton = page.locator('.app-header').getByRole('button', { name: 'History', exact: true });
  const toolsDialog = page.locator('.workspace-tools-dialog');
  await test.step('exports and imports a workspace backup', async () => {
  await exportButton.click();
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
  });

  await test.step('paginates history and reopens a recorded request', async () => {
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
  historyEntries.splice(0, historyEntries.length, ...globalHistorySnapshot);
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
  });

  await test.step('imports and executes a pasted browser cURL', async () => {
  await importButton.click();
  await page.getByRole('dialog', { name: 'Import', exact: true }).waitFor();
  await toolsDialog.getByRole('combobox', { name: 'Import format', exact: true }).selectOption('curl');
  await toolsDialog.getByRole('textbox', { name: 'Paste cURL or JSON', exact: true }).fill(`curl '${url}/user' -H 'Authorization: Bearer curl-ui'`);
  await toolsDialog.getByRole('button', { name: 'Import', exact: true }).last().click();
  await toolsDialog.waitFor({ state: 'detached' });
  await page.getByRole('button', { name: 'Send', exact: true }).click();
  await page.locator('.response pre').filter({ hasText: 'Bearer curl-ui' }).waitFor();
  });

  await test.step('moves complete tabs into the responsive overflow menu', async () => {
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
  });

  await test.step('cancels host close when the active request is dirty', async () => {
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
  });

  await test.step('offers exit without saving and ignores release failures during host close', async () => {
  bridgeFailures.failNextSave = true;
  bridgeFailures.failRelease = true;
  let close = page.evaluate(() => window.mytoolsBeforeClose());
  await page
    .getByRole('dialog', {name: 'Unsaved changes', exact: true})
    .getByRole('button', {name: 'Save', exact: true})
    .click();
  const saveFailed = page.getByRole('dialog', {name: 'Save failed', exact: true});
  await saveFailed.getByText(
    'Your latest changes may not have been saved. Exit without saving?',
    {exact: true},
  ).waitFor();
  await saveFailed.getByRole('button', {name: 'Cancel', exact: true}).click();
  assert.equal(await close, false);

  bridgeFailures.failNextSave = true;
  close = page.evaluate(() => window.mytoolsBeforeClose());
  await page
    .getByRole('dialog', {name: 'Unsaved changes', exact: true})
    .getByRole('button', {name: 'Save', exact: true})
    .click();
  await page
    .getByRole('dialog', {name: 'Save failed', exact: true})
    .getByRole('button', {name: 'Exit without saving', exact: true})
    .click();
  assert.equal(await close, true);
  assert.ok(bridgeFailures.failedReleaseAttempts > 0);
  });

  await test.step('applies live language and theme host events', async () => {
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
  });
  assert.deepEqual(errors, []);
});
