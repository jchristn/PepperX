/**
 * Endpoint entry.
 *
 * PepperX has no login, so this is the app's front door: the only thing it establishes is *which*
 * node the console will operate. The URL is validated against the node's identity response before
 * being accepted, so a typo pointing at some unrelated service fails here rather than nine views
 * later.
 */

import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import ApiClient, { ApiError } from '@utils/api.js';
import { useApp } from '@context/AppContext.jsx';
import { GlobeIcon, MoonIcon, SunIcon } from '@components/Icons.jsx';

export default function ConnectView() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { setEndpoint, recentEndpoints, theme, toggleTheme, locale, locales, setLocale } = useApp();

  const [url, setUrl] = useState(recentEndpoints[0] ?? 'http://localhost:8000');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const connect = async (candidate) => {
    const target = (candidate ?? url).trim().replace(/\/+$/, '');
    if (!target) return;

    setBusy(true);
    setError(null);
    try {
      await new ApiClient(target).validate();
      setEndpoint(target);
      navigate('/', { replace: true });
    } catch (caught) {
      const isNetwork = caught instanceof ApiError && caught.isNetworkError && !caught.message.includes('not a PepperX');
      setError(isNetwork ? t('connect.errorUnreachable') : t('connect.errorNotPepperX'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="connect-view">
      <div className="connect-toolbar">
        <label className="topbar-locale">
          <GlobeIcon size={16} />
          <span className="visually-hidden">{t('connect.language')}</span>
          <select value={locale} onChange={(event) => setLocale(event.target.value)} title={t('connect.language')}>
            {Object.values(locales).map((entry) => (
              <option key={entry.code} value={entry.code}>
                {entry.nativeName}
              </option>
            ))}
          </select>
        </label>
        <button type="button" className="button-icon" onClick={toggleTheme} title={t('topbar.theme')} aria-label={t('topbar.theme')}>
          {theme === 'dark' ? <SunIcon size={16} /> : <MoonIcon size={16} />}
        </button>
      </div>

      <form
        className="connect-card"
        onSubmit={(event) => {
          event.preventDefault();
          void connect();
        }}
      >
        {/* Decorative: the product name is the adjacent heading, so alt text would only repeat it. */}
        <img className="connect-logo" src="/logo.png" alt="" width="72" height="72" />
        <h1>{t('connect.title')}</h1>
        <p className="connect-subtitle">{t('connect.subtitle')}</p>

        <div className="form-field">
          <label htmlFor="connect-url">{t('connect.serverUrl')}</label>
          <input
            id="connect-url"
            type="url"
            value={url}
            autoFocus
            spellCheck={false}
            placeholder={t('connect.serverUrlPlaceholder')}
            onChange={(event) => setUrl(event.target.value)}
          />
          {error ? <p className="field-error">{error}</p> : null}
        </div>

        <button type="submit" className="button-primary connect-submit" disabled={busy || !url.trim()}>
          {busy ? t('connect.connecting') : t('connect.connect')}
        </button>

        {recentEndpoints.length > 0 ? (
          <div className="connect-recent">
            <span className="connect-recent-label">{t('connect.recent')}</span>
            {recentEndpoints.map((entry) => (
              <button key={entry} type="button" className="connect-recent-item" onClick={() => void connect(entry)}>
                {entry}
              </button>
            ))}
          </div>
        ) : null}
      </form>
    </div>
  );
}
