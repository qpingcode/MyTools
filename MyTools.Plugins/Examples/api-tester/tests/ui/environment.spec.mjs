import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

const SearchFixtureEnvironmentName = 'Search Fixture';

test('supports environment menus, creation, filtering, and editing', async ({app, page}) => {
  const {errors} = app;
  const environmentMenu = page.locator('.environment-bar details:not(.environment-selector)');
  const menuTrigger = environmentMenu.locator('summary');
  const environmentSelect = page.locator('.environment-selector > summary');
  assert.equal(
    await page.locator('html').evaluate(element => getComputedStyle(element).colorScheme),
    'dark',
  );
  const optionColors = await page.locator('.environment-option-list > button')
    .first()
    .evaluate(element => ({
      background: getComputedStyle(element).backgroundColor,
      text: getComputedStyle(element).color,
    }));
  assert.notEqual(optionColors.background, optionColors.text);
  await menuTrigger.click();
  assert.equal(await environmentMenu.evaluate(element => element.open), true);
  await environmentSelect.click();
  assert.equal(await environmentMenu.evaluate(element => element.open), false);
  await environmentSelect.press('Escape');
  await menuTrigger.click();
  await menuTrigger.press('Escape');
  assert.equal(await environmentMenu.evaluate(element => element.open), false);
  await menuTrigger.click();
  await page.getByRole('button', {name: 'Default execution settings', exact: true}).click();
  assert.equal(await environmentMenu.evaluate(element => element.open), false);
  await page.getByRole('dialog').getByRole('button', {name: 'Cancel', exact: true}).click();

  await environmentSelect.click();
  const environmentOptions = page.locator('.environment-options');
  await environmentOptions.getByRole('button', {name: 'New environment', exact: true}).click();
  assert.equal(await page.locator('.environment-selector').evaluate(element => element.open), false);
  const newEnvironmentDialog = page.getByRole('dialog', {name: 'New environment', exact: true});
  assert.equal(await newEnvironmentDialog.getByRole('button', {name: 'Save', exact: true}).isDisabled(), true);
  await newEnvironmentDialog.getByRole('textbox', {name: 'Environment name', exact: true}).fill(SearchFixtureEnvironmentName);
  await newEnvironmentDialog.getByRole('textbox', {name: 'Name', exact: true}).first().fill('fixtureToken');
  await newEnvironmentDialog.getByRole('textbox', {name: 'Value', exact: true}).first().fill('fixtureValue');
  await newEnvironmentDialog.getByRole('button', {name: 'Save', exact: true}).click();

  await environmentSelect.click();
  const environmentSearch = environmentOptions.getByRole('searchbox', {name: 'Search environments', exact: true});
  const searchBox = await environmentSearch.boundingBox();
  const optionListBox = await page.locator('.environment-option-list').boundingBox();
  assert.ok(searchBox.width > (await environmentOptions.boundingBox()).width / 2);
  assert.ok(searchBox.y < optionListBox.y);
  await environmentSearch.fill('SEARCH FIX');
  await environmentOptions.getByRole('button', {name: SearchFixtureEnvironmentName, exact: true}).waitFor();
  await environmentSearch.fill('unmatched environment');
  assert.equal(await environmentOptions.getByRole('button', {name: SearchFixtureEnvironmentName, exact: true}).count(), 0);
  await environmentOptions.getByText('No matching environments.', {exact: true}).waitFor();
  await environmentOptions.getByRole('button', {name: 'New environment', exact: true}).click();
  await page.getByRole('dialog').getByRole('button', {name: 'Cancel', exact: true}).click();
  await environmentSelect.click();
  assert.equal(await environmentSearch.inputValue(), '');
  await environmentOptions.getByRole('button', {name: SearchFixtureEnvironmentName, exact: true}).waitFor();
  await environmentSearch.press('Escape');
  await environmentSelect.click();
  await environmentOptions.getByRole('button', {name: SearchFixtureEnvironmentName, exact: true}).click();
  await page.getByRole('button', {name: 'Edit environment', exact: true}).click();
  const editEnvironmentDialog = page.getByRole('dialog', {name: 'Edit environment', exact: true});
  assert.equal(await editEnvironmentDialog.getByRole('textbox', {name: 'Name', exact: true}).first().inputValue(), 'fixtureToken');
  assert.equal(await editEnvironmentDialog.getByRole('textbox', {name: 'Value', exact: true}).first().inputValue(), 'fixtureValue');
  await editEnvironmentDialog.getByRole('button', {name: 'Cancel', exact: true}).click();
  await environmentSelect.click();
  await environmentOptions.getByRole('button', {name: 'No environment', exact: true}).click();
  assert.deepEqual(errors, []);
});
