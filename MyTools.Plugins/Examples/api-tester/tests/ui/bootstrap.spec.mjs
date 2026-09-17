import assert from 'node:assert/strict';
import {test} from './fixtures/app.fixture.mjs';

test.describe('Workspace bootstrap and host events', () => {
  test.use({failInitialWorkspaceLoad: true});

  test('recovers from a load failure and applies host language changes', async ({app, page}) => {
    const {errors, messages: {en, zh}, routes} = app;
    await page.getByText('Could not load the workspace. Please retry.', {exact: true}).waitFor();
    assert.equal(await page.locator('.workspace-overlay').evaluate(element => getComputedStyle(element).position), 'fixed');
    assert.equal((await page.locator('.app-header').boundingBox()).y, 0);
    assert.equal(await page.locator('.app-shell').evaluate(element => element.inert), true);
    await page.evaluate(
      ({route, messages}) => window.testHostEvent(route, {
        locale: 'zh-CN',
        fallbackLocale: 'en-US',
        messages,
      }),
      {route: routes.HostEvent.LanguageChanged, messages: zh},
    );
    await page.getByText('无法加载工作区，请重试。', {exact: true}).waitFor();
    await page.getByRole('button', {name: '重试', exact: true}).click();
    await page.waitForFunction(() => !document.querySelector('.app-shell').inert);
    assert.equal(await page.locator('.workspace-overlay').count(), 0);
    assert.equal((await page.locator('.app-header').boundingBox()).y, 0);
    await page.evaluate(
      ({route, messages}) => window.testHostEvent(route, {
        locale: 'en-US',
        fallbackLocale: 'en-US',
        messages,
      }),
      {route: routes.HostEvent.LanguageChanged, messages: en},
    );
    await page.getByRole('button', {name: 'Import', exact: true}).waitFor();
    assert.deepEqual(errors, []);
  });
});
