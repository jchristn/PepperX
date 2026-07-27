/**
 * Objects inside one container: browse, filter, upload, inspect, and delete.
 *
 * Mounts two ways from one component. At `containers/:container` the container is the route param —
 * the drill-down from the container list. At `/objects` it is a discrete page with a container
 * dropdown, and the choice is mirrored in `?container=` so a refresh or a shared link lands on the
 * same container. Either way the object-management UI below is identical.
 *
 * Metadata editing rewrites the object with its existing payload, because extents are immutable —
 * there is no in-place metadata update to expose, and pretending otherwise would misrepresent what
 * the store does.
 */

import React, { useCallback, useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ActionMenu from '@components/ActionMenu.jsx';
import ConfirmModal from '@components/ConfirmModal.jsx';
import { JsonViewerModal } from '@components/JsonViewer.jsx';
import { EditMetadataModal, ObjectDetailModal } from '@components/ObjectModals.jsx';
import Modal from '@components/Modal.jsx';
import PageHeader, { Card, Metric } from '@components/PageHeader.jsx';
import TableFrame from '@components/TableFrame.jsx';
import EmptyState, { ErrorBanner } from '@components/EmptyState.jsx';
import { ChipInput, Field, FilterActions, FilterGrid, TagEditor, cleanTags } from '@components/FilterBar.jsx';
import { ContainerIcon, UploadIcon } from '@components/Icons.jsx';
import { persistedPageSize } from '@components/TablePagination.jsx';

const EMPTY_FILTERS = { prefix: '', labels: [], tags: {} };

export default function ObjectsView() {
  const { t } = useTranslation();
  // Two entry points share this view: the `containers/:container` drill-down (route param) and the
  // discrete `/objects` page (dropdown-driven, reflected in `?container=`).
  const { container: routeContainer } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const discrete = routeContainer === undefined;
  const container = discrete ? searchParams.get('container') || '' : routeContainer;

  const { client, notify } = useApp();
  const formatters = useFormatters();

  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [page, setPage] = useState({ items: [], totalCount: 0 });
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(() => persistedPageSize('objects', 25));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  // The container list feeds the discrete dropdown; statistics drive the KPI cards. Both are
  // supplementary — a failure leaves the object management below fully usable.
  const [containers, setContainers] = useState([]);
  const [stats, setStats] = useState(null);

  const [uploadOpen, setUploadOpen] = useState(false);
  const [detail, setDetail] = useState(null);
  const [jsonTarget, setJsonTarget] = useState(null);
  const [editTarget, setEditTarget] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(
    async (nextFilters = filters, nextPageNumber = pageNumber, nextPageSize = pageSize) => {
      if (!client || !container) return;
      setLoading(true);
      setError(null);
      try {
        const result = await client.enumerateObjects(container, {
          ...nextFilters,
          tags: cleanTags(nextFilters.tags),
          pageSize: nextPageSize,
          skip: (nextPageNumber - 1) * nextPageSize,
          ordering: 'CreatedDescending',
        });
        setPage(result);
      } catch (caught) {
        setError(caught);
      } finally {
        setLoading(false);
      }
    },
    [client, container, filters, pageNumber, pageSize],
  );

  useEffect(() => {
    if (!client || !discrete) return;
    client
      .enumerateContainers({ pageSize: 100 })
      .then((result) => setContainers(result.items))
      .catch(() => setContainers([]));
  }, [client, discrete]);

  useEffect(() => {
    if (!client) return;
    client.statistics().then(setStats).catch(() => setStats(null));
  }, [client]);

  const selectContainer = (name) => {
    const next = new URLSearchParams(searchParams);
    if (name) next.set('container', name);
    else next.delete('container');
    setSearchParams(next, { replace: true });
  };

  useEffect(() => {
    setFilters(EMPTY_FILTERS);
    setPageNumber(1);
    // Reload from scratch when the container changes; the filter state above is intentionally reset.
  }, [container]);

  useEffect(() => {
    void load(filters, pageNumber, pageSize);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, container, pageNumber, pageSize]);

  const openDetail = async (item) => {
    try {
      const full = await client.readObjectMetadata(container, item.Key);
      setDetail(full);
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  // View JSON needs the full metadata, including the freeform Object the list query omits.
  const openJson = async (item) => {
    try {
      setJsonTarget(await client.readObjectMetadata(container, item.Key));
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  // Edit must load the full metadata first. The enumeration row omits the freeform Object, so
  // editing from it and saving would clear an object that actually exists.
  const openEdit = async (item) => {
    try {
      setEditTarget(await client.readObjectMetadata(container, item.Key));
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const submitDelete = async () => {
    try {
      await client.deleteObject(container, deleteTarget.Key);
      setDeleteTarget(null);
      notify(t('common.delete'), 'success');
      await load();
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const columns = [
    { key: 'Key', label: t('objects.key'), render: (item) => <span className="mono link-text">{item.Key}</span> },
    {
      key: 'SizeBytes',
      label: t('objects.size'),
      align: 'right',
      render: (item) => formatters.bytes(item.SizeBytes),
    },
    { key: 'ContentType', label: t('objects.contentType'), render: (item) => item.ContentType || <span className="muted">—</span> },
    {
      key: 'Labels',
      label: t('objects.labels'),
      render: (item) =>
        item.Labels?.length ? (
          <span className="chip-list">
            {item.Labels.slice(0, 3).map((label) => (
              <span className="chip is-static" key={label}>
                {label}
              </span>
            ))}
            {item.Labels.length > 3 ? <span className="muted">+{item.Labels.length - 3}</span> : null}
          </span>
        ) : (
          <span className="muted">{t('common.none')}</span>
        ),
    },
    {
      key: 'CreatedUtc',
      label: t('objects.created'),
      render: (item) => <span title={formatters.dateTime(item.CreatedUtc)}>{formatters.relative(item.CreatedUtc)}</span>,
    },
    {
      key: 'actions',
      label: '',
      style: { width: '48px' },
      render: (item) => (
        <ActionMenu
          items={[
            { key: 'view', label: t('common.view'), onClick: () => void openDetail(item) },
            { key: 'edit', label: t('common.edit'), onClick: () => void openEdit(item) },
            { key: 'json', label: t('common.viewJson'), onClick: () => void openJson(item) },
            {
              key: 'download',
              label: t('objects.downloadPayload'),
              onClick: () => window.open(client.objectUrl(container, item.Key), '_blank', 'noopener'),
            },
            { key: 'delete', label: t('common.delete'), variant: 'danger', onClick: () => setDeleteTarget(item) },
          ]}
        />
      ),
    },
  ];

  // The selected container's rollup comes from the statistics envelope, which carries a per-container
  // object count and size — cheaper and more complete than re-reading the container here.
  const containerStats = container ? (stats?.Containers ?? []).find((entry) => entry.Name === container) : null;

  return (
    <div className="page">
      <PageHeader
        breadcrumb={discrete ? null : <Link to="/containers">{t('nav.containers')}</Link>}
        title={discrete ? t('objects.pageTitle') : t('objects.title', { container })}
        subtitle={t('objects.subtitle')}
        actions={
          container ? (
            <button type="button" className="button-primary" onClick={() => setUploadOpen(true)}>
              <UploadIcon size={16} />
              {t('objects.upload')}
            </button>
          ) : null
        }
      />

      <ErrorBanner error={error} onRetry={() => void load()} />

      {container ? (
        <div className="metric-grid">
          <Metric label={t('objects.kpiObjects')} value={formatters.number(containerStats?.ObjectCount ?? page.totalCount)} />
          <Metric label={t('objects.kpiSize')} value={containerStats ? formatters.bytes(containerStats.TotalBytes) : '—'} />
          <Metric label={t('objects.kpiContainers')} value={stats ? formatters.number(stats.ContainerCount) : '—'} />
          <Metric label={t('objects.kpiStored')} value={stats ? formatters.bytes(stats.TotalBytes) : '—'} />
        </div>
      ) : (
        <div className="metric-grid">
          <Metric label={t('objects.kpiContainers')} value={stats ? formatters.number(stats.ContainerCount) : '—'} />
          <Metric label={t('objects.kpiObjects')} value={stats ? formatters.number(stats.ObjectCount) : '—'} />
          <Metric label={t('objects.kpiStored')} value={stats ? formatters.bytes(stats.TotalBytes) : '—'} />
          <Metric label={t('capacity.storageFree')} value={stats ? formatters.bytes(stats.StorageFreeBytes) : '—'} />
        </div>
      )}

      {discrete ? (
        <Card>
          <Field id="objects-container" label={t('objects.selectContainer')}>
            <select id="objects-container" value={container} onChange={(event) => selectContainer(event.target.value)}>
              <option value="">{t('objects.selectContainerPlaceholder')}</option>
              {containers.map((entry) => (
                <option key={entry.Id} value={entry.Name}>
                  {entry.Name}
                </option>
              ))}
            </select>
          </Field>
        </Card>
      ) : null}

      {!container ? (
        <EmptyState
          icon={<ContainerIcon size={22} />}
          title={t('objects.selectPrompt')}
          message={t('objects.selectPromptHint')}
        />
      ) : (
        <>
          <Card
            title={t('common.filters')}
            actions={
              <FilterActions
                disabled={loading}
                onApply={() => {
                  setPageNumber(1);
                  void load(filters, 1, pageSize);
                }}
                onClear={() => {
                  setFilters(EMPTY_FILTERS);
                  setPageNumber(1);
                  void load(EMPTY_FILTERS, 1, pageSize);
                }}
              />
            }
          >
            <FilterGrid wide>
              <Field id="filter-prefix" label={t('objects.filterPrefix')}>
                <input
                  id="filter-prefix"
                  type="text"
                  value={filters.prefix}
                  spellCheck={false}
                  onChange={(event) => setFilters({ ...filters, prefix: event.target.value })}
                />
              </Field>
              <Field id="filter-labels" label={t('objects.filterLabels')}>
                <ChipInput
                  id="filter-labels"
                  values={filters.labels}
                  onChange={(labels) => setFilters({ ...filters, labels })}
                  placeholder={t('objects.labelsPlaceholder')}
                />
              </Field>
              <Field id="filter-tags" label={t('objects.filterTags')}>
                <TagEditor
                  tags={filters.tags}
                  onChange={(tags) => setFilters({ ...filters, tags })}
                  keyLabel={t('containers.tagKey')}
                  valueLabel={t('containers.tagValue')}
                  addLabel={t('containers.addTag')}
                />
              </Field>
            </FilterGrid>
          </Card>

          <TableFrame
            columns={columns}
            items={page.items}
            totalRecords={page.totalCount}
            pageNumber={pageNumber}
            pageSize={pageSize}
            onPageChange={setPageNumber}
            onPageSizeChange={(size) => {
              setPageSize(size);
              setPageNumber(1);
            }}
            onRefresh={() => void load()}
            loading={loading}
            storageKey="objects"
            emptyMessage={filters.prefix || filters.labels.length ? t('objects.noMatches') : t('objects.empty')}
            rowId={(item) => item.Key}
            onRowClick={(item) => void openDetail(item)}
          />
        </>
      )}

      <UploadModal
        open={uploadOpen}
        container={container}
        onClose={() => setUploadOpen(false)}
        onUploaded={async () => {
          setUploadOpen(false);
          setPageNumber(1);
          await load(filters, 1, pageSize);
        }}
      />

      <ObjectDetailModal
        detail={detail}
        container={container}
        onClose={() => setDetail(null)}
        onDelete={(item) => {
          setDetail(null);
          setDeleteTarget(item);
        }}
      />

      <EditMetadataModal
        target={editTarget}
        container={container}
        onClose={() => setEditTarget(null)}
        onSaved={async () => {
          setEditTarget(null);
          await load();
        }}
      />

      <JsonViewerModal
        open={Boolean(jsonTarget)}
        onClose={() => setJsonTarget(null)}
        type={t('common.typeObject')}
        id={jsonTarget?.Key}
        value={jsonTarget}
      />

      <ConfirmModal
        open={Boolean(deleteTarget)}
        danger
        title={t('common.delete')}
        message={deleteTarget ? t('objects.deleteConfirm', { key: deleteTarget.Key }) : ''}
        confirmLabel={t('common.delete')}
        onConfirm={submitDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

/** Upload form: payload plus the three metadata forms the store supports. */
function UploadModal({ open, container, onClose, onUploaded }) {
  const { t } = useTranslation();
  const { client, notify } = useApp();

  const [key, setKey] = useState('');
  const [file, setFile] = useState(null);
  const [contentType, setContentType] = useState('');
  const [labels, setLabels] = useState([]);
  const [tags, setTags] = useState({});
  const [metadataText, setMetadataText] = useState('');
  const [jsonError, setJsonError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    setKey('');
    setFile(null);
    setContentType('');
    setLabels([]);
    setTags({});
    setMetadataText('');
    setJsonError(null);
  }, [open]);

  const submit = async () => {
    let metadataObject;
    if (metadataText.trim()) {
      try {
        metadataObject = JSON.parse(metadataText);
      } catch {
        setJsonError(t('objects.metadataObjectInvalid'));
        return;
      }
    }

    setBusy(true);
    try {
      const bytes = file ? new Uint8Array(await file.arrayBuffer()) : new Uint8Array(0);
      await client.writeObject(container, key.trim(), bytes, {
        contentType: contentType || file?.type || 'application/octet-stream',
        labels,
        tags: cleanTags(tags),
        metadataObject,
      });
      notify(t('objects.upload'), 'success');
      await onUploaded();
    } catch (caught) {
      notify(caught.message, 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('objects.uploadTitle', { container })}
      size="large"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="button-primary" onClick={submit} disabled={busy || !key.trim()}>
            {busy ? t('common.loading') : t('objects.upload')}
          </button>
        </>
      }
    >
      <Field id="upload-key" label={t('objects.key')} hint={t('objects.keyHelp')}>
        <input
          id="upload-key"
          type="text"
          value={key}
          spellCheck={false}
          placeholder={t('objects.keyPlaceholder')}
          onChange={(event) => setKey(event.target.value)}
        />
      </Field>

      <Field id="upload-file" label={t('objects.file')}>
        <input
          id="upload-file"
          type="file"
          onChange={(event) => {
            const selected = event.target.files?.[0] ?? null;
            setFile(selected);
            // Offer the browser's guess, but leave it editable.
            if (selected && !contentType) setContentType(selected.type || '');
            if (selected && !key.trim()) setKey(selected.name);
          }}
        />
      </Field>

      <Field id="upload-content-type" label={t('objects.contentType')}>
        <input
          id="upload-content-type"
          type="text"
          value={contentType}
          spellCheck={false}
          placeholder="application/octet-stream"
          onChange={(event) => setContentType(event.target.value)}
        />
      </Field>

      <Field id="upload-labels" label={t('objects.labels')}>
        <ChipInput id="upload-labels" values={labels} onChange={setLabels} placeholder={t('objects.labelsPlaceholder')} />
      </Field>

      <Field id="upload-tags" label={t('containers.tags')}>
        <TagEditor
          tags={tags}
          onChange={setTags}
          keyLabel={t('containers.tagKey')}
          valueLabel={t('containers.tagValue')}
          addLabel={t('containers.addTag')}
        />
      </Field>

      <Field id="upload-object" label={t('objects.metadataObject')} error={jsonError}>
        <textarea
          id="upload-object"
          rows={6}
          value={metadataText}
          spellCheck={false}
          placeholder={'{\n  "source": "ingest"\n}'}
          onChange={(event) => {
            setMetadataText(event.target.value);
            setJsonError(null);
          }}
        />
      </Field>
    </Modal>
  );
}
