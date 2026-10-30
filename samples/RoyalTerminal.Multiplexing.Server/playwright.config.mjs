import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './browser-tests/e2e',
  timeout: 45_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: process.env.ROYALTERMINAL_MUX_TEST_URL || 'http://127.0.0.1:7331',
    headless: true,
    viewport: { width: 1440, height: 960 },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    launchOptions: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH } : {},
  },
  webServer: process.env.ROYALTERMINAL_MUX_EXTERNAL_SERVER ? undefined : {
    command: 'dotnet run --project RoyalTerminal.Multiplexing.Server.csproj -c Release --no-build',
    url: 'http://127.0.0.1:7331/healthz',
    timeout: 30_000,
    reuseExistingServer: false,
    env: { ROYALTERMINAL_MUX_TOKEN: 'browser-test-token-012345678901234567890123456789', ROYALTERMINAL_MUX_URL: 'http://127.0.0.1:7331' },
  },
});
