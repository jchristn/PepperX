/**
 * In-progress multipart uploads, one container at a time.
 *
 * A discrete page over the same list the container detail modal shows: uploads that were started but
 * never completed or aborted, which hold storage until they finish or expire. The container is chosen
 * from a dropdown and mirrored in `?container=` so a refresh or a shared link lands on the same one.
 */

import React, { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ConfirmModal from '@components/ConfirmModal.jsx';
import DataTable from '@components/DataTable.jsx';
import PageHeader, { Card, Metric } from '@components/PageHeader.jsx';
import EmptyState, { ErrorBanner } from '@components/EmptyState.jsx';
import { Field } from '@components/FilterBar.jsx';
import { ContainerIcon, RefreshIcon } from '@components/Icons.jsx';

export default function UploadsView() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const container = searchParams.get('container') || '';

  const { client, notify } = useApp();
  const formatters = useFormatters();

  // The container list feeds the dropdown; statistics drive the KPI cards. Both are supplementary —
  // a failure leaves the uploads table below usable.
  const [containers, setContainers] = useState([]);
  const [stats, setStats] = useState(null);

  const [uploads, setUploads] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [abortTarget, setAbortTarget] = useState(null);

  useEffect(() => {
    if (!client) return;
    client
      .enumerateContainers({ pageSize: 100 })
      .then((result) => setContainers(result.items))
      .catch(() => setContainers([]));
  }, [client]);

  useEffect(() => {
    if (!client) return;
    client.statistics().then(setStats).catch(() => setStats(null));
  }, [client]);

  const load = useCallback(async () => {
    if (!client || !container) {
      setUploads([]);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const result = await client.containerMultipartUploads(container);
      setUploads(result?.Uploads ?? []);
    } catch (caught) {
      setError(caught);
      setUploads([]);
    } finally {
      setLoading(false);
    }
  }, [client, container]);

  useEffect(() => {
    setAbortTarget(null);
    void load();
  }, [load]);

  const selectContainer = (name) => {
    const next = new URLSearchParams(searchParams);
    if (name) next.set('container', name);
    else next.delete('container');
    setSearchParams(next, { replace: true });
  };

  const submitAbort = async () => {
    try {
      await client.abortMultipartUpload(container, abortTarget.UploadId);
      setAbortTarget(null);
      notify(t('multipart.aborted'), 'success');
      await load();
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const columns = [
    {
      key: 'key',
      label: t('multipart.key'),
      render: (item) => <span className="mono">{item.Key}</span>,
    },
    {
      key: 'uploadId',
      label: t('multipart.uploadId'),
      render: (item) => (
        <span className="mono" title={item.UploadId}>
          {item.UploadId}
        </span>
      ),
    },
    {
      key: 'initiatedUtc',
      label: t('multipart.initiated'),
      render: (item) => (
        <span title={formatters.dateTime(item.InitiatedUtc)}>{formatters.relative(item.InitiatedUtc)}</span>
      ),
    },
    {
      key: 'expiresUtc',
      label: t('multipart.expires'),
      render: (item) => (
        <span title={formatters.dateTime(item.ExpiresUtc)}>{formatters.relative(item.ExpiresUtc)}</span>
      ),
    },
    {
      key: 'actions',
      label: '',
      style: { width: '48px' },
      render: (item) => (
        <button type="button" className="button-danger" onClick={() => setAbortTarget(item)}>
          {t('multipart.abort')}
        </button>
      ),
    },
  ];

  // The selected container's rollup comes from the statistics envelope, which carries a per-container
  // object count and size alongside the cluster totals.
  const containerStats = container ? (stats?.Containers ?? []).find((entry) => entry.Name === container) : null;

  return (
    <div className="page">
      <PageHeader title={t('uploads.title')} subtitle={t('uploads.subtitle')} />

      <ErrorBanner error={error} onRetry={() => void load()} />

      {container ? (
        <div className="metric-grid">
          <Metric label={t('uploads.kpiInProgress')} value={formatters.number(uploads.length)} />
          <Metric label={t('uploads.kpiObjects')} value={containerStats ? formatters.number(containerStats.ObjectCount) : '—'} />
          <Metric label={t('uploads.kpiSize')} value={containerStats ? formatters.bytes(containerStats.TotalBytes) : '—'} />
          <Metric label={t('uploads.kpiContainers')} value={stats ? formatters.number(stats.ContainerCount) : '—'} />
        </div>
      ) : (
        <div className="metric-grid">
          <Metric label={t('uploads.kpiContainers')} value={stats ? formatters.number(stats.ContainerCount) : '—'} />
          <Metric label={t('uploads.kpiObjects')} value={stats ? formatters.number(stats.ObjectCount) : '—'} />
          <Metric label={t('uploads.kpiStored')} value={stats ? formatters.bytes(stats.TotalBytes) : '—'} />
          <Metric label={t('capacity.storageFree')} value={stats ? formatters.bytes(stats.StorageFreeBytes) : '—'} />
        </div>
      )}

      <Card>
        <Field id="uploads-container" label={t('uploads.selectContainer')}>
          <select id="uploads-container" value={container} onChange={(event) => selectContainer(event.target.value)}>
            <option value="">{t('uploads.selectContainerPlaceholder')}</option>
            {containers.map((entry) => (
              <option key={entry.Id} value={entry.Name}>
                {entry.Name}
              </option>
            ))}
          </select>
        </Field>
      </Card>

      {!container ? (
        <EmptyState
          icon={<ContainerIcon size={22} />}
          title={t('uploads.selectPrompt')}
          message={t('uploads.selectPromptHint')}
        />
      ) : (
        <Card
          title={t('multipart.section')}
          help={t('multipart.hint')}
          actions={
            <button
              type="button"
              className="button-icon"
              onClick={() => void load()}
              disabled={loading}
              title={t('common.refresh')}
              aria-label={t('common.refresh')}
            >
              <RefreshIcon size={16} />
            </button>
          }
        >
          <DataTable
            columns={columns}
            items={uploads}
            loading={loading}
            rowId={(item) => item.UploadId}
            emptyMessage={t('multipart.empty')}
          />
        </Card>
      )}

      <ConfirmModal
        open={Boolean(abortTarget)}
        danger
        title={t('multipart.abortTitle')}
        message={abortTarget ? t('multipart.abortConfirm', { key: abortTarget.Key }) : ''}
        confirmLabel={t('multipart.abort')}
        onConfirm={submitAbort}
        onCancel={() => setAbortTarget(null)}
      />
    </div>
  );
}
