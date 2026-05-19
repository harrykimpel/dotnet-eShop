import { defineConfig, devices } from '@playwright/test';
require('dotenv').config({ path: './.env' });

const BASE_URL = process.env.BASE_URL || 'http://localhost:5045';

export default defineConfig({
  testDir: './loadgen',
  fullyParallel: true,
  reporter: [['list']],
  // Each virtual-user test is a long-running loop, so we disable the per-test timeout.
  timeout: 0,
  workers: parseInt(process.env.WORKERS || '5', 10),
  use: {
    baseURL: BASE_URL,
    ...devices['Desktop Chrome'],
  },
  projects: [
    {
      name: 'setup',
      testMatch: '**/auth.setup.ts',
      // Setup tests just need a baseURL and a clean context — no storageState.
    },
    {
      name: 'loadgen',
      testMatch: '**/loadgen.spec.ts',
      // Each virtual user picks its own storageState by workerIndex inside the spec.
    },
  ],
  // No webServer block — the load generator assumes Aspire is already running.
});
