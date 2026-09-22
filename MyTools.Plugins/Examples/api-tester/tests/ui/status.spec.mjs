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
});
