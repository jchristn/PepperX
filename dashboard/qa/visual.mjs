// Visual QA sweep: every route at three widths in both themes, capturing screenshots plus any
// console error or horizontal-overflow the page produces.
import { chromium } from 'playwright';
import fs from 'node:fs';

const OUT = process.argv[2];
const BASE = process.env.PEPPERX_DASHBOARD || 'http://localhost:3000';
const ENDPOINT = process.env.PEPPERX_ENDPOINT || 'http://localhost:8100';

const VIEWPORTS = [
  { name: 'desktop', width: 1280, height: 900 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 390, height: 844 },
];

const ROUTES = [
  ['home', '/'],
  ['containers', '/containers'],
  ['objects', '/containers/telemetry'],
  ['search', '/search'],
  ['capacity', '/capacity'],
  ['requests', '/requests'],
  ['explorer', '/explorer'],
  ['settings', '/settings'],
];

fs.mkdirSync(OUT, { recursive: true });
const problems = [];

const browser = await chromium.launch();

for (const theme of ['light', 'dark']) {
  for (const viewport of VIEWPORTS) {
    const context = await browser.newContext({ viewport: { width: viewport.width, height: viewport.height } });
    await context.addInitScript(
      ([endpoint, themeValue]) => {
        window.localStorage.setItem('pepperx.endpoint', endpoint);
        window.localStorage.setItem('pepperx.theme', themeValue);
        window.localStorage.setItem('pepperx.setupDismissed', 'true');
      },
      [ENDPOINT, theme],
    );

    const page = await context.newPage();
    page.on('console', (message) => {
      if (message.type() === 'error') problems.push(`[console ${theme}/${viewport.name}] ${message.text()}`);
    });
    page.on('pageerror', (error) => problems.push(`[pageerror ${theme}/${viewport.name}] ${error.message}`));

    for (const [name, route] of ROUTES) {
      await page.goto(BASE + route, { waitUntil: 'networkidle' });
      await page.waitForTimeout(700);

      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );
      if (overflow > 1) problems.push(`[overflow ${theme}/${viewport.name}/${name}] ${overflow}px horizontal scroll`);

      await page.screenshot({ path: `${OUT}/${theme}-${viewport.name}-${name}.png`, fullPage: true });
    }

    await context.close();
  }
}

// Connect screen has no endpoint, so it gets its own pass.
for (const theme of ['light', 'dark']) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  await context.addInitScript((themeValue) => window.localStorage.setItem('pepperx.theme', themeValue), theme);
  const page = await context.newPage();
  await page.goto(`${BASE}/connect`, { waitUntil: 'networkidle' });
  await page.screenshot({ path: `${OUT}/${theme}-desktop-connect.png`, fullPage: true });
  await context.close();
}

await browser.close();

fs.writeFileSync(`${OUT}/report.txt`, problems.join('\n') || 'no problems detected');
console.log(problems.length ? problems.join('\n') : 'CLEAN: no console errors or overflow');
