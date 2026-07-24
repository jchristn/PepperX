# Dashboard QA scripts

Two Playwright sweeps that catch the class of regression unit tests cannot: layout that breaks at a
particular width, a theme that renders unreadable text, a locale whose longer strings overflow their
container, and console errors that only appear against a live server.

Both need a PepperX node and the dev server running:

```bash
# terminal 1 — a node with some data in it
PepperX.Server

# terminal 2
cd dashboard && npm run dev
```

Playwright is not a dependency of the dashboard, so install it on demand:

```bash
npm install --no-save playwright
npx playwright install chromium
```

## visual.mjs

Every route at 1280 / 768 / 390 px in both light and dark themes. Fails on any console error,
uncaught exception, or horizontal page overflow.

```bash
node qa/visual.mjs ./qa-output
```

Edit `ENDPOINT` at the top if the node is not on `http://localhost:8100`.

## locales.mjs

German, Japanese, and the two pseudo-locales across the main routes. Checks that
`document.documentElement` carries the right `lang` and `dir`, and that nothing overflows.

```bash
node qa/locales.mjs ./qa-output-locales
```

`en-XA` accents every letter and pads strings by ~40%, which is roughly how much longer real German
and Finnish run — if a layout survives it, translation will not break it. `ar-XB` wraps strings in
bidi controls to force a right-to-left layout without needing a real RTL translation.

Both scripts write screenshots plus a `report.txt` into the output directory. Review the screenshots
even when the report is clean: they catch what assertions cannot, like a chart with colliding axis
labels or a card with an unbalanced header.
