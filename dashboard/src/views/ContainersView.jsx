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
import Modal from '@components/Modal.jsx';
import PageHeader from '@components/PageHeader.jsx';
import TableFrame from '@components/TableFrame.jsx';
import { CopyableId } from '@components/CopyButton.jsx';
import { ErrorBanner } from '@components/EmptyState.jsx';
import { JsonViewerModal } from '@components/JsonViewer.jsx';
import { Field, TagEditor, cleanTags } from '@components/FilterBar.jsx';
import { PlusIcon } from '@components/Icons.jsx';
import { persistedPageSize } from '@components/TablePagination.jsx';

/** Mirrors the server's container naming rule, so bad names fail before a round trip. */
const NAME_PATTERN = /^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$/;

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

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [newTags, setNewTags] = useState({});
  const [nameError, setNameError] = useState(null);
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

  const openCreate = () => {
    setNewName('');
    setNewTags({});
    setNameError(null);
    setCreateOpen(true);
  };

  const submitCreate = async () => {
    const name = newName.trim();
    if (!NAME_PATTERN.test(name)) {
      setNameError(t('containers.nameInvalid'));
      return;
    }

    setSaving(true);
    try {
      await client.createContainer(name, cleanTags(newTags));
      setCreateOpen(false);
      notify(t('containers.create'), 'success');
      setPageNumber(1);
      await load(1, pageSize);
    } catch (caught) {
      setNameError(caught.status === 409 ? t('containers.nameTaken') : caught.message);
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
        onRowClick={(item) => navigate(`/containers/${encodeURIComponent(item.Name)}`)}
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

  useEffect(() => {
    if (container) setTags({ ...(container.Tags ?? {}) });
  }, [container]);

  if (!container) return null;

  const submit = async () => {
    setBusy(true);
    try {
      await client.updateContainerTags(container.Name, cleanTags(tags));
      notify(t('common.save'), 'success');
      await onSaved();
    } catch (caught) {
      notify(caught.message, 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
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
    </Modal>
  );
}
