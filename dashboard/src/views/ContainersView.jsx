/**
 * Container list, creation, tag editing, and deletion.
 *
 * Deleting a non-empty container destroys every object inside it, so that path requires typing the
 * container name — the one place in the console where a confirm button alone is not enough.
 */

import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ActionMenu from '@components/ActionMenu.jsx';
import ConfirmModal from '@components/ConfirmModal.jsx';
import DataTable from '@components/DataTable.jsx';
import Modal from '@components/Modal.jsx';
import PageHeader, { Metric } from '@components/PageHeader.jsx';
import TableFrame from '@components/TableFrame.jsx';
import { CopyableId } from '@components/CopyButton.jsx';
import { ErrorBanner } from '@components/EmptyState.jsx';
import { JsonViewerModal } from '@components/JsonViewer.jsx';
import { Field, TagEditor, cleanTags } from '@components/FilterBar.jsx';
import { PlusIcon } from '@components/Icons.jsx';
import { persistedPageSize } from '@components/TablePagination.jsx';

/** Mirrors the server's container naming rule, so bad names fail before a round trip. */
const NAME_PATTERN = /^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$/;

/**
 * The server's own container-cache defaults, mirrored here so the create modal shows exactly what an
 * omitted `Cache` object would produce on the server.
 */
const CACHE_DEFAULTS = {
  Enabled: true,
  Policy: 'LRU',
  MaxObjects: 1000,
  MaxMemoryBytes: 256 * 1024 * 1024,
  EvictCount: 10,
  MaxCacheableObjectBytes: 1048576,
};

const CACHE_POLICIES = ['LRU', 'FIFO'];

// RESP addresses databases by integer only, 0..DatabaseCount-1 (16 by default), so the index is a
// bounded choice rather than free text. "None" (an empty draft) leaves the container unreachable over RESP.
const RESP_DATABASE_INDICES = Array.from({ length: 16 }, (_, i) => i);

/** Pull cache settings out of a response (or container) into editable form state. */
function cacheToDraft(cache) {
  return {
    Enabled: cache?.Enabled ?? CACHE_DEFAULTS.Enabled,
    Policy: cache?.Policy || CACHE_DEFAULTS.Policy,
    MaxObjects: cache?.MaxObjects ?? CACHE_DEFAULTS.MaxObjects,
    MaxMemoryBytes: cache?.MaxMemoryBytes ?? CACHE_DEFAULTS.MaxMemoryBytes,
    EvictCount: cache?.EvictCount ?? CACHE_DEFAULTS.EvictCount,
    MaxCacheableObjectBytes: cache?.MaxCacheableObjectBytes ?? CACHE_DEFAULTS.MaxCacheableObjectBytes,
  };
}

/** Turn form state into the numeric request body the server expects. */
function draftToCacheRequest(draft) {
  return {
    Enabled: Boolean(draft.Enabled),
    Policy: draft.Policy,
    MaxObjects: Number(draft.MaxObjects),
    MaxMemoryBytes: Number(draft.MaxMemoryBytes),
    EvictCount: Number(draft.EvictCount),
    MaxCacheableObjectBytes: Number(draft.MaxCacheableObjectBytes),
  };
}

/** Light client-side validation, mirroring the server's rules so obvious mistakes fail fast. */
function validateCacheDraft(draft, t) {
  if (!draft.Enabled) return null;
  const maxObjects = Number(draft.MaxObjects);
  const evict = Number(draft.EvictCount);
  const memory = Number(draft.MaxMemoryBytes);
  const objectSize = Number(draft.MaxCacheableObjectBytes);

  if (!Number.isInteger(maxObjects) || maxObjects < 1) return t('cache.validationMaxObjects');
  if (!Number.isInteger(evict) || evict < 1) return t('cache.validationEvictCount');
  if (evict > maxObjects) return t('cache.validationEvictTooLarge');
  if (!Number.isInteger(memory) || memory < 0) return t('cache.validationMemory');
  if (!Number.isInteger(objectSize) || objectSize < 0) return t('cache.validationObjectSize');
  return null;
}

/**
 * Normalize a RESP-index input string: blank/whitespace means "clear" (null), otherwise the numeric
 * value the server expects. Kept separate from validation so callers can build the request body.
 */
function respIndexToRequest(value) {
  const trimmed = String(value ?? '').trim();
  if (trimmed === '') return null;
  return Number(trimmed);
}

/** Turn a container's RESP index into editable input state (null becomes an empty string). */
function respIndexToDraft(index) {
  return index === undefined || index === null ? '' : String(index);
}

/** Validate a RESP-index draft: blank clears it, otherwise it must be a whole number >= 0. */
function validateRespIndexDraft(value, t) {
  const trimmed = String(value ?? '').trim();
  if (trimmed === '') return null;
  const parsed = Number(trimmed);
  if (!Number.isInteger(parsed) || parsed < 0) return t('resp.negative');
  return null;
}

/**
 * The editable cache-settings fields, shared by the create modal and the detail modal's edit mode.
 *
 * The toggle gates the rest: when caching is off the numeric knobs are irrelevant, so they collapse.
 */
function CacheSettingsFields({ idPrefix, draft, onChange }) {
  const { t } = useTranslation();
  const set = (field, value) => onChange({ ...draft, [field]: value });

  return (
    <>
      <Field id={`${idPrefix}-enabled`} label={t('cache.enable')} hint={t('cache.enableHint')}>
        <label className="checkbox-row" htmlFor={`${idPrefix}-enabled-input`}>
          <input
            id={`${idPrefix}-enabled-input`}
            type="checkbox"
            checked={draft.Enabled}
            onChange={(event) => set('Enabled', event.target.checked)}
          />
          <span>{draft.Enabled ? t('common.yes') : t('common.no')}</span>
        </label>
      </Field>

      {draft.Enabled ? (
        <div className="settings-form">
          <Field id={`${idPrefix}-policy`} label={t('cache.policy')}>
            <select id={`${idPrefix}-policy`} value={draft.Policy} onChange={(event) => set('Policy', event.target.value)}>
              {CACHE_POLICIES.map((policy) => (
                <option key={policy} value={policy}>
                  {t(policy === 'LRU' ? 'cache.policyLru' : 'cache.policyFifo')}
                </option>
              ))}
            </select>
          </Field>

          <Field id={`${idPrefix}-max-objects`} label={t('cache.maxObjects')} hint={t('cache.maxObjectsHint')}>
            <input
              id={`${idPrefix}-max-objects`}
              type="number"
              min="1"
              value={draft.MaxObjects}
              onChange={(event) => set('MaxObjects', event.target.value)}
            />
          </Field>

          <Field id={`${idPrefix}-evict`} label={t('cache.evictCount')} hint={t('cache.evictCountHint')}>
            <input
              id={`${idPrefix}-evict`}
              type="number"
              min="1"
              value={draft.EvictCount}
              onChange={(event) => set('EvictCount', event.target.value)}
            />
          </Field>

          <Field id={`${idPrefix}-max-memory`} label={t('cache.maxMemory')} hint={t('cache.maxMemoryHint')}>
            <input
              id={`${idPrefix}-max-memory`}
              type="number"
              min="0"
              value={draft.MaxMemoryBytes}
              onChange={(event) => set('MaxMemoryBytes', event.target.value)}
            />
          </Field>

          <Field id={`${idPrefix}-max-object-size`} label={t('cache.maxObjectSize')} hint={t('cache.maxObjectSizeHint')}>
            <input
              id={`${idPrefix}-max-object-size`}
              type="number"
              min="0"
              value={draft.MaxCacheableObjectBytes}
              onChange={(event) => set('MaxCacheableObjectBytes', event.target.value)}
            />
          </Field>
        </div>
      ) : null}
    </>
  );
}

/**
 * The editable RESP (Redis) database-index field, shared by the create modal and the detail modal's
 * edit mode. Blank clears the index; any whole number >= 0 claims it (uniqueness is enforced server-side).
 */
function RespIndexField({ idPrefix, value, onChange, error }) {
  const { t } = useTranslation();
  return (
    <Field id={`${idPrefix}-resp-index`} label={t('resp.label')} hint={t('resp.hint')} error={error}>
      <select
        id={`${idPrefix}-resp-index`}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{t('resp.none')}</option>
        {RESP_DATABASE_INDICES.map((index) => (
          <option key={index} value={String(index)}>
            {index}
          </option>
        ))}
      </select>
    </Field>
  );
}

export default function ContainersView() {
  const { t } = useTranslation();
  const { client, notify } = useApp();
  const formatters = useFormatters();
  const navigate = useNavigate();

  const [page, setPage] = useState({ items: [], totalCount: 0 });
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(() => persistedPageSize('containers', 25));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  // Cluster-wide rollup for the KPI cards. Supplementary to the table — a failure here leaves the
  // list fully usable, so the cards fall back to an em dash rather than surfacing an error.
  const [stats, setStats] = useState(null);

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [newTags, setNewTags] = useState({});
  const [newCache, setNewCache] = useState(cacheToDraft(CACHE_DEFAULTS));
  const [newRespIndex, setNewRespIndex] = useState('');
  const [nameError, setNameError] = useState(null);
  const [cacheError, setCacheError] = useState(null);
  const [respError, setRespError] = useState(null);
  const [saving, setSaving] = useState(false);

  // One modal renders both View and Edit; `detailMode` decides whether the tags are editable.
  const [detailTarget, setDetailTarget] = useState(null);
  const [detailMode, setDetailMode] = useState('view');
  const [jsonTarget, setJsonTarget] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(
    async (nextPageNumber = pageNumber, nextPageSize = pageSize) => {
      if (!client) return;
      setLoading(true);
      setError(null);
      try {
        const result = await client.enumerateContainers({
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
    [client, pageNumber, pageSize],
  );

  useEffect(() => {
    void load(pageNumber, pageSize);
  }, [load, pageNumber, pageSize]);

  const loadStats = useCallback(() => {
    if (!client) return;
    client.statistics().then(setStats).catch(() => setStats(null));
  }, [client]);

  useEffect(() => {
    loadStats();
  }, [loadStats]);

  const openCreate = () => {
    setNewName('');
    setNewTags({});
    setNewCache(cacheToDraft(CACHE_DEFAULTS));
    setNewRespIndex('');
    setNameError(null);
    setCacheError(null);
    setRespError(null);
    setCreateOpen(true);
  };

  const submitCreate = async () => {
    const name = newName.trim();
    if (!NAME_PATTERN.test(name)) {
      setNameError(t('containers.nameInvalid'));
      return;
    }

    const cacheValidation = validateCacheDraft(newCache, t);
    if (cacheValidation) {
      setCacheError(cacheValidation);
      return;
    }

    const respValidation = validateRespIndexDraft(newRespIndex, t);
    if (respValidation) {
      setRespError(respValidation);
      return;
    }

    setSaving(true);
    try {
      await client.createContainer(
        name,
        cleanTags(newTags),
        draftToCacheRequest(newCache),
        respIndexToRequest(newRespIndex),
      );
      setCreateOpen(false);
      notify(t('containers.create'), 'success');
      setPageNumber(1);
      await load(1, pageSize);
      loadStats();
    } catch (caught) {
      // A create can 409 on a duplicate name or on an already-claimed RESP index; when the operator
      // supplied an index, attribute the conflict to it and surface it beside that field.
      if (caught.status === 409 && respIndexToRequest(newRespIndex) !== null) {
        setRespError(caught.message || t('resp.conflict'));
      } else if (caught.status === 409) {
        setNameError(t('containers.nameTaken'));
      } else if (caught.status === 400) {
        setCacheError(caught.message);
      } else {
        setNameError(caught.message);
      }
    } finally {
      setSaving(false);
    }
  };

  const openDetail = (item, mode) => {
    setDetailMode(mode);
    setDetailTarget(item);
  };

  const submitDelete = async () => {
    try {
      await client.deleteContainer(deleteTarget.Name, deleteTarget.ObjectCount > 0);
      setDeleteTarget(null);
      notify(t('common.delete'), 'success');
      await load();
      loadStats();
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const columns = [
    {
      key: 'Name',
      label: t('common.name'),
      render: (item) => <strong className="link-text">{item.Name}</strong>,
    },
    { key: 'Id', label: 'ID', render: (item) => <CopyableId value={item.Id} /> },
    {
      key: 'ObjectCount',
      label: t('containers.objects'),
      align: 'right',
      render: (item) => formatters.number(item.ObjectCount),
    },
    {
      key: 'TotalBytes',
      label: t('containers.size'),
      align: 'right',
      render: (item) => formatters.bytes(item.TotalBytes),
    },
    {
      key: 'Tags',
      label: t('containers.tags'),
      render: (item) => {
        const entries = Object.entries(item.Tags ?? {});
        if (entries.length === 0) return <span className="muted">{t('common.none')}</span>;
        return (
          <span className="chip-list">
            {entries.slice(0, 3).map(([key, value]) => (
              <span className="chip is-static" key={key}>
                {key}={value}
              </span>
            ))}
            {entries.length > 3 ? <span className="muted">+{entries.length - 3}</span> : null}
          </span>
        );
      },
    },
    {
      key: 'CreatedUtc',
      label: t('containers.created'),
      render: (item) => <span title={formatters.dateTime(item.CreatedUtc)}>{formatters.relative(item.CreatedUtc)}</span>,
    },
    {
      key: 'actions',
      label: '',
      style: { width: '48px' },
      render: (item) => (
        <ActionMenu
          items={[
            { key: 'view', label: t('common.view'), onClick: () => openDetail(item, 'view') },
            { key: 'edit', label: t('common.edit'), onClick: () => openDetail(item, 'edit') },
            { key: 'json', label: t('common.viewJson'), onClick: () => setJsonTarget(item) },
            { key: 'browse', label: t('containers.browse'), onClick: () => navigate(`/containers/${encodeURIComponent(item.Name)}`) },
            { key: 'delete', label: t('common.delete'), variant: 'danger', onClick: () => setDeleteTarget(item) },
          ]}
        />
      ),
    },
  ];

  return (
    <div className="page">
      <PageHeader
        title={t('containers.title')}
        subtitle={t('containers.subtitle')}
        actions={
          <button type="button" className="button-primary" onClick={openCreate}>
            <PlusIcon size={16} />
            {t('containers.create')}
          </button>
        }
      />

      <ErrorBanner error={error} onRetry={() => void load()} />

      <div className="metric-grid">
        <Metric label={t('home.containers')} value={stats ? formatters.number(stats.ContainerCount) : '—'} />
        <Metric label={t('home.objects')} value={stats ? formatters.number(stats.ObjectCount) : '—'} />
        <Metric label={t('home.stored')} value={stats ? formatters.bytes(stats.TotalBytes) : '—'} />
        <Metric
          label={t('capacity.storageFree')}
          value={stats?.StorageFreeBytes > 0 ? formatters.bytes(stats.StorageFreeBytes) : '—'}
        />
      </div>

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
        storageKey="containers"
        emptyMessage={t('containers.empty')}
        rowId={(item) => item.Id}
        onRowClick={(item) => openDetail(item, 'edit')}
      />

      <Modal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        title={t('containers.createTitle')}
        size="medium"
        footer={
          <>
            <button type="button" className="button-secondary" onClick={() => setCreateOpen(false)} disabled={saving}>
              {t('common.cancel')}
            </button>
            <button type="button" className="button-primary" onClick={submitCreate} disabled={saving}>
              {t('common.create')}
            </button>
          </>
        }
      >
        <Field id="container-name" label={t('common.name')} hint={t('containers.nameHelp')} error={nameError}>
          <input
            id="container-name"
            type="text"
            value={newName}
            spellCheck={false}
            placeholder={t('containers.namePlaceholder')}
            onChange={(event) => {
              setNewName(event.target.value);
              setNameError(null);
            }}
            onKeyDown={(event) => {
              if (event.key === 'Enter') void submitCreate();
            }}
          />
        </Field>

        <Field id="container-tags" label={t('containers.tags')}>
          <TagEditor
            tags={newTags}
            onChange={setNewTags}
            keyLabel={t('containers.tagKey')}
            valueLabel={t('containers.tagValue')}
            addLabel={t('containers.addTag')}
          />
        </Field>

        <h3 className="detail-heading">{t('cache.section')}</h3>
        <CacheSettingsFields
          idPrefix="create-cache"
          draft={newCache}
          onChange={(next) => {
            setNewCache(next);
            setCacheError(null);
          }}
        />
        {cacheError ? <p className="field-error">{cacheError}</p> : null}

        <h3 className="detail-heading">{t('resp.section')}</h3>
        <RespIndexField
          idPrefix="create"
          value={newRespIndex}
          error={respError}
          onChange={(next) => {
            setNewRespIndex(next);
            setRespError(null);
          }}
        />
      </Modal>

      <ContainerDetailModal
        container={detailTarget}
        mode={detailMode}
        onClose={() => setDetailTarget(null)}
        onSaved={async () => {
          setDetailTarget(null);
          await load();
        }}
      />

      <JsonViewerModal
        open={Boolean(jsonTarget)}
        onClose={() => setJsonTarget(null)}
        type={t('common.typeContainer')}
        id={jsonTarget?.Id}
        value={jsonTarget}
      />

      <ConfirmModal
        open={Boolean(deleteTarget)}
        danger
        title={t('containers.deleteTitle')}
        message={deleteTarget ? t('containers.deleteConfirm', { name: deleteTarget.Name }) : ''}
        detail={
          deleteTarget?.ObjectCount > 0
            ? t('containers.deleteNotEmpty', { name: deleteTarget.Name, count: deleteTarget.ObjectCount })
            : null
        }
        // Emptying a container is recoverable; destroying its contents is not.
        requireText={deleteTarget?.ObjectCount > 0 ? deleteTarget.Name : null}
        requireTextLabel={t('containers.deleteTypeName')}
        confirmLabel={t('common.delete')}
        onConfirm={submitDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}

/**
 * Container details in view or edit mode.
 *
 * View and Edit are the same layout; edit mode makes tags writable and shows a Save button. Name,
 * ID, counts, and dates are derived or immutable, so they are read-only in both — the only thing a
 * container has that can change is its tags.
 */
function ContainerDetailModal({ container, mode, onClose, onSaved }) {
  const { t } = useTranslation();
  const { client, notify } = useApp();
  const formatters = useFormatters();

  const editable = mode === 'edit';
  const [tags, setTags] = useState({});
  const [busy, setBusy] = useState(false);

  // The live cache settings and per-node statistics, loaded when the modal opens. `cacheDraft` holds
  // the editable copy; `cacheError` surfaces client validation and the server's 400 message.
  const [cache, setCache] = useState(null);
  const [cacheDraft, setCacheDraft] = useState(cacheToDraft(CACHE_DEFAULTS));
  const [cacheLoading, setCacheLoading] = useState(false);
  const [cacheError, setCacheError] = useState(null);

  // The container's RESP database index, edited alongside the cache settings.
  const [respIndexDraft, setRespIndexDraft] = useState('');
  const [respError, setRespError] = useState(null);

  // The container's in-progress multipart uploads, read-only. `abortTarget` drives the confirm modal.
  const [uploads, setUploads] = useState([]);
  const [uploadsLoading, setUploadsLoading] = useState(false);
  const [abortTarget, setAbortTarget] = useState(null);

  const loadUploads = useCallback(async () => {
    if (!container) return;
    setUploadsLoading(true);
    try {
      const result = await client.containerMultipartUploads(container.Name);
      setUploads(result?.uploads ?? []);
    } catch {
      // Supplementary panel: a transient error just shows no uploads rather than blocking the modal.
      setUploads([]);
    } finally {
      setUploadsLoading(false);
    }
  }, [client, container]);

  useEffect(() => {
    setAbortTarget(null);
    void loadUploads();
  }, [loadUploads]);

  useEffect(() => {
    if (!container) return undefined;
    setTags({ ...(container.Tags ?? {}) });
    setRespIndexDraft(respIndexToDraft(container.RespDatabaseIndex));
    setRespError(null);
    setCache(null);
    setCacheError(null);
    setCacheLoading(true);

    let active = true;
    client
      .containerCache(container.Name)
      .then((loaded) => {
        if (!active) return;
        setCache(loaded);
        setCacheDraft(cacheToDraft(loaded));
      })
      .catch(() => {
        // Fall back to whatever the container response carried, so edit mode still has values.
        if (!active) return;
        setCache(null);
        setCacheDraft(cacheToDraft(container.Cache));
      })
      .finally(() => {
        if (active) setCacheLoading(false);
      });

    return () => {
      active = false;
    };
  }, [container, client]);

  if (!container) return null;

  const submit = async () => {
    const cacheValidation = validateCacheDraft(cacheDraft, t);
    if (cacheValidation) {
      setCacheError(cacheValidation);
      return;
    }

    const respValidation = validateRespIndexDraft(respIndexDraft, t);
    if (respValidation) {
      setRespError(respValidation);
      return;
    }

    setBusy(true);
    try {
      await client.updateContainerTags(container.Name, cleanTags(tags));
      const updated = await client.updateContainerCache(container.Name, draftToCacheRequest(cacheDraft));
      setCache(updated);
      setCacheDraft(cacheToDraft(updated));
      // The RESP-index endpoint returns the refreshed container; keep the draft in sync with it.
      const refreshed = await client.updateContainerRespIndex(
        container.Name,
        respIndexToRequest(respIndexDraft),
      );
      setRespIndexDraft(respIndexToDraft(refreshed?.RespDatabaseIndex));
      notify(t('cache.saved'), 'success');
      await onSaved();
    } catch (caught) {
      // Only the RESP-index update can 409 (its index is unique across containers).
      if (caught.status === 409) setRespError(caught.message || t('resp.conflict'));
      else if (caught.status === 400) setCacheError(caught.message);
      else notify(caught.message, 'danger');
    } finally {
      setBusy(false);
    }
  };

  const submitAbort = async () => {
    try {
      await client.abortMultipartUpload(container.Name, abortTarget.uploadId);
      setAbortTarget(null);
      notify(t('multipart.aborted'), 'success');
      await loadUploads();
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const uploadColumns = [
    {
      key: 'key',
      label: t('multipart.key'),
      render: (item) => <span className="mono">{item.key}</span>,
    },
    {
      key: 'uploadId',
      label: t('multipart.uploadId'),
      render: (item) => (
        <span className="mono" title={item.uploadId}>
          {item.uploadId}
        </span>
      ),
    },
    {
      key: 'initiatedUtc',
      label: t('multipart.initiated'),
      render: (item) => (
        <span title={formatters.dateTime(item.initiatedUtc)}>{formatters.relative(item.initiatedUtc)}</span>
      ),
    },
    {
      key: 'expiresUtc',
      label: t('multipart.expires'),
      render: (item) => (
        <span title={formatters.dateTime(item.expiresUtc)}>{formatters.relative(item.expiresUtc)}</span>
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

  return (
    <>
    <Modal
      open={Boolean(container)}
      onClose={onClose}
      title={t('common.typeDetails', { type: t('common.typeContainer') })}
      subtitle={<CopyableId value={container.Id} />}
      size="medium"
      footer={
        editable ? (
          <>
            <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>
              {t('common.cancel')}
            </button>
            <button type="button" className="button-primary" onClick={submit} disabled={busy}>
              {t('common.save')}
            </button>
          </>
        ) : (
          <button type="button" className="button-secondary" onClick={onClose}>
            {t('common.close')}
          </button>
        )
      }
    >
      <dl className="detail-grid">
        <dt>{t('common.name')}</dt>
        <dd>
          <strong>{container.Name}</strong>
        </dd>
        <dt>{t('containers.objects')}</dt>
        <dd>{formatters.number(container.ObjectCount)}</dd>
        <dt>{t('containers.size')}</dt>
        <dd>{formatters.bytes(container.TotalBytes)}</dd>
        <dt>{t('containers.created')}</dt>
        <dd title={formatters.dateTime(container.CreatedUtc)}>{formatters.dateTime(container.CreatedUtc)}</dd>
        <dt>{t('containers.updated')}</dt>
        <dd title={formatters.dateTime(container.LastUpdateUtc)}>{formatters.dateTime(container.LastUpdateUtc)}</dd>
      </dl>

      <h3 className="detail-heading">{t('containers.tags')}</h3>
      {editable ? (
        <TagEditor
          tags={tags}
          onChange={setTags}
          keyLabel={t('containers.tagKey')}
          valueLabel={t('containers.tagValue')}
          addLabel={t('containers.addTag')}
        />
      ) : Object.keys(container.Tags ?? {}).length ? (
        <div className="chip-list">
          {Object.entries(container.Tags).map(([key, value]) => (
            <span className="chip is-static" key={key}>
              {key}={value}
            </span>
          ))}
        </div>
      ) : (
        <p className="muted">{t('common.none')}</p>
      )}

      <h3 className="detail-heading">{t('cache.settings')}</h3>
      {editable ? (
        <>
          <CacheSettingsFields
            idPrefix="edit-cache"
            draft={cacheDraft}
            onChange={(next) => {
              setCacheDraft(next);
              setCacheError(null);
            }}
          />
          {cacheError ? <p className="field-error">{cacheError}</p> : null}
        </>
      ) : cacheLoading ? (
        <p className="muted">{t('common.loading')}</p>
      ) : (
        <>
          <dl className="detail-grid">
            <dt>{t('cache.enabledLabel')}</dt>
            <dd>{cache?.Enabled ? t('common.yes') : t('common.no')}</dd>
            {cache?.Enabled ? (
              <>
                <dt>{t('cache.policy')}</dt>
                <dd>{cache.Policy}</dd>
                <dt>{t('cache.maxObjects')}</dt>
                <dd>{formatters.number(cache.MaxObjects)}</dd>
                <dt>{t('cache.maxMemory')}</dt>
                <dd>{cache.MaxMemoryBytes > 0 ? formatters.bytes(cache.MaxMemoryBytes) : t('cache.noCap')}</dd>
                <dt>{t('cache.evictCount')}</dt>
                <dd>{formatters.number(cache.EvictCount)}</dd>
                <dt>{t('cache.maxObjectSize')}</dt>
                <dd>
                  {cache.MaxCacheableObjectBytes > 0 ? formatters.bytes(cache.MaxCacheableObjectBytes) : t('cache.noCeiling')}
                </dd>
              </>
            ) : null}
          </dl>

          {cache?.Enabled ? (
            <>
              <h3 className="detail-heading">{t('cache.statistics')}</h3>
              <dl className="detail-grid">
                <dt>{t('cache.hitRate')}</dt>
                <dd>{formatters.percent(cache.HitRate)}</dd>
                <dt>{t('cache.currentCount')}</dt>
                <dd>{formatters.number(cache.CurrentCount)}</dd>
                <dt>{t('cache.currentMemory')}</dt>
                <dd>{formatters.bytes(cache.CurrentMemoryBytes)}</dd>
                <dt>{t('cache.hits')}</dt>
                <dd>{formatters.number(cache.HitCount)}</dd>
                <dt>{t('cache.misses')}</dt>
                <dd>{formatters.number(cache.MissCount)}</dd>
                <dt>{t('cache.evictions')}</dt>
                <dd>{formatters.number(cache.EvictionCount)}</dd>
              </dl>
            </>
          ) : (
            <p className="muted">{t('cache.disabled')}</p>
          )}
        </>
      )}

      <h3 className="detail-heading">{t('resp.section')}</h3>
      {editable ? (
        <RespIndexField
          idPrefix="edit"
          value={respIndexDraft}
          error={respError}
          onChange={(next) => {
            setRespIndexDraft(next);
            setRespError(null);
          }}
        />
      ) : (
        <dl className="detail-grid">
          <dt>{t('resp.label')}</dt>
          <dd>
            {container.RespDatabaseIndex === undefined || container.RespDatabaseIndex === null
              ? t('resp.notSet')
              : formatters.number(container.RespDatabaseIndex)}
          </dd>
        </dl>
      )}

      <h3 className="detail-heading">{t('multipart.section')}</h3>
      <p className="muted">{t('multipart.hint')}</p>
      {uploadsLoading ? (
        <p className="muted">{t('common.loading')}</p>
      ) : (
        <DataTable
          columns={uploadColumns}
          items={uploads}
          rowId={(item) => item.uploadId}
          emptyMessage={t('multipart.empty')}
        />
      )}
    </Modal>

    <ConfirmModal
      open={Boolean(abortTarget)}
      danger
      title={t('multipart.abortTitle')}
      message={abortTarget ? t('multipart.abortConfirm', { key: abortTarget.key }) : ''}
      confirmLabel={t('multipart.abort')}
      onConfirm={submitAbort}
      onCancel={() => setAbortTarget(null)}
    />
    </>
  );
}
