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

  const [tagTarget, setTagTarget] = useState(null);
  const [tagDraft, setTagDraft] = useState({});
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

  const submitTags = async () => {
    setSaving(true);
    try {
      await client.updateContainerTags(tagTarget.Name, cleanTags(tagDraft));
      setTagTarget(null);
      notify(t('common.save'), 'success');
      await load();
    } catch (caught) {
      notify(caught.message, 'danger');
    } finally {
      setSaving(false);
    }
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
    { key: 'Id', label: 'ID', render: (item) => <CopyableId value={item.Id} truncate={16} /> },
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
            { key: 'browse', label: t('containers.browse'), onClick: () => navigate(`/containers/${encodeURIComponent(item.Name)}`) },
            {
              key: 'tags',
              label: t('containers.editTags'),
              onClick: () => {
                setTagDraft({ ...(item.Tags ?? {}) });
                setTagTarget(item);
              },
            },
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

      <Modal
        open={Boolean(tagTarget)}
        onClose={() => setTagTarget(null)}
        title={tagTarget ? t('containers.editTagsTitle', { name: tagTarget.Name }) : ''}
        size="medium"
        footer={
          <>
            <button type="button" className="button-secondary" onClick={() => setTagTarget(null)} disabled={saving}>
              {t('common.cancel')}
            </button>
            <button type="button" className="button-primary" onClick={submitTags} disabled={saving}>
              {t('common.save')}
            </button>
          </>
        }
      >
        <TagEditor
          tags={tagDraft}
          onChange={setTagDraft}
          keyLabel={t('containers.tagKey')}
          valueLabel={t('containers.tagValue')}
          addLabel={t('containers.addTag')}
        />
      </Modal>

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
