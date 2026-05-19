import { test as setup, expect } from '@playwright/test';
import path from 'path';
import fs from 'fs';

const USERS = (process.env.USERS || 'alice,bob')
  .split(',')
  .map(u => u.trim())
  .filter(Boolean);
const PASSWORD = process.env.PASSWORD || 'Pass123$';
const AUTH_DIR = path.join(__dirname, '..', 'playwright', '.auth');

fs.mkdirSync(AUTH_DIR, { recursive: true });

for (const username of USERS) {
  setup(`auth as ${username}`, async ({ page }) => {
    const statePath = path.join(AUTH_DIR, `${username}.json`);
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Ready for a new adventure?' })).toBeVisible();

    await page.getByLabel('Sign in').click();
    await expect(page.getByRole('heading', { name: 'Login' })).toBeVisible();

    await page.getByPlaceholder('Username').fill(username);
    await page.getByPlaceholder('Password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Login' }).click();
    await expect(page.getByRole('heading', { name: 'Ready for a new adventure?' })).toBeVisible();

    await page.context().storageState({ path: statePath });
    console.log(`[setup] wrote ${statePath}`);
  });
}
