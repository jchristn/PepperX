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
import ActionMenu from '@components/ActionMenu.jsx';
import AutoRefresh from '@components/AutoRefresh.jsx';
import ConfirmModal from '@components/ConfirmModal.jsx';
import DataTable from '@components/DataTable.jsx';
import Modal from '@components/Modal.jsx';
import { JsonViewerModal } from '@components/JsonViewer.jsx';
import { CopyableId } from '@components/CopyButton.jsx';
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
  const [partsTarget, setPartsTarget] = useState(null);

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
    setPartsTarget(null);
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
        <button
          type="button"
          className="button-danger"
          onClick={(event) => {
            // The row is clickable to open its parts; keep Abort from also triggering that.
            event.stopPropagation();
            setAbortTarget(item);
          }}
        >
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
            <>
              <AutoRefresh onRefresh={() => load()} storageKey="uploads" disabled={loading} />
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
            </>
          }
        >
          <DataTable
            columns={columns}
            items={uploads}
            loading={loading}
            rowId={(item) => item.UploadId}
            emptyMessage={t('multipart.empty')}
            onRowClick={(item) => setPartsTarget(item)}
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

      <MultipartPartsModal
        upload={partsTarget}
        container={container}
        onClose={() => setPartsTarget(null)}
      />
    </div>
  );
}

/**
 * The parts of one in-progress multipart upload: inspect, view a single part's fresh metadata, and
 * delete parts. The list is fetched on open and re-fetched after a delete so the modal always reflects
 * what is actually staged server-side.
 */
function MultipartPartsModal({ upload, container, onClose }) {
  const { t } = useTranslation();
  const { client, notify } = useApp();
  const formatters = useFormatters();

  const [parts, setParts] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [detailPart, setDetailPart] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const uploadId = upload?.UploadId;

  const load = useCallback(async () => {
    if (!client || !container || !uploadId) return;
    setLoading(true);
    setError(null);
    try {
      const result = await client.multipartUploadParts(container, uploadId);
      // The server returns PascalCase: parts live under `Parts`, each with `PartNumber`, `Md5`,
      // `Sha256`, `SizeBytes`, `CreatedUtc`, `ETag`.
      setParts(result?.Parts ?? []);
    } catch (caught) {
      setError(caught);
      setParts([]);
    } finally {
      setLoading(false);
    }
  }, [client, container, uploadId]);

  useEffect(() => {
    if (!uploadId) {
      setParts([]);
      setError(null);
      setDetailPart(null);
      setDeleteTarget(null);
      return;
    }
    void load();
  }, [uploadId, load]);

  const openPartDetail = async (item) => {
    try {
      const full = await client.multipartUploadPart(container, uploadId, item.PartNumber);
      setDetailPart(full);
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const submitDelete = async () => {
    try {
      await client.deleteMultipartUploadPart(container, uploadId, deleteTarget.PartNumber);
      setDeleteTarget(null);
      notify(t('multipart.partDeleted'), 'success');
      await load();
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const columns = [
    {
      key: 'PartNumber',
      label: t('multipart.partNumber'),
      render: (item) => <span className="mono">{item.PartNumber}</span>,
    },
    {
      key: 'SizeBytes',
      label: t('multipart.partSize'),
      align: 'right',
      render: (item) => formatters.bytes(item.SizeBytes),
    },
    {
      key: 'Md5',
      label: t('multipart.partMd5'),
      render: (item) => <CopyableId value={item.Md5} truncate={12} />,
    },
    {
      key: 'Sha256',
      label: t('multipart.partSha256'),
      render: (item) => <CopyableId value={item.Sha256} truncate={12} />,
    },
    {
      key: 'CreatedUtc',
      label: t('multipart.partCreated'),
      render: (item) => (
        <span title={formatters.dateTime(item.CreatedUtc)}>{formatters.relative(item.CreatedUtc)}</span>
      ),
    },
    {
      key: 'actions',
      label: '',
      style: { width: '48px' },
      render: (item) => (
        <ActionMenu
          items={[
            { key: 'view', label: t('multipart.viewPart'), onClick: () => void openPartDetail(item) },
            { key: 'delete', label: t('multipart.deletePart'), variant: 'danger', onClick: () => setDeleteTarget(item) },
          ]}
        />
      ),
    },
  ];

  return (
    <>
      <Modal
        open={Boolean(upload)}
        onClose={onClose}
        title={upload ? t('multipart.partsTitle', { key: upload.Key }) : ''}
        subtitle={t('multipart.partsHint')}
        size="large"
      >
        {upload ? (
          <>
            <dl className="detail-grid" style={{ marginBottom: 'var(--spacing-lg)' }}>
              <dt>{t('multipart.key')}</dt>
              <dd className="mono">{upload.Key}</dd>
              <dt>{t('multipart.uploadId')}</dt>
              <dd>
                <CopyableId value={upload.UploadId} />
              </dd>
              <dt>{t('multipart.initiated')}</dt>
              <dd title={formatters.dateTime(upload.InitiatedUtc)}>{formatters.relative(upload.InitiatedUtc)}</dd>
              <dt>{t('multipart.expires')}</dt>
              <dd title={formatters.dateTime(upload.ExpiresUtc)}>{formatters.relative(upload.ExpiresUtc)}</dd>
            </dl>

            <ErrorBanner error={error} onRetry={() => void load()} />

            <DataTable
              columns={columns}
              items={parts}
              loading={loading}
              rowId={(item) => item.PartNumber}
              emptyMessage={t('multipart.partsEmpty')}
            />
          </>
        ) : null}
      </Modal>

      <JsonViewerModal
        open={Boolean(detailPart)}
        onClose={() => setDetailPart(null)}
        title={detailPart ? t('multipart.partDetailTitle', { partNumber: detailPart.PartNumber }) : ''}
        value={detailPart}
      />

      <ConfirmModal
        open={Boolean(deleteTarget)}
        danger
        title={t('multipart.deletePartTitle')}
        message={deleteTarget ? t('multipart.deletePartConfirm', { partNumber: deleteTarget.PartNumber }) : ''}
        confirmLabel={t('multipart.deletePart')}
        onConfirm={submitDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </>
  );
}
