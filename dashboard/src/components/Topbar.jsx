/**
 * Header showing which node is being operated and whether it is answering.
 *
 * The endpoint is displayed permanently: with multiple nodes in a cluster and no authentication to
 * distinguish them, the address is the only thing that tells an operator where a destructive action
 * will land.
 */

import React from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import { DiscordIcon, GlobeIcon, MoonIcon, PowerIcon, SunIcon } from './Icons.jsx';

export default function Topbar() {
  const { t } = useTranslation();
  const { endpoint, health, theme, toggleTheme, locale, locales, setLocale, disconnect } = useApp();

  const healthLabel =
    health === 'healthy' ? t('topbar.healthy') : health === 'unreachable' ? t('topbar.unreachable') : t('topbar.checking');

  return (
    <header className="topbar">
      <div className="topbar-endpoint">
        <span className={`health-dot is-${health}`} title={healthLabel} />
        <div className="topbar-endpoint-text">
          <span className="topbar-endpoint-label">{t('topbar.endpoint')}</span>
          <code title={endpoint ?? ''}>{endpoint}</code>
        </div>
        <span className="topbar-health-label">{healthLabel}</span>
      </div>

      <div className="topbar-actions">
        <label className="topbar-locale">
          <GlobeIcon size={16} />
          <span className="visually-hidden">{t('topbar.language')}</span>
          <select value={locale} onChange={(event) => setLocale(event.target.value)} title={t('topbar.language')}>
            {Object.values(locales).map((entry) => (
              <option key={entry.code} value={entry.code}>
                {entry.nativeName}
              </option>
            ))}
          </select>
        </label>

        <button
          type="button"
          className="button-icon"
          onClick={toggleTheme}
          title={t('topbar.theme')}
          aria-label={t('topbar.theme')}
        >
          {theme === 'dark' ? <SunIcon size={16} /> : <MoonIcon size={16} />}
        </button>

        <a
          className="button-icon"
          href="https://discord.gg/tRAN8HgvK5"
          target="_blank"
          rel="noopener noreferrer"
          title={t('topbar.discord')}
          aria-label={t('topbar.discord')}
        >
          <DiscordIcon size={16} />
        </a>

        <button
          type="button"
          className="button-icon"
          onClick={disconnect}
          title={t('topbar.disconnect')}
          aria-label={t('topbar.disconnect')}
        >
          <PowerIcon size={16} />
        </button>
      </div>
    </header>
  );
}
