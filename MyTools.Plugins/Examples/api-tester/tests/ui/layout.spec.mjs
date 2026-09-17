import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

const SidebarDragDistance = 90;
const ResponseDragDistance = 80;

test('resizes both workspace dividers with pointer and keyboard input', async ({app, page}) => {
  const {errors} = app;
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
  assert.deepEqual(errors, []);
});
