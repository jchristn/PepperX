// Locale QA: German (longest real strings), Japanese (CJK glyphs), pseudo-expansion (layout stress),
// and pseudo-RTL (direction). Overflow here means a layout that will break on real translation.
import { chromium } from 'playwright';
import fs from 'node:fs';

const OUT = process.argv[2];
const BASE = 'http://localhost:3000';
const ENDPOINT = 'http://localhost:8100';

const LOCALES = ['de', 'ja', 'en-XA', 'ar-XB'];
const ROUTES = [
  ['home', '/'],
  ['containers', '/containers'],
  ['requests', '/requests'],
  ['settings', '/settings'],
];

fs.mkdirSync(OUT, { recursive: true });
const problems = [];
const browser = await chromium.launch();

for (const locale of LOCALES) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  await context.addInitScript(
    ([endpoint, localeValue]) => {
      window.localStorage.setItem('pepperx.endpoint', endpoint);
      window.localStorage.setItem('pepperx.locale', localeValue);
      window.localStorage.setItem('pepperx.setupDismissed', 'true');
    },
    [ENDPOINT, locale],
  );

  const page = await context.newPage();
  page.on('pageerror', (error) => problems.push(`[pageerror ${locale}] ${error.message}`));

  for (const [name, route] of ROUTES) {
    await page.goto(BASE + route, { waitUntil: 'networkidle' });
    await page.waitForTimeout(600);

    const state = await page.evaluate(() => ({
      lang: document.documentElement.lang,
      dir: document.documentElement.dir,
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    }));

    if (state.overflow > 1) problems.push(`[overflow ${locale}/${name}] ${state.overflow}px`);
    if (state.lang !== locale) problems.push(`[lang ${locale}/${name}] document.lang is "${state.lang}"`);

    const expectedDir = locale === 'ar-XB' ? 'rtl' : 'ltr';
    if (state.dir !== expectedDir) problems.push(`[dir ${locale}/${name}] expected ${expectedDir}, got "${state.dir}"`);

    await page.screenshot({ path: `${OUT}/${locale}-${name}.png`, fullPage: true });
  }

  await context.close();
}

await browser.close();
fs.writeFileSync(`${OUT}/report.txt`, problems.join('\n') || 'no problems detected');
console.log(problems.length ? problems.join('\n') : 'CLEAN: all locales render without overflow, lang/dir correct');
