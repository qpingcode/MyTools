import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

test('send normalizes the effective URL without rewriting the URL input', async ({app, page}) => {
  const {runtime, workspace, url} = app;
  const collection = runtime.newCollection('url-normalization', 'URL normalization');
  const request = runtime.newRequest('spaces', 'Whitespace URL');
  const originalUrl = `  ${url}/user  `;
  request.url = originalUrl;
  collection.requests.push(request);
  workspace.collections.push(collection);
  await page.reload();

  await page.locator('.collection-title').getByText(collection.name, {exact: true}).click();
  await page.locator('.request-title').getByText(request.name, {exact: true}).click();
  const urlInput = page.locator('[data-primary-input]');
  assert.equal(await urlInput.inputValue(), originalUrl);

  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await page.locator('.response .status-badge').filter({hasText: '200'}).waitFor();
  assert.equal(await urlInput.inputValue(), originalUrl);
});
