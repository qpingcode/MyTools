import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

test('infers response formats and previews structured text and binary media safely', async ({app, page}) => {
  const {runtime, workspace, url} = app;
  const collection = runtime.newCollection('formats', 'Response formats');
  const cases = [
    {id: 'json', name: 'JSON response', path: '/user'},
    {id: 'html', name: 'HTML response', path: '/html-response'},
    {id: 'xml', name: 'XML response', path: '/xml-response'},
    {id: 'javascript', name: 'JavaScript response', path: '/javascript-response'},
    {id: 'png', name: 'PNG response', path: '/png-response'},
    {id: 'changing', name: 'Changing response', path: '/changing-response'},
    {id: 'text', name: 'Text response', path: '/text-response'},
  ];
  for (const item of cases) {
    const request = runtime.newRequest(item.id, item.name);
    request.url = url + item.path;
    collection.requests.push(request);
  }
  workspace.collections.push(collection);
  await page.reload();
  await page.locator('.collection-title').getByText(collection.name, {exact: true}).click();

  const response = page.locator('.response');
  const format = response.getByRole('combobox', {name: 'Response format', exact: true});
  const preview = response.getByRole('button', {name: 'Preview', exact: true});
  const formatBody = response.getByRole('button', {name: 'Format', exact: true});
  await page.locator('.request-title').getByText('JSON response', {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await response.locator('.status-badge').filter({hasText: '200'}).waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (JSON)');
  assert.ok(await response.locator('.syntax-property').count() > 0);
  assert.ok(await response.locator('.syntax-string').count() > 0);

  await page.locator('.request-title').getByText('HTML response', {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await response.locator('.status-badge').filter({hasText: '200'}).waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (HTML)');
  assert.ok(await response.locator('.syntax-tag').count() > 0);
  assert.ok(await response.locator('.syntax-attribute').count() > 0);
  assert.equal(await preview.isEnabled(), true);
  await preview.click();
  const frame = response.locator('iframe[title="HTML response preview"]');
  assert.equal(await frame.getAttribute('sandbox'), '');
  await frame.contentFrame().getByRole('heading', {name: 'Preview fixture', exact: true}).waitFor();
  assert.equal(await frame.contentFrame().locator('body').getAttribute('data-script'), null);
  const initialPreviewHeight = (await frame.boundingBox()).height;
  const responseDivider = page.locator('.response-splitter');
  await responseDivider.press('Home');
  const expandedPreviewHeight = (await frame.boundingBox()).height;
  assert.ok(expandedPreviewHeight > initialPreviewHeight);
  await response.getByRole('button', {name: 'Maximize response', exact: true}).click();
  const maximizedPreviewHeight = (await frame.boundingBox()).height;
  assert.ok(maximizedPreviewHeight > initialPreviewHeight);
  await response.getByRole('button', {name: 'Restore request and response panes', exact: true}).click();

  await page.locator('.request-title').getByText('Changing response', {exact: true}).click();
  const send = page.getByRole('button', {name: 'Send', exact: true});
  await send.click();
  await response.locator('.status-badge').filter({hasText: '200'}).waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (HTML)');
  await preview.click();
  const changingFrame = response.locator('iframe[title="HTML response preview"]');
  await changingFrame.waitFor();

  await send.click();
  await changingFrame.waitFor({state: 'detached'});
  const previewUnavailable = response.getByText('The current response format cannot be previewed.', {exact: true});
  await previewUnavailable.waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (XML)');
  assert.equal(await preview.isDisabled(), true);

  await send.click();
  await previewUnavailable.waitFor({state: 'detached'});
  await changingFrame.waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (HTML)');

  await page.locator('.request-title').getByText('XML response', {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await response.locator('.status-badge').filter({hasText: '200'}).waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (XML)');
  assert.equal(await preview.isDisabled(), true);
  assert.ok(await response.locator('.syntax-tag').count() > 0);
  assert.equal(await response.locator('.response-code').innerText(), '<?xml version="1.0"?><root><item>value</item></root>');
  await formatBody.click();
  await page.waitForFunction(() => document.querySelector('.response-code')?.textContent?.includes('\n  <item>'));
  assert.match(await response.locator('.response-code').innerText(), /<root>\s+<item>value<\/item>\s+<\/root>/);

  await page.locator('.request-title').getByText('JavaScript response', {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await response.locator('.status-badge').filter({hasText: '200'}).waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (JavaScript)');
  assert.equal(await preview.isDisabled(), true);
  assert.ok(await response.locator('.syntax-keyword').count() > 0);
  assert.ok(await response.locator('.syntax-comment').count() > 0);
  assert.ok(await response.locator('.syntax-string').count() > 0);
  assert.ok(await response.locator('.syntax-number').count() > 0);
  assert.ok(await response.locator('.syntax-literal').count() > 0);

  await page.locator('.request-title').getByText('PNG response', {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await response.locator('.status-badge').filter({hasText: '200'}).waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (Hex)');
  assert.match(await response.locator('.response-code').innerText(), /89 50 4e 47/);
  assert.equal(await formatBody.isDisabled(), true);
  await format.selectOption('base64');
  assert.match(await response.locator('.response-code').innerText(), /^iVBOR/);
  assert.equal(await preview.isEnabled(), true);
  await preview.click();
  const image = response.getByRole('img', {name: 'Image response preview', exact: true});
  await image.waitFor();
  await page.waitForFunction(() => document.querySelector('.binary-image-preview')?.naturalWidth === 1);

  await page.locator('.request-title').getByText('Text response', {exact: true}).click();
  await page.getByRole('button', {name: 'Send', exact: true}).click();
  await response.locator('.status-badge').filter({hasText: '200'}).waitFor();
  assert.equal((await format.locator('option:checked').textContent()).trim(), 'Auto (Raw)');
  assert.equal(await preview.isDisabled(), true);
  assert.equal(await response.locator('.syntax-token').count(), 0);
  await format.selectOption('javascript');
  assert.equal(await preview.isDisabled(), true);
  assert.equal(await response.locator('.syntax-keyword, .syntax-string, .syntax-number, .syntax-literal, .syntax-comment').count(), 0);
  await format.selectOption('json');
  assert.equal(await preview.isEnabled(), true);
  assert.equal(await response.locator('.syntax-token').count(), 0);
  await formatBody.click();
  assert.equal(await response.locator('.response-code').innerText(), 'plain response');
  await preview.click();
  await response.getByText('This response is not valid JSON and cannot be shown as a tree.', {exact: true}).waitFor();
});
