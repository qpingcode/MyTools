import { defineConfig } from '@playwright/test';

const TestTimeoutMs = 30_000;
const WideViewport = {width: 1280, height: 960};

export default defineConfig({
  testDir: './tests/ui',
  outputDir: './bin/AgentVerification/playwright',
  fullyParallel: true,
  timeout: TestTimeoutMs,
  reporter: 'list',
  use: {
    browserName: 'chromium',
    channel: 'msedge',
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    viewport: WideViewport,
  },
});
