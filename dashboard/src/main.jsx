/**
 * Entry point.
 *
 * i18n is imported before the app so the first paint is already translated, and the theme is applied
 * to the document element before React mounts to avoid a flash of the wrong palette.
 */

import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';

import './i18n/index.js';
import './index.css';
import './styles/components.css';
import App from './App.jsx';
import { loadRuntimeConfig } from '@utils/runtimeConfig.js';

// Applied here rather than in a hook: a hook runs after the first paint, which is exactly when a
// dark-mode user would see a white flash.
try {
  const stored = window.localStorage.getItem('pepperx.theme');
  const theme = stored === 'light' || stored === 'dark'
    ? stored
    : window.matchMedia?.('(prefers-color-scheme: dark)').matches
      ? 'dark'
      : 'light';
  document.documentElement.setAttribute('data-theme', theme);
} catch {
  document.documentElement.setAttribute('data-theme', 'light');
}

// Loaded before the first render so the connect screen never shows a placeholder URL and then swaps
// it out from under someone mid-keystroke. It is a same-origin static file, so the wait is
// negligible, and it resolves to defaults rather than rejecting when absent.
//
// Chained rather than awaited at the top level: top-level await would force the build target up to
// es2022, dropping browsers this otherwise supports, for no benefit here.
loadRuntimeConfig().then(() => {
  createRoot(document.getElementById('root')).render(
    <React.StrictMode>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </React.StrictMode>,
  );
});
