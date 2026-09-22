import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

test('colors success, redirection, and client error response statuses', async ({app, page}) => {
  const {runtime, workspace, url} = app;
  const collection = runtime.newCollection('statuses', 'Status requests');
  const cases = [
    {id: 'success', name: 'Success response', path: '/user', status: '200', category: 'success'},
    {id: 'redirect', name: 'Redirect response', path: '/redirect-status', status: '301', category: 'redirection'},
    {id: 'missing', name: 'Missing response', path: '/missing-api', status: '404', category: 'client-error'},
  ];
  for (const item of cases) {
    const request = runtime.newRequest(item.id, item.name);
    request.url = url + item.path;
    collection.requests.push(request);
  }
  const noResponse = runtime.newRequest('no-response', 'No response');
  noResponse.url = 'mailto:test@example.com';
  collection.requests.push(noResponse);
  const responseWithError = runtime.newRequest('response-error', 'Response with error');
  responseWithError.url = url + '/user';
  responseWithError.scripts = {
    enabled: true,
    before: '',
    after: "throw new Error('ui response failure')",
  };
  collection.requests.push(responseWithError);
  workspace.collections.push(collection);
  await page.reload();
  await page.locator('.collection-title').getByText(collection.name, {exact: true}).click();

  const colors = [];
  for (const item of cases) {
    await page.locator('.request-title').getByText(item.name, {exact: true}).click();
    await page.getByRole('button', {name: 'Send', exact: true}).click();
    const badge = page.locator('.response .status-badge').filter({hasText: item.status});
    await badge.waitFor();
    assert.equal(await badge.getAttribute('data-status-category'), item.category);
    const appearance = await badge.evaluate(element => {
      const style = getComputedStyle(element);
      return {color: style.color, background: style.backgroundColor};
    });
    assert.notEqual(appearance.background, 'rgba(0, 0, 0, 0)');
    colors.push(appearance.color);
  }
  assert.equal(new Set(colors).size, cases.length);

  await page.locator('.request-title').getByText(noResponse.name, {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  const errorCard = page.locator('.response-error-card');
  await errorCard.getByRole('heading', {name: 'Invalid request configuration', exact: true}).waitFor();
  await errorCard.getByText('No HTTP response was received.', {exact: true}).waitFor();
  assert.match(await errorCard.locator('pre').innerText(), /url/);
  assert.equal(await page.locator('.response-tabs').count(), 0);
  await page.getByRole('button', {name: 'Maximize response', exact: true}).waitFor();

  await page.locator('.request-title').getByText(responseWithError.name, {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  const errorBanner = page.locator('.response-error-banner');
  await errorBanner.getByText('Script execution failed.', {exact: true}).waitFor();
  assert.match(await errorBanner.locator('pre').innerText(), /after[\s\S]*ui response failure/);
  await page.locator('.response-tabs').waitFor();
  await page.locator('.response .status-badge').filter({hasText: '200'}).waitFor();
});
