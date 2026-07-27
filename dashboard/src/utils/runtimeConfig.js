/**
 * Configuration injected at container start.
 *
 * A static bundle cannot read environment variables, so the container's entrypoint writes
 * `config.json` into the web root and this module fetches it before the app renders. That keeps one
 * image usable against any node — the alternative, baking the URL in at build time, would mean
 * rebuilding to repoint the console.
 */

const DEFAULT_CONFIG = {
  // 127.0.0.1, not localhost: Windows browsers prefer IPv6 (::1) for localhost, and the WSL2 port
  // relay can wedge that path after a container recreate. Pinning IPv4 avoids the resulting hang.
  defaultServerUrl: 'http://127.0.0.1:8000',
};

let config = DEFAULT_CONFIG;

/**
 * Fetch `config.json` and cache it.
 *
 * Never throws: a missing or malformed file falls back to the defaults, because a console that
 * refuses to start over a configuration hint would be worse than one offering the wrong hint.
 */
export async function loadRuntimeConfig() {
  try {
    // cache: 'no-store' because the file is rewritten on every container start, and a cached copy
    // would quietly serve the previous deployment's endpoint.
    const response = await fetch('config.json', { cache: 'no-store' });
    if (!response.ok) return config;

    const loaded = await response.json();
    config = { ...DEFAULT_CONFIG, ...loaded };
  } catch {
    // No config.json (the dev server has none) or unparseable: the defaults stand.
  }
  return config;
}

/** The loaded configuration. Returns defaults if `loadRuntimeConfig` has not resolved yet. */
export function runtimeConfig() {
  return config;
}

/** Server URL to offer on the connect screen when the operator has no history of their own. */
export function defaultServerUrl() {
  return config.defaultServerUrl || DEFAULT_CONFIG.defaultServerUrl;
}
