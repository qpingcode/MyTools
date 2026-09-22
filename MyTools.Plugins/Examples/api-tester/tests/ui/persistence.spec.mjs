import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

test('restores open requests, selected views, expanded collections, and environment', async ({app, page}) => {
  const {runtime, workspace, url} = app;
  const collection = runtime.newCollection('collection', 'Saved collection');
  const first = runtime.newRequest('first', 'First request');
  const second = runtime.newRequest('second', 'Second request');
  first.url = url + '/user';
  second.url = url + '/user';
  collection.requests.push(first, second);
  workspace.collections.push(collection);
  workspace.environments.push({id: 'staging', name: 'Staging', variables: []});
  workspace.environmentId = 'staging';
  await page.reload();

  const collectionToggle = page.locator('.collection-toggle');
  await page.locator('.collection-title').getByText('Saved collection', {exact: true}).click();
  assert.equal(await collectionToggle.getAttribute('aria-expanded'), 'true');

  await page.locator('.request-title').getByText('First request', {exact: true}).click();
  await page.getByRole('tab', {name: 'Headers', exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await page.locator('.response').getByRole('button', {name: 'Format', exact: true}).click();
  await page.locator('.response').getByRole('combobox', {name: 'Response format', exact: true}).selectOption('json');
  await page.locator('.response').getByRole('button', {name: 'Response headers', exact: true}).click();
  await page.getByRole('button', {name: 'History', exact: true}).click();
  await page.locator('.history-row').filter({hasText: 'First request'}).click();
  await page.getByRole('button', {name: 'Reopen request', exact: true}).click();
  assert.equal(await page.locator('.document-tab button[title="First request"]').count(), 2);

  await page.locator('.request-title').getByText('Second request', {exact: true}).click();
  await page.getByRole('tab', {name: 'Body', exact: true}).click();
  assert.equal(await page.locator('.environment-selector > summary').getAttribute('title'), 'Staging');

  await page.evaluate(() => localStorage.clear());
  await page.reload();

  await page.locator('.document-tab.selected button[title="Second request"]').waitFor();
  assert.equal(await page.locator('.document-tab button[title="First request"]').count(), 2);
  assert.equal(await page.locator('.document-tab button[title="Second request"]').count(), 1);
  assert.equal(await page.getByRole('tab', {name: 'Body', exact: true}).getAttribute('aria-selected'), 'true');
  assert.equal(await collectionToggle.getAttribute('aria-expanded'), 'true');
  assert.equal(await page.locator('.environment-selector > summary').getAttribute('title'), 'Staging');

  await page.locator('.document-tab button[title="First request"]').first().click();
  assert.equal(await page.getByRole('tab', {name: 'Headers', exact: true}).getAttribute('aria-selected'), 'true');
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  const response = page.locator('.response');
  assert.equal(await response.getByRole('button', {name: 'Response headers', exact: true}).getAttribute('aria-pressed'), 'true');
  await response.getByRole('button', {name: 'Body', exact: true}).click();
  assert.equal(await response.getByRole('combobox', {name: 'Response format', exact: true}).inputValue(), 'json');
  assert.equal(await response.getByRole('button', {name: 'Format', exact: true}).getAttribute('aria-pressed'), 'true');
  await page.waitForFunction(() => document.querySelector('.response-code')?.textContent?.includes('\n'));
  assert.match(await response.locator('.response-code').innerText(), /\{\s+"auth": ""\s+\}/);

  const historyTab = page.locator('.document-tab').filter({has: page.locator('button[title="First request"]')}).nth(1);
  await historyTab.getByRole('button', {name: 'Close', exact: true}).click();
  await page.getByRole('dialog').getByRole('button', {name: 'Discard', exact: true}).click();
  const secondTab = page.locator('.document-tab').filter({has: page.locator('button[title="Second request"]')});
  await secondTab.getByRole('button', {name: 'Close', exact: true}).click();
  await page.locator('.document-tab button[title="First request"]').click();
  assert.equal(await page.evaluate(() => window.mytoolsBeforeClose()), true);
  await page.reload();

  assert.equal(await page.locator('.document-tab button[title="First request"]').count(), 1);
  assert.equal(await page.locator('.document-tab button[title="Second request"]').count(), 0);
  await page.locator('.document-tab.selected button[title="First request"]').waitFor();
});
