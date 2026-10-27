import { defineConfig, devices } from '@playwright/test';

// The execution agent supplies WINGS_BASE_URL from the run's variables (default: the sample portal on a local orchestrator).
const baseURL = (process.env.WINGS_BASE_URL ?? 'http://localhost:5057/sample-portal/').replace(/\/?$/, '/');

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  retries: 0,
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    // Microsoft Edge is only available on agents where it is installed; enable with WINGS_EDGE=1.
    ...(process.env.WINGS_EDGE === '1' ? [{ name: 'msedge', use: { ...devices['Desktop Edge'], channel: 'msedge' } }] : []),
  ],
});
