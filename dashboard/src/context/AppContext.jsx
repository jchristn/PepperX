/**
 * Application-wide state: which node we are pointed at, the theme, and transient notifications.
 *
 * The endpoint lives here rather than in a route because every view needs a client for it, and
 * because losing it on navigation would mean re-entering a URL on every page.
 */

import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import ApiClient from '@utils/api.js';
import { currentLocale, setLocale as applyLocale } from '../i18n/index.js';
import { LOCALES } from '../i18n/localeRegistry.js';

const ENDPOINT_KEY = 'pepperx.endpoint';
const RECENT_KEY = 'pepperx.recentEndpoints';
const THEME_KEY = 'pepperx.theme';
const SETUP_KEY = 'pepperx.setupDismissed';

/** How often the topbar re-checks that the node is still answering. */
const HEALTH_INTERVAL_MS = 15000;

const AppContext = createContext(null);

function readStored(key, fallback = null) {
  try {
    return window.localStorage.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
}

function writeStored(key, value) {
  try {
    if (value === null || value === undefined) window.localStorage.removeItem(key);
    else window.localStorage.setItem(key, value);
  } catch {
    // Private browsing or a full quota: the app works without persistence.
  }
}

function readRecent() {
  try {
    const parsed = JSON.parse(readStored(RECENT_KEY, '[]'));
    return Array.isArray(parsed) ? parsed.filter((entry) => typeof entry === 'string') : [];
  } catch {
    return [];
  }
}

function preferredTheme() {
  const stored = readStored(THEME_KEY);
  if (stored === 'light' || stored === 'dark') return stored;
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function AppProvider({ children }) {
  const { i18n } = useTranslation();

  const [endpoint, setEndpointState] = useState(() => readStored(ENDPOINT_KEY));
  const [recentEndpoints, setRecentEndpoints] = useState(readRecent);
  const [theme, setThemeState] = useState(preferredTheme);
  const [serverInfo, setServerInfo] = useState(null);
  const [health, setHealth] = useState('checking');
  const [toasts, setToasts] = useState([]);
  const [setupDismissed, setSetupDismissedState] = useState(() => readStored(SETUP_KEY) === 'true');

  const toastId = useRef(0);

  const client = useMemo(() => (endpoint ? new ApiClient(endpoint) : null), [endpoint]);

  // ------------------------------------------------------------------ theme

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
  }, [theme]);

  const setTheme = useCallback((next) => {
    setThemeState(next);
    writeStored(THEME_KEY, next);
  }, []);

  const toggleTheme = useCallback(() => {
    setThemeState((previous) => {
      const next = previous === 'dark' ? 'light' : 'dark';
      writeStored(THEME_KEY, next);
      return next;
    });
  }, []);

  // --------------------------------------------------------------- endpoint

  const setEndpoint = useCallback((url) => {
    const trimmed = (url || '').replace(/\/+$/, '');
    setEndpointState(trimmed || null);
    writeStored(ENDPOINT_KEY, trimmed || null);

    if (trimmed) {
      setRecentEndpoints((previous) => {
        const next = [trimmed, ...previous.filter((entry) => entry !== trimmed)].slice(0, 5);
        writeStored(RECENT_KEY, JSON.stringify(next));
        return next;
      });
    }
  }, []);

  const disconnect = useCallback(() => {
    setEndpointState(null);
    setServerInfo(null);
    setHealth('checking');
    writeStored(ENDPOINT_KEY, null);
  }, []);

  // ----------------------------------------------------------------- health

  const checkHealth = useCallback(async () => {
    if (!client) return;
    try {
      const info = await client.serverInfo();
      setServerInfo(info);
      setHealth('healthy');
    } catch {
      setHealth('unreachable');
    }
  }, [client]);

  useEffect(() => {
    if (!client) {
      setHealth('checking');
      return undefined;
    }

    let cancelled = false;
    const run = async () => {
      try {
        const info = await client.serverInfo();
        if (!cancelled) {
          setServerInfo(info);
          setHealth('healthy');
        }
      } catch {
        if (!cancelled) setHealth('unreachable');
      }
    };

    void run();
    const timer = window.setInterval(run, HEALTH_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [client]);

  // ----------------------------------------------------------------- toasts

  const dismissToast = useCallback((id) => {
    setToasts((previous) => previous.filter((toast) => toast.id !== id));
  }, []);

  const notify = useCallback(
    (message, tone = 'info') => {
      toastId.current += 1;
      const id = toastId.current;
      setToasts((previous) => [...previous, { id, message, tone }]);
      window.setTimeout(() => dismissToast(id), tone === 'danger' ? 8000 : 4000);
      return id;
    },
    [dismissToast],
  );

  // ---------------------------------------------------------------- locale

  const setLocale = useCallback((locale) => {
    void applyLocale(locale);
  }, []);

  const dismissSetup = useCallback((dismissed = true) => {
    setSetupDismissedState(dismissed);
    writeStored(SETUP_KEY, dismissed ? 'true' : null);
  }, []);

  const value = useMemo(
    () => ({
      client,
      endpoint,
      setEndpoint,
      disconnect,
      recentEndpoints,
      serverInfo,
      health,
      checkHealth,
      theme,
      setTheme,
      toggleTheme,
      locale: currentLocale(),
      locales: LOCALES,
      setLocale,
      toasts,
      notify,
      dismissToast,
      setupDismissed,
      dismissSetup,
    }),
    // `i18n.resolvedLanguage` is a dependency because `currentLocale()` reads it and consumers
    // re-render on language change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [
      client, endpoint, setEndpoint, disconnect, recentEndpoints, serverInfo, health, checkHealth,
      theme, setTheme, toggleTheme, i18n.resolvedLanguage, setLocale, toasts, notify, dismissToast,
      setupDismissed, dismissSetup,
    ],
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}

/** Access application state. Throws outside the provider so the mistake surfaces immediately. */
export function useApp() {
  const context = useContext(AppContext);
  if (!context) throw new Error('useApp must be used within an AppProvider.');
  return context;
}

export default AppContext;
