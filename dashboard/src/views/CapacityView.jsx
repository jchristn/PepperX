/**
 * Storage usage, cluster liveness, and rehydration.
 *
 * Rehydration is here rather than in Settings because it is a capacity operation: it reconciles the
 * metadata database against what is actually on disk. Verify is the default and the only mode that
 * cannot change anything.
 */

import React, { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ConfirmModal from '@components/ConfirmModal.jsx';
import DataTable from '@components/DataTable.jsx';
import Modal from '@components/Modal.jsx';
import PageHeader, { Card, Metric } from '@components/PageHeader.jsx';
import { CopyableId } from '@components/CopyButton.jsx';
import { ErrorBanner, LoadingState } from '@components/EmptyState.jsx';
import { Badge } from '@components/Badges.jsx';
import { RefreshIcon } from '@components/Icons.jsx';

export default function CapacityView() {
  const { t } = useTranslation();
  const { client, notify } = useApp();
  const formatters = useFormatters();

  const [stats, setStats] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const [rehydrateOpen, setRehydrateOpen] = useState(false);
  const [mode, setMode] = useState('Verify');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [report, setReport] = useState(null);
  const [running, setRunning] = useState(false);

  const load = useCallback(async () => {
    if (!client) return;
    setLoading(true);
    setError(null);
    try {
      setStats(await client.statistics());
    } catch (caught) {
      setError(caught);
    } finally {
      setLoading(false);
    }
  }, [client]);

  useEffect(() => {
    void load();
  }, [load]);

  const runRehydration = async () => {
    setConfirmOpen(false);
    setRunning(true);
    try {
      const result = await client.rehydrate(mode);
      setReport(result);
      setRehydrateOpen(false);
      notify(t('capacity.rehydrateReport'), result.Success ? 'success' : 'danger');
      await load();
    } catch (caught) {
      notify(caught.message, 'danger');
    } finally {
      setRunning(false);
    }
  };

  if (loading && !stats) return <LoadingState />;

  const containers = [...(stats?.Containers ?? [])].sort((a, b) => b.TotalBytes - a.TotalBytes);
  const largest = containers[0]?.TotalBytes ?? 0;
  const nodes = stats?.Nodes ?? [];

  return (
    <div className="page">
      <PageHeader
        title={t('capacity.title')}
        subtitle={t('capacity.subtitle')}
        actions={
          <>
            <button type="button" className="button-secondary" onClick={() => void load()} disabled={loading}>
              <RefreshIcon size={16} />
              {t('common.refresh')}
            </button>
            <button type="button" className="button-primary" onClick={() => setRehydrateOpen(true)}>
              {t('capacity.rehydrate')}
            </button>
          </>
        }
      />

      <ErrorBanner error={error} onRetry={() => void load()} />

      <div className="metric-grid">
        <Metric label={t('capacity.storageUsed')} value={formatters.bytes(stats?.TotalBytes ?? 0)} />
        <Metric
          label={t('capacity.storageFree')}
          value={stats?.StorageFreeBytes > 0 ? formatters.bytes(stats.StorageFreeBytes) : '—'}
          note={stats?.StorageTotalBytes > 0 ? `${t('capacity.storageTotal')}: ${formatters.bytes(stats.StorageTotalBytes)}` : null}
        />
        <Metric label={t('capacity.databaseSize')} value={formatters.bytes(stats?.DatabaseSizeBytes ?? 0)} />
        <Metric
          label={t('home.nodes')}
          value={formatters.number(nodes.length)}
          note={t('home.nodesAlive', { alive: nodes.filter((node) => node.IsAlive).length, total: nodes.length })}
        />
      </div>

      <Card title={t('capacity.byContainer')}>
        {containers.length === 0 ? (
          <p className="muted card-padded">{t('containers.empty')}</p>
        ) : (
          <ul className="usage-list">
            {containers.map((entry) => (
              <li key={entry.Id}>
                <div className="usage-row">
                  <span className="usage-name">{entry.Name}</span>
                  <span className="usage-value">
                    {formatters.bytes(entry.TotalBytes)}
                    <span className="muted"> · {formatters.number(entry.ObjectCount)}</span>
                  </span>
                </div>
                {/* Bars are scaled to the largest container, not the volume: relative size is the
                    question this card answers. */}
                <div className="usage-bar">
                  <span style={{ width: `${largest > 0 ? (entry.TotalBytes / largest) * 100 : 0}%` }} />
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title={t('capacity.clusterNodes')} help={nodes.length <= 1 ? t('capacity.singleNode') : null}>
        <DataTable
          columns={[
            { key: 'Id', label: 'ID', render: (item) => <CopyableId value={item.Id} /> },
            { key: 'Hostname', label: t('common.host'), render: (item) => item.Hostname || '—' },
            {
              key: 'IsAlive',
              label: t('common.status'),
              render: (item) => (
                <Badge tone={item.IsAlive ? 'success' : 'danger'}>
                  {item.IsAlive ? t('capacity.alive') : t('capacity.dead')}
                </Badge>
              ),
            },
            {
              key: 'StartedUtc',
              label: t('settings.startedAt'),
              render: (item) => <span title={formatters.dateTime(item.StartedUtc)}>{formatters.relative(item.StartedUtc)}</span>,
            },
            {
              key: 'LastHeartbeatUtc',
              label: t('capacity.lastHeartbeat'),
              render: (item) => (
                <span title={formatters.dateTime(item.LastHeartbeatUtc)}>
                  {formatters.duration(item.HeartbeatAgeSeconds * 1000)}
                </span>
              ),
            },
          ]}
          items={nodes}
          rowId={(item) => item.Id}
          emptyMessage={t('capacity.singleNode')}
        />
      </Card>

      <Modal
        open={rehydrateOpen}
        onClose={() => setRehydrateOpen(false)}
        title={t('capacity.rehydrateTitle')}
        subtitle={t('capacity.rehydrateHint')}
        size="medium"
        footer={
          <>
            <button type="button" className="button-secondary" onClick={() => setRehydrateOpen(false)} disabled={running}>
              {t('common.cancel')}
            </button>
            <button
              type="button"
              className={mode === 'Verify' ? 'button-primary' : 'button-danger'}
              onClick={() => (mode === 'Verify' ? void runRehydration() : setConfirmOpen(true))}
              disabled={running}
            >
              {running ? t('capacity.rehydrateRunning') : t('capacity.rehydrate')}
            </button>
          </>
        }
      >
        <div className="form-field">
          <label htmlFor="rehydrate-mode">{t('capacity.rehydrateMode')}</label>
          <select id="rehydrate-mode" value={mode} onChange={(event) => setMode(event.target.value)}>
            <option value="Verify">{t('capacity.rehydrateVerify')}</option>
            <option value="Repair">{t('capacity.rehydrateRepair')}</option>
            <option value="Rebuild">{t('capacity.rehydrateRebuild')}</option>
          </select>
        </div>
      </Modal>

      <ConfirmModal
        open={confirmOpen}
        danger
        title={t('capacity.rehydrate')}
        message={t('capacity.rehydrateConfirm', { mode })}
        confirmLabel={t('common.confirm')}
        onConfirm={runRehydration}
        onCancel={() => setConfirmOpen(false)}
      />

      <Modal
        open={Boolean(report)}
        onClose={() => setReport(null)}
        title={t('capacity.rehydrateReport')}
        size="medium"
      >
        {report ? (
          <>
            <dl className="detail-grid">
              <dt>{t('capacity.rehydrateMode')}</dt>
              <dd>{report.Mode}</dd>
              <dt>{t('containers.title')}</dt>
              <dd>{formatters.number(report.ContainersDiscovered)}</dd>
              <dt>{t('capacity.extentsDiscovered')}</dt>
              <dd>{formatters.number(report.ExtentsDiscovered)}</dd>
              <dt>{t('capacity.rowsAdded')}</dt>
              <dd>{formatters.number(report.RowsAdded)}</dd>
              <dt>{t('capacity.rowsRemoved')}</dt>
              <dd>{formatters.number(report.RowsRemoved)}</dd>
              <dt>{t('requests.duration')}</dt>
              <dd>{formatters.duration(report.DurationMs)}</dd>
            </dl>

            <h3 className="detail-heading">{t('capacity.drift')}</h3>
            {report.Drift?.length ? (
              <ul className="drift-list">
                {report.Drift.map((entry, index) => (
                  // eslint-disable-next-line react/no-array-index-key
                  <li key={index}>{entry}</li>
                ))}
              </ul>
            ) : (
              <p className="muted">{t('common.none')}</p>
            )}
          </>
        ) : null}
      </Modal>
    </div>
  );
}
