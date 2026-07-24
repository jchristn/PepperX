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

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </React.StrictMode>,
);
