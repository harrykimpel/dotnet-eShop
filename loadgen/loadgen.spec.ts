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

// One test per virtual user — Playwright workers parallelize *across tests*, not within a single
// test, so a lone `test()` would leave WORKERS-1 workers idle.
const VU_COUNT = parseInt(process.env.WORKERS || '5', 10);

// Fraction of virtual users that browse anonymously (no storageState). Distributed
// deterministically across vu indices so the split is predictable per run.
const ANONYMOUS_RATIO = Math.min(1, Math.max(0, parseFloat(process.env.ANONYMOUS_RATIO || '0')));

function pickStorageState(vu: number): string | undefined {
  if (AUTH_FILES.length === 0) return undefined;
  if (ANONYMOUS_RATIO > 0) {
    // Every `stride`-th vu is anonymous (vu 0, stride, 2*stride, …).
    const stride = Math.max(1, Math.round(1 / ANONYMOUS_RATIO));
    if (vu % stride === 0) return undefined;
  }
  // Round-robin across seeded users, ignoring anonymous slots so each authed user
  // still gets roughly equal share.
  const authedIndex = ANONYMOUS_RATIO > 0
    ? vu - Math.floor(vu / Math.max(1, Math.round(1 / ANONYMOUS_RATIO))) - 1
    : vu;
  return AUTH_FILES[((authedIndex % AUTH_FILES.length) + AUTH_FILES.length) % AUTH_FILES.length];
}

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

for (let vu = 0; vu < VU_COUNT; vu++) {
  const storageState = pickStorageState(vu);
  const userLabel = storageState ? path.basename(storageState, '.json') : 'anonymous';

  test(`virtual user ${vu} (${userLabel})`, async ({ browser }) => {
    const isLoggedIn = !!storageState;

    const context = await browser.newContext(storageState ? { storageState } : {});
    const page = await context.newPage();

    const endTime = Date.now() + DURATION_S * 1000;
    let nav = 0;
    let failures = 0;

    console.log(`[vu ${vu}/${userLabel}] starting (logged-in=${isLoggedIn}, duration=${DURATION_S}s, chaos=${CHAOS})`);

    try {
      while (Date.now() < endTime) {
        const url = buildUrl(isLoggedIn);
        const start = Date.now();
        nav++;
        try {
          await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30_000 });
          const ms = Date.now() - start;
          console.log(`[vu ${vu}/${userLabel}] #${nav} ${url} → ${ms}ms`);
        } catch (err) {
          failures++;
          const ms = Date.now() - start;
          console.log(`[vu ${vu}/${userLabel}] #${nav} ${url} → FAIL ${ms}ms: ${(err as Error).message.split('\n')[0]}`);
        }
        await sleep(randInt(THINK_MIN_MS, THINK_MAX_MS));
      }
    } finally {
      await context.close();
    }

    console.log(`[vu ${vu}/${userLabel}] done — ${nav} navigations, ${failures} failures`);
  });
}
