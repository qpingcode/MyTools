import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

const SidebarDragDistance = 90;
const ResponseDragDistance = 80;

test('resizes both workspace dividers with pointer and keyboard input', async ({app, page}) => {
  const {errors, runtime, workspace} = app;
  const collection = runtime.newCollection('layout-collection', 'Layout collection');
  collection.requests.push(runtime.newRequest('layout-request', 'Layout request'));
  workspace.collections.push(collection);
  await page.reload();
  await page.locator('.collection-title').getByText('Layout collection', {exact: true}).click();
  await page.locator('.request-title').getByText('Layout request', {exact: true}).click();
  const divider = page.locator('.splitter');
  const initialWidth = Number(await divider.getAttribute('aria-valuenow'));
  const dividerBox = await divider.boundingBox();
  await page.mouse.move(dividerBox.x + dividerBox.width / 2, dividerBox.y + 100);
  await page.mouse.down();
  await page.mouse.move(dividerBox.x + SidebarDragDistance, dividerBox.y + 100);
  await page.mouse.up();
  assert.ok(Number(await divider.getAttribute('aria-valuenow')) > initialWidth);
  await divider.press('Home');
  assert.equal(await divider.getAttribute('aria-valuenow'), await divider.getAttribute('aria-valuemin'));
  await divider.dblclick();
  assert.equal(Number(await divider.getAttribute('aria-valuenow')), initialWidth);

  const responseDivider = page.locator('.response-splitter');
  const requestPane = page.locator('.request-pane');
  const initialRequestHeight = (await requestPane.boundingBox()).height;
  const initialRequestPercent = await responseDivider.getAttribute('aria-valuenow');
  const responseDividerBox = await responseDivider.boundingBox();
  const centerX = responseDividerBox.x + responseDividerBox.width / 2;
  const centerY = responseDividerBox.y + responseDividerBox.height / 2;
  await page.mouse.move(centerX, centerY);
  await page.mouse.down();
  await page.mouse.move(centerX, centerY + ResponseDragDistance);
  await page.mouse.up();
  assert.ok((await requestPane.boundingBox()).height > initialRequestHeight);
  await responseDivider.press('Home');
  assert.equal(await responseDivider.getAttribute('aria-valuenow'), await responseDivider.getAttribute('aria-valuemin'));
  await responseDivider.dblclick();
  assert.equal(await responseDivider.getAttribute('aria-valuenow'), initialRequestPercent);

  const restoredRequestHeight = (await requestPane.boundingBox()).height;
  const maximizeResponse = page.getByRole('button', {name: 'Maximize response', exact: true});
  assert.equal(await page.locator('.response-toolbar > button').first().getAttribute('aria-label'), 'Maximize response');
  await maximizeResponse.click();
  await requestPane.waitFor({state: 'hidden'});
  await responseDivider.waitFor({state: 'hidden'});
  const restorePanes = page.getByRole('button', {name: 'Restore request and response panes', exact: true});
  assert.equal(await restorePanes.getAttribute('aria-pressed'), 'true');
  await restorePanes.click();
  await requestPane.waitFor({state: 'visible'});
  assert.equal(Math.round((await requestPane.boundingBox()).height), Math.round(restoredRequestHeight));

  const responseToolbar = page.locator('.response-toolbar');
  const toolbarCenter = async () => {
    const bounds = await responseToolbar.boundingBox();
    return {x: bounds.width / 2, y: bounds.height / 2};
  };
  await responseToolbar.dblclick({position: await toolbarCenter()});
  await requestPane.waitFor({state: 'hidden'});
  await page.getByRole('button', {name: 'Restore request and response panes', exact: true}).waitFor();
  await responseToolbar.dblclick({position: await toolbarCenter()});
  await requestPane.waitFor({state: 'visible'});
  await page.getByRole('button', {name: 'Maximize response', exact: true}).waitFor();
  assert.deepEqual(errors, []);
});
