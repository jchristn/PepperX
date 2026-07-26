# PepperX Dashboard

An administrative console for operating a PepperX node: containers and objects, cross-container
metadata search, capacity and cluster health, request history, and an API explorer.

```bash
npm install
npm run dev      # http://localhost:3000
```

The dashboard is a static single-page app. It talks to a node directly from the browser and has no
backend of its own — the node address is entered on the connect screen at runtime, not baked in at
build time, which is what lets one build point at any node.

---

## Structure

```
src/
  main.jsx            Entry point. Applies the theme before React mounts.
  App.jsx             Routes.
  index.css           Design tokens (light + dark) and base element styles.
  styles/
    components.css    Component styles. Every value comes from a token.
  context/
    AppContext.jsx    Endpoint, client, theme, locale, health polling, toasts.
  hooks/
    useFormatters.js  Locale-bound formatting helpers.
    useAsync.js       Request state with a stale-response guard.
  utils/
    api.js            ApiClient — the single point of backend access.
  i18n/
    index.js          i18next init, imported before first paint.
    localeRegistry.js Supported locales, normalization, document lang/dir.
    formatters.js     Intl wrappers, each taking an explicit locale.
    resources.js      Catalogs; generates the two pseudo-locales.
    en/de/ja.json     Translation catalogs.
  components/         Shared UI. Built before the views, and used by all of them.
  views/              One file per route.
qa/                   Playwright QA sweeps. See qa/README.md.
```

### Conventions worth knowing before changing anything

**Views never call `fetch`.** Everything goes through `ApiClient` in `utils/api.js`, so paging,
error shapes, and header conventions stay consistent. Adding an endpoint means adding a method there.

**No hardcoded colors.** `index.css` defines semantic tokens (`--color-surface`, `--color-danger`,
`--color-chart-1`) for both themes. A component referencing a hex value will look wrong in one of
them.

**Every user-visible string goes through `t()`.** Adding one means adding it to all three catalogs;
a test asserts the key sets match exactly, so a missing translation fails the build rather than
silently falling back to English.

**Formatters take an explicit locale.** `i18n/formatters.js` never calls `toLocaleString` without
one — the browser default would drift from the language the operator actually selected. `useFormatters()`
binds the active locale so components do not have to thread it through.

**Tables use `TableFrame`.** It composes the pagination strip, optional bulk-action bar, and table so
paging behaves identically everywhere. `DataTable` alone is for inline card tables with no paging.

**Row menus are portaled.** `ActionMenu` renders into `document.body` with fixed positioning to
escape the table's `overflow: auto` wrapper. The same applies to `Modal` and the toast host.

---

## The chart

`components/ActivityChart.jsx` is hand-rolled SVG rather than a charting library — the library would
be the larger part of the bundle for one chart. Two techniques in it are worth understanding before
modifying it:

- **The bucket skeleton is generated client-side** and the server's buckets are merged onto it, so a
  time window always renders the same number of bars regardless of what the server returns.
- **A fixed internal coordinate space with `width="100%"`** makes it fluid with no resize observer.

Axis labels are thinned by measuring a real rendered label rather than guessing a character count,
because label width varies enormously across locales.

---

## Internationalization

English (source), German, and Japanese, plus two generated pseudo-locales:

| Locale | Purpose |
|---|---|
| `en-XA` | Accents every letter and pads strings ~40%, matching how much longer real German and Finnish run. If a layout survives it, translation will not break it. |
| `ar-XB` | Wraps strings in bidi controls to force RTL without needing a real RTL translation. |

Both are generated from the English catalog at load time, so they never go stale.

Switch locales from the topbar, or pin one with `?lang=de`.

---

## Testing

```bash
npm test          # component and formatter tests
npm run lint
```

The Playwright sweeps in [`qa/`](qa/) catch what unit tests cannot — layout that breaks at a
particular width, a theme that renders unreadable text, a locale that overflows its container. They
need a running node and dev server:

```bash
npm install --no-save playwright
node qa/visual.mjs ./qa-output           # 8 routes × 3 widths × 2 themes
node qa/locales.mjs ./qa-output-locales  # de, ja, and both pseudo-locales
```

Both fail on console errors and horizontal overflow. Review the screenshots even when the report is
clean — they catch things assertions do not, like colliding axis labels or an unbalanced card header.

---

## Building

```bash
npm run build     # -> dist/
```

Serve `dist/` from any static host. It must fall back to `index.html` for unknown paths, or reloading
a route like `/containers` returns a 404 — see [`../docker/dashboard/nginx.conf`](../docker/dashboard/nginx.conf)
for a working configuration.

---

## Per-container caching

Each container has an in-memory read cache, configured from the Containers view. The **create**
modal exposes a Caching section that mirrors the server's defaults (enabled, LRU, 1,000 objects, a
256 MiB memory cap, evict 10, and a 1 MiB per-object ceiling); leaving it on creates the container
exactly as the server would by default. The container **detail** modal reads the live settings and
per-node statistics through `ApiClient.containerCache(name)` — hit rate, cached-object count, cache
memory, hits, misses, and evictions — and its **edit** mode saves changes via
`ApiClient.updateContainerCache(name, settings)` and refreshes the displayed stats. A memory cap or
per-object ceiling of `0` means no limit. All labels live under the `cache.*` translation keys.

## RESP (Redis) database index

A container can be addressed over the RESP protocol by an explicit database index: a Redis client
that issues `SELECT n` with a container's assigned index reaches that container instead of the
default `resp{n}`. The index is unique across all containers and must fall within the node's
`Resp.DatabaseCount` (16 by default) to be reachable. The Containers view exposes this in both the
**create** modal (an optional field, blank means unset) and the container **detail** modal — read in
view mode ("Not set" when unassigned) and editable in edit mode, where clearing the field releases
the index. Edits save through `ApiClient.updateContainerRespIndex(name, index)` (`null` to clear); a
`409` means the index is already claimed by another container and a `400` means it was negative. The
dashboard validates only that the value is a whole number `>= 0` — it does not know the node's
`DatabaseCount`, so the upper bound is left to the server. All strings live under the `resp.*`
translation keys.

## Known gaps

- **No focus trap in modals.** Escape closes them and focus moves into the dialog on open, but Tab
  can still reach the page behind.
- **Object payload previews are download-only.** The detail modal shows metadata and links to the
  payload; it does not render images or text inline.
