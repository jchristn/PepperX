/**
 * Node configuration and console preferences.
 *
 * Two ways to edit the node's settings, both persisting to its configuration file: a form of common
 * operational knobs for convenience, and a full JSON editor covering every field. Either way the
 * change takes effect after a restart, because the server captures its configuration into services at
 * startup rather than reading it live — the Restart button exits the process so a container restart
 * policy applies it. Theme and language are separate: those live only in this browser.
 */

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ConfirmModal from '@components/ConfirmModal.jsx';
import DataTable from '@components/DataTable.jsx';
import PageHeader, { Card } from '@components/PageHeader.jsx';
import CopyButton, { CopyableId } from '@components/CopyButton.jsx';
import { Field } from '@components/FilterBar.jsx';
import { Badge } from '@components/Badges.jsx';

const LOG_LEVELS = ['Debug', 'Info', 'Warn', 'Error', 'Alert', 'Critical', 'Emergency'];
const DELETE_MODES = ['Cluster', 'Local'];

/** The editable fields, pulled out of a settings response into form state. */
function toDraft(settings) {
  return {
    LogMinimumSeverity: settings?.LogMinimumSeverity || 'Info',
    VerifyChecksumOnRead: Boolean(settings?.VerifyChecksumOnRead),
    DeleteCoordinationMode: settings?.DeleteCoordinationMode || 'Cluster',
    RequestHistoryEnabled: Boolean(settings?.RequestHistoryEnabled),
    RequestHistoryRetentionDays: settings?.RequestHistoryRetentionDays ?? 0,
    RequestHistoryMaxRequestBodyBytes: settings?.RequestHistoryMaxRequestBodyBytes ?? 0,
    RequestHistoryMaxResponseBodyBytes: settings?.RequestHistoryMaxResponseBodyBytes ?? 0,
  };
}

export default function SettingsView() {
  const { t } = useTranslation();
  const { client, serverInfo, endpoint, theme, setTheme, locale, locales, setLocale, dismissSetup, notify } = useApp();
  const formatters = useFormatters();

  const [settings, setSettings] = useState(null);
  const [draft, setDraft] = useState(toDraft(null));
  const [saving, setSaving] = useState(false);
  const [restartOpen, setRestartOpen] = useState(false);

  // The full configuration document, edited as raw JSON so every field — including ones with no form
  // control — is editable.
  const [rawText, setRawText] = useState('');
  const [rawError, setRawError] = useState(null);
  const [rawBusy, setRawBusy] = useState(false);

  useEffect(() => {
    if (!client) return;
    // A node running an older build has no settings route; the rest of the page still works.
    client
      .serverSettings()
      .then((loaded) => {
        setSettings(loaded);
        setDraft(toDraft(loaded));
      })
      .catch(() => setSettings(null));

    client
      .rawServerSettings()
      .then((full) => setRawText(JSON.stringify(full, null, 2)))
      .catch(() => setRawText(''));
  }, [client]);

  const saveRawSettings = async () => {
    let parsed;
    try {
      parsed = JSON.parse(rawText);
    } catch {
      setRawError(t('settings.invalidJson'));
      return;
    }

    setRawBusy(true);
    setRawError(null);
    try {
      await client.updateRawServerSettings(parsed);
      // Reload so the editor reflects exactly what was persisted (normalized formatting and all).
      const full = await client.rawServerSettings();
      setRawText(JSON.stringify(full, null, 2));
      notify(t('settings.fullConfigSaved'), 'success');
    } catch (caught) {
      notify(caught.message, 'danger');
    } finally {
      setRawBusy(false);
    }
  };

  const saveSettings = async () => {
    setSaving(true);
    try {
      const updated = await client.updateServerSettings({
        ...draft,
        RequestHistoryRetentionDays: Number(draft.RequestHistoryRetentionDays),
        RequestHistoryMaxRequestBodyBytes: Number(draft.RequestHistoryMaxRequestBodyBytes),
        RequestHistoryMaxResponseBodyBytes: Number(draft.RequestHistoryMaxResponseBodyBytes),
      });
      setSettings(updated);
      setDraft(toDraft(updated));
      notify(t('settings.saved'), 'success');
    } catch (caught) {
      notify(caught.message, 'danger');
    } finally {
      setSaving(false);
    }
  };

  const restartNode = async () => {
    setRestartOpen(false);
    try {
      await client.restartServer();
      notify(t('settings.restarting'), 'info');
    } catch (caught) {
      // The node may drop the connection mid-response as it exits; that is success, not failure.
      if (!caught.isNetworkError) notify(caught.message, 'danger');
      else notify(t('settings.restarting'), 'info');
    }
  };

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

      <Card
        title={t('settings.editable')}
        help={t('settings.rebootNote')}
        actions={
          <button type="button" className="button-primary" onClick={saveSettings} disabled={saving || !settings}>
            {saving ? t('common.loading') : t('common.save')}
          </button>
        }
      >
        <div className="settings-form">
          <Field id="set-log" label={t('settings.logSeverity')}>
            <select
              id="set-log"
              value={draft.LogMinimumSeverity}
              onChange={(event) => setDraft({ ...draft, LogMinimumSeverity: event.target.value })}
            >
              {LOG_LEVELS.map((level) => (
                <option key={level} value={level}>
                  {level}
                </option>
              ))}
            </select>
          </Field>

          <Field id="set-delete" label={t('settings.deleteMode')}>
            <select
              id="set-delete"
              value={draft.DeleteCoordinationMode}
              onChange={(event) => setDraft({ ...draft, DeleteCoordinationMode: event.target.value })}
            >
              {DELETE_MODES.map((mode) => (
                <option key={mode} value={mode}>
                  {mode}
                </option>
              ))}
            </select>
          </Field>

          <Field id="set-verify" label={t('settings.verifyChecksum')}>
            <label className="checkbox-row" htmlFor="set-verify-input">
              <input
                id="set-verify-input"
                type="checkbox"
                checked={draft.VerifyChecksumOnRead}
                onChange={(event) => setDraft({ ...draft, VerifyChecksumOnRead: event.target.checked })}
              />
              <span>{t('common.yes')}</span>
            </label>
          </Field>

          <Field id="set-history" label={t('settings.requestHistoryEnabled')}>
            <label className="checkbox-row" htmlFor="set-history-input">
              <input
                id="set-history-input"
                type="checkbox"
                checked={draft.RequestHistoryEnabled}
                onChange={(event) => setDraft({ ...draft, RequestHistoryEnabled: event.target.checked })}
              />
              <span>{t('common.yes')}</span>
            </label>
          </Field>

          <Field id="set-retention" label={t('settings.retentionDays')}>
            <input
              id="set-retention"
              type="number"
              min="0"
              value={draft.RequestHistoryRetentionDays}
              onChange={(event) => setDraft({ ...draft, RequestHistoryRetentionDays: event.target.value })}
            />
          </Field>

          <Field id="set-reqbody" label={t('settings.maxRequestBody')}>
            <input
              id="set-reqbody"
              type="number"
              min="0"
              value={draft.RequestHistoryMaxRequestBodyBytes}
              onChange={(event) => setDraft({ ...draft, RequestHistoryMaxRequestBodyBytes: event.target.value })}
            />
          </Field>

          <Field id="set-respbody" label={t('settings.maxResponseBody')}>
            <input
              id="set-respbody"
              type="number"
              min="0"
              value={draft.RequestHistoryMaxResponseBodyBytes}
              onChange={(event) => setDraft({ ...draft, RequestHistoryMaxResponseBodyBytes: event.target.value })}
            />
          </Field>
        </div>

        <div className="settings-restart">
          <div>
            <strong>{t('settings.restart')}</strong>
            <p className="field-hint">{t('settings.restartHint')}</p>
          </div>
          <button type="button" className="button-danger" onClick={() => setRestartOpen(true)} disabled={!settings}>
            {t('settings.restart')}
          </button>
        </div>
      </Card>

      <Card
        title={t('settings.fullConfig')}
        help={t('settings.fullConfigHint')}
        actions={
          <button type="button" className="button-primary" onClick={saveRawSettings} disabled={rawBusy || !rawText}>
            {rawBusy ? t('common.loading') : t('common.save')}
          </button>
        }
      >
        <div className="settings-raw">
          <textarea
            className="settings-raw-editor"
            value={rawText}
            spellCheck={false}
            rows={22}
            onChange={(event) => {
              setRawText(event.target.value);
              setRawError(null);
            }}
          />
          {rawError ? <p className="field-error">{rawError}</p> : null}
        </div>
      </Card>

      <ConfirmModal
        open={restartOpen}
        danger
        title={t('settings.restart')}
        message={t('settings.restartConfirm')}
        confirmLabel={t('settings.restart')}
        onConfirm={restartNode}
        onCancel={() => setRestartOpen(false)}
      />

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
