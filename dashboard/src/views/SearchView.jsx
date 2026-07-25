/**
 * Cross-container object search.
 *
 * The container list is loaded up front so the scope selector shows names rather than asking the
 * operator to remember them. Nothing is queried until Search is pressed — a cluster-wide enumeration
 * is not something to fire on every keystroke.
 */

import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ActionMenu from '@components/ActionMenu.jsx';
import PageHeader, { Card } from '@components/PageHeader.jsx';
import TableFrame from '@components/TableFrame.jsx';
import { ErrorBanner } from '@components/EmptyState.jsx';
import { JsonViewerModal } from '@components/JsonViewer.jsx';
import { EditMetadataModal, ObjectDetailModal } from '@components/ObjectModals.jsx';
import { ChipInput, Field, FilterActions, FilterGrid, TagEditor, cleanTags } from '@components/FilterBar.jsx';
import { persistedPageSize } from '@components/TablePagination.jsx';

const EMPTY_FILTERS = { prefix: '', labels: [], tags: {}, containers: [], caseInsensitive: false };

export default function SearchView() {
  const { t } = useTranslation();
  const { client, notify } = useApp();
  const formatters = useFormatters();
  const navigate = useNavigate();

  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [containers, setContainers] = useState([]);
  const [page, setPage] = useState(null);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(() => persistedPageSize('search', 25));
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const [detail, setDetail] = useState(null);
  const [editTarget, setEditTarget] = useState(null);
  const [jsonTarget, setJsonTarget] = useState(null);

  // Search results carry a container name per row, so the object modals — which take an explicit
  // container — work unchanged on cross-container results. Full metadata is fetched on demand; the
  // enumeration row omits the freeform Object.
  const openDetail = async (item) => {
    try {
      setDetail(await client.readObjectMetadata(item.ContainerName, item.Key));
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const openJson = async (item) => {
    try {
      setJsonTarget(await client.readObjectMetadata(item.ContainerName, item.Key));
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  // Full metadata before editing, so the freeform Object the list omits is not cleared on save.
  const openEdit = async (item) => {
    try {
      setEditTarget(await client.readObjectMetadata(item.ContainerName, item.Key));
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  useEffect(() => {
    if (!client) return;
    client
      .enumerateContainers({ pageSize: 100 })
      .then((result) => setContainers(result.items))
      .catch(() => setContainers([]));
  }, [client]);

  const run = useCallback(
    async (nextFilters = filters, nextPageNumber = 1, nextPageSize = pageSize) => {
      if (!client) return;
      setLoading(true);
      setError(null);
      try {
        const result = await client.search({
          ...nextFilters,
          tags: cleanTags(nextFilters.tags),
          pageSize: nextPageSize,
          skip: (nextPageNumber - 1) * nextPageSize,
          ordering: 'CreatedDescending',
        });
        setPage(result);
        setPageNumber(nextPageNumber);
      } catch (caught) {
        setError(caught);
      } finally {
        setLoading(false);
      }
    },
    [client, filters, pageSize],
  );

  const columns = [
    {
      key: 'ContainerName',
      label: t('search.container'),
      render: (item) => <span className="link-text">{item.ContainerName}</span>,
    },
    { key: 'Key', label: t('objects.key'), render: (item) => <span className="mono">{item.Key}</span> },
    { key: 'SizeBytes', label: t('objects.size'), align: 'right', render: (item) => formatters.bytes(item.SizeBytes) },
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
              key: 'open',
              label: t('search.openInContainer'),
              onClick: () => navigate(`/containers/${encodeURIComponent(item.ContainerName)}`),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <div className="page">
      <PageHeader title={t('search.title')} subtitle={t('search.subtitle')} />

      <ErrorBanner error={error} onRetry={() => void run()} />

      <Card
        title={t('common.filters')}
        actions={
          <FilterActions
            disabled={loading}
            onApply={() => void run(filters, 1, pageSize)}
            onClear={() => {
              setFilters(EMPTY_FILTERS);
              setPage(null);
            }}
          />
        }
      >
        <FilterGrid wide>
          <Field id="search-prefix" label={t('objects.filterPrefix')}>
            <input
              id="search-prefix"
              type="text"
              value={filters.prefix}
              spellCheck={false}
              onChange={(event) => setFilters({ ...filters, prefix: event.target.value })}
              onKeyDown={(event) => {
                if (event.key === 'Enter') void run(filters, 1, pageSize);
              }}
            />
          </Field>

          <Field id="search-containers" label={t('search.containers')}>
            <select
              id="search-containers"
              // Single-select: an empty value means "all containers", which the enumeration query
              // expresses as an empty Containers list.
              value={filters.containers[0] ?? ''}
              onChange={(event) =>
                setFilters({
                  ...filters,
                  containers: event.target.value ? [event.target.value] : [],
                })
              }
            >
              <option value="">{t('search.containersAll')}</option>
              {containers.map((entry) => (
                <option key={entry.Id} value={entry.Name}>
                  {entry.Name}
                </option>
              ))}
            </select>
          </Field>

          <Field id="search-labels" label={t('objects.filterLabels')}>
            <ChipInput
              id="search-labels"
              values={filters.labels}
              onChange={(labels) => setFilters({ ...filters, labels })}
              placeholder={t('objects.labelsPlaceholder')}
            />
          </Field>

          <Field id="search-tags" label={t('objects.filterTags')}>
            <TagEditor
              tags={filters.tags}
              onChange={(tags) => setFilters({ ...filters, tags })}
              keyLabel={t('containers.tagKey')}
              valueLabel={t('containers.tagValue')}
              addLabel={t('containers.addTag')}
            />
          </Field>

          <Field id="search-case" label={t('search.caseInsensitive')}>
            <label className="checkbox-row" htmlFor="search-case-input">
              <input
                id="search-case-input"
                type="checkbox"
                checked={filters.caseInsensitive}
                onChange={(event) => setFilters({ ...filters, caseInsensitive: event.target.checked })}
              />
              <span>{t('search.caseInsensitive')}</span>
            </label>
          </Field>
        </FilterGrid>
      </Card>

      {page === null ? (
        <Card>
          <p className="muted card-padded">{t('search.guidance')}</p>
        </Card>
      ) : (
        <TableFrame
          columns={columns}
          items={page.items}
          totalRecords={page.totalCount}
          pageNumber={pageNumber}
          pageSize={pageSize}
          onPageChange={(next) => void run(filters, next, pageSize)}
          onPageSizeChange={(size) => {
            setPageSize(size);
            void run(filters, 1, size);
          }}
          onRefresh={() => void run(filters, pageNumber, pageSize)}
          loading={loading}
          storageKey="search"
          emptyMessage={t('objects.noMatches')}
          rowId={(item) => `${item.ContainerName}/${item.Key}`}
          onRowClick={(item) => void openDetail(item)}
        />
      )}

      <ObjectDetailModal detail={detail} container={detail?.ContainerName} onClose={() => setDetail(null)} />

      <EditMetadataModal
        target={editTarget}
        container={editTarget?.ContainerName}
        onClose={() => setEditTarget(null)}
        onSaved={() => {
          setEditTarget(null);
          void run(filters, pageNumber, pageSize);
        }}
      />

      <JsonViewerModal
        open={Boolean(jsonTarget)}
        onClose={() => setJsonTarget(null)}
        type={t('common.typeObject')}
        id={jsonTarget?.Key}
        value={jsonTarget}
      />
    </div>
  );
}
