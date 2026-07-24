/**
 * Node configuration and console preferences.
 *
 * Server settings are read-only: PepperX is configured by its settings file and restarted, so
 * editable fields would imply a capability the node does not have. Theme and language are the
 * exception — those live only in this browser.
 */

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import DataTable from '@components/DataTable.jsx';
import PageHeader, { Card } from '@components/PageHeader.jsx';
import CopyButton, { CopyableId } from '@components/CopyButton.jsx';
import { Badge } from '@components/Badges.jsx';

export default function SettingsView() {
  const { t } = useTranslation();
  const { client, serverInfo, endpoint, theme, setTheme, locale, locales, setLocale, dismissSetup } = useApp();
  const formatters = useFormatters();

  const [settings, setSettings] = useState(null);

  useEffect(() => {
    if (!client) return;
    // A node running an older build has no settings route; the rest of the page still works.
    client
      .serverSettings()
      .then(setSettings)
      .catch(() => setSettings(null));
  }, [client]);

  return (
    <div className="page">
      <PageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />

      <Card title={t('settings.server')}>
        <dl className="detail-grid">
          <dt>{t('topbar.endpoint')}</dt>
          <dd className="row">
            <code>{endpoint}</code>
            <CopyButton value={endpoint} />
          </dd>
          <dt>{t('settings.version')}</dt>
          <dd>{serverInfo?.Version ?? '—'}</dd>
          <dt>{t('settings.startedAt')}</dt>
          <dd>{serverInfo?.StartTimeUtc ? formatters.dateTime(serverInfo.StartTimeUtc) : '—'}</dd>
          <dt>{t('settings.uptime')}</dt>
          <dd>{serverInfo?.UptimeMs ? formatters.duration(serverInfo.UptimeMs) : '—'}</dd>
        </dl>
      </Card>

      <Card title={t('settings.protocols')} help={t('settings.protocolHint')}>
        <DataTable
          columns={[
            { key: 'Name', label: t('common.name'), render: (item) => <strong>{item.Name}</strong> },
            {
              key: 'Enabled',
              label: t('common.status'),
              render: (item) => (
                <Badge tone={item.Enabled ? 'success' : 'neutral'}>{item.Enabled ? t('common.yes') : t('common.no')}</Badge>
              ),
            },
            {
              key: 'address',
              label: t('topbar.endpoint'),
              render: (item) => <code>{`${item.Scheme}://${item.Hostname}:${item.Port}`}</code>,
            },
          ]}
          items={settings?.Protocols ?? []}
          rowId={(item) => item.Name}
          emptyMessage={t('common.unknown')}
        />
      </Card>

      <Card title={t('settings.storage')}>
        <dl className="detail-grid">
          <dt>{t('settings.storageDriver')}</dt>
          <dd>{settings?.StorageDriver ?? '—'}</dd>
          <dt>{t('settings.storageDirectory')}</dt>
          <dd className="mono break-all">{settings?.StorageDirectory ?? '—'}</dd>
          <dt>{t('settings.verifyChecksum')}</dt>
          <dd>{settings ? (settings.VerifyChecksumOnRead ? t('common.yes') : t('common.no')) : '—'}</dd>
          <dt>{t('settings.database')}</dt>
          <dd className="mono break-all">
            {settings
              ? `${settings.DatabaseType} · ${settings.DatabaseHostname}:${settings.DatabasePort}/${settings.DatabaseName}`
              : '—'}
          </dd>
        </dl>
      </Card>

      <Card title={t('settings.cluster')}>
        <dl className="detail-grid">
          <dt>{t('settings.nodeId')}</dt>
          <dd>{settings ? <CopyableId value={settings.NodeId} /> : '—'}</dd>
          <dt>{t('settings.deleteMode')}</dt>
          <dd>{settings?.DeleteCoordinationMode ?? '—'}</dd>
          <dt>{t('settings.requestHistory')}</dt>
          <dd>
            {settings
              ? settings.RequestHistoryEnabled
                ? t('settings.retentionDays', { count: settings.RequestHistoryRetentionDays })
                : t('common.no')
              : '—'}
          </dd>
        </dl>
      </Card>

      <Card title={t('settings.appearance')}>
        <div className="form-field">
          <label htmlFor="settings-theme">{t('settings.theme')}</label>
          <select id="settings-theme" value={theme} onChange={(event) => setTheme(event.target.value)}>
            <option value="light">{t('settings.themeLight')}</option>
            <option value="dark">{t('settings.themeDark')}</option>
          </select>
        </div>

        <div className="form-field">
          <label htmlFor="settings-locale">{t('settings.language')}</label>
          <select id="settings-locale" value={locale} onChange={(event) => setLocale(event.target.value)}>
            {Object.values(locales).map((entry) => (
              <option key={entry.code} value={entry.code}>
                {entry.nativeName}
              </option>
            ))}
          </select>
        </div>

        <div className="form-field">
          <button type="button" className="button-secondary" onClick={() => dismissSetup(false)}>
            {t('topbar.runSetup')}
          </button>
        </div>
      </Card>
    </div>
  );
}
