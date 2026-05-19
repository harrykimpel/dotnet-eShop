import { test } from '@playwright/test';
import path from 'path';
import fs from 'fs';

const AUTH_DIR = path.join(__dirname, '..', 'playwright', '.auth');
const LEGACY_STATE = path.join(AUTH_DIR, 'user.json');

function discoverAuthFiles(): string[] {
  if (!fs.existsSync(AUTH_DIR)) return [];
  const perUser = fs
    .readdirSync(AUTH_DIR)
    .filter(f => f.endsWith('.json') && f !== 'user.json')
    .map(f => path.join(AUTH_DIR, f));
  if (perUser.length > 0) return perUser;
  // Fallback to the legacy single-user state from e2e/login.setup.ts
  return fs.existsSync(LEGACY_STATE) ? [LEGACY_STATE] : [];
}

const AUTH_FILES = discoverAuthFiles();

const DURATION_S = parseInt(process.env.DURATION_S || '300', 10);
const THINK_MIN_MS = parseInt(process.env.THINK_MIN_MS || '1000', 10);
const THINK_MAX_MS = parseInt(process.env.THINK_MAX_MS || '5000', 10);
const CHAOS = (process.env.CHAOS || 'off').toLowerCase();
const CHAOS_PROBABILITY = parseFloat(process.env.CHAOS_PROBABILITY || '0.3');

// The seed catalog has 100 items by default. Override with PRODUCT_ID_MAX if you've added more.
const PRODUCT_ID_MAX = parseInt(process.env.PRODUCT_ID_MAX || '100', 10);
const PRODUCT_IDS = Array.from({ length: PRODUCT_ID_MAX }, (_, i) => i + 1);

// Match the chaos modes implemented in WebApp's ChaosState / ChaosMiddleware.
const FRONTEND_MODES = ['slow', 'broken-images', 'slow,broken-images'];

function pick<T>(arr: T[]): T {
  return arr[Math.floor(Math.random() * arr.length)];
}

function randInt(min: number, max: number): number {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

function buildUrl(isLoggedIn: boolean): string {
  const r = Math.random();
  let url: string;

  if (r < 0.5) {
    // 50% home / paginated catalog
    const pageNum = randInt(1, 4);
    url = pageNum === 1 ? '/' : `/?page=${pageNum}`;
  } else if (r < 0.85) {
    // 35% item detail
    url = `/item/${pick(PRODUCT_IDS)}`;
  } else if (isLoggedIn) {
    // 15% cart (only meaningful when authenticated)
    url = '/cart';
  } else {
    // Anonymous fallback: another item view instead of cart.
    url = `/item/${pick(PRODUCT_IDS)}`;
  }

  if (CHAOS === 'on' && Math.random() < CHAOS_PROBABILITY) {
    const mode = pick(FRONTEND_MODES);
    const sep = url.includes('?') ? '&' : '?';
    url = `${url}${sep}chaos=${encodeURIComponent(mode)}`;
  }

  return url;
}

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}

test('virtual user', async ({ browser }) => {
  const workerId = test.info().workerIndex;
  const storageState = AUTH_FILES.length > 0 ? AUTH_FILES[workerId % AUTH_FILES.length] : undefined;
  const userLabel = storageState ? path.basename(storageState, '.json') : 'anonymous';
  const isLoggedIn = !!storageState;

  const context = await browser.newContext(storageState ? { storageState } : {});
  const page = await context.newPage();

  const endTime = Date.now() + DURATION_S * 1000;
  let nav = 0;
  let failures = 0;

  console.log(`[vu ${workerId}/${userLabel}] starting (logged-in=${isLoggedIn}, duration=${DURATION_S}s, chaos=${CHAOS})`);

  try {
    while (Date.now() < endTime) {
      const url = buildUrl(isLoggedIn);
      const start = Date.now();
      nav++;
      try {
        await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30_000 });
        const ms = Date.now() - start;
        console.log(`[vu ${workerId}/${userLabel}] #${nav} ${url} → ${ms}ms`);
      } catch (err) {
        failures++;
        const ms = Date.now() - start;
        console.log(`[vu ${workerId}/${userLabel}] #${nav} ${url} → FAIL ${ms}ms: ${(err as Error).message.split('\n')[0]}`);
      }
      await sleep(randInt(THINK_MIN_MS, THINK_MAX_MS));
    }
  } finally {
    await context.close();
  }

  console.log(`[vu ${workerId}/${userLabel}] done — ${nav} navigations, ${failures} failures`);
});
